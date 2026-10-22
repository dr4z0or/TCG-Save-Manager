using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Serialization;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Core
{
    public class Campaign
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string GameModeId { get; set; } = "";
        public string ModProfileId { get; set; } = "";
    }

    // Offline laboratory: immutable snapshots, no restore or writes to the game.
    public sealed partial class LabStore
    {
        private readonly string root;
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(Campaign));
        public LabStore(string root)
        {
            this.root = Path.GetFullPath(root);
            if (!File.Exists(Path.Combine(this.root, ".tcg-save-lab")))
                throw new InvalidOperationException(T("Dossier de laboratoire non initialisé."));
        }

        public Campaign Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Nom requis.");
            if (List().Any(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(T("Ce nom est déjà utilisé. Choisir un autre nom."));
            var campaign = new Campaign { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() };
            var folder = Folder(campaign.Id);
            Directory.CreateDirectory(folder);
            using (var stream = new FileStream(Path.Combine(folder, "campaign.xml"), FileMode.CreateNew))
                Serializer.Serialize(stream, campaign);
            return campaign;
        }

        public void Rename(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Nom requis.");
            var campaign = List().Single(c => c.Id == id);
            if (List().Any(c => c.Id != id && string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(T("Ce nom est déjà utilisé."));
            campaign.Name = name.Trim();
            var path = Path.Combine(Folder(id), "campaign.xml");
            var pending = path + ".pending";
            using (var stream = File.Create(pending)) Serializer.Serialize(stream, campaign);
            File.Replace(pending, path, null);
        }

        public Campaign Import(string name, string source, int slot, Func<string, string>? gameHash = null)
        {
            if (string.IsNullOrWhiteSpace(name) || List().Any(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(T("Choisir un nom de boutique libre."));
            // Publish the campaign only after its snapshot has passed every capture check.
            // Unity's Mono cannot reliably open paths over MAX_PATH. Keep this
            // extra level short, especially for Furniture's 64-character filenames.
            string staging;
            do { staging = Path.Combine(Directory.GetParent(root)!.FullName, ".i" + Guid.NewGuid().ToString("N").Substring(0, 8)); }
            while (Directory.Exists(staging) || File.Exists(staging));
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, ".tcg-save-lab"), "Staged import");
            var temporary = new LabStore(staging);
            var campaign = temporary.Create(name);
            temporary.Capture(campaign.Id, slot == 0 ? "auto" : "manual", source, slot, gameHash);
            Directory.Move(Path.Combine(staging, campaign.Id), Folder(campaign.Id));
            return campaign;
        }

        public Campaign[] List() => Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "campaign.xml")))
            .Select(d => { using (var s = File.OpenRead(Path.Combine(d, "campaign.xml"))) return (Campaign)Serializer.Deserialize(s); })
            .ToArray();

        public string Capture(string id, string kind, string source, int slot, Func<string, string>? gameHash = null)
        {
            source = Path.GetFullPath(source);
            if (kind != "auto" && kind != "manual") throw new ArgumentException(T("Type : auto ou manual."));
            var folder = Folder(id);
            if (!File.Exists(Path.Combine(folder, "campaign.xml"))) throw new InvalidOperationException(T("Partie inconnue."));
            var files = SlotFiles.Existing(source, slot, true, gameHash).ToArray();
            foreach (var folderName in SlotFiles.FurnitureFolders.Skip(2))
            {
                var companion = Path.Combine(source, folderName, $"slot{slot}.json");
                if (!File.Exists(companion)) continue;
                var main = Path.Combine(source, $"savedGames_Release{slot}.json");
                if (!File.Exists(main) || SlotFiles.MainHash(companion) != (gameHash == null ? Hash(main).ToLowerInvariant() : gameHash(main)))
                    throw new IOException(T("Compagnon Furniture non associé à la sauvegarde native. Archivage différé."));
            }
            var hashes = files.ToDictionary(p => p, Hash);
            if (!files.Any(p => p.EndsWith($"Release{slot}.json", StringComparison.Ordinal) || p.EndsWith($"Release{slot}.gd", StringComparison.Ordinal)))
                throw new InvalidOperationException(T("Aucune sauvegarde principale."));
            // Never publish a partially copied snapshot as complete.
            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
            var pending = Path.Combine(folder, kind, stamp + ".pending");
            Directory.CreateDirectory(pending);
            foreach (var file in files)
            {
                var before = hashes[file];
                var destination = Path.Combine(pending, SlotFiles.Relative(source, file).Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, false);
                if (before != Hash(destination) || before != Hash(file))
                    throw new IOException(T("Source modifiée pendant la copie. Instantané incomplet conservé dans .pending."));
            }
            if (!files.SequenceEqual(SlotFiles.Existing(source, slot, true, gameHash)) || hashes.Any(p => Hash(p.Key) != p.Value))
                throw new IOException(T("Le groupe de fichiers a changé pendant l'archivage."));
            File.WriteAllLines(Path.Combine(pending, "files-sha256.txt"), files.Select(p => SlotFiles.Relative(source, p) + "|" + hashes[p]).ToArray());
            File.WriteAllText(Path.Combine(pending, "snapshot.txt"), $"sourceSlot={slot}\nkind={kind}\nadapters=furniture-v1\n");
            var committed = Path.Combine(folder, kind, stamp);
            Directory.Move(pending, committed);
            return committed;
        }

        public string? Latest(string id, string kind)
            => Snapshots(id, kind).FirstOrDefault();

        public string[] Snapshots(string id, string kind)
        {
            if (kind != "auto" && kind != "manual") throw new ArgumentException(T("Type invalide."));
            var directory = Path.Combine(Folder(id), kind);
            return Directory.Exists(directory) ? Directory.GetDirectories(directory)
                .Where(d => !d.EndsWith(".pending", StringComparison.Ordinal)).OrderByDescending(d => d, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
        }

        private string Folder(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException(T("Identifiant invalide."));
            return Path.Combine(root, id);
        }

        public static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
