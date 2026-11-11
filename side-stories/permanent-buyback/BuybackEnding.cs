using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace XiaomanVisitor.SideStories
{
    public sealed partial class BuybackTestMod
    {
        internal const string PhotoNoteBook = "last_note";
        internal const string PhotoNoteItem = Id + ".last_note";
        internal const string BuybackEndingId = Id + ".ending.buyback";
        private Texture2D notePhotoTexture;
        private Sprite notePhoto;
        private bool deliveringPhotoNote, endingSubmitted;

        private void TryDeliverPhotoNote()
        {
            if (!enabled || deliveringPhotoNote || !PlayerStore.HasInstance || StoreStation.Instance == null || EmporiumEntry.Instance == null) return;
            deliveringPhotoNote = true;
            try
            {
                var progress = ReadDoorProgress();
                if (!progress.NeedsNote(StoreStation.Instance.dayCounter)) return;
                bool owned = EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i != null && i.identifier == PhotoNoteItem);
                if (!owned)
                {
                    var item = ModHelper.SpawnPaperItem(Id, PhotoNoteBook);
                    if (item == null) return;
                    ModHelper.SetItemOwnerShip(item, true);
                    GraphUtils.TryAcceptAllMid(EmporiumEntry.Instance.frontInvinvElement, item);
                    if (item.parentInventory == null) foreach (var book in EmporiumEntry.Instance.GetAllOwnedItems()) {
                        if (book.identifier != NotebookRules.ItemId || book.contentWindow == null) continue;
                        var inventory = GraphUtils.FindAllChildrenType<GameGridInventory>(book.contentWindow).FirstOrDefault();
                        if (inventory == null) continue;
                        GraphUtils.TryAcceptAll(inventory, item);
                        if (item.parentInventory != null) break;
                    }
                    if (item.parentInventory == null) GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement, item);
                    if (item.parentInventory == null) GraphUtils.TryAcceptAll(EmporiumEntry.Instance.backInvinvElement, item);
                    if (item.parentInventory == null) { item.Destroy(); log.Warning("Photo note waits for free inventory space."); return; }
                }
                progress.noteDelivered = true; SaveDoorProgress(progress);
                log.Log("Delivered the refusal ending's photo note once.");
            }
            catch (Exception error) { log.Warning("Photo note delivery remains pending: " + error); }
            finally { deliveringPhotoNote = false; }
        }

        private void OnPhotoNoteOpened(string id, PaperUI paper)
        {
            if (id != PaperBookRegistry.FullId(Id, PhotoNoteBook) || paper == null) return;
            try
            {
                if (notePhoto == null)
                {
                    var assets = BuybackStage.ParseData<BuybackAssets>(ModHelper.ReadModFileText(Id, "Scenes/assets.json"));
                    var image = assets.images.Single(i => i.file == RefusalText().note_photo);
                    byte[] bytes = ModHelper.ReadModFileBytes(Id, "Scenes/" + image.file);
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        if (BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != image.sha256)
                            throw new InvalidOperationException("Ending photo differs from approved CG05");
                    notePhotoTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(notePhotoTexture, bytes, true)) throw new InvalidOperationException("Ending photo could not load");
                    notePhotoTexture.filterMode = FilterMode.Bilinear;
                    notePhotoTexture.wrapMode = TextureWrapMode.Clamp;
                    notePhoto = Sprite.Create(notePhotoTexture, new Rect(0, 0, notePhotoTexture.width, notePhotoTexture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                }
                var photoElement = paper.Data.AllElements().Single(e => e.id == "photo");
                var binding = paper.GetBinding(photoElement);
                if (binding == null || binding.image == null) throw new InvalidOperationException("Paper photo binding is missing");
                binding.image.sprite = notePhoto; binding.image.color = Color.white; binding.image.preserveAspect = true;
                binding.image.enabled = true; binding.image.gameObject.SetActive(true);
            }
            catch (Exception error) { log.Warning("Ending photo unavailable: " + error); }
        }

        private void ClosePhotoNote()
        {
            var entry = PaperBookRegistry.Find(Id, PhotoNoteBook);
            if (entry != null && entry.open != null) entry.open.Close();
            if (notePhoto != null) UnityEngine.Object.Destroy(notePhoto);
            if (notePhotoTexture != null) UnityEngine.Object.Destroy(notePhotoTexture);
            notePhoto = null; notePhotoTexture = null;
        }

        internal void CompleteEnding()
        {
            if (!enabled || endingSubmitted || !PlayerStore.HasInstance || StoreStation.Instance == null || GameOverUIManager.Instance == null) return;
            endingSubmitted = true;
            stage?.LeaveForEnding(); stage = null;
            nightMusic.Restore(true); ClosePhotoNote();
            string id = BuybackEndingId;
            string title = "永久回购";
            string description = "晚安，前辈。\n……明天也要在一起哦。";
            // Native run accounting + native GameOver/RunSummary UI, with a mod-specific ending ID.
            var store = PlayerStore.Instance;
            RunStats.OnRunEnded(StoreStation.Instance.dayCounter, store.GetTotalEstimatedValue(), store.playerCash);
            GameOverUIManager.Instance.SetEnding(id);
            GameOverUIManager.Instance.DisplayUI(title, description);
            if (RunSummaryUI.CanOpen()) GameOverUIManager.Instance.OnMenuButtonClick();
        }
    }

    [HarmonyPatch(typeof(RunSummaryUI), "EndingTitle")]
    public static class BuybackEndingTitlePatch
    {
        public static bool Prefix(string endingId, ref string __result)
        {
            if (endingId == BuybackTestMod.BuybackEndingId) { __result = "永久回购"; return false; }
            return true;
        }
    }
}
