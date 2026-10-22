using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

internal static class ImportTest
{
    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic import");
        var saves = Path.Combine(root, "saves"); Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
        File.WriteAllText(Path.Combine(saves, "savedGames_Release0.json"), "active");
        var source = Path.Combine(root, "original-saves-copy"); Directory.CreateDirectory(source);
        Put("savedGames_Release0.json", "{\"import\":\"auto\"}");
        Put("savedGames_Release3.json", "{\"import\":\"manual\"}");
        Put("savedGames_Release3.gd", "binary-placeholder");
        Put("ShopLotC_Save3.json", "shop"); Put("Upstairs/Upstairs_3.json", "upstairs");
        Put("FurnitureOverhaulPaint/slot3.json", "paint");
        var mainHash = LabStore.Hash(Path.Combine(source, "savedGames_Release3.json")).ToLowerInvariant();
        const string staffBody = "StaffOverhaul 3|seed=12\nindex=0\tjob1=1\tjob2=2\n";
        string bodyHash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            bodyHash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(staffBody))).Replace("-", "").ToLowerInvariant();
        var staffName = "StaffOverhaulSnapshots/" + mainHash + ".txt";
        var staffText = "StaffOverhaulSnapshot 1|game=" + mainHash + "|body=" + bodyHash + "\n" + staffBody;
        Put(staffName, staffText);
        Put("savedGames_ReleaseBackupFile3.json", "import-backup");
        var backupHash = LabStore.Hash(Path.Combine(source, "savedGames_ReleaseBackupFile3.json")).ToLowerInvariant();
        var backupStaffName = "StaffOverhaulSnapshots/" + backupHash + ".txt";
        var backupStaffText = "StaffOverhaulSnapshot 1|game=" + backupHash + "|body=" + bodyHash + "\n" + staffBody;
        Put(backupStaffName, backupStaffText);
        var normalizedBackup = new string('a', 64);
        var normalizedStaffName = "StaffOverhaulSnapshots/" + normalizedBackup + ".txt";
        Put(normalizedStaffName, "StaffOverhaulSnapshot 1|game=" + normalizedBackup + "|body=" + bodyHash + "\n" + staffBody);
        Put("StaffOverhaulSnapshots/initialized-slot3.txt", mainHash);
        foreach (var folder in new[] { "FurnitureOverhaulCheckout", "FurnitureOverhaulClaw" })
        {
            var linked = "{\"mainHash\":\"" + mainHash + "\"}";
            Put(folder + "/slot3.json", linked); Put(folder + "/" + mainHash + ".json", linked);
        }
        Put("other-mod.txt", "unmanaged");
        var sourceHashes = Directory.GetFiles(source, "*", SearchOption.AllDirectories).ToDictionary(p => p, LabStore.Hash);
        var workspace = new CampaignWorkspace(saves); workspace.PrepareLaunch();
        var sessionHash = LabStore.Hash(Path.Combine(root, "active-session.xml"));
        Check(workspace.ImportSlots().SequenceEqual(new[] { 0, 3 }), "emplacements disponibles détectés");
        var auto = workspace.Import("Import auto", 0);
        var manual = workspace.Import("Import manuel", 3);
        var store = new LabStore(Path.Combine(root, "campaigns"));
        store.Capture(manual.Id, "manual", source, 3, p => Path.GetFileName(p).Contains("BackupFile") ? normalizedBackup : LabStore.Hash(p).ToLowerInvariant());
        var stagedPath = Path.Combine(root, ".i00000000", manual.Id, "manual",
            Path.GetFileName(workspace.Latest(manual.Id, "manual")!) + ".pending", "FurnitureOverhaulCheckout", mainHash + ".json");
        Check(stagedPath.Length < 260, "chemin de préparation Furniture inférieur à MAX_PATH dans ce laboratoire");
        Check(workspace.Latest(auto.Id, "auto") != null && workspace.Latest(auto.Id, "manual") == null &&
            workspace.Latest(manual.Id, "manual") != null && workspace.Latest(manual.Id, "auto") == null, "branches auto et manuelle distinguées");
        Reject(() => workspace.Import("Import auto", 0), "nom dupliqué refusé");
        Reject(() => workspace.Import("Absent", 2), "emplacement absent refusé");
        var count = workspace.List().Length;
        var claw = File.ReadAllText(Path.Combine(source, "FurnitureOverhaulClaw", "slot3.json"));
        Put("FurnitureOverhaulClaw/slot3.json", "invalid-hash");
        Reject(() => workspace.Import("Incomplet", 3), "compagnon incohérent refusé");
        Put("FurnitureOverhaulClaw/slot3.json", claw);
        Check(workspace.List().Length == count && !workspace.List().Any(c => c.Name == "Incomplet"), "import incomplet non publié");
        Put(staffName, staffText + "tampered");
        Reject(() => workspace.Import("Staff corrompu", 3), "corps Staff altéré refusé");
        Put(staffName, staffText);
        Reject(() => workspace.CheckRequiredMods(manual.Id, "manual", "", new[] { "com.qolmods.shoplotc", "com.qolmods.upstairs", "com.qolmods.furnitureoverhaul" }), "Staff manquant refuse le chargement");
        Check(sessionHash == LabStore.Hash(Path.Combine(root, "active-session.xml")) && !workspace.HasRequest &&
            File.ReadAllText(Path.Combine(saves, "savedGames_Release0.json")) == "active", "import ne change ni session ni sauvegarde active");
        Check(sourceHashes.All(p => LabStore.Hash(p.Key) == p.Value), "copie source inchangée");
        workspace.Request(manual.Id, "manual"); workspace.PrepareLaunch();
        Check(File.ReadAllText(Path.Combine(saves, "savedGames_Release0.json")) == "{\"import\":\"manual\"}" &&
            File.ReadAllText(Path.Combine(saves, "ShopLotC_Save0.json")) == "shop" &&
            File.ReadAllText(Path.Combine(saves, "FurnitureOverhaulPaint", "slot0.json")) == "paint" &&
            File.Exists(Path.Combine(saves, "FurnitureOverhaulClaw", mainHash + ".json")) &&
            File.ReadAllText(Path.Combine(saves, staffName.Replace('/', Path.DirectorySeparatorChar))) == staffText &&
            !File.Exists(Path.Combine(saves, "other-mod.txt")), "boutique importée restaurée avec remappage des compagnons connus");
        Check(File.ReadAllText(Path.Combine(saves, backupStaffName.Replace('/', Path.DirectorySeparatorChar))) == backupStaffText &&
            File.Exists(Path.Combine(saves, normalizedStaffName.Replace('/', Path.DirectorySeparatorChar))) &&
            File.ReadAllText(Path.Combine(saves, "StaffOverhaulSnapshots", "initialized-slot0.txt")) == mainHash,
            "Staff du secours brut et normalisé conservé, marqueur de slot remappé");
        var blank = workspace.Create("Boutique sans employés");
        workspace.Request(blank.Id, "new"); workspace.PrepareLaunch();
        Check(!SlotFiles.Working(saves).Any(), "nouvelle boutique sans snapshots ni marqueurs Staff de la précédente");
        var transaction = Directory.GetDirectories(Path.Combine(root, "recovery")).OrderByDescending(p => p, StringComparer.Ordinal).First();
        var partial = Path.Combine(saves, staffName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        File.WriteAllText(partial, "partial staff write");
        File.WriteAllText(Path.Combine(root, "restore-in-progress.txt"), transaction);
        workspace.RecoverInterrupted();
        Check(File.ReadAllText(partial) == staffText &&
            File.ReadAllText(Path.Combine(saves, backupStaffName.Replace('/', Path.DirectorySeparatorChar))) == backupStaffText,
            "récupération restaure Staff courant et de secours");
        var rescue = Directory.GetDirectories(transaction, "interrupted-*").Single();
        Check(File.ReadAllText(Path.Combine(rescue, staffName.Replace('/', Path.DirectorySeparatorChar))) == "partial staff write",
            "écriture Staff interrompue conservée dans la copie de secours");
        Console.WriteLine("Tous les tests d'import ont réussi : " + root);
        void Put(string name, string data)
        {
            var path = Path.Combine(source, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, data);
        }
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("ÉCHEC : " + label);
        Console.WriteLine("OK : " + label);
    }
    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidOperationException)
        { Console.WriteLine("OK : " + label); return; }
        throw new Exception("ÉCHEC : " + label);
    }
}
