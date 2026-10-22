using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

internal static class FurnitureTest
{
    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Nouveau dossier requis.");
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic furniture test");
        var saves = Path.Combine(root, "saves"); Directory.CreateDirectory(saves); File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
        State(0, "A-auto"); State(1, "A-manual");
        Put("savedGames_ReleaseBackupFile0.json", "{\"state\":\"A-backup\"}");
        var backupHash = LabStore.Hash(Path.Combine(saves, "savedGames_ReleaseBackupFile0.json")).ToLowerInvariant();
        foreach (var folder in SlotFiles.FurnitureFolders.Skip(2)) Put(folder + "/" + backupHash + ".json", "{\"mainHash\":\"" + backupHash + "\"}");
        var workspace = new CampaignWorkspace(saves); workspace.PrepareLaunch(); var a = workspace.Active!.CampaignId;
        var oldManual = Path.GetFileName(workspace.Latest(a, "manual")!);
        Check(File.Exists(Path.Combine(workspace.Latest(a, "auto")!, "FurnitureOverhaulClaw", Hash(0) + ".json")), "snapshot Claw lié archivé");
        Check(File.Exists(Path.Combine(workspace.Latest(a, "auto")!, "FurnitureOverhaulCheckout", backupHash + ".json")), "état Checkout de la sauvegarde de secours archivé séparément");
        State(1, "A-manual-new"); workspace.ArchiveCurrent(true);
        var b = workspace.Create("B"); workspace.Request(b.Id, "new"); workspace.PrepareLaunch();
        var firstTransaction = Directory.GetDirectories(Path.Combine(root, "recovery")).Single();
        Check(!SlotFiles.Working(saves).Any(), "boutique vierge sans fichiers de meubles ni snapshots précédents");
        State(0, "B-auto"); workspace.ArchiveCurrent();
        workspace.Request(a, "manual", oldManual); workspace.PrepareLaunch();
        Check(File.ReadAllText(Path.Combine(saves, "FurnitureOverhaulPaint", "slot0.json")) == "A-manual-paint" &&
            File.ReadAllText(Path.Combine(saves, "FurnitureOverhaulDisplayOnly", "slot1.json")) == "A-manual-display", "ancien manuel restaure Paint et DisplayOnly dans les bons slots");
        Check(SlotFiles.MainHash(Path.Combine(saves, "FurnitureOverhaulCheckout", "slot0.json")) == Hash(0), "Checkout associé au JSON manuel choisi");
        var before = workspace.Snapshots(a, "manual").Length; workspace.ArchiveCurrent();
        Check(workspace.Snapshots(a, "manual").Length == before, "chargement ne réarchive pas artificiellement les meubles");
        workspace.Request(b.Id, "auto"); workspace.PrepareLaunch();
        Check(File.ReadAllText(Path.Combine(saves, "FurnitureOverhaulPaint", "slot0.json")) == "B-auto-paint" &&
            Directory.GetFiles(Path.Combine(saves, "FurnitureOverhaulClaw"), "*.json").Length == 2, "retour B sans snapshots orphelins de A");
        var primary = Path.Combine(saves, "savedGames_Release0.json");
        var original = File.ReadAllText(primary); File.WriteAllText(primary, "{\"state\":\"mismatch\"}");
        try { workspace.ArchiveCurrent(); throw new Exception("Association incorrecte acceptée."); }
        catch (IOException) { Console.WriteLine("OK : compagnon d'une autre version refuse l'archivage"); }
        File.WriteAllText(primary, original);
        var linked = Path.Combine(saves, "FurnitureOverhaulClaw", Hash(0) + ".json"); File.Move(linked, linked + ".missing");
        try { workspace.ArchiveCurrent(true); throw new Exception("Compagnon manquant accepté."); }
        catch (IOException) { Console.WriteLine("OK : fichier par empreinte manquant refusé"); }
        File.Move(linked + ".missing", linked);
        File.WriteAllText(Path.Combine(root, "restore-in-progress.txt"), firstTransaction);
        Put("FurnitureOverhaulClaw/slot0.json", "partial-invalid-companion"); workspace.RecoverInterrupted();
        Check(File.ReadAllText(Path.Combine(saves, "FurnitureOverhaulPaint", "slot0.json")) == "A-auto-paint" &&
            File.Exists(Path.Combine(saves, "FurnitureOverhaulCheckout", Hash(0) + ".json")), "récupération restaure fichiers de meubles et noms par empreinte");
        Check(File.Exists(Path.Combine(saves, "FurnitureOverhaulClaw", backupHash + ".json")), "récupération conserve aussi le snapshot de secours");
        // Rehearse a normalized backup hash different from the raw hash, as Unity may produce.
        var normalized = new string('a', 64);
        foreach (var folder in SlotFiles.FurnitureFolders.Skip(2)) Put(folder + "/" + normalized + ".json", "{\"mainHash\":\"" + normalized + "\"}");
        var store = new LabStore(Path.Combine(root, "campaigns"));
        var normalizedSnapshot = store.Capture(a, "auto", saves, 0, p => Path.GetFileName(p).Contains("BackupFile") ? normalized : LabStore.Hash(p).ToLowerInvariant());
        Check(File.Exists(Path.Combine(normalizedSnapshot, "FurnitureOverhaulClaw", normalized + ".json")), "empreinte normalisée de secours conservée");
        workspace.Request(a, "auto"); workspace.PrepareLaunch();
        Check(File.Exists(Path.Combine(saves, "FurnitureOverhaulClaw", normalized + ".json")), "restauration hors Unity conserve le snapshot normalisé attesté par manifeste");
        Console.WriteLine("Tous les tests Furniture ont réussi : " + root);
        void State(int slot, string name)
        {
            Put($"savedGames_Release{slot}.json", "{\"state\":\"" + name + "\"}"); Put($"savedGames_Release{slot}.gd", name + "-binary");
            Put($"FurnitureOverhaulPaint/slot{slot}.json", name + "-paint"); Put($"FurnitureOverhaulPaint/slot{slot}.backup.json", name + "-paint-backup");
            Put($"FurnitureOverhaulDisplayOnly/slot{slot}.json", name + "-display");
            foreach (var folder in SlotFiles.FurnitureFolders.Skip(2))
            {
                var json = "{\"mainHash\":\"" + Hash(slot) + "\",\"fixture\":\"" + name + "\"}";
                Put($"{folder}/slot{slot}.json", json); Put($"{folder}/{Hash(slot)}.json", json);
            }
        }
        string Hash(int slot) => LabStore.Hash(Path.Combine(saves, $"savedGames_Release{slot}.json")).ToLowerInvariant();
        void Put(string relative, string value) { var path = Path.Combine(saves, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value); }
        static void Check(bool value, string label) { if (!value) throw new Exception("ÉCHEC : " + label); Console.WriteLine("OK : " + label); }
    }
}
