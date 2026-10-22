using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

internal static class RequiredModsTest
{
    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic requirements");
        var saves = Path.Combine(root, "saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
        File.WriteAllText(Path.Combine(saves, "savedGames_Release0.json"), "auto-native");
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.json"), "manual-native");
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.gd"), "manual-binary");
        var workspace = new CampaignWorkspace(saves);
        workspace.PrepareLaunch();
        var id = workspace.Active!.CampaignId;
        var oldManual = Path.GetFileName(workspace.Latest(id, "manual")!);
        workspace.CheckRequiredMods(id, "auto", "", Array.Empty<string>());
        Console.WriteLine("OK : sauvegardes natives sans mods autorisées");

        var cases = new[] {
            ("ShopLotC_Save1.json", "com.qolmods.shoplotc", "Shop C"),
            ("Upstairs/Upstairs_1.json", "com.qolmods.upstairs", "Upstairs"),
            ("Upstairs_Save1.json", "com.qolmods.upstairs", "Upstairs"),
            ("FurnitureOverhaulPaint/slot1.json", "com.qolmods.furnitureoverhaul", "Furniture Overhaul"),
            ("FurnitureOverhaulDisplayOnly/slot1.json", "com.qolmods.furnitureoverhaul", "Furniture Overhaul")
        };
        foreach (var item in cases)
        {
            foreach (var file in SlotFiles.Working(saves).Where(p => !Path.GetFileName(p).StartsWith("savedGames_"))) File.Delete(file);
            var companion = Path.Combine(saves, item.Item1.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(companion)!);
            File.WriteAllText(companion, "fixture");
            workspace.ArchiveCurrent(true);
            var workingHashes = SlotFiles.Working(saves).ToDictionary(p => p, LabStore.Hash);
            var sessionHash = LabStore.Hash(Path.Combine(root, "active-session.xml"));
            Reject("auto", ""); // Only the manual staged alongside auto requires the mod.
            Reject("manual", "");
            workspace.CheckRequiredMods(id, "auto", "", new[] { item.Item2 });
            workspace.CheckRequiredMods(id, "manual", oldManual, Array.Empty<string>());
            if (workspace.HasRequest || sessionHash != LabStore.Hash(Path.Combine(root, "active-session.xml")) ||
                workingHashes.Any(p => LabStore.Hash(p.Key) != p.Value)) throw new Exception("Le contrôle a modifié la session ou les fichiers.");
            Console.WriteLine("OK : " + item.Item1 + " détecté, version ancienne indépendante, fichiers conservés");
            void Reject(string kind, string snapshot)
            {
                try { workspace.CheckRequiredMods(id, kind, snapshot, Array.Empty<string>()); }
                catch (InvalidOperationException ex) when (ex.Message.Contains(item.Item3)) { return; }
                throw new Exception("Mod manquant accepté : " + item.Item1);
            }
        }
        Console.WriteLine("Tous les tests de mods requis ont réussi : " + root);
    }
}
