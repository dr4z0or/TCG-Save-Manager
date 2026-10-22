using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

// Save Manager 1.2: Enhanced Prefab Loader (mod-edition cards) and Collection Tracker files follow each shop.
static class ModFilesTest
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("ÉCHEC : " + label);
        Console.WriteLine("OK : " + label);
    }
    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic mod files test");
        var saves = Path.Combine(root, "saves");
        Directory.CreateDirectory(Path.Combine(saves, "PrefabLoader"));
        Directory.CreateDirectory(Path.Combine(saves, "CollectionTracker"));
        File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
        string Epl(int slot) => Path.Combine(saves, "PrefabLoader", "EnhancedPrefabLoader_" + slot);
        string Tracker(int slot) => Path.Combine(saves, "CollectionTracker", "ct_save_" + slot + ".txt");
        var auto = Path.Combine(saves, "savedGames_Release0.json");

        // Legacy shop A: snapshots archived before 1.2 have no mod files.
        File.WriteAllText(auto, "{\"m_CurrentDay\":1}");
        var workspace = new CampaignWorkspace(saves);
        workspace.PrepareLaunch();
        var a = workspace.Active!.CampaignId;
        var legacyAuto = Path.GetFileName(workspace.Latest(a, "auto")!);
        Check(!Directory.EnumerateFiles(workspace.Latest(a, "auto")!, "*", SearchOption.AllDirectories).Any(f => f.Contains("PrefabLoader")), "instantané ancien sans fichier EPL");

        // The game writes A's mod-edition cards.
        File.WriteAllText(Epl(0), "A-cards"); File.WriteAllText(Tracker(0), "A-tracker");
        workspace.Request(a, "auto", legacyAuto);
        workspace.PrepareLaunch();
        Check(File.ReadAllText(Epl(0)) == "A-cards" && File.ReadAllText(Tracker(0)) == "A-tracker", "même boutique, ancien instantané : cartes EPL actuelles conservées");
        var archived = workspace.Latest(a, "auto")!;
        Check(File.Exists(Path.Combine(archived, "PrefabLoader", "EnhancedPrefabLoader_0")) && File.ReadAllText(Path.Combine(archived, "PrefabLoader", "EnhancedPrefabLoader_0")) == "A-cards", "les cartes EPL sont maintenant archivées avec la sauvegarde");

        // Shop B starts empty: A's cards must not follow.
        var b = workspace.Create("Boutique B").Id;
        workspace.Request(b, "new");
        workspace.PrepareLaunch();
        Check(!File.Exists(Epl(0)) && !File.Exists(Tracker(0)), "nouvelle boutique : aucune carte EPL d'une autre boutique");
        File.WriteAllText(auto, "{\"m_CurrentDay\":2}"); File.WriteAllText(Epl(0), "B-cards"); File.WriteAllText(Tracker(0), "B-tracker");
        workspace.ArchiveCurrent();

        // Back to A, then to B: each shop gets its own cards.
        workspace.Request(a, "auto");
        workspace.PrepareLaunch();
        Check(File.ReadAllText(Epl(0)) == "A-cards" && File.ReadAllText(Tracker(0)) == "A-tracker", "retour boutique A : ses cartes EPL");
        workspace.Request(b, "auto");
        workspace.PrepareLaunch();
        Check(File.ReadAllText(Epl(0)) == "B-cards", "retour boutique B : ses cartes EPL");

        // Manual slot saves carry the mod file of slot 1 and restore it into slot 0.
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.json"), "{\"m_CurrentDay\":3}");
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.gd"), "b-manual");
        File.WriteAllText(Epl(1), "B-cards-manual");
        workspace.ArchiveCurrent(true);
        workspace.AssignSlot(b, workspace.LastManualCapture!, 1, "B manuel");
        File.WriteAllText(Epl(0), "B-cards-later"); File.WriteAllText(auto, "{\"m_CurrentDay\":4}");
        workspace.ArchiveCurrent();
        workspace.Request(b, "manual", workspace.Slots(b)[0].Current!.Id);
        workspace.PrepareLaunch();
        Check(File.ReadAllText(Epl(0)) == "B-cards-manual" && File.ReadAllText(Epl(1)) == "B-cards-manual", "emplacement manuel : ses cartes EPL rechargées");

        // Legacy snapshot of another shop: no file carried over from the current shop.
        workspace.Request(a, "auto", legacyAuto);
        workspace.PrepareLaunch();
        Check(!File.Exists(Epl(0)), "ancien instantané d'une autre boutique : pas de cartes d'une autre boutique");

        try { workspace.CheckRequiredMods(b, "auto", "", new[] { "local.tcg.savemanager.diagnostics" }); throw new Exception("Mod requis ignoré."); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("Enhanced Prefab Loader") && ex.Message.Contains("Collection Tracker"), "Enhanced Prefab Loader et Collection Tracker requis pour charger"); }
        Console.WriteLine("Tous les tests des fichiers de mods ont réussi : " + root);
    }
}
