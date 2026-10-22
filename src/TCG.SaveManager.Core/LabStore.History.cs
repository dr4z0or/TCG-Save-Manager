using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Core
{
    // Per-snapshot details shown to the player. Snapshots themselves stay immutable:
    // slot, name and the values read from the save live in <campaign>/snapshots.xml.
    public sealed class SnapshotInfo
    {
        public string Kind { get; set; } = "";
        public string Id { get; set; } = "";
        public bool Scanned { get; set; }
        public int Day { get; set; } = -1;
        public int Level { get; set; } = -1;
        public bool HasMoney { get; set; }
        public double Money { get; set; }
        public string ShopName { get; set; } = "";
        // Name given by the player to the manual slot holding this save.
        public string Note { get; set; } = "";
        // Manual slot (1 to SlotCount) this save belongs to; 0 = not in a slot.
        public int Slot { get; set; }
        [XmlIgnore] public string Path { get; set; } = "";
        [XmlIgnore] public DateTime SavedUtc { get; set; }
    }
    public sealed class SnapshotIndex
    {
        public int SchemaVersion { get; set; } = 1;
        // Set once existing manual saves have been placed in slots.
        public bool SlotsReady { get; set; }
        public List<SnapshotInfo> Entries { get; set; } = new List<SnapshotInfo>();
    }
    // A manual slot: the save it holds and the single backup kept when it was overwritten.
    public sealed class SaveSlot
    {
        public int Number { get; set; }
        public SnapshotInfo? Current { get; set; }
        public SnapshotInfo? Previous { get; set; }
        public string Name => Current != null && Current.Note != "" ? Current.Note : F("Emplacement {0}", Number);
    }
    public sealed class SaveSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public int AutoKeep { get; set; } = SaveSettingsDefaults.AutoKeep;
    }
    public static class SaveSettingsDefaults
    {
        // The latest automatic save plus one backup, like the game's single autosave.
        public const int AutoKeep = 2;
        public const int MinimumAutoKeep = 1;
        public const int MaximumAutoKeep = 20;
        public const int SlotCount = 3;
        // Versions kept per manual slot: the current one and the one it replaced.
        public const int SlotVersions = 2;
    }

    public sealed partial class LabStore
    {
        private static readonly XmlSerializer IndexSerializer = new XmlSerializer(typeof(SnapshotIndex));
        private static readonly XmlSerializer SettingsSerializer = new XmlSerializer(typeof(SaveSettings));
        private string IndexPath(string id) => System.IO.Path.Combine(Folder(id), "snapshots.xml");

        private static void CheckKind(string kind)
        {
            if (kind != "auto" && kind != "manual") throw new ArgumentException(T("Type invalide."));
        }
        private static void CheckSnapshotId(string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(snapshotId) || System.IO.Path.GetFileName(snapshotId) != snapshotId || snapshotId.EndsWith(".pending", StringComparison.Ordinal))
                throw new ArgumentException(T("Sauvegarde invalide."));
        }
        private static void CheckSlot(int slot)
        {
            if (slot < 1 || slot > SaveSettingsDefaults.SlotCount) throw new ArgumentOutOfRangeException(nameof(slot), T("Emplacement invalide."));
        }
        private static void RefuseLink(string path)
        {
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(T("Lien de dossier refusé."));
        }

        private SnapshotIndex ReadIndex(string id)
        {
            var path = IndexPath(id);
            if (!File.Exists(path)) return new SnapshotIndex();
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var index = (SnapshotIndex?)IndexSerializer.Deserialize(stream) ?? new SnapshotIndex();
                    if (index.SchemaVersion != 1) return new SnapshotIndex();
                    index.Entries = index.Entries.Where(e => e != null && (e.Kind == "auto" || e.Kind == "manual") && !string.IsNullOrEmpty(e.Id))
                        .GroupBy(e => e.Kind + "/" + e.Id).Select(g => g.First()).ToList();
                    return index;
                }
            }
            // A damaged index only loses names and slots; it must never block loading saves.
            catch (InvalidOperationException) { return new SnapshotIndex(); }
            catch (IOException) { return new SnapshotIndex(); }
        }
        private void WriteIndex(string id, SnapshotIndex index)
        {
            var path = IndexPath(id);
            var pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var stream = new FileStream(pending, FileMode.CreateNew)) IndexSerializer.Serialize(stream, index);
                if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
        private void TryWriteIndex(string id, SnapshotIndex index)
        {
            try { WriteIndex(id, index); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private static SnapshotInfo Entry(SnapshotIndex index, string kind, string snapshotId)
        {
            var entry = index.Entries.FirstOrDefault(e => e.Kind == kind && e.Id == snapshotId);
            if (entry == null) { entry = new SnapshotInfo { Kind = kind, Id = snapshotId }; index.Entries.Add(entry); }
            return entry;
        }

        // Main native file of a snapshot, used for its date and the values shown to the player.
        private static string? MainFile(string snapshot)
        {
            var info = System.IO.Path.Combine(snapshot, "snapshot.txt");
            var slot = -1;
            if (File.Exists(info))
            {
                var line = File.ReadAllLines(info).FirstOrDefault(l => l.StartsWith("sourceSlot=", StringComparison.Ordinal));
                if (line != null && int.TryParse(line.Substring(11), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) slot = parsed;
            }
            foreach (var candidate in Enumerable.Range(0, 4).OrderBy(s => s == slot ? 0 : 1))
            {
                var json = System.IO.Path.Combine(snapshot, $"savedGames_Release{candidate}.json");
                if (File.Exists(json)) return json;
                var binary = System.IO.Path.Combine(snapshot, $"savedGames_Release{candidate}.gd");
                if (File.Exists(binary)) return binary;
            }
            return null;
        }
        public static DateTime SavedTime(string snapshot)
        {
            var main = MainFile(snapshot);
            return main != null ? File.GetLastWriteTimeUtc(main) : Directory.GetLastWriteTimeUtc(snapshot);
        }
        private static readonly Regex DayPattern = new Regex("\"m_CurrentDay\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant);
        private static readonly Regex LevelPattern = new Regex("\"m_ShopLevel\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant);
        private static readonly Regex MoneyDoublePattern = new Regex("\"m_CoinAmountDouble\"\\s*:\\s*(-?[0-9][0-9.eE+-]*)", RegexOptions.CultureInvariant);
        private static readonly Regex MoneyPattern = new Regex("\"m_CoinAmount\"\\s*:\\s*(-?[0-9][0-9.eE+-]*)", RegexOptions.CultureInvariant);
        private static readonly Regex NamePattern = new Regex("\"m_PlayerName\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);
        // Reads a few values from the native JSON text without deserializing it.
        public static void ReadGameValues(string json, SnapshotInfo entry)
        {
            var day = DayPattern.Match(json);
            if (day.Success && int.TryParse(day.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)) entry.Day = d + 1;
            var level = LevelPattern.Match(json);
            if (level.Success && int.TryParse(level.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) entry.Level = l + 1;
            var money = MoneyDoublePattern.Match(json);
            if (!money.Success) money = MoneyPattern.Match(json);
            entry.HasMoney = money.Success && double.TryParse(money.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && !double.IsNaN(m) && !double.IsInfinity(m);
            entry.Money = entry.HasMoney ? double.Parse(money.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture) : 0;
            var name = NamePattern.Match(json);
            entry.ShopName = name.Success ? Regex.Unescape(name.Groups[1].Value).Trim() : "";
        }
        private static void Scan(string snapshot, SnapshotInfo entry)
        {
            entry.Scanned = true;
            var main = MainFile(snapshot);
            if (main == null || !main.EndsWith(".json", StringComparison.Ordinal)) return;
            try { ReadGameValues(File.ReadAllText(main), entry); }
            catch (IOException) { entry.Scanned = false; }
            catch (UnauthorizedAccessException) { entry.Scanned = false; }
        }
        public static long DirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }

        // Every snapshot of a shop, newest first. scan=true reads the save values once and caches them.
        public SnapshotInfo[] History(string id, bool scan = true)
        {
            var index = ReadIndex(id);
            var changed = false;
            var result = new List<SnapshotInfo>();
            foreach (var kind in new[] { "auto", "manual" })
                foreach (var snapshot in Snapshots(id, kind))
                {
                    var snapshotId = System.IO.Path.GetFileName(snapshot);
                    var before = index.Entries.Count;
                    var entry = Entry(index, kind, snapshotId);
                    if (index.Entries.Count != before) changed = true;
                    if (scan && !entry.Scanned) { Scan(snapshot, entry); changed = true; }
                    entry.Path = snapshot;
                    entry.SavedUtc = SavedTime(snapshot);
                    result.Add(entry);
                }
            if (changed) TryWriteIndex(id, index);
            return result.OrderByDescending(e => e.SavedUtc).ThenByDescending(e => e.Id, StringComparer.Ordinal).ToArray();
        }
        public SnapshotInfo? Describe(string id, string kind, string snapshot, bool scan = true)
        {
            CheckKind(kind);
            var snapshotId = System.IO.Path.GetFileName(snapshot);
            if (!Snapshots(id, kind).Any(s => System.IO.Path.GetFileName(s) == snapshotId)) return null;
            var index = ReadIndex(id);
            var before = index.Entries.Count;
            var entry = Entry(index, kind, snapshotId);
            var changed = index.Entries.Count != before;
            if (scan && !entry.Scanned) { Scan(snapshot, entry); changed = true; }
            if (changed) TryWriteIndex(id, index);
            entry.Path = snapshot; entry.SavedUtc = SavedTime(snapshot);
            return entry;
        }
        private string RequireSnapshot(string id, string kind, string snapshotId)
        {
            CheckKind(kind); CheckSnapshotId(snapshotId);
            return Snapshots(id, kind).SingleOrDefault(s => System.IO.Path.GetFileName(s) == snapshotId)
                ?? throw new InvalidOperationException(T("Cette sauvegarde n'existe plus."));
        }
        private static string CleanName(string name, int slot)
        {
            name = (name ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (name.Length > 40) name = name.Substring(0, 40).Trim();
            return name == "" ? F("Emplacement {0}", slot) : name;
        }

        // Permanent removal of one snapshot (an overwritten slot backup or an old automatic save).
        private void DeleteSnapshot(string id, string kind, string snapshotId, SnapshotIndex index)
        {
            var folder = RequireSnapshot(id, kind, snapshotId);
            RefuseLink(System.IO.Path.Combine(Folder(id), kind)); RefuseLink(folder);
            Directory.Delete(folder, true);
            index.Entries.RemoveAll(e => e.Kind == kind && e.Id == snapshotId);
        }

        // Existing manual saves (made before slots existed) fill the empty slots, newest first.
        private bool EnsureSlots(string id, SnapshotIndex index)
        {
            if (index.SlotsReady) return false;
            var manuals = Snapshots(id, "manual").Select(System.IO.Path.GetFileName).ToArray();
            var free = Enumerable.Range(1, SaveSettingsDefaults.SlotCount)
                .Where(n => !index.Entries.Any(e => e.Kind == "manual" && e.Slot == n && manuals.Contains(e.Id))).ToList();
            foreach (var snapshot in Snapshots(id, "manual").OrderByDescending(SavedTime))
            {
                if (free.Count == 0) break;
                var entry = Entry(index, "manual", System.IO.Path.GetFileName(snapshot));
                if (entry.Slot > 0) continue;
                entry.Slot = free[0]; free.RemoveAt(0);
                entry.Note = CleanName(entry.Note, entry.Slot);
            }
            index.SlotsReady = true;
            return true;
        }
        public SaveSlot[] Slots(string id, bool scan = true)
        {
            var index = ReadIndex(id);
            var changed = EnsureSlots(id, index);
            var manuals = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var snapshot in Snapshots(id, "manual")) manuals[System.IO.Path.GetFileName(snapshot)] = snapshot;
            var result = new List<SaveSlot>();
            for (var number = 1; number <= SaveSettingsDefaults.SlotCount; number++)
            {
                var versions = index.Entries.Where(e => e.Kind == "manual" && e.Slot == number && manuals.ContainsKey(e.Id)).ToList();
                foreach (var entry in versions)
                {
                    entry.Path = manuals[entry.Id]; entry.SavedUtc = SavedTime(entry.Path);
                    if (scan && !entry.Scanned) { Scan(entry.Path, entry); changed = true; }
                }
                versions = versions.OrderByDescending(e => e.SavedUtc).ThenByDescending(e => e.Id, StringComparer.Ordinal).ToList();
                result.Add(new SaveSlot { Number = number, Current = versions.FirstOrDefault(), Previous = versions.Skip(1).FirstOrDefault() });
            }
            if (changed) TryWriteIndex(id, index);
            return result.ToArray();
        }
        // Puts a newly captured manual save in a slot. The save it replaces becomes the slot's
        // backup; the backup before that is deleted, as the game does when a slot is overwritten.
        public void AssignSlot(string id, string snapshotId, int slot, string name)
        {
            CheckSlot(slot);
            RequireSnapshot(id, "manual", snapshotId);
            var index = ReadIndex(id);
            EnsureSlots(id, index);
            var entry = Entry(index, "manual", snapshotId);
            entry.Slot = slot;
            entry.Note = CleanName(name, slot);
            var live = Snapshots(id, "manual").ToDictionary(System.IO.Path.GetFileName, s => s, StringComparer.Ordinal);
            var versions = index.Entries.Where(e => e.Kind == "manual" && e.Slot == slot && live.ContainsKey(e.Id))
                .OrderByDescending(e => e.Id == snapshotId ? 1 : 0).ThenByDescending(e => SavedTime(live[e.Id])).ThenByDescending(e => e.Id, StringComparer.Ordinal).ToList();
            foreach (var old in versions.Skip(SaveSettingsDefaults.SlotVersions).ToArray()) DeleteSnapshot(id, "manual", old.Id, index);
            WriteIndex(id, index);
        }
        public void RenameSlot(string id, int slot, string name)
        {
            CheckSlot(slot);
            var current = Slots(id, false).Single(s => s.Number == slot).Current ?? throw new InvalidOperationException(T("Cet emplacement est vide."));
            var index = ReadIndex(id);
            Entry(index, "manual", current.Id).Note = CleanName(name, slot);
            WriteIndex(id, index);
        }

        // Keeps the newest automatic saves; older ones are deleted, like the game's own autosave.
        public int Prune(string id, string kind, int keep)
        {
            CheckKind(kind);
            if (keep < SaveSettingsDefaults.MinimumAutoKeep) keep = SaveSettingsDefaults.MinimumAutoKeep;
            var index = ReadIndex(id);
            var old = Snapshots(id, kind).OrderByDescending(SavedTime).ThenByDescending(System.IO.Path.GetFileName, StringComparer.Ordinal).Skip(keep).ToArray();
            foreach (var snapshot in old) DeleteSnapshot(id, kind, System.IO.Path.GetFileName(snapshot), index);
            if (old.Length > 0) TryWriteIndex(id, index);
            return old.Length;
        }

        public SaveSettings Settings
        {
            get
            {
                var path = System.IO.Path.Combine(root, "save-settings.xml");
                if (!File.Exists(path)) return new SaveSettings();
                try
                {
                    using (var stream = File.OpenRead(path))
                    {
                        var value = (SaveSettings?)SettingsSerializer.Deserialize(stream) ?? new SaveSettings();
                        value.AutoKeep = Math.Max(SaveSettingsDefaults.MinimumAutoKeep, Math.Min(SaveSettingsDefaults.MaximumAutoKeep, value.AutoKeep));
                        return value;
                    }
                }
                catch (InvalidOperationException) { return new SaveSettings(); }
            }
        }
        public void WriteSettings(SaveSettings settings)
        {
            settings.AutoKeep = Math.Max(SaveSettingsDefaults.MinimumAutoKeep, Math.Min(SaveSettingsDefaults.MaximumAutoKeep, settings.AutoKeep));
            var path = System.IO.Path.Combine(root, "save-settings.xml");
            var pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var stream = new FileStream(pending, FileMode.CreateNew)) SettingsSerializer.Serialize(stream, settings);
                if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }

        // Permanent: the shop folder and all its saves are removed.
        public void DeleteCampaign(string id)
        {
            var folder = Folder(id);
            if (!File.Exists(System.IO.Path.Combine(folder, "campaign.xml"))) throw new InvalidOperationException(T("Boutique inconnue."));
            RefuseLink(folder);
            foreach (var directory in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)) RefuseLink(directory);
            Directory.Delete(folder, true);
        }
        public long CampaignSize(string id) => DirectorySize(System.IO.Path.Combine(Folder(id), "auto")) + DirectorySize(System.IO.Path.Combine(Folder(id), "manual"));
    }
}
