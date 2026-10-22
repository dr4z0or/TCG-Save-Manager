using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

internal static class CompanionTest
{
    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic companions");
        var saves = Path.Combine(root, "saves"); Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
        var auto = Path.Combine(saves, "savedGames_Release0.json");
        Put("savedGames_Release0.json", "A-auto"); Put("savedGames_Release1.json", "A-manual"); Put("savedGames_Release1.gd", "A-binary");
        Put("ShopLotC_Save0.json", "A-shop-auto"); Put("ShopLotC_Save1.json", "A-shop-manual");
        Put("Upstairs/Upstairs_0.json", "A-up-auto"); Put("Upstairs/Upstairs_1.json", "A-up-manual");
        Put("Upstairs_Save0.json", "A-legacy"); Put("unmanaged.txt", "keep");
        var workspace = new CampaignWorkspace(saves); workspace.PrepareLaunch();
        var a = workspace.Active!.CampaignId;
        var firstManual = Path.GetFileName(workspace.Latest(a, "manual")!);
        var firstAuto = workspace.Latest(a, "auto");
        Put("ShopLotC_Save0.json", "A-shop-auto-changed"); workspace.ArchiveCurrent();
        Check(workspace.Latest(a, "auto") != firstAuto, "changement de compagnon seul archivé");
        Put("ShopLotC_Save1.json", "A-shop-manual-changed"); workspace.ArchiveCurrent(true);
        var b = workspace.Create("Boutique B"); workspace.Request(b.Id, "new"); workspace.PrepareLaunch();
        Check(!Enumerable.Range(0, 4).SelectMany(s => SlotFiles.Existing(saves, s)).Any() && File.ReadAllText(Path.Combine(saves, "unmanaged.txt")) == "keep", "boutique vierge sans compagnons précédents, fichier indépendant conservé");
        var transaction = Directory.GetDirectories(Path.Combine(root, "recovery")).Single();
        Put("savedGames_Release0.json", "B-auto"); Put("ShopLotC_Save0.json", "B-shop");
        workspace.ArchiveCurrent(); workspace.Request(a, "manual", firstManual); workspace.PrepareLaunch();
        Check(File.ReadAllText(Path.Combine(saves, "ShopLotC_Save0.json")) == "A-shop-manual" &&
            File.ReadAllText(Path.Combine(saves, "ShopLotC_Save1.json")) == "A-shop-manual" &&
            File.ReadAllText(Path.Combine(saves, "Upstairs", "Upstairs_0.json")) == "A-up-manual", "ancien manuel et compagnons remappés ensemble");
        Check(!File.Exists(Path.Combine(saves, "Upstairs_Save0.json")), "compagnon absent de l'ancien manuel non hérité");
        workspace.Request(b.Id, "auto"); workspace.PrepareLaunch();
        Check(File.ReadAllText(auto) == "B-auto" && File.ReadAllText(Path.Combine(saves, "ShopLotC_Save0.json")) == "B-shop" &&
            !File.Exists(Path.Combine(saves, "Upstairs", "Upstairs_0.json")), "retour B sans état Upstairs de A");
        workspace.Request(a, "auto");
        var snapshot = workspace.Latest(a, "auto")!;
        var companion = Path.Combine(snapshot, "ShopLotC_Save0.json");
        var content = File.ReadAllText(companion); File.WriteAllText(companion, "tampered");
        try { workspace.PrepareLaunch(); throw new Exception("Altération acceptée."); }
        catch (IOException) { Check(File.ReadAllText(auto) == "B-auto", "compagnon altéré refuse la transaction avant remplacement"); }
        File.WriteAllText(companion, content); workspace.CancelRequest();
        File.WriteAllText(Path.Combine(root, "restore-in-progress.txt"), transaction);
        Put("ShopLotC_Save0.json", "interrupted-shop"); Put("Upstairs/Upstairs_0.json", "interrupted-up");
        workspace.RecoverInterrupted();
        Check(File.ReadAllText(Path.Combine(saves, "ShopLotC_Save0.json")) == "A-shop-auto-changed" &&
            File.ReadAllText(Path.Combine(saves, "Upstairs", "Upstairs_0.json")) == "A-up-auto", "récupération restaure les compagnons et la boutique précédente");
        var rescue = Directory.GetDirectories(transaction, "interrupted-*").Single();
        Check(File.ReadAllText(Path.Combine(rescue, "Upstairs", "Upstairs_0.json")) == "interrupted-up", "compagnons interrompus préservés dans rescue");
        // Existing native-only archives remain readable.
        var latest = workspace.Latest(a, "manual")!;
        foreach (var file in SlotFiles.Existing(latest, 1).Where(p => !Path.GetFileName(p).StartsWith("savedGames_"))) File.Delete(file);
        File.Delete(Path.Combine(latest, "files-sha256.txt"));
        File.WriteAllText(Path.Combine(latest, "snapshot.txt"), "sourceSlot=1\nkind=manual\nnativeOnly=true\n");
        workspace.Request(a, "manual", Path.GetFileName(latest)); workspace.PrepareLaunch();
        Check(File.ReadAllText(auto) == "A-manual" && !File.Exists(Path.Combine(saves, "ShopLotC_Save0.json")), "ancienne archive native seule reste compatible sans compagnons hérités");
        Console.WriteLine("Tous les tests de compagnons ont réussi : " + root);
        void Put(string name, string content)
        {
            var path = Path.Combine(saves, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
        }
        static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("ÉCHEC : " + label);
            Console.WriteLine("OK : " + label);
        }
    }
}
