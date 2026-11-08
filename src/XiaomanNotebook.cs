using System;
using System.Collections.Generic;
using System.Linq;

namespace XiaomanVisitor
{
    public static class NotebookRules
    {
        public const string ItemId = XiaomanMod.Id + ".letter_notebook";

        public static bool IsLetter(string identifier)
        {
            string prefix = XiaomanMod.Id + ".";
            if (identifier == null || !identifier.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string ignored;
            return LetterInkRules.TryResolve(XiaomanMod.Id, identifier.Substring(prefix.Length), "en", out ignored);
        }

        public static bool CanStore(string identifier, bool letterOwned, bool notebookOwned, bool isContainer)
        { return letterOwned && notebookOwned && !isContainer && IsLetter(identifier); }

        public static bool ShouldGrant(bool alreadyDelivered, bool ownsNotebook, bool ownsLetter, int today, int firstVisitDay)
        {
            return !alreadyDelivered && !ownsNotebook && today >= 1
                && (ownsLetter || firstVisitDay >= 1 && today > firstVisitDay);
        }
    }

    public sealed partial class XiaomanMod
    {
        private const string NotebookReceipt = "letter_notebook_delivered";
        private const string NotebookEmptySprite = "xiaoman_letterbook";
        private const string NotebookFullSprite = "xiaoman_letterbook_full";

        private GameItem CreateNotebook()
        {
            // Reuse the native book window and its save/restore graph. Never
            // replace this content window in onLoaded: it owns the saved letters.
            var window = DirectoryUtils.CreateBookWindow();
            GameItem book = ItemDirectory.CreateEmptyItem(NotebookRules.ItemId).SetContentWindow(window.Item1);
            ConfigureNotebook(book);
            book.onLoaded += ConfigureNotebook;
            AudioHelper.InitDossier(book);
            ModHelper.SetSource(book, Id, ModName);
            return book;
        }

        private static GameGridInventory NotebookInventory(GameItem book)
        {
            if (book == null || book.identifier != NotebookRules.ItemId || book.contentWindow == null) return null;
            return GraphUtils.FindAllChildrenType<GameGridInventory>(book.contentWindow).FirstOrDefault();
        }

        private void ConfigureNotebook(GameItem book)
        {
            book.name = Text("gift_notebook_name");
            book.shortDescription = Text("gift_notebook_desc");
            book.flavorText = "";
            book.unitValue = 0;
            book.SetGameItemType("DOCUMENT");
            ContainerHelper.InitContainerItemTagOnly(book);
            // Native backpacks normally reject nested containers. This is the
            // native placement exception for a small dedicated item container.
            book.EnableTag(ContainerHelper.NESTABLE_DEVICE_TAG);
            GameGridInventory inventory = NotebookInventory(book);
            if (inventory == null) return;
            inventory.mayInventoryAddItemFunc = (letter, target) => letter != null && target != null
                && NotebookRules.CanStore(letter.identifier, GeneralHelper.IsItemOwned(letter),
                    GeneralHelper.IsItemOwned(target.GetParentItem()), letter.IsTag(ContainerHelper.CONTAINER_TAG));
            inventory.onSlotAddItemEndFunc = RefreshNotebookCover;
            inventory.onSlotRemoveItemEndFunc = RefreshNotebookCover;
            RefreshNotebookCover(null, inventory, default(SlotMarker));
        }

        private static void RefreshNotebookCover(GameItem _, GameInventory inventory, SlotMarker marker)
        {
            GameItem book = inventory == null ? null : inventory.GetParentItem();
            if (book == null || book.identifier != NotebookRules.ItemId) return;
            // Own palette variants preserve the native 32x48 footprint (2x3
            // cells), while the mentor's original blue notebook stays untouched.
            book.SetSpriteAndShapeFromMod(Id, inventory.childItems.Count == 0 ? NotebookEmptySprite : NotebookFullSprite);
        }

        private void TryDeliverNotebook(int today, StoryProgress story)
        {
            if (ModHelper.GetModDataBool(Id, NotebookReceipt)) return;
            List<GameItem> owned = EmporiumEntry.Instance.GetAllOwnedItems();
            bool hasBook = owned.Any(item => item != null && item.identifier == NotebookRules.ItemId);
            if (hasBook)
            {
                ModHelper.SetModDataBool(Id, NotebookReceipt, true);
                return;
            }
            bool hasLetter = owned.Any(item => item != null && NotebookRules.IsLetter(item.identifier));
            StoryVisitRecord first = story.Visits.FirstOrDefault(visit => visit.Index == 1);
            if (!NotebookRules.ShouldGrant(false, false, hasLetter, today, first == null ? -1 : first.Day)) return;
            GameItem book = ModHelper.SpawnItem(NotebookRules.ItemId);
            if (!TryPlaceGift(book)) return;
            // Only receipt a real placement; a full counter keeps delivery pending.
            ModHelper.SetModDataBool(Id, NotebookReceipt, true);
            Log.Log("Delivered Xiaoman's letterbook once; existing letters can be stored with native drag and drop.");
        }

        private bool TryPlaceStoryGift(GameItem item)
        {
            if (item == null) return false;
            if (NotebookRules.IsLetter(item.identifier))
            {
                ModHelper.SetItemOwnerShip(item, true);
                // Move only the newly spawned letter. Never sweep or rearrange
                // the player's existing letters, folders or notebook contents.
                foreach (GameItem book in EmporiumEntry.Instance.GetAllOwnedItems())
                {
                    GameGridInventory inventory = NotebookInventory(book);
                    if (inventory == null) continue;
                    GraphUtils.TryAcceptAll(inventory, item);
                    if (item.parentInventory == inventory) return true;
                }
            }
            if (TryStoreCassetteGift(item)) return true;
            // Missing/full notebook preserves the existing native delivery path.
            return TryPlaceGift(item);
        }
    }
}
