using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Core
{
    // Explicit version 1 adapters for Shop C, Upstairs and Furniture Overhaul.
    public static class SlotFiles
    {
        public static IEnumerable<string> Names(int slot)
        {
            if (slot < 0 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
            return new[] { $"savedGames_Release{slot}.json", $"savedGames_Release{slot}.gd", $"savedGames_ReleaseBackupFile{slot}.json",
                $"ShopLotC_Save{slot}.json", $"Upstairs/Upstairs_{slot}.json", $"Upstairs_Save{slot}.json", $"StaffOverhaulSnapshots/initialized-slot{slot}.txt" }
                .Concat(FurnitureFolders.SelectMany(f => new[] { $"{f}/slot{slot}.json", $"{f}/slot{slot}.backup.json" }))
                .Concat(LateAdapters(slot));
        }
        // 1.2 : Enhanced Prefab Loader keeps the cards of mod editions (Pokémon packs) in its own
        // per-slot file, and Collection Tracker its counters. Snapshots made before 1.2 lack them.
        public static IEnumerable<string> LateAdapters(int slot)
        {
            if (slot < 0 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
            return new[] { $"PrefabLoader/EnhancedPrefabLoader_{slot}", $"CollectionTracker/ct_save_{slot}.txt" };
        }
        public static readonly string[] FurnitureFolders = { "FurnitureOverhaulPaint", "FurnitureOverhaulDisplayOnly", "FurnitureOverhaulCheckout", "FurnitureOverhaulClaw" };
        public static bool HashName(string name) => Regex.IsMatch(name, "^(?:FurnitureOverhaul(?:Checkout|Claw)/[a-f0-9]{64}\\.json|StaffOverhaulSnapshots/[a-f0-9]{64}\\.txt)$");
        public static string MainHash(string file)
        {
            if (Path.GetFileName(Path.GetDirectoryName(file)) == "StaffOverhaulSnapshots")
            {
                var text = File.ReadAllText(file);
                var split = text.IndexOf('\n');
                if (split < 0) throw new IOException(T("Snapshot Staff incomplet."));
                var header = Regex.Match(text.Substring(0, split), "^StaffOverhaulSnapshot 1\\|game=([a-f0-9]{64})\\|body=([a-f0-9]{64})$");
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var bodyHash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text.Substring(split + 1)))).Replace("-", "").ToLowerInvariant();
                    if (!header.Success || bodyHash != header.Groups[2].Value) throw new IOException(T("Snapshot Staff altéré."));
                }
                return header.Groups[1].Value;
            }
            var matches = Regex.Matches(File.ReadAllText(file), "\"mainHash\"\\s*:\\s*\"([a-f0-9]{64})\"");
            if (matches.Count != 1) throw new IOException(F("Empreinte de compagnon absente ou invalide : {0}", file));
            return matches[0].Groups[1].Value;
        }
        public static IEnumerable<string> Existing(string root, int slot, bool validateLinked = true, Func<string, string>? gameHash = null)
        {
            foreach (var name in Names(slot))
            {
                var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
                var parent = Path.GetDirectoryName(path)!;
                if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien de compagnon refusé."));
                if (!File.Exists(path)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien de fichier refusé."));
                yield return path;
            }
            if (!validateLinked) yield break;
            var linkedFiles = new HashSet<string>(StringComparer.Ordinal);
            var staffDirectory = Path.Combine(root, "StaffOverhaulSnapshots");
            if (Directory.Exists(staffDirectory))
            {
                if ((File.GetAttributes(staffDirectory) & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien Staff refusé."));
                foreach (var native in new[] { $"savedGames_Release{slot}.json", $"savedGames_ReleaseBackupFile{slot}.json" })
                {
                    var path = Path.Combine(root, native);
                    if (!File.Exists(path)) continue;
                    var raw = LabStore.Hash(path).ToLowerInvariant();
                    foreach (var hash in new[] { raw, gameHash == null ? raw : gameHash(path) }.Distinct())
                    {
                        var linked = Path.Combine(staffDirectory, hash + ".txt");
                        if (!File.Exists(linked)) continue;
                        if ((File.GetAttributes(linked) & FileAttributes.ReparsePoint) != 0 || MainHash(linked) != hash) throw new IOException(T("Snapshot Staff incohérent."));
                        if (linkedFiles.Add(linked)) yield return linked;
                    }
                }
            }
            foreach (var folder in FurnitureFolders.Skip(2))
            {
                var companion = Path.Combine(root, folder, $"slot{slot}.json");
                if (!File.Exists(companion)) continue;
                var hash = MainHash(companion);
                var linked = Path.Combine(root, folder, hash + ".json");
                if (!File.Exists(linked) || (File.GetAttributes(linked) & FileAttributes.ReparsePoint) != 0 || MainHash(linked) != hash)
                    throw new IOException(T("Snapshot de meuble lié manquant ou incohérent."));
                if (linkedFiles.Add(linked)) yield return linked;
            }
            // Preserve hash-keyed states for the native backup too, when present.
            var backup = Path.Combine(root, $"savedGames_ReleaseBackupFile{slot}.json");
            if (!File.Exists(backup)) yield break;
            var backupHash = gameHash == null ? LabStore.Hash(backup).ToLowerInvariant() : gameHash(backup);
            if (!Regex.IsMatch(backupHash, "^[a-f0-9]{64}$")) throw new IOException(T("Empreinte de secours invalide."));
            foreach (var folder in FurnitureFolders.Skip(2))
            {
                var linked = Path.Combine(root, folder, backupHash + ".json");
                if (!File.Exists(linked)) continue;
                if ((File.GetAttributes(linked) & FileAttributes.ReparsePoint) != 0 || MainHash(linked) != backupHash)
                    throw new IOException(T("Snapshot de secours Furniture incohérent."));
                if (linkedFiles.Add(linked)) yield return linked;
            }
        }
        public static IEnumerable<string> Working(string root)
        {
            var files = Enumerable.Range(0, 4).SelectMany(s => Existing(root, s, false)).Distinct(StringComparer.Ordinal).ToList();
            foreach (var folder in FurnitureFolders.Skip(2).Concat(new[] { "StaffOverhaulSnapshots" }))
            {
                var directory = Path.Combine(root, folder);
                if (!Directory.Exists(directory)) continue;
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien de meuble refusé."));
                foreach (var file in Directory.GetFiles(directory, folder == "StaffOverhaulSnapshots" ? "*.txt" : "*.json"))
                    if (HashName(Relative(root, file)))
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien de meuble refusé."));
                        files.Add(file);
                    }
            }
            return files.Distinct(StringComparer.Ordinal);
        }
        public static string Relative(string root, string path) => path.Substring(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
        public static string Remap(string name, int source, int target)
        {
            if (HashName(name)) return name;
            var names = Names(source).ToArray(); var index = Array.IndexOf(names, name);
            if (index < 0) throw new IOException(F("Fichier hors adaptateurs : {0}", name));
            return Names(target).ElementAt(index);
        }
    }
}
