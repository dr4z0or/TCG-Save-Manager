using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Serialization;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Core
{
    public enum ModChoice { Pending, Enabled, Disabled }
    // What happens to a mod a shop has never seen.
    public enum NewModPolicy { Ask, Enable, Disable }
    public class ModDecision
    {
        public string Id { get; set; } = "";
        public string ReviewedVersion { get; set; } = "";
        public string ReviewedHash { get; set; } = "";
        public ModChoice Choice { get; set; }
        // Display name remembered for mods that are no longer installed.
        public string Name { get; set; } = "";
        // Version reviewed before an update kept this choice; cleared once the player confirms.
        public string UpdatedFrom { get; set; } = "";
    }
    public class ModSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public NewModPolicy NewMods { get; set; } = NewModPolicy.Ask;
    }
    public class ModProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public string CampaignId { get; set; } = "";
        public ModDecision[] Mods { get; set; } = Array.Empty<ModDecision>();
    }
    public class ModDependency
    {
        public string Id { get; set; } = "";
        public bool Required { get; set; }
        public string MinimumVersion { get; set; } = "";
    }
    public class InstalledMod
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Assembly { get; set; } = "";
        public bool Essential { get; set; }
        public ModDependency[] Dependencies { get; set; } = Array.Empty<ModDependency>();
        public string[] Incompatibilities { get; set; } = Array.Empty<string>();
    }
    public static class ModProfiles
    {
        // 0.3.0 : an updated mod keeps its previous choice and is only flagged (UpdatedFrom).
        // New mods follow the policy; only undecided mods and "Ask" produce Pending.
        public static ModProfile Reconcile(ModProfile profile, InstalledMod[] installed, NewModPolicy policy = NewModPolicy.Ask)
        {
            Unique(installed.Select(m => m.Id)); Unique(profile.Mods.Select(m => m.Id));
            return new ModProfile { CampaignId = profile.CampaignId, Mods = installed.Select(mod =>
            {
                var old = profile.Mods.SingleOrDefault(d => d.Id == mod.Id);
                var name = mod.Name != "" ? mod.Name : old?.Name ?? "";
                if (mod.Essential) return new ModDecision { Id = mod.Id, Name = name, ReviewedVersion = mod.Version, ReviewedHash = mod.Sha256, Choice = ModChoice.Enabled };
                if (old == null || old.Choice == ModChoice.Pending && old.ReviewedHash == "")
                {
                    var choice = old?.Choice == ModChoice.Pending ? ModChoice.Pending : policy == NewModPolicy.Enable ? ModChoice.Enabled : policy == NewModPolicy.Disable ? ModChoice.Disabled : ModChoice.Pending;
                    return new ModDecision { Id = mod.Id, Name = name, Choice = choice,
                        ReviewedVersion = choice == ModChoice.Pending ? "" : mod.Version, ReviewedHash = choice == ModChoice.Pending ? "" : mod.Sha256 };
                }
                var changed = old.ReviewedVersion != mod.Version || old.ReviewedHash != mod.Sha256;
                if (!changed) return new ModDecision { Id = mod.Id, Name = name, ReviewedVersion = old.ReviewedVersion, ReviewedHash = old.ReviewedHash, Choice = old.Choice, UpdatedFrom = old.UpdatedFrom };
                var from = old.UpdatedFrom != "" ? old.UpdatedFrom : old.ReviewedVersion == mod.Version ? old.ReviewedVersion + " (fichier modifié)" : old.ReviewedVersion;
                return new ModDecision { Id = mod.Id, Name = name, ReviewedVersion = mod.Version, ReviewedHash = mod.Sha256, Choice = old.Choice, UpdatedFrom = from == "" ? "?" : from };
            }).Concat(profile.Mods.Where(d => !installed.Any(m => m.Id == d.Id))).ToArray() };
        }
        // Mods that require the given one, directly or through other mods (hard dependencies only).
        public static string[] Dependents(string id, InstalledMod[] installed)
        {
            var result = new List<string>(); var queue = new Queue<string>(); queue.Enqueue(id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var mod in installed.Where(m => m.Dependencies.Any(d => d.Required && d.Id == current)))
                    if (mod.Id != id && !result.Contains(mod.Id)) { result.Add(mod.Id); queue.Enqueue(mod.Id); }
            }
            return result.ToArray();
        }
        // Mods the given one needs, recursively (hard dependencies only).
        public static string[] Requirements(string id, InstalledMod[] installed)
        {
            var result = new List<string>(); var queue = new Queue<string>(); queue.Enqueue(id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var mod = installed.FirstOrDefault(m => m.Id == current);
                if (mod == null) continue;
                foreach (var dep in mod.Dependencies.Where(d => d.Required))
                    if (dep.Id != id && !result.Contains(dep.Id)) { result.Add(dep.Id); queue.Enqueue(dep.Id); }
            }
            return result.ToArray();
        }
        public static ModSettings LoadSettings(string campaigns)
        {
            var path = Path.Combine(campaigns, "mod-settings.xml");
            if (!File.Exists(path)) return new ModSettings();
            try { using (var stream = File.OpenRead(path)) return (ModSettings?)new XmlSerializer(typeof(ModSettings)).Deserialize(stream) ?? new ModSettings(); }
            catch (InvalidOperationException) { return new ModSettings(); }
        }
        public static void SaveSettings(string campaigns, ModSettings settings)
        {
            if (!File.Exists(Path.Combine(campaigns, ".tcg-save-lab"))) throw new IOException(T("Espace de boutiques invalide."));
            var path = Path.Combine(campaigns, "mod-settings.xml");
            var pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var stream = new FileStream(pending, FileMode.CreateNew)) new XmlSerializer(typeof(ModSettings)).Serialize(stream, settings);
                if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
        public static string[] Validate(ModProfile profile, InstalledMod[] installed)
        {
            Unique(installed.Select(m => m.Id)); Unique(profile.Mods.Select(m => m.Id));
            var errors = new HashSet<string>(StringComparer.Ordinal);
            bool Enabled(string id) => profile.Mods.Any(d => d.Id == id && d.Choice == ModChoice.Enabled);
            foreach (var mod in installed.Where(m => m.Essential))
                if (!Enabled(mod.Id)) errors.Add(F("Composant indispensable : {0}", mod.Id));
            foreach (var decision in profile.Mods.Where(d => d.Choice == ModChoice.Enabled))
            {
                var mod = installed.SingleOrDefault(m => m.Id == decision.Id);
                if (mod == null) { errors.Add(F("Mod actif absent : {0}", decision.Id)); continue; }
                if (!mod.Essential && (decision.ReviewedVersion != mod.Version || decision.ReviewedHash != mod.Sha256)) errors.Add(F("Mod changé à examiner : {0}", mod.Id));
                foreach (var dep in mod.Dependencies.Where(d => d.Required))
                {
                    var target = installed.SingleOrDefault(m => m.Id == dep.Id);
                    if (target == null || !Enabled(dep.Id)) errors.Add(F("Dépendance requise : {0} → {1}", mod.Id, dep.Id));
                    else if (!string.IsNullOrEmpty(dep.MinimumVersion) &&
                        (!Version.TryParse(target.Version, out var actual) || !Version.TryParse(dep.MinimumVersion, out var minimum) || actual < minimum))
                        errors.Add(F("Version de dépendance insuffisante : {0}", dep.Id));
                }
                foreach (var incompatible in mod.Incompatibilities.Where(Enabled)) errors.Add(F("Incompatibilité : {0} / {1}", mod.Id, incompatible));
            }
            foreach (var group in installed.Where(m => !string.IsNullOrWhiteSpace(m.Assembly)).GroupBy(m => m.Assembly, StringComparer.OrdinalIgnoreCase))
                if (group.Any(m => Enabled(m.Id)) && group.Any(m => !Enabled(m.Id))) errors.Add(F("Sélection différente dans une même DLL : {0}", group.Key));
            return errors.OrderBy(e => e, StringComparer.Ordinal).ToArray();
        }
        public static void Save(string campaigns, ModProfile profile)
        {
            Unique(profile.Mods.Select(d => d.Id));
            if (profile.SchemaVersion != 1 || !Guid.TryParseExact(profile.CampaignId, "N", out _)) throw new ArgumentException(T("Profil invalide."));
            var folder = Path.Combine(campaigns, profile.CampaignId);
            if (!File.Exists(Path.Combine(campaigns, ".tcg-save-lab")) || !File.Exists(Path.Combine(folder, "campaign.xml"))) throw new IOException(T("Boutique inconnue."));
            foreach (var directory in new[] { campaigns, folder })
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien refusé."));
            var path = Path.Combine(folder, "mod-profile.xml");
            using (var stream = new FileStream(path + ".pending", FileMode.CreateNew)) new XmlSerializer(typeof(ModProfile)).Serialize(stream, profile);
            if (File.Exists(path)) File.Replace(path + ".pending", path, null); else File.Move(path + ".pending", path);
        }
        public static ModProfile Load(string campaigns, string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException(T("Identifiant invalide."));
            var path = Path.Combine(campaigns, id, "mod-profile.xml");
            if (!File.Exists(path)) return new ModProfile { CampaignId = id };
            using (var stream = File.OpenRead(path))
            {
                var profile = (ModProfile?)new XmlSerializer(typeof(ModProfile)).Deserialize(stream) ?? throw new IOException(T("Profil vide."));
                if (profile.CampaignId != id || profile.SchemaVersion != 1) throw new IOException(T("Identité ou version du profil invalide."));
                Unique(profile.Mods.Select(d => d.Id)); return profile;
            }
        }
        private static void Unique(IEnumerable<string> ids)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (ids.Any(id => string.IsNullOrWhiteSpace(id) || !seen.Add(id))) throw new ArgumentException(T("Identifiant de mod vide ou dupliqué."));
        }
    }
}
