using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

// 1.4.4 : after a game update, Furniture and Staff fingerprints computed at save time no longer
// match a fingerprint computed with the new game's classes. The shop must still be archived with
// its companion files, and the game start must never be blocked by it.
internal static class GameUpdateTest
{
    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic update");
        var saves = Path.Combine(root, "saves"); Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
        var recorded = new string('a', 64);
        var newGame = new string('b', 64);
        Func<string, string> gameHash = p => newGame;
        const string body = "StaffOverhaul 3|seed=12\nindex=0\tjob1=1\n";
        string bodyHash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            bodyHash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(body))).Replace("-", "").ToLowerInvariant();
        Put("savedGames_Release1.json", "{\"m_CurrentDay\":40}");
        Put("savedGames_Release1.gd", new string('x', 2048));
        Put("StaffOverhaulSnapshots/" + recorded + ".txt", "StaffOverhaulSnapshot 1|game=" + recorded + "|body=" + bodyHash + "\n" + body);
        foreach (var folder in new[] { "FurnitureOverhaulCheckout", "FurnitureOverhaulClaw" })
        {
            var linked = "{\"mainHash\":\"" + recorded + "\"}";
            Put(folder + "/slot1.json", linked); Put(folder + "/" + recorded + ".json", linked);
        }
        Check(SlotFiles.SavedHash(saves, 1) == null, "même version du jeu : aucune empreinte de remplacement");
        // The game is updated after this save was made.
        SlotFiles.GameBuildUtc = DateTime.UtcNow.AddSeconds(30);
        Check(SlotFiles.SavedHash(saves, 1) == recorded, "empreinte enregistrée par les compagnons retrouvée");
        // Game start with the current shop: archived normally.
        var workspace = new CampaignWorkspace(saves, gameHash);
        workspace.PrepareLaunch();
        Check(workspace.Latest(workspace.Active!.CampaignId, "manual") != null, "démarrage : boutique active archivée");

        var store = new LabStore(Path.Combine(root, "campaigns"));
        var shop = store.Create("Après mise à jour");
        var snapshot = store.Capture(shop.Id, "manual", saves, 1, gameHash);
        Check(File.Exists(Path.Combine(snapshot, "StaffOverhaulSnapshots", recorded + ".txt")) &&
            File.Exists(Path.Combine(snapshot, "FurnitureOverhaulCheckout", recorded + ".json")) &&
            File.Exists(Path.Combine(snapshot, "FurnitureOverhaulClaw", recorded + ".json")), "boutique archivée avec Staff et Furniture malgré la nouvelle empreinte du jeu");

        // A companion left by an older save (written long before) is still refused.
        var old = DateTime.UtcNow.AddMinutes(-10);
        foreach (var folder in new[] { "FurnitureOverhaulCheckout", "FurnitureOverhaulClaw" }) File.SetLastWriteTimeUtc(Path.Combine(saves, folder, "slot1.json"), old);
        File.SetLastWriteTimeUtc(Path.Combine(saves, "StaffOverhaulSnapshots", recorded + ".txt"), old);
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.json"), "{\"m_CurrentDay\":41}");
        Check(SlotFiles.SavedHash(saves, 1) == null, "compagnon d'une ancienne sauvegarde non retenu");
        Reject(() => store.Capture(shop.Id, "manual", saves, 1, gameHash), "compagnon Furniture étranger toujours refusé");

        // Game start: an archive that cannot be made keeps the current shop and cancels the switch.
        var other = workspace.Create("Autre boutique");
        workspace.Request(other.Id, "new");
        var before = File.ReadAllText(Path.Combine(saves, "savedGames_Release1.json"));
        var message = workspace.PrepareStartup();
        Check(!workspace.HasRequest && File.ReadAllText(Path.Combine(saves, "savedGames_Release1.json")) == before, "démarrage : boutique active conservée, changement annulé : " + message);
        Reject(() => workspace.PrepareLaunch(), "changement depuis le menu toujours refusé tant que l'archivage échoue");
        SlotFiles.GameBuildUtc = null;
        Console.WriteLine("Tous les tests de mise à jour du jeu ont réussi : " + root);
        void Put(string name, string data)
        {
            var path = Path.Combine(saves, name.Replace('/', Path.DirectorySeparatorChar));
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
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is ArgumentException) { Console.WriteLine("OK : " + label + " (" + ex.Message + ")"); return; }
        throw new Exception("ÉCHEC : " + label);
    }
}
