using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Core
{
    public class Session
    {
        public string CampaignId { get; set; } = "";
        public string AutoFingerprint { get; set; } = "";
        public string ManualFingerprint { get; set; } = "";
        public string LoadedKind { get; set; } = "";
    }
    public class LoadRequest
    {
        public string CampaignId { get; set; } = "";
        public string Kind { get; set; } = "";
        public string SnapshotId { get; set; } = "";
    }

    // Sandbox only. Never instantiated for the user's original LocalLow directory.
    public sealed partial class CampaignWorkspace
    {
        private readonly string saves;
        private readonly string root;
        private readonly string activePath;
        private readonly string requestPath;
        private readonly LabStore store;
        private readonly Func<string, string>? gameHash;
        private string? lastArchiveStamp;
        private readonly bool laboratory;
        public CampaignWorkspace(string saves, Func<string, string>? gameHash = null)
        {
            this.gameHash = gameHash;
            this.saves = Path.GetFullPath(saves);
            root = Directory.GetParent(this.saves)!.FullName;
            var sandbox = File.Exists(Path.Combine(root, ".tcg-test-instance")) && File.Exists(Path.Combine(this.saves, ".tcg-sandbox"));
            var managed = File.Exists(Path.Combine(root, ".tcg-managed-instance")) && File.Exists(Path.Combine(this.saves, ".tcg-managed-saves"));
            if (Path.GetFileName(this.saves) != "saves" || (!sandbox && !managed)) throw new InvalidOperationException(T("Espace isolé non initialisé."));
            laboratory = sandbox;
            foreach (var path in new[] { root, this.saves })
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException(T("Lien de dossier refusé."));
            var campaigns = Path.Combine(root, "campaigns");
            Directory.CreateDirectory(campaigns);
            File.WriteAllText(Path.Combine(campaigns, ".tcg-save-lab"), "TCG sandbox campaigns");
            store = new LabStore(campaigns);
            activePath = Path.Combine(root, "active-session.xml");
            requestPath = Path.Combine(root, "load-request.xml");
        }
        public Campaign[] List() => store.List();
        public ModProfile LoadModProfile(string id) => ModProfiles.Load(Path.Combine(root, "campaigns"), id);
        public void SaveModProfile(ModProfile profile) => ModProfiles.Save(Path.Combine(root, "campaigns"), profile);
        public Campaign Create(string name) => store.Create(name);
        public void Rename(string id, string name) => store.Rename(id, name);
        public string? Latest(string id, string kind) => store.Latest(id, kind);
        public string[] Snapshots(string id, string kind) => store.Snapshots(id, kind);
        // The game's own save folder (LocalLow), read only. Used for imports outside the laboratory,
        // where no safety copy exists: the files are hashed and copied, never written.
        public string? NativeSaves { get; set; }
        // Laboratory copies never read the real game folder.
        public bool ImportFromGameFolder => CustomImportSource == null && !laboratory && !Directory.Exists(Path.Combine(root, "original-saves-copy")) && NativeSaves != null;
        // 1.4 : a folder the player picked (USB key, backup…). Read only, like the game folder.
        public string? CustomImportSource { get; set; }
        public string ImportSourcePath => ImportSource();
        // Save slots (0 = automatic, 1-3 = manual) found in a folder.
        public static int[] SlotsIn(string folder)
        {
            try
            {
                return Enumerable.Range(0, 4).Where(s => LooksLikeSave(folder, s)).ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) { return Array.Empty<int>(); }
        }
        // 1.4.1 : any folder can now be picked, so a file merely named like a save (test data, a
        // truncated copy) must not become a shop the game cannot load. The game writes its JSON
        // save as an object; without JSON, its binary save must at least have a plausible size.
        private static bool LooksLikeSave(string folder, int slot)
        {
            var json = Path.Combine(folder, $"savedGames_Release{slot}.json");
            if (File.Exists(json))
            {
                using var stream = File.OpenRead(json);
                int b;
                while ((b = stream.ReadByte()) == 0xEF || b == 0xBB || b == 0xBF || b == ' ' || b == '\t' || b == '\r' || b == '\n') { }
                return b == '{';
            }
            var binary = new FileInfo(Path.Combine(folder, $"savedGames_Release{slot}.gd"));
            return binary.Exists && binary.Length >= 1024;
        }
        private string ImportSource()
        {
            if (CustomImportSource != null)
            {
                var custom = Path.GetFullPath(CustomImportSource);
                if (!Directory.Exists(custom)) throw new InvalidOperationException(T("Ce dossier n'existe plus (clé USB retirée ?)."));
                var inside = custom.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (inside.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(T("Ce dossier appartient à Save Manager : choisir les sauvegardes d'origine."));
                if ((File.GetAttributes(custom) & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien de dossier refusé."));
                return custom;
            }
            var source = Path.Combine(root, "original-saves-copy");
            if (ImportFromGameFolder)
            {
                source = Path.GetFullPath(NativeSaves!);
                if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar), saves.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(T("Le dossier du jeu est déjà celui de Save Manager."));
                if (!Directory.Exists(source)) throw new InvalidOperationException(T("Aucune sauvegarde du jeu trouvée sur ce PC."));
            }
            if (!Directory.Exists(source)) throw new InvalidOperationException(T("Aucune copie de sécurité disponible pour cet import."));
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien de dossier refusé."));
            return source;
        }
        public int[] ImportSlots()
        {
            return SlotsIn(ImportSource());
        }
        public Campaign Import(string name, int slot)
        {
            if (!ImportSlots().Contains(slot)) throw new ArgumentException(T("Emplacement à importer indisponible."));
            return store.Import(name, ImportSource(), slot, gameHash);
        }
        public string ImportPreviewFile(int slot)
        {
            if (!ImportSlots().Contains(slot)) throw new ArgumentException(T("Emplacement indisponible."));
            var source = ImportSource();
            var files = SlotFiles.Existing(source, slot, false).ToArray();
            return files.FirstOrDefault(p => Path.GetFileName(p) == $"savedGames_Release{slot}.json")
                ?? files.First(p => Path.GetFileName(p) == $"savedGames_Release{slot}.gd");
        }
        private string? Selected(string id, string kind, string snapshotId)
        {
            if (string.IsNullOrEmpty(snapshotId)) return Latest(id, kind);
            // Any kept version can be resumed: older autos as well as older manual saves.
            if ((kind != "manual" && kind != "auto") || Path.GetFileName(snapshotId) != snapshotId) throw new ArgumentException(T("Instantané invalide."));
            return Snapshots(id, kind).SingleOrDefault(p => Path.GetFileName(p) == snapshotId)
                ?? throw new InvalidOperationException(kind == "manual" ? T("Cette sauvegarde manuelle n'existe plus.") : T("Cette sauvegarde automatique n'existe plus."));
        }
        public Session? Active => File.Exists(activePath) ? Read<Session>(activePath) : null;
        public bool HasRequest => File.Exists(requestPath);
        public LoadRequest? Pending => HasRequest ? Read<LoadRequest>(requestPath) : null;
        public void Request(string id, string kind, string snapshotId = "")
        {
            if (!List().Any(c => c.Id == id)) throw new InvalidOperationException(T("Boutique inconnue."));
            if (kind != "new" && kind != "auto" && kind != "manual") throw new ArgumentException(T("Choix invalide."));
            if (kind == "new" && (Latest(id, "auto") != null || Latest(id, "manual") != null)) throw new InvalidOperationException(T("Boutique déjà sauvegardée."));
            if (kind == "new" && snapshotId != "") throw new ArgumentException(T("Instantané incompatible avec une nouvelle partie."));
            if (kind != "new" && Selected(id, kind, snapshotId) == null) throw new InvalidOperationException(T("Aucune sauvegarde de ce type."));
            Write(requestPath, new LoadRequest { CampaignId = id, Kind = kind, SnapshotId = snapshotId });
        }
        public void CancelRequest() { if (File.Exists(requestPath)) File.Delete(requestPath); }

        // Inspect both snapshots that PrepareLaunch will stage, including the manual
        // companion of an auto load. No game state or working files are modified.
        public void CheckRequiredMods(string id, string kind, string snapshotId, System.Collections.Generic.IEnumerable<string> available)
        {
            if (kind == "new") return;
            if (kind != "auto" && kind != "manual") throw new ArgumentException(T("Choix invalide."));
            var selected = Selected(id, kind, snapshotId) ?? throw new InvalidOperationException(T("Aucune sauvegarde de ce type."));
            var manual = kind == "manual" ? selected : Latest(id, "manual");
            var paths = new[] { selected, manual }.Where(p => p != null).Distinct()
                .SelectMany(p => SlotFiles.Working(p!).Select(f => SlotFiles.Relative(p!, f))).ToArray();
            var missing = RequiredMods(paths, available).Where(m => !m.Loaded).Select(m => m.Name).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException(F("Cette sauvegarde nécessite : {0}. Relancez avec ces mods pour charger la boutique.", string.Join(", ", missing)));
        }

        // Mods whose data a save carries (files of the adapters Save Manager knows), and whether they are loaded.
        public sealed class ModRequirement { public string Name = "", Id = ""; public bool Loaded; }
        static readonly (string Name, string Id, Func<string, bool> Uses)[] Adapters =
        {
            ("Staff Overhaul", "com.qolmods.staffoverhaul", p => p.StartsWith("StaffOverhaulSnapshots/", StringComparison.Ordinal)),
            ("Shop C", "com.qolmods.shoplotc", p => p.StartsWith("ShopLotC_Save", StringComparison.Ordinal)),
            ("Upstairs", "com.qolmods.upstairs", p => p.StartsWith("Upstairs_", StringComparison.Ordinal) || p.StartsWith("Upstairs/", StringComparison.Ordinal)),
            ("Furniture Overhaul", "com.qolmods.furnitureoverhaul", p => p.StartsWith("FurnitureOverhaul", StringComparison.Ordinal)),
            ("Enhanced Prefab Loader", "EnhancedPrefabLoader", p => p.StartsWith("PrefabLoader/", StringComparison.Ordinal)),
            ("Collection Tracker", "iitzsamurai.collectiontracker", p => p.StartsWith("CollectionTracker/", StringComparison.Ordinal)),
        };
        public static ModRequirement[] RequiredMods(System.Collections.Generic.IEnumerable<string> relativePaths, System.Collections.Generic.IEnumerable<string> available)
        {
            var paths = relativePaths.ToArray();
            var loaded = new System.Collections.Generic.HashSet<string>(available, StringComparer.Ordinal);
            return Adapters.Where(a => paths.Any(a.Uses)).Select(a => new ModRequirement { Name = a.Name, Id = a.Id, Loaded = loaded.Contains(a.Id) }).ToArray();
        }
        // 1.4 : known mods a shop's latest saves need and that are not loaded now (shown on its card).
        public string[] MissingMods(string id, System.Collections.Generic.IEnumerable<string> available)
        {
            var paths = new[] { Latest(id, "auto"), Latest(id, "manual") }.Where(p => p != null).Distinct()
                .SelectMany(p => SlotFiles.Working(p!).Select(f => SlotFiles.Relative(p!, f)));
            return RequiredMods(paths, available).Where(m => !m.Loaded).Select(m => m.Name).ToArray();
        }
        // 1.4 : what an import slot needs, shown before importing.
        public ModRequirement[] ImportRequirements(int slot, System.Collections.Generic.IEnumerable<string> available)
        {
            var source = ImportSource();
            return RequiredMods(SlotFiles.Existing(source, slot, false).Select(p => SlotFiles.Relative(source, p)), available);
        }

        // A cheap polling hint only: actual capture/restore still performs full hashes.
        public bool ArchiveIfChanged()
        {
            var stamp = ArchiveStamp();
            if (stamp == lastArchiveStamp) return false;
            var changed = ArchiveCurrent();
            // Do not cache a failed operation or a group that changed during capture.
            if (stamp == ArchiveStamp()) lastArchiveStamp = stamp;
            return changed;
        }
        private string ArchiveStamp()
        {
            return string.Join("\n", SlotFiles.Working(saves).Concat(new[] { activePath })
                .Where(File.Exists).OrderBy(p => p, StringComparer.Ordinal).Select(p =>
                {
                    var file = new FileInfo(p);
                    return p + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
                }));
        }
        public bool ArchiveCurrent(bool forceManual = false)
        {
            var session = Active;
            if (session == null) return false;
            var changed = false;
            var autoCaptured = false;
            var auto = Fingerprint(0);
            if (auto != "" && auto != session.AutoFingerprint)
            {
                store.Capture(session.CampaignId, "auto", saves, 0, gameHash);
                session.AutoFingerprint = auto;
                changed = true;
                autoCaptured = true;
            }
            // Native auto-to-empty-slot copies only JSON. A manual save must have its own .gd.
            var manualSlot = Enumerable.Range(1, 3).Where(s => File.Exists(Path.Combine(saves, $"savedGames_Release{s}.gd")))
                .OrderByDescending(s => File.GetLastWriteTimeUtc(Path.Combine(saves, $"savedGames_Release{s}.gd"))).FirstOrDefault();
            var manual = manualSlot == 0 ? "" : Fingerprint(manualSlot);
            LastManualCapture = null;
            // forceManual: an explicit save into a slot always gets its own copy.
            if (manual != "" && (forceManual || manual != session.ManualFingerprint))
            {
                LastManualCapture = Path.GetFileName(store.Capture(session.CampaignId, "manual", saves, manualSlot, gameHash));
                session.ManualFingerprint = manual;
                changed = true;
            }
            if (changed) Write(activePath, session);
            if (autoCaptured) PruneAutos(session.CampaignId);
            return changed;
        }

        // Called by the launcher only, before the game process exists.
        public string PrepareLaunch()
        {
            if (File.Exists(Path.Combine(root, "restore-in-progress.txt")))
                throw new InvalidOperationException(T("Remplacement interrompu. Conserver les fichiers de recovery et restaurer avant de relancer."));
            if (Active == null)
            {
                var initial = store.Create(T("Ma boutique"));
                Write(activePath, new Session { CampaignId = initial.Id });
            }
            ArchiveCurrent();
            if (!HasRequest) return T("Boutique active conservée.");
            var request = Read<LoadRequest>(requestPath);
            if (!List().Any(c => c.Id == request.CampaignId)) throw new InvalidOperationException(T("Boutique demandée inconnue."));
            if (request.Kind != "new" && request.Kind != "auto" && request.Kind != "manual") throw new InvalidOperationException(T("Requête invalide."));
            if (request.Kind == "new" && request.SnapshotId != "") throw new InvalidOperationException(T("Requête invalide."));
            var selected = request.Kind == "new" ? null : Selected(request.CampaignId, request.Kind, request.SnapshotId);
            if (request.Kind != "new" && selected == null) throw new InvalidOperationException(T("Instantané absent."));
            if (request.Kind == "new" && (Latest(request.CampaignId, "auto") != null || Latest(request.CampaignId, "manual") != null))
                throw new InvalidOperationException(T("Une nouvelle partie ne peut pas vider une boutique existante."));
            var previousCampaign = Active?.CampaignId;
            var transaction = Path.Combine(root, "recovery", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N"));
            var staging = Path.Combine(transaction, "staging");
            var backup = Path.Combine(transaction, "before");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(backup);
            if (selected != null) Stage(selected, staging, 0);
            var manualSnapshot = request.Kind == "manual" ? selected : Latest(request.CampaignId, "manual");
            if (manualSnapshot != null) Stage(manualSnapshot, staging, 1);
            // A snapshot made before 1.2 has no Enhanced Prefab Loader / Collection Tracker file. When the
            // same shop is reloaded, keep its current file instead of deleting its mod-edition cards.
            if (request.Kind != "new" && request.CampaignId == previousCampaign)
                foreach (var target in manualSnapshot != null ? new[] { 0, 1 } : new[] { 0 })
                    foreach (var name in SlotFiles.LateAdapters(target))
                    {
                        var staged = Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(staged)) continue;
                        var prefix = name.Substring(0, name.Length - (name.EndsWith(".txt", StringComparison.Ordinal) ? 5 : 1));
                        var live = Enumerable.Range(0, 4).SelectMany(SlotFiles.LateAdapters).Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                            .Select(n => Path.Combine(saves, n.Replace('/', Path.DirectorySeparatorChar))).Where(File.Exists)
                            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                        if (live != null) CopyChecked(live, staged);
                    }
            var originals = SlotFiles.Working(saves).ToArray();
            foreach (var file in originals) CopyChecked(file, Path.Combine(backup, SlotFiles.Relative(saves, file).Replace('/', Path.DirectorySeparatorChar)));
            File.WriteAllLines(Path.Combine(transaction, "backup-sha256.txt"), Directory.GetFiles(backup, "*", SearchOption.AllDirectories).Select(p => SlotFiles.Relative(backup, p) + "|" + LabStore.Hash(p)).ToArray());
            File.Copy(activePath, Path.Combine(transaction, "active-session-before.xml"));
            File.Copy(requestPath, Path.Combine(transaction, "request.xml"));
            File.WriteAllText(Path.Combine(root, "restore-in-progress.txt"), transaction);
            try
            {
                foreach (var file in originals) File.Delete(file);
                foreach (var file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories)) CopyChecked(file, Path.Combine(saves, SlotFiles.Relative(staging, file).Replace('/', Path.DirectorySeparatorChar)));
                var session = new Session { CampaignId = request.CampaignId, AutoFingerprint = Fingerprint(0), ManualFingerprint = Fingerprint(1), LoadedKind = request.Kind };
                Write(activePath, session);
                File.Delete(requestPath);
                File.Delete(Path.Combine(root, "restore-in-progress.txt"));
                return request.Kind == "new" ? T("Boutique préparée : choisir Nouvelle partie dans le jeu.") : T("Boutique préparée : choisir Continuer (slot auto) dans le jeu.");
            }
            catch
            {
                foreach (var file in SlotFiles.Working(saves).ToArray()) File.Delete(file);
                foreach (var file in Directory.GetFiles(backup, "*", SearchOption.AllDirectories)) CopyChecked(file, Path.Combine(saves, SlotFiles.Relative(backup, file).Replace('/', Path.DirectorySeparatorChar)));
                File.Copy(Path.Combine(transaction, "active-session-before.xml"), activePath, true);
                File.Delete(Path.Combine(root, "restore-in-progress.txt"));
                throw;
            }
        }

        public string RecoverInterrupted()
        {
            var marker = Path.Combine(root, "restore-in-progress.txt");
            if (!File.Exists(marker)) return T("Aucun changement interrompu.");
            var transaction = Path.GetFullPath(File.ReadAllText(marker).Trim());
            var recovery = Path.Combine(root, "recovery");
            if (!string.Equals(Directory.GetParent(transaction)?.FullName, recovery, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(T("Le journal de récupération pointe hors du dossier de test."));
            var backup = Path.Combine(transaction, "before");
            foreach (var path in new[] { recovery, transaction, backup })
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException(T("Lien de récupération refusé."));
            var session = Read<Session>(Path.Combine(transaction, "active-session-before.xml"));
            if (!List().Any(c => c.Id == session.CampaignId)) throw new InvalidOperationException(T("Boutique de récupération inconnue."));
            var expected = File.ReadAllLines(Path.Combine(transaction, "backup-sha256.txt"));
            var files = Directory.GetFiles(backup, "*", SearchOption.AllDirectories);
            if (files.Length != expected.Length) throw new IOException(T("Copie de récupération incomplète."));
            var seenBackup = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in expected)
            {
                var parts = entry.Split('|');
                if (parts.Length != 2 || (!Enumerable.Range(0, 4).SelectMany(SlotFiles.Names).Contains(parts[0]) && !SlotFiles.HashName(parts[0])) || !seenBackup.Add(parts[0])) throw new IOException(T("Journal de récupération invalide."));
                var file = Path.Combine(backup, parts[0].Replace('/', Path.DirectorySeparatorChar));
                var allowed = SlotFiles.Working(backup).Contains(file);
                if (!allowed || LabStore.Hash(file) != parts[1]) throw new IOException(F("Copie de récupération altérée : {0}", parts[0]));
            }
            // Keep even partially written current files before attempting restoration.
            var rescue = Path.Combine(transaction, "interrupted-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rescue);
            var current = SlotFiles.Working(saves).ToArray();
            foreach (var file in current) CopyChecked(file, Path.Combine(rescue, SlotFiles.Relative(saves, file).Replace('/', Path.DirectorySeparatorChar)));
            foreach (var file in current) File.Delete(file);
            foreach (var file in files) CopyChecked(file, Path.Combine(saves, SlotFiles.Relative(backup, file).Replace('/', Path.DirectorySeparatorChar)));
            Write(activePath, session);
            CancelRequest();
            File.WriteAllText(Path.Combine(transaction, "recovered.txt"), DateTime.UtcNow.ToString("O"));
            File.Delete(marker);
            return F("Boutique précédente restaurée. Fichiers interrompus conservés dans {0}", rescue);
        }

        private string Fingerprint(int slot)
        {
            var file = Path.Combine(saves, $"savedGames_Release{slot}.json");
            if (!File.Exists(file)) file = Path.Combine(saves, $"savedGames_Release{slot}.gd");
            if (!File.Exists(file)) return "";
            var companions = SlotFiles.Existing(saves, slot, true, gameHash).Where(p => !Path.GetFileName(p).StartsWith("savedGames_", StringComparison.Ordinal)).ToArray();
            return LabStore.Hash(file) + string.Concat(companions.Select(p => "|" + SlotFiles.Remap(SlotFiles.Relative(saves, p), slot, 0) + ":" + LabStore.Hash(p)));
        }
        private static void Stage(string snapshot, string staging, int targetSlot)
        {
            var info = File.ReadAllLines(Path.Combine(snapshot, "snapshot.txt"));
            var slot = int.Parse(info.Single(l => l.StartsWith("sourceSlot=", StringComparison.Ordinal)).Substring(11));
            if (slot < 0 || slot > 3) throw new InvalidOperationException(T("Slot d'instantané invalide."));
            var files = SlotFiles.Existing(snapshot, slot).ToArray();
            if (!files.Any(p => p.EndsWith($"Release{slot}.json", StringComparison.Ordinal) || p.EndsWith($"Release{slot}.gd", StringComparison.Ordinal)))
                throw new InvalidOperationException(T("Instantané incomplet : aucun fichier principal. Aucun remplacement effectué."));
            var manifest = Path.Combine(snapshot, "files-sha256.txt");
            if ((info.Contains("adapters=shopc-upstairs-v1") || info.Contains("adapters=furniture-v1")) && !File.Exists(manifest)) throw new IOException(T("Manifeste d'instantané manquant."));
            if (File.Exists(manifest))
            {
                var expected = File.ReadAllLines(manifest);
                // A normalized backup hash may differ from the raw JSON hash outside Unity.
                // Accept additional hash-named files only when the immutable manifest attests them.
                var extra = expected.Select(e => e.Split('|')).Where(p => p.Length == 2 && SlotFiles.HashName(p[0]))
                    .Select(p => Path.Combine(snapshot, p[0].Replace('/', Path.DirectorySeparatorChar)));
                files = files.Concat(extra).Distinct(StringComparer.Ordinal).ToArray();
                if (expected.Length != files.Length) throw new IOException(T("Instantané incomplet."));
                var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in expected)
                {
                    var parts = entry.Split('|');
                    if (parts.Length != 2 || (!SlotFiles.Names(slot).Contains(parts[0]) && !SlotFiles.HashName(parts[0])) || !seen.Add(parts[0])) throw new IOException(T("Manifeste d'instantané invalide."));
                    var path = Path.Combine(snapshot, parts[0].Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien d'instantané refusé."));
                    if (SlotFiles.HashName(parts[0]) && SlotFiles.MainHash(path) != Path.GetFileNameWithoutExtension(path)) throw new IOException(T("Empreinte interne d'instantané incohérente."));
                    if (!files.Contains(path) || LabStore.Hash(path) != parts[1]) throw new IOException(T("Instantané altéré."));
                }
            }
            foreach (var file in files)
            {
                var name = SlotFiles.Remap(SlotFiles.Relative(snapshot, file), slot, targetSlot);
                CopyChecked(file, Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar)));
            }
        }
        private static void CopyChecked(string source, string target)
        {
            var hash = LabStore.Hash(source);
            if (File.Exists(target))
            {
                if (LabStore.Hash(target) == hash) return;
                throw new IOException(T("Deux versions incompatibles pour le même fichier associé."));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, false);
            if (hash != LabStore.Hash(target) || hash != LabStore.Hash(source)) throw new IOException(T("Copie non stable."));
        }
        private static T Read<T>(string path)
        {
            using (var stream = File.OpenRead(path)) return (T)new XmlSerializer(typeof(T)).Deserialize(stream);
        }
        private static void Write<T>(string path, T value)
        {
            var pending = path + ".pending";
            using (var stream = File.Create(pending)) new XmlSerializer(typeof(T)).Serialize(stream, value);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    // Windows may briefly hold the session file without delete sharing.
                    // Keep the atomic replacement; never delete the previous session as a fallback.
                    System.Threading.Thread.Sleep(20 * (attempt + 1));
                }
            }
        }
    }
}


