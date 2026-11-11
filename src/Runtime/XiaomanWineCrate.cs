using System;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private bool deliveringWineCrate;
        private WineCrateProgress ReadWineCrate()
        { return WineCrateProgress.Restore(ModHelper.GetModData(Id,WineCrateRules.SaveKey,"")); }
        private void SaveWineCrate(WineCrateProgress p)
        {
            string raw=p.Data;ModHelper.SetModData(Id,WineCrateRules.SaveKey,raw);
            if (ModHelper.GetModData(Id,WineCrateRules.SaveKey,"")!=raw) throw new InvalidOperationException("Wine crate receipt was not saved");
        }
        private GameItem CreateWineCrate()
        {
            var window=DirectoryUtils.CreateGridWindow(WineCrateRules.Columns,WineCrateRules.Rows);
            var box=ItemDirectory.CreateEmptyItem(WineCrateRules.ItemId).SetContentWindow(window.Item1);
            ConfigureWineCrate(box);box.onLoaded+=ConfigureWineCrate;ModHelper.SetSource(box,Id,ModName);return box;
        }
        private static GameGridInventory WineCrateInventory(GameItem box)
        { return box?.identifier==WineCrateRules.ItemId && box.contentWindow!=null ? GraphUtils.FindAllChildrenType<GameGridInventory>(box.contentWindow).FirstOrDefault() : null; }
        private void ConfigureWineCrate(GameItem box)
        {
            box.name=Text("ui_wine_crate_name");box.shortDescription=Text("ui_wine_crate_desc");box.flavorText="";box.unitValue=0;
            box.SetSpriteAndShapeFromMod(Id,WineCrateRules.SpriteKey);
            box.SetShape(new GridShapeBuilder().SetData(Enumerable.Repeat((byte)1,WineCrateRules.Footprint*WineCrateRules.Footprint).ToArray(),WineCrateRules.Footprint));
            box.SetGameItemType("STORAGE");ContainerHelper.InitContainerItemTagOnly(box);box.EnableTag(ContainerHelper.NESTABLE_DEVICE_TAG);
            var inventory=WineCrateInventory(box);
            if (inventory!=null) inventory.mayInventoryAddItemFunc=(item,target) => item!=null && target!=null
                && WineCrateRules.CanStore(item.identifier,GeneralHelper.IsItemOwned(item),GeneralHelper.IsItemOwned(target.GetParentItem()),item.IsTag(ContainerHelper.CONTAINER_TAG));
        }
        private void RequestWineCrate(GameItem wine)
        {
            if (!WineCrateRules.IsBottledWine(wine?.identifier) || wine.parentInventory==null || !GeneralHelper.IsItemOwned(wine)) return;
            try { var p=ReadWineCrate();if (p.RequestForDelivery(wine.identifier,true)) SaveWineCrate(p);TryDeliverWineCrate(); }
            catch (Exception e) { Log.Warning("Wine crate remains pending: "+e.Message); }
        }
        private void TryDeliverWineCrate()
        {
            if (!enabled || deliveringWineCrate || !PlayerStore.HasInstance || EmporiumEntry.Instance==null || ModHelper.IsOutsideStore()) return;
            deliveringWineCrate=true;
            try {
                var p=ReadWineCrate();if (!p.Requested || p.Phase=="complete" || p.Phase=="uncertain") return;
                var owned=EmporiumEntry.Instance.GetAllOwnedItems();
                if (p.Phase=="placing") { p.Recover(owned.Any(i => i.uniqueId==p.ItemId));SaveWineCrate(p); }
                if (p.Phase!="waiting") return;
                if (owned.Any(i => i.identifier==WineCrateRules.ItemId)) { p.Prepare(owned.First(i => i.identifier==WineCrateRules.ItemId).GetUniqueID());p.Placed(true);SaveWineCrate(p);return; }
                var item=ModHelper.SpawnItem(WineCrateRules.ItemId);if (item==null) return;
                if (!p.Prepare(item.GetUniqueID())) { item.Destroy();return; }SaveWineCrate(p);
                p.Placed(TryPlaceGift(item));SaveWineCrate(p);
            } catch (Exception e) { Log.Warning("Wine crate remains pending: "+e.Message); }
            finally { deliveringWineCrate=false; }
        }
        private bool TryStoreWineGift(GameItem wine)
        {
            if (!WineCrateRules.IsWine(wine?.identifier)) return false;
            foreach (var box in EmporiumEntry.Instance.GetAllOwnedItems()) {
                var inventory=WineCrateInventory(box);if (inventory==null) continue;
                GraphUtils.TryAcceptAll(inventory,wine);if (wine.parentInventory==inventory) return true;
            }
            return false;
        }
    }
}
