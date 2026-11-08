using System;
using System.Collections.Generic;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private const string CassetteCaseReceipt = "cassette_case_delivered";
        private const string CassetteCaseAtlas = "Items/items_sec";
        private GameInventory modifiedCassetteStorage;
        private Func<GameItem, GameInventory, bool> originalCassetteStorageFilter;
        private Func<GameItem, GameInventory, bool> cassetteStorageFilter;

        private GameItem CreateCassetteCase()
        {
            var window = DirectoryUtils.CreateGridWindow(CassetteCaseRules.Columns, CassetteCaseRules.Rows);
            GameItem box = ItemDirectory.CreateEmptyItem(CassetteCaseRules.ItemId).SetContentWindow(window.Item1);
            ConfigureCassetteCase(box);
            box.onLoaded += ConfigureCassetteCase;
            ModHelper.SetSource(box, Id, ModName);
            return box;
        }

        private static GameGridInventory CassetteCaseInventory(GameItem box)
        {
            if (box == null || box.identifier != CassetteCaseRules.ItemId || box.contentWindow == null) return null;
            return GraphUtils.FindAllChildrenType<GameGridInventory>(box.contentWindow).FirstOrDefault();
        }

        private void ConfigureCassetteCase(GameItem box)
        {
            box.name = Text("gift_cassette_case_name");
            box.shortDescription = Text("gift_cassette_case_desc");
            box.flavorText = "";
            box.unitValue = 0;
            box.SetGameItemType("STORAGE");
            ContainerHelper.InitContainerItemTagOnly(box);
            box.EnableTag(ContainerHelper.NESTABLE_DEVICE_TAG);
            // This is a physical case, never a playable cassette. The native
            // playback bay must continue rejecting it and reading real tapes.
            box.DisableTag(CassetteHelper.CASSETTE_TAG);
            box.DisableTag(CassetteHelper.CASSETTE_CONTENT);
            GameGridInventory inventory = CassetteCaseInventory(box);
            if (inventory == null) return;
            inventory.mayInventoryAddItemFunc = (tape, target) => tape != null && target != null
                && CassetteCaseRules.CanStore(tape.identifier, GeneralHelper.IsItemOwned(tape),
                    GeneralHelper.IsItemOwned(target.GetParentItem()), tape.IsTag(CassetteHelper.CASSETTE_TAG),
                    tape.IsTag(ContainerHelper.CONTAINER_TAG));
            inventory.onSlotAddItemEndFunc = RefreshCassetteCaseCover;
            inventory.onSlotRemoveItemEndFunc = RefreshCassetteCaseCover;
            RefreshCassetteCaseCover(null, inventory, default(SlotMarker));
        }

        private static void RefreshCassetteCaseCover(GameItem _, GameInventory inventory, SlotMarker marker)
        {
            GameItem box = inventory == null ? null : inventory.GetParentItem();
            if (box == null || box.identifier != CassetteCaseRules.ItemId) return;
            // Both native box sprites are 32x32: the stored case keeps a 2x2 footprint.
            box.SetSpriteAndShape(CassetteCaseAtlas, inventory.childItems.Count == 0 ? "ammo_box" : "ammo_box_packed");
        }

        private void EnsureCassetteCaseStorage()
        {
            GameItem machine = EmporiumEntry.Instance == null ? null : EmporiumEntry.Instance.cassettePlayer;
            GameInventory storage = MachineHelper.GetBodyStorage(machine, 0) as GameInventory;
            if (storage == modifiedCassetteStorage && storage != null && storage.mayInventoryAddItemFunc == cassetteStorageFilter) return;
            RestoreCassetteCaseStorage();
            if (storage == null || !enabled) return;
            Func<GameItem, GameInventory, bool> original = storage.mayInventoryAddItemFunc;
            modifiedCassetteStorage = storage;
            originalCassetteStorageFilter = original;
            cassetteStorageFilter = (item, target) => {
                if (item == null) return false;
                bool nativeAccepts = original == null || original(item, target);
                if (target != storage) return nativeAccepts;
                return CassetteCaseRules.MachineStorageAccepts(item.identifier, GeneralHelper.IsItemOwned(item), enabled, nativeAccepts);
            };
            // Only storage panel 0 is extended. The native playback panel 1,
            // playlist, track tags and player controls remain exactly native.
            storage.mayInventoryAddItemFunc = cassetteStorageFilter;
        }

        private void RestoreCassetteCaseStorage()
        {
            if (modifiedCassetteStorage != null && modifiedCassetteStorage.mayInventoryAddItemFunc == cassetteStorageFilter)
                modifiedCassetteStorage.mayInventoryAddItemFunc = originalCassetteStorageFilter;
            modifiedCassetteStorage = null;
            originalCassetteStorageFilter = null;
            cassetteStorageFilter = null;
        }

        private static List<GameItem> OwnedItemsWithCassettePlayer()
        {
            var result = new HashSet<GameItem>(EmporiumEntry.Instance.GetAllOwnedItems());
            // Native GetAllOwnedItems omits the fixed cassette player. Include
            // it explicitly so a case stored there is still found for new gifts.
            GameItem machine = EmporiumEntry.Instance.cassettePlayer;
            if (machine != null)
                foreach (GameItem item in machine.FindAllChildItems())
                    if (GeneralHelper.IsItemOwned(item)) result.Add(item);
            return result.ToList();
        }

        private void TryDeliverCassetteCase(int today, StoryProgress story)
        {
            if (ModHelper.GetModDataBool(Id, CassetteCaseReceipt)) return;
            List<GameItem> owned = OwnedItemsWithCassettePlayer();
            bool hasCase = owned.Any(item => item != null && item.identifier == CassetteCaseRules.ItemId);
            if (hasCase)
            {
                ModHelper.SetModDataBool(Id, CassetteCaseReceipt, true);
                return;
            }
            bool hasTape = owned.Any(item => item != null && CassetteCaseRules.IsCassette(item.identifier));
            StoryVisitRecord first = story.Visits.FirstOrDefault(visit => visit.Index == 1);
            // Same next-day, once-per-save utility grant as the letterbook.
            if (!NotebookRules.ShouldGrant(false, false, hasTape, today, first == null ? -1 : first.Day)) return;
            GameItem box = ModHelper.SpawnItem(CassetteCaseRules.ItemId);
            if (!TryPlaceGift(box)) return;
            ModHelper.SetModDataBool(Id, CassetteCaseReceipt, true);
            Log.Log("Delivered Xiaoman's cassette case once; owned tapes can be stored without changing playback.");
        }

        private bool TryStoreCassetteGift(GameItem tape)
        {
            if (tape == null || !CassetteCaseRules.IsCassette(tape.identifier)) return false;
            ModHelper.SetItemOwnerShip(tape, true);
            foreach (GameItem box in OwnedItemsWithCassettePlayer())
            {
                GameGridInventory inventory = CassetteCaseInventory(box);
                if (inventory == null) continue;
                GraphUtils.TryAcceptAll(inventory, tape);
                if (tape.parentInventory == inventory) return true;
            }
            return false;
        }
    }
}
