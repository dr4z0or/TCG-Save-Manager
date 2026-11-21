using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

// Save Manager 1.4.8: "Continuer" on the shop already in the game's slots replaces nothing when the
// slots hold exactly its latest saves; any difference still goes through the full, recoverable switch.
static class ReloadTest
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("ÉCHEC : " + label);
        Console.WriteLine("OK : " + label);
    }
    static string Game(int day) => "{\"m_PlayerName\":\"Boutique\",\"m_CurrentDay\":" + day + ",\"m_ShopLevel\":3,\"m_CoinAmountDouble\":10.0,\"m_CoinAmount\":1.0}";
    static int Transactions(string root) => Directory.Exists(Path.Combine(root, "recovery")) ? Directory.GetDirectories(Path.Combine(root, "recovery")).Length : 0;
    static string Stamps(string saves) => string.Join("\n", Directory.GetFiles(saves, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)
        .Select(p => p + "|" + File.GetLastWriteTimeUtc(p).Ticks + "|" + LabStore.Hash(p)));

    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic reload test");
        var saves = Path.Combine(root, "saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
        var main = Path.Combine(saves, "savedGames_Release0.json");
        File.WriteAllText(main, Game(10));
        File.WriteAllText(Path.Combine(saves, "savedGames_Release0.gd"), "binary-10");
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.json"), Game(8));
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.gd"), "binary-8");
        File.WriteAllText(Path.Combine(saves, "ShopLotC_Save0.json"), "{\"shopc\":10}");
        // A Staff companion left by an earlier save: named after a fingerprint, part of no snapshot.
        Directory.CreateDirectory(Path.Combine(saves, "StaffOverhaulSnapshots"));
        var orphan = Path.Combine(saves, "StaffOverhaulSnapshots", new string('a', 64) + ".txt");
        File.WriteAllText(orphan, "old staff snapshot");
        var workspace = new CampaignWorkspace(saves);
        workspace.PrepareLaunch();
        var id = workspace.Active!.CampaignId;
        Check(workspace.Latest(id, "auto") != null && workspace.Latest(id, "manual") != null, "boutique archivée (auto et manuelle)");

        // Same shop, latest saves already in the slots: nothing replaced, no recovery transaction.
        var before = Stamps(saves);
        var transactions = Transactions(root);
        workspace.Request(id, "auto");
        var message = workspace.PrepareLaunch();
        Check(message == "Boutique préparée : choisir Continuer (slot auto) dans le jeu.", "même message qu'un changement complet");
        Check(Transactions(root) == transactions, "même boutique déjà en place : aucun échange de fichiers");
        Check(Stamps(saves) == before, "fichiers du jeu intacts (dates et contenus)");
        Check(File.Exists(orphan), "compagnon Staff orphelin conservé");
        Check(!workspace.HasRequest && workspace.Active!.CampaignId == id && workspace.Active!.LoadedKind == "auto", "demande traitée, session à jour");
        Check(!workspace.ArchiveCurrent() && workspace.Snapshots(id, "auto").Length == 1, "empreintes de session cohérentes : rien à réarchiver");

        // An older auto save in the slots ("Sauvegardes…" → older version): Continuer must restore the latest.
        var older = Path.GetFileName(workspace.Latest(id, "auto")!);
        File.WriteAllText(main, Game(11));
        File.WriteAllText(Path.Combine(saves, "savedGames_Release0.gd"), "binary-11");
        Check(workspace.ArchiveCurrent(), "nouvelle sauvegarde automatique archivée");
        workspace.Request(id, "auto", older);
        workspace.PrepareLaunch();
        Check(File.ReadAllText(main) == Game(10) && Transactions(root) == transactions + 1, "ancienne version chargée par un échange complet");
        workspace.Request(id, "auto");
        workspace.PrepareLaunch();
        Check(File.ReadAllText(main) == Game(11) && Transactions(root) == transactions + 2, "Continuer après une ancienne version : la plus récente est remise par un échange complet");

        // A file differing from the snapshot without a new archive (the .gd is not part of the archive
        // fingerprint) is never trusted: the full switch puts the snapshot back.
        File.WriteAllText(Path.Combine(saves, "savedGames_Release0.gd"), "binary-tampered");
        workspace.Request(id, "auto");
        workspace.PrepareLaunch();
        Check(Transactions(root) == transactions + 3 && File.ReadAllText(Path.Combine(saves, "savedGames_Release0.gd")) == "binary-11", "fichier différent de l'archive : échange complet");

        // Another shop is always a full switch.
        var other = workspace.Create("Autre boutique");
        var source = Path.Combine(root, "other");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "savedGames_Release0.json"), Game(1));
        File.WriteAllText(Path.Combine(source, "savedGames_Release0.gd"), "binary-1");
        new LabStore(Path.Combine(root, "campaigns")).Capture(other.Id, "auto", source, 0);
        workspace.Request(other.Id, "auto");
        workspace.PrepareLaunch();
        Check(workspace.Active!.CampaignId == other.Id && File.ReadAllText(main) == Game(1) && !File.Exists(Path.Combine(saves, "savedGames_Release1.json")), "autre boutique : échange complet");
        workspace.Request(id, "auto");
        workspace.PrepareLaunch();
        Check(workspace.Active!.CampaignId == id && File.ReadAllText(main) == Game(11), "retour à la première boutique intact");
    }
}
