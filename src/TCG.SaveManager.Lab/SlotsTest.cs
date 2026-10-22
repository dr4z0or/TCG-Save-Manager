using System;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;

// Save Manager 1.1: three overwritable manual slots per shop, automatic save + backups, save details.
static class SlotsTest
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("ÉCHEC : " + label);
        Console.WriteLine("OK : " + label);
    }
    static string Game(int day, int level, double money, string name) =>
        "{\"m_PlayerName\":\"" + name + "\",\"m_CurrentDay\":" + day + ",\"m_ShopLevel\":" + level + ",\"m_CoinAmountDouble\":" + money.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"m_CoinAmount\":1.0}";
    static int stamp;
    // Writes a manual save as the game does, then archives it like the in-game "Sauvegarder" button.
    static string SaveTo(CampaignWorkspace workspace, string saves, string id, int slot, string name, int day)
    {
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.json"), Game(day, 3, day * 10, "Boutique"));
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.gd"), "binary-" + day);
        foreach (var file in new[] { "savedGames_Release1.json", "savedGames_Release1.gd" }) File.SetLastWriteTimeUtc(Path.Combine(saves, file), DateTime.UtcNow.AddMinutes(++stamp));
        workspace.ArchiveCurrent(true);
        var captured = workspace.LastManualCapture ?? throw new Exception("Sauvegarde non archivée.");
        workspace.AssignSlot(id, captured, slot, name);
        return captured;
    }

    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic slots test");
        var saves = Path.Combine(root, "saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
        var auto = Path.Combine(saves, "savedGames_Release0.json");
        File.WriteAllText(auto, Game(0, 0, 100, "Auto"));
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.json"), Game(4, 2, 1234.5, "Ma \\\"boutique\\\""));
        File.WriteAllText(Path.Combine(saves, "savedGames_Release1.gd"), "binary-legacy");
        var workspace = new CampaignWorkspace(saves);
        workspace.PrepareLaunch();
        var id = workspace.Active!.CampaignId;

        var legacy = workspace.Describe(id, "manual")!;
        Check(legacy.Day == 5 && legacy.Level == 3 && legacy.HasMoney && Math.Abs(legacy.Money - 1234.5) < 0.001 && legacy.ShopName == "Ma \"boutique\"", "jour, niveau, argent et nom lus dans la sauvegarde");
        var parsed = new SnapshotInfo();
        LabStore.ReadGameValues("{\"m_CoinAmount\":42.25}", parsed);
        Check(parsed.Day == -1 && parsed.Level == -1 && parsed.HasMoney && parsed.Money == 42.25, "ancien format : argent simple accepté, valeurs absentes signalées");

        var slots = workspace.Slots(id);
        Check(slots.Length == 3 && slots[0].Current?.Id == legacy.Id && slots[0].Name == "Emplacement 1" && slots[1].Current == null && slots[2].Current == null, "ancienne sauvegarde manuelle placée dans l'emplacement 1, les autres vides");

        var start = SaveTo(workspace, saves, id, 2, "  Début\nde partie  ", 2);
        slots = workspace.Slots(id);
        Check(slots[1].Current?.Id == start && slots[1].Name == "Début de partie" && slots[1].Previous == null && slots[1].Current!.Day == 3, "sauvegarde dans un emplacement vide, nom nettoyé, détails lus");
        Check(slots[0].Current?.Id == legacy.Id, "les autres emplacements ne changent pas");

        var second = SaveTo(workspace, saves, id, 2, "Avant Shop C", 10);
        slots = workspace.Slots(id);
        Check(slots[1].Current?.Id == second && slots[1].Previous?.Id == start && slots[1].Name == "Avant Shop C", "écrasement : nouvelle version, l'ancienne gardée en secours");
        var third = SaveTo(workspace, saves, id, 2, "", 12);
        slots = workspace.Slots(id);
        Check(slots[1].Current?.Id == third && slots[1].Previous?.Id == second && slots[1].Name == "Emplacement 2" && !workspace.Snapshots(id, "manual").Any(s => Path.GetFileName(s) == start),
            "deuxième écrasement : un seul secours, la plus ancienne supprimée, nom vide remplacé");

        var same = SaveTo(workspace, saves, id, 3, "Copie", 12);
        Check(workspace.Slots(id)[2].Current?.Id == same && same != third, "même contenu dans un autre emplacement : copie distincte");

        workspace.RenameSlot(id, 3, "Copie renommée");
        Check(workspace.Slots(id)[2].Name == "Copie renommée", "emplacement renommé");
        try { workspace.RenameSlot(id, 3, ""); } catch { throw new Exception("Nom vide refusé."); }
        Check(workspace.Slots(id)[2].Name == "Emplacement 3", "nom vide : nom par défaut");
        try { workspace.AssignSlot(id, third, 4, "x"); throw new Exception("Emplacement 4 accepté."); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("OK : emplacement hors limites refusé"); }

        // Loading the backup of a slot.
        workspace.Request(id, "manual", second);
        workspace.PrepareLaunch();
        Check(File.ReadAllText(auto).Contains("\"m_CurrentDay\":10"), "version de secours d'un emplacement chargée exactement");

        // Automatic saves: latest + one backup by default, older ones deleted.
        Check(workspace.AutoKeep == SaveSettingsDefaults.AutoKeep && SaveSettingsDefaults.AutoKeep == 2, "réglage par défaut : dernière auto + 1 secours");
        for (var day = 20; day <= 24; day++)
        {
            File.WriteAllText(auto, Game(day, day, 100 + day, "Auto"));
            File.SetLastWriteTimeUtc(auto, DateTime.UtcNow.AddMinutes(++stamp));
            workspace.ArchiveCurrent();
        }
        var autos = workspace.Snapshots(id, "auto");
        Check(autos.Length == 2 && File.ReadAllText(Path.Combine(workspace.Latest(id, "auto")!, "savedGames_Release0.json")).Contains("\"m_CurrentDay\":24"), "auto : 2 versions gardées, les plus anciennes supprimées");
        var newest = workspace.Newest(id)!;
        Check(newest.Kind == "auto" && newest.Day == 25, "Continuer vise la sauvegarde la plus récente");
        var older = Path.GetFileName(autos[1]);
        workspace.Request(id, "auto", older);
        workspace.PrepareLaunch();
        Check(File.ReadAllText(auto).Contains("\"m_CurrentDay\":23"), "secours de l'auto chargeable");
        try { workspace.Request(id, "auto", "../x"); throw new Exception("Chemin accepté."); }
        catch (ArgumentException) { Console.WriteLine("OK : chemin arbitraire refusé"); }
        workspace.AutoKeep = 0;
        Check(workspace.AutoKeep == SaveSettingsDefaults.MinimumAutoKeep && workspace.PruneAutos(id) == 1 && workspace.Snapshots(id, "auto").Length == 1, "limite bornée au minimum et appliquée");

        // Shops: the active one is protected, others are deleted for good.
        var other = workspace.Create("Boutique secondaire");
        try { workspace.DeleteCampaign(id); throw new Exception("Boutique active supprimée."); }
        catch (InvalidOperationException) { Console.WriteLine("OK : boutique active protégée"); }
        workspace.DeleteCampaign(other.Id);
        Check(workspace.List().All(c => c.Id != other.Id) && !Directory.Exists(Path.Combine(root, "campaigns", other.Id)), "boutique supprimée définitivement");

        // A damaged index never blocks the shop; slots are rebuilt from the saves.
        File.WriteAllText(Path.Combine(root, "campaigns", id, "snapshots.xml"), "<pas du xml");
        slots = workspace.Slots(id);
        Check(slots.Count(s => s.Current != null) == 3 && workspace.Snapshots(id, "manual").Length == 4, "index illisible : sauvegardes conservées et replacées dans les emplacements");
        Console.WriteLine("Tous les tests d'emplacements ont réussi : " + root);
    }
}
