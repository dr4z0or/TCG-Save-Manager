using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

// Installed outside the laboratory: imports read the game's own save folder and never change it.
internal static class NativeImportTest
{
    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
        var managed = Path.Combine(root, "TCGSM");
        Directory.CreateDirectory(managed);
        File.WriteAllText(Path.Combine(managed, ".tcg-managed-instance"), "TCG Save Manager isolated workspace v1");
        var saves = Path.Combine(managed, "saves"); Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, ".tcg-managed-saves"), "TCG Save Manager isolated saves v1");
        var native = Path.Combine(root, "LocalLow", "OPNeonGames", "Card Shop Simulator");
        Directory.CreateDirectory(native);
        File.WriteAllText(Path.Combine(native, "savedGames_Release0.json"), "{\"m_CurrentDay\":4}");
        File.WriteAllText(Path.Combine(native, "savedGames_Release2.json"), "{\"m_CurrentDay\":9}");
        var before = Directory.GetFiles(native, "*", SearchOption.AllDirectories).ToDictionary(p => p, LabStore.Hash);

        var workspace = new CampaignWorkspace(saves);
        Reject(() => workspace.ImportSlots(), "sans dossier du jeu connu, import refusé");
        workspace.NativeSaves = native;
        Check(workspace.ImportFromGameFolder, "installation normale : import depuis le dossier du jeu");
        Check(workspace.ImportSlots().SequenceEqual(new[] { 0, 2 }), "emplacements du jeu détectés");
        var campaign = workspace.Import("Ma partie", 2);
        Check(workspace.Latest(campaign.Id, "manual") != null, "partie du jeu importée dans une boutique");
        Check(before.All(p => LabStore.Hash(p.Key) == p.Value) && Directory.GetFiles(native, "*", SearchOption.AllDirectories).Length == before.Count,
            "sauvegardes du jeu inchangées");
        workspace.NativeSaves = Path.Combine(root, "absent");
        Reject(() => workspace.ImportSlots(), "dossier du jeu absent signalé");
        workspace.NativeSaves = saves;
        Reject(() => workspace.ImportSlots(), "les emplacements de Save Manager ne sont pas une source d'import");

        // 1.4 : a folder chosen by the player (USB key…), and the mods its saves need.
        var usb = Path.Combine(root, "USB", "Backup TCG");
        Directory.CreateDirectory(Path.Combine(usb, "StaffOverhaulSnapshots"));
        File.WriteAllText(Path.Combine(usb, "savedGames_Release1.json"), "{\"m_CurrentDay\":12}");
        File.WriteAllText(Path.Combine(usb, "ShopLotC_Save1.json"), "{}");
        File.WriteAllText(Path.Combine(usb, "StaffOverhaulSnapshots", "initialized-slot1.txt"), "x");
        var usbBefore = Directory.GetFiles(usb, "*", SearchOption.AllDirectories).ToDictionary(p => p, LabStore.Hash);
        workspace.NativeSaves = native;
        Check(CampaignWorkspace.SlotsIn(usb).SequenceEqual(new[] { 1 }) && CampaignWorkspace.SlotsIn(Path.Combine(root, "absent")).Length == 0, "emplacements trouvés dans un dossier quelconque");
        // 1.4.1 : files merely named like saves (test data, truncated copy) are not offered.
        var fake = Path.Combine(root, "fake-saves"); Directory.CreateDirectory(fake);
        File.WriteAllText(Path.Combine(fake, "savedGames_Release1.json"), "A-manual");
        File.WriteAllText(Path.Combine(fake, "savedGames_Release1.gd"), "A-manual-binary");
        File.WriteAllText(Path.Combine(fake, "savedGames_Release2.gd"), "tiny");
        File.WriteAllText(Path.Combine(fake, "savedGames_Release3.json"), "\uFEFF \r\n{\"m_CurrentDay\":1}");
        Check(CampaignWorkspace.SlotsIn(fake).SequenceEqual(new[] { 3 }), "fichiers qui ne sont pas des sauvegardes ignorés, vraie sauvegarde avec BOM gardée");
        workspace.CustomImportSource = usb;
        Check(!workspace.ImportFromGameFolder && workspace.ImportSlots().SequenceEqual(new[] { 1 }) && workspace.ImportSourcePath == Path.GetFullPath(usb), "dossier choisi utilisé à la place du dossier du jeu");
        var needs = workspace.ImportRequirements(1, new[] { "com.qolmods.shoplotc" });
        Check(needs.Length == 2 && needs.Single(n => n.Name == "Shop C").Loaded && !needs.Single(n => n.Name == "Staff Overhaul").Loaded, "mods utilisés par la sauvegarde : présent et absent signalés");
        var fromUsb = workspace.Import("Clé USB", 1);
        Check(workspace.Latest(fromUsb.Id, "manual") != null && usbBefore.All(p => LabStore.Hash(p.Key) == p.Value), "import depuis le dossier choisi, source inchangée");
        Check(workspace.MissingMods(fromUsb.Id, new[] { "com.qolmods.shoplotc" }).SequenceEqual(new[] { "Staff Overhaul" }) &&
            workspace.MissingMods(fromUsb.Id, new[] { "com.qolmods.shoplotc", "com.qolmods.staffoverhaul" }).Length == 0, "boutique : mods absents signalés sur sa carte");
        workspace.CustomImportSource = Path.Combine(managed, "campaigns");
        Reject(() => workspace.ImportSlots(), "dossier interne de Save Manager refusé comme source");
        workspace.CustomImportSource = Path.Combine(root, "USB", "retirée");
        Reject(() => workspace.ImportSlots(), "dossier disparu signalé");
        workspace.CustomImportSource = null;
        Check(workspace.ImportFromGameFolder, "retour au dossier du jeu");

        // A laboratory copy keeps its safety copy and never looks at the real folder.
        var lab = Path.Combine(root, "lab");
        Directory.CreateDirectory(lab);
        File.WriteAllText(Path.Combine(lab, ".tcg-test-instance"), "synthetic");
        var labSaves = Path.Combine(lab, "saves"); Directory.CreateDirectory(labSaves);
        File.WriteAllText(Path.Combine(labSaves, ".tcg-sandbox"), "test");
        var labWorkspace = new CampaignWorkspace(labSaves) { NativeSaves = native };
        Check(!labWorkspace.ImportFromGameFolder, "laboratoire : dossier du jeu jamais utilisé");
        Reject(() => labWorkspace.ImportSlots(), "laboratoire sans copie de sécurité : import refusé");
        Console.WriteLine("Tous les tests d'import depuis le jeu ont réussi : " + root);
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
