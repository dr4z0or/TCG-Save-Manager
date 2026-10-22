using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

// Messages checked by these tests are the French ones.
AppDomain.CurrentDomain.SetData("TCG.Language", "fr");

try
{
    if (args.Length == 2 && args[0] == "inventory")
        Console.WriteLine(SaveInventory.Describe(args[1]));
    else if (args.Length == 2 && args[0] == "prepare-session")
        Console.WriteLine(new CampaignWorkspace(args[1]).PrepareLaunch());
    else if (args.Length == 2 && args[0] == "recover-session")
        Console.WriteLine(new CampaignWorkspace(args[1]).RecoverInterrupted());
    else if (args.Length == 4 && args[0] == "mod-audit")
        ModAudit.Export(args[1], int.Parse(args[2]), args[3]);
    else if (args.Length == 4 && args[0] == "mod-restore-test")
        ModAudit.Restore(args[1], int.Parse(args[2]), args[3]);
    else if (args.Length == 2 && args[0] == "mod-restore-self-test")
        ModAudit.SelfTest(args[1]);
    else if (args.Length == 2 && args[0] == "campaign-test")
        CampaignTest(args[1]);
    else if (args.Length == 2 && args[0] == "history-test")
        HistoryTest(args[1]);
    else if (args.Length == 2 && args[0] == "companion-test")
        CompanionTest.Run(args[1]);
    else if (args.Length == 2 && args[0] == "furniture-test")
        FurnitureTest.Run(args[1]);
    else if (args.Length == 2 && args[0] == "required-mods-test")
        RequiredModsTest.Run(args[1]);
    else if (args.Length == 2 && args[0] == "import-test")
        ImportTest.Run(args[1]);
    else if (args.Length == 2 && args[0] == "polling-test")
        PollingTest.Run(args[1]);
    else if (args.Length == 2 && args[0] == "profile-test")
        ProfileTest.Run(args[1]);
    else if (args.Length == 2 && args[0] == "mod-files-test")
        ModFilesTest.Run(args[1]);
    else if (args.Length == 2 && args[0] == "native-import-test")
        NativeImportTest.Run(args[1]);
    else if (args.Length == 2 && args[0] == "slots-test")
        SlotsTest.Run(args[1]);
    else if (args.Length == 2 && args[0] == "self-test")
        SelfTest(args[1]);
    else
    {
        Console.WriteLine("inventory <dossier sauvegardes> | self-test <nouveau dossier laboratoire>");
        return 2;
    }
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

static void SelfTest(string root)
{
    root = Path.GetFullPath(root);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, ".tcg-save-lab"), "Synthetic tests only");
    var source = Path.Combine(root, "fixture");
    Directory.CreateDirectory(source);
    var json = Path.Combine(source, "savedGames_Release0.json");
    File.WriteAllText(json, "{\"test\":1}");
    File.WriteAllText(Path.Combine(source, "savedGames_Release0.gd"), "synthetic binary placeholder");
    var store = new LabStore(root);
    var first = store.Create("Boutique A");
    var second = store.Create("Boutique B");
    var originalHash = LabStore.Hash(json);
    var manual = store.Capture(first.Id, "manual", source, 0);
    Require(LabStore.Hash(json) == originalHash, "source inchangée après copie");
    File.WriteAllText(json, "{\"test\":2}");
    var auto = store.Capture(first.Id, "auto", source, 0);
    Require(LabStore.Hash(Path.Combine(manual, Path.GetFileName(json))) == originalHash, "manuel conservé après auto");
    Require(LabStore.Hash(Path.Combine(auto, Path.GetFileName(json))) == LabStore.Hash(json), "auto distinct");
    var other = store.Capture(second.Id, "manual", source, 0);
    Require(other != manual && first.Id != second.Id, "parties indépendantes");
    Require(new LabStore(root).List().Length == 2, "identifiants et noms persistants");
    try { store.Capture("../escape", "auto", source, 0); throw new Exception("Identifiant dangereux accepté."); }
    catch (ArgumentException) { Console.WriteLine("OK : identifiant invalide refusé"); }
    var missing = Path.Combine(root, "missing");
    Directory.CreateDirectory(missing);
    try { store.Capture(first.Id, "auto", missing, 0); throw new Exception("Source vide acceptée."); }
    catch (InvalidOperationException) { Console.WriteLine("OK : source vide refusée"); }
    Console.WriteLine("Tous les tests ont réussi. Fichiers synthétiques conservés : " + root);
}

static void Require(bool condition, string label)
{
    if (!condition) throw new Exception("ÉCHEC : " + label);
    Console.WriteLine("OK : " + label);
}

static void HistoryTest(string root)
{
    root = Path.GetFullPath(root);
    if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic history test");
    var saves = Path.Combine(root, "saves");
    Directory.CreateDirectory(saves);
    File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
    var auto = Path.Combine(saves, "savedGames_Release0.json");
    var manual = Path.Combine(saves, "savedGames_Release1.json");
    var binary = Path.Combine(saves, "savedGames_Release1.gd");
    File.WriteAllText(auto, "independent-auto");
    File.WriteAllText(manual, "manual-1"); File.WriteAllText(binary, "binary-1");
    var workspace = new CampaignWorkspace(saves);
    workspace.PrepareLaunch();
    var id = workspace.Active!.CampaignId;
    var sessionPath = Path.Combine(root, "active-session.xml");
    var sessionTime = File.GetLastWriteTimeUtc(sessionPath);
    Require(!workspace.ArchiveCurrent() && File.GetLastWriteTimeUtc(sessionPath) == sessionTime,
        "sauvegardes inchangées : aucune réécriture de session ni nouvelle archive");
    var oldest = Path.GetFileName(workspace.Latest(id, "manual")!);
    for (var version = 2; version <= 3; version++)
    {
        File.WriteAllText(manual, "manual-" + version); File.WriteAllText(binary, "binary-" + version);
        workspace.ArchiveCurrent(true);
    }
    Require(workspace.Snapshots(id, "manual").Length == 3, "trois versions manuelles distinctes conservées");
    var other = workspace.Create("Autre boutique");
    try { workspace.Request(other.Id, "manual", oldest); throw new Exception("Historique mélangé."); }
    catch (InvalidOperationException) { Console.WriteLine("OK : version d'une autre boutique refusée"); }
    try { workspace.Request(id, "manual", "../escape"); throw new Exception("Chemin accepté."); }
    catch (ArgumentException) { Console.WriteLine("OK : chemin arbitraire refusé"); }
    workspace.Request(id, "manual", oldest);
    Require(new CampaignWorkspace(saves).Pending!.SnapshotId == oldest, "choix exact conservé dans la requête XML");
    // A newer manual arrives between the selection and the transactional preparation.
    File.WriteAllText(manual, "manual-4"); File.WriteAllText(binary, "binary-4");
    workspace.PrepareLaunch();
    Require(File.ReadAllText(auto) == "manual-1" && File.ReadAllText(manual) == "manual-1" && File.ReadAllText(binary) == "binary-1", "ancienne version chargée exactement dans les slots de travail");
    workspace.ArchiveCurrent();
    Require(workspace.Snapshots(id, "manual").Length == 4 && File.ReadAllText(Path.Combine(workspace.Latest(id, "manual")!, "savedGames_Release1.json")) == "manual-4", "chargement ancien ne remplace ni ne réarchive le dernier manuel");
    Require(File.ReadAllText(Path.Combine(workspace.Latest(id, "auto")!, "savedGames_Release0.json")) == "independent-auto", "historique manuel indépendant de l'auto");
    File.WriteAllText(manual, "resumed-from-old"); File.WriteAllText(binary, "resumed-binary");
    workspace.ArchiveCurrent(true);
    Require(workspace.Snapshots(id, "manual").Length == 5, "nouvelle sauvegarde après retour ancien ajoute une version");
    workspace.Request(id, "manual"); workspace.PrepareLaunch();
    Require(File.ReadAllText(auto) == "resumed-from-old", "requête sans identifiant charge encore la plus récente");
    Console.WriteLine("Tous les tests d'historique ont réussi : " + root);
}

static void CampaignTest(string root)
{
    root = Path.GetFullPath(root);
    if (Directory.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic campaign test");
    var saves = Path.Combine(root, "saves");
    Directory.CreateDirectory(saves);
    File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
    var auto = Path.Combine(saves, "savedGames_Release0.json");
    var manual = Path.Combine(saves, "savedGames_Release1.json");
    File.WriteAllText(auto, "A-auto");
    File.WriteAllText(manual, "A-manual");
    File.WriteAllText(Path.Combine(saves, "savedGames_Release1.gd"), "A-manual-binary");
    File.WriteAllText(Path.Combine(saves, "savedGames_Release2.json"), "native-auto-copy");
    var workspace = new CampaignWorkspace(saves);
    workspace.PrepareLaunch();
    var a = workspace.Active!.CampaignId;
    Require(File.ReadAllText(Path.Combine(workspace.Latest(a, "manual")!, "savedGames_Release1.json")) == "A-manual", "copie auto native exclue du manuel");
    var b = workspace.Create("Boutique B");
    try { workspace.Create(" boutique b "); throw new Exception("Nom dupliqué accepté."); }
    catch (ArgumentException) { Console.WriteLine("OK : nom dupliqué refusé"); }
    var aManualBeforeRename = workspace.Latest(a, "manual");
    var autoBeforeForcedManual = workspace.Latest(a, "auto");
    workspace.ArchiveCurrent(true);
    Require(workspace.Latest(a, "manual") != aManualBeforeRename && workspace.LastManualCapture != null && workspace.Latest(a, "auto") == autoBeforeForcedManual, "confirmation manuelle renouvelée sans changer auto");
    aManualBeforeRename = workspace.Latest(a, "manual");
    workspace.Rename(a, "Boutique A renommée");
    Require(workspace.List().Single(c => c.Id == a).Name == "Boutique A renommée" && workspace.Latest(a, "manual") == aManualBeforeRename, "renommage conserve identifiant et sauvegardes");
    workspace.Request(b.Id, "new");
    workspace.PrepareLaunch();
    Require(!File.Exists(auto) && !File.Exists(manual) && !File.Exists(Path.Combine(saves, "savedGames_Release2.json")), "nouvelle boutique avec slots vides");
    Require(Directory.GetDirectories(Path.Combine(root, "recovery")).Length == 1, "copie de restauration avant remplacement");
    File.WriteAllText(auto, "B-auto");
    File.WriteAllText(manual, "B-manual");
    File.WriteAllText(Path.Combine(saves, "savedGames_Release1.gd"), "B-manual-binary");
    workspace.ArchiveCurrent();
    workspace.Request(a, "manual");
    workspace.PrepareLaunch();
    Require(File.ReadAllText(auto) == "A-manual", "chargement manuel via slot de continuation");
    Require(File.ReadAllText(manual) == "A-manual", "manuel également disponible au slot 1");
    workspace.ArchiveCurrent();
    Require(File.ReadAllText(Path.Combine(workspace.Latest(a, "auto")!, "savedGames_Release0.json")) == "A-auto", "chargement manuel ne remplace pas archive auto");
    File.WriteAllText(auto, "A-auto-after-playing");
    workspace.ArchiveCurrent();
    Require(File.ReadAllText(Path.Combine(workspace.Latest(a, "manual")!, "savedGames_Release1.json")) == "A-manual", "nouvelle auto conserve manuel");
    workspace.Request(b.Id, "auto");
    workspace.PrepareLaunch();
    Require(File.ReadAllText(auto) == "B-auto" && File.ReadAllText(manual) == "B-manual", "retour boutique B sans mélange");
    workspace.Request(a, "auto");
    workspace.PrepareLaunch();
    Require(File.ReadAllText(auto) == "A-auto-after-playing", "retour boutique A avec progression conservée");
    workspace.Request(b.Id, "manual");
    workspace.CancelRequest();
    Require(!workspace.HasRequest, "annulation du changement");
    var bAuto = workspace.Latest(b.Id, "auto")!;
    var snapshotMain = Path.Combine(bAuto, "savedGames_Release0.json");
    File.Move(snapshotMain, snapshotMain + ".missing");
    workspace.Request(b.Id, "auto");
    var previousAuto = File.ReadAllText(auto);
    try { workspace.PrepareLaunch(); throw new Exception("Instantané incomplet accepté."); }
    catch (InvalidOperationException) { Require(File.ReadAllText(auto) == previousAuto && workspace.Active!.CampaignId == a, "instantané incomplet refusé sans modifier la partie"); }
    workspace.CancelRequest();
    File.WriteAllText(Path.Combine(root, "restore-in-progress.txt"), "test interruption");
    try { workspace.PrepareLaunch(); throw new Exception("Interruption ignorée."); }
    catch (InvalidOperationException) { Console.WriteLine("OK : remplacement interrompu bloque le lancement"); }
    try { workspace.RecoverInterrupted(); throw new Exception("Journal invalide accepté."); }
    catch (InvalidOperationException) { Require(File.ReadAllText(auto) == previousAuto, "journal de récupération invalide refusé sans toucher aux fichiers"); }
    var transaction = Directory.GetDirectories(Path.Combine(root, "recovery")).OrderBy(p => p, StringComparer.Ordinal).First();
    File.WriteAllText(Path.Combine(root, "restore-in-progress.txt"), transaction);
    File.WriteAllText(auto, "partially-written-test");
    workspace.RecoverInterrupted();
    Require(File.ReadAllText(auto) == "A-auto" && workspace.Active!.CampaignId == a, "restauration des fichiers et de la boutique précédente");
    Require(Directory.GetDirectories(transaction, "interrupted-*").Any(d => File.ReadAllText(Path.Combine(d, "savedGames_Release0.json")) == "partially-written-test"), "fichiers interrompus conservés avant restauration");
    Require(!File.Exists(Path.Combine(root, "restore-in-progress.txt")), "blocage levé après restauration complète");
    File.WriteAllText(Path.Combine(root, "restore-in-progress.txt"), transaction);
    File.WriteAllText(Path.Combine(transaction, "before", "savedGames_Release0.json"), "damaged-backup");
    try { workspace.RecoverInterrupted(); throw new Exception("Copie altérée acceptée."); }
    catch (IOException) { Require(File.ReadAllText(auto) == "A-auto", "copie de récupération altérée refusée sans modifier la partie"); }
    Console.WriteLine("Tous les tests de boutiques ont réussi : " + root);
}
