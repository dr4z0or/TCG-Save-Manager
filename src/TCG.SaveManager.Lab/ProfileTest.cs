using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

internal static class ProfileTest
{
    public static void Run(string root)
    {
        if (Directory.Exists(root)) throw new IOException("Nouveau dossier requis.");
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, ".tcg-save-lab"), "synthetic profiles");
        var store = new LabStore(root); var a = store.Create("A"); var b = store.Create("B");
        var api = new InstalledMod { Id = "api", Version = "6.1.0", Sha256 = "hash1", Assembly = "api.dll" };
        var mod = new InstalledMod { Id = "mod", Version = "1.0", Sha256 = "hash2", Assembly = "mod.dll", Dependencies = new[] { new ModDependency { Id = "api", Required = true, MinimumVersion = "6.1.0" } } };
        var essential = new InstalledMod { Id = "core", Essential = true, Assembly = "core.dll" };
        var installed = new[] { api, mod, essential };
        var profile = ModProfiles.Reconcile(new ModProfile { CampaignId = a.Id }, installed);
        Check(profile.Mods.Single(d => d.Id == "api").Choice == ModChoice.Pending && profile.Mods.Single(d => d.Id == "core").Choice == ModChoice.Enabled, "nouveaux mods à décider, composant principal actif");
        foreach (var entry in new[] { api, mod })
        {
            var decision = profile.Mods.Single(d => d.Id == entry.Id); decision.Choice = ModChoice.Enabled; decision.ReviewedVersion = entry.Version; decision.ReviewedHash = entry.Sha256;
        }
        Check(ModProfiles.Validate(profile, installed).Length == 0, "profil et dépendance compatibles");
        ModProfiles.Save(root, profile); ModProfiles.Save(root, profile);
        Check(ModProfiles.Load(root, a.Id).Mods.Length == 3 && ModProfiles.Load(root, b.Id).Mods.Length == 0, "persistance atomique et boutiques indépendantes");
        api.Version = "6.0.0";
        Check(ModProfiles.Validate(profile, installed).Any(e => e.Contains("insuffisante")), "version de dépendance trop ancienne refusée"); api.Version = "6.1.0";
        Check(ModProfiles.Validate(profile, new[] { mod, essential }).Any(e => e.Contains("requise")), "dépendance absente signalée");
        api.Sha256 = "newhash";
        // Mod Profiles 0.3.0 : une DLL mise à jour garde son choix et est signalée.
        var updated = ModProfiles.Reconcile(profile, installed).Mods.Single(d => d.Id == "api");
        Check(updated.Choice == ModChoice.Enabled && updated.UpdatedFrom == "6.1.0 (fichier modifié)" && updated.ReviewedHash == "newhash", "DLL changée : choix conservé et signalé"); api.Sha256 = "hash1";
        api.Version = "6.2.0"; api.Sha256 = "hash3";
        Check(ModProfiles.Reconcile(profile, installed).Mods.Single(d => d.Id == "api").UpdatedFrom == "6.1.0" && ModProfiles.Validate(ModProfiles.Reconcile(profile, installed), installed).Length == 0, "nouvelle version : choix conservé sans erreur de validation");
        api.Version = "6.1.0"; api.Sha256 = "hash1";
        Check(ModProfiles.Dependents("api", installed).SequenceEqual(new[] { "mod" }) && ModProfiles.Requirements("mod", installed).SequenceEqual(new[] { "api" }), "dépendants et dépendances calculés");
        var fresh = new InstalledMod { Id = "fresh", Name = "Nouveau mod", Version = "1", Sha256 = "f", Assembly = "fresh.dll" };
        var withFresh = installed.Concat(new[] { fresh }).ToArray();
        Check(ModProfiles.Reconcile(profile, withFresh).Mods.Single(d => d.Id == "fresh").Choice == ModChoice.Pending, "nouveau mod : à décider par défaut");
        Check(ModProfiles.Reconcile(profile, withFresh, NewModPolicy.Enable).Mods.Single(d => d.Id == "fresh").Choice == ModChoice.Enabled, "nouveau mod : activé selon le réglage");
        var disabledFresh = ModProfiles.Reconcile(profile, withFresh, NewModPolicy.Disable);
        Check(disabledFresh.Mods.Single(d => d.Id == "fresh").Choice == ModChoice.Disabled && disabledFresh.Mods.Single(d => d.Id == "fresh").Name == "Nouveau mod", "nouveau mod : désactivé selon le réglage, nom mémorisé");
        Check(ModProfiles.Reconcile(disabledFresh, installed).Mods.Single(d => d.Id == "fresh").Name == "Nouveau mod", "mod retiré : nom conservé pour l'affichage");
        Check(ModProfiles.Reconcile(ModProfiles.Reconcile(profile, withFresh), withFresh, NewModPolicy.Enable).Mods.Single(d => d.Id == "fresh").Choice == ModChoice.Pending, "réglage non appliqué aux mods déjà en attente");
        Check(ModProfiles.LoadSettings(root).NewMods == NewModPolicy.Ask, "réglage par défaut : demander");
        ModProfiles.SaveSettings(root, new ModSettings { NewMods = NewModPolicy.Disable });
        Check(ModProfiles.LoadSettings(root).NewMods == NewModPolicy.Disable, "réglage des nouveaux mods conservé");
        mod.Incompatibilities = new[] { "api" };
        Check(ModProfiles.Validate(profile, installed).Any(e => e.Contains("Incompatibilité")), "incompatibilité détectée"); mod.Incompatibilities = Array.Empty<string>();
        api.Assembly = "mod.dll"; profile.Mods.Single(d => d.Id == "api").Choice = ModChoice.Disabled;
        Check(ModProfiles.Validate(profile, installed).Any(e => e.Contains("même DLL")), "plugins d'une même DLL groupés");
        Console.WriteLine("Tous les tests de profils ont réussi.");
    }
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("OK : " + label); }
}
