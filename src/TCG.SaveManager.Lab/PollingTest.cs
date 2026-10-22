using System;
using System.IO;
using TCG.SaveManager.Core;

internal static class PollingTest
{
    public static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Nouveau dossier requis.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic polling");
        var saves = Path.Combine(root, "saves"); Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, ".tcg-sandbox"), "test");
        var main = Path.Combine(saves, "savedGames_Release0.json");
        File.WriteAllText(main, "initial");
        File.WriteAllText(Path.Combine(saves, "savedGames_ReleaseBackupFile0.json"), "backup");
        var calls = 0;
        var workspace = new CampaignWorkspace(saves, p => { calls++; return LabStore.Hash(p).ToLowerInvariant(); });
        workspace.PrepareLaunch();
        workspace.ArchiveIfChanged();
        var before = calls;
        for (var index = 0; index < 10; index++)
            if (workspace.ArchiveIfChanged()) throw new Exception("Archive créée sans changement.");
        if (calls != before) throw new Exception("Normalisation répétée sans changement.");
        Console.WriteLine("OK : dix sondages inchangés sans normalisation Furniture");
        File.WriteAllText(main, "nouvelle sauvegarde");
        if (!workspace.ArchiveIfChanged()) throw new Exception("Sauvegarde modifiée ignorée.");
        Console.WriteLine("OK : sauvegarde modifiée archivée");
        workspace.ArchiveIfChanged(); // Session metadata has also changed.
        var companion = Path.Combine(saves, "ShopLotC_Save0.json"); File.WriteAllText(companion, "new sidecar");
        if (!workspace.ArchiveIfChanged()) throw new Exception("Compagnon ajouté ignoré.");
        workspace.ArchiveIfChanged();
        File.Delete(companion);
        if (!workspace.ArchiveIfChanged()) throw new Exception("Compagnon supprimé ignoré.");
        Console.WriteLine("OK : ajout et suppression de compagnon détectés");
        Console.WriteLine("Tous les tests de sondage ont réussi : " + root);
    }
}
