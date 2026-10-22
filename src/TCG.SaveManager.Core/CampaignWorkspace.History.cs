using System;
using System.Linq;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Core
{
    public sealed partial class CampaignWorkspace
    {
        // Folder name of the manual snapshot created by the last ArchiveCurrent call, if any.
        public string? LastManualCapture { get; private set; }

        public SnapshotInfo[] History(string id, bool scan = true) => store.History(id, scan);
        public SnapshotInfo? Describe(string id, string kind, bool scan = true)
        {
            var latest = Latest(id, kind);
            return latest == null ? null : store.Describe(id, kind, latest, scan);
        }
        public SaveSlot[] Slots(string id, bool scan = true) => store.Slots(id, scan);
        // The save "Continuer" resumes: the most recently written one, automatic or in a slot.
        public SnapshotInfo? Newest(string id, bool scan = true)
        {
            var candidates = Slots(id, scan).Select(s => s.Current).Concat(new[] { Describe(id, "auto", scan) }).Where(s => s != null).Select(s => s!).ToArray();
            return candidates.OrderByDescending(s => s.SavedUtc).FirstOrDefault();
        }
        public void AssignSlot(string id, string snapshotId, int slot, string name) => store.AssignSlot(id, snapshotId, slot, name);
        public void RenameSlot(string id, int slot, string name) => store.RenameSlot(id, slot, name);
        public void DeleteCampaign(string id)
        {
            if (Active?.CampaignId == id) throw new InvalidOperationException(T("La boutique active ne peut pas être supprimée. Chargez d'abord une autre boutique."));
            if (HasRequest && Pending?.CampaignId == id) throw new InvalidOperationException(T("Cette boutique est en cours de chargement."));
            store.DeleteCampaign(id);
        }
        public long CampaignSize(string id) => store.CampaignSize(id);
        public int AutoKeep
        {
            get => store.Settings.AutoKeep;
            set { var settings = store.Settings; settings.AutoKeep = value; store.WriteSettings(settings); }
        }
        // Applies the automatic-save limit to one shop.
        public int PruneAutos(string id)
        {
            try { return store.Prune(id, "auto", AutoKeep); }
            // Housekeeping only: a locked folder must never fail an archive or a load.
            catch (System.IO.IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }
        public int PruneAllAutos() => List().Sum(c => PruneAutos(c.Id));
    }
}
