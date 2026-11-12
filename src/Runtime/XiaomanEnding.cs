using System;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private const string RokaGiftKey="w12_roka_gift_v1",RokaGiftSource="private_roka_visit_12";
        private bool rokaGiftBusy;
        private bool TavernTrading {
            get {
                if (SideStories.BuybackStory.LineClosed) return false;
                if (EconomyAvailable) {
                    var p=ReadEconomy();
                    if (p.Blocked) return false;
                    if (p.Active) return p.Trading && StoreStation.Instance.dayCounter>=Math.Max(p.StartDay,p.ResumeDay);
                }
                return StoryRules.BusinessOpen(ReadStory().VisitIndex);
            }
        }
        private ImprovGiftProgress ReadRokaGift()
        { return ImprovGiftProgress.Restore(ModHelper.GetModData(Id,RokaGiftKey,""),RokaGiftSource); }
        private void SaveRokaGift(ImprovGiftProgress p)
        {
            string data=p.Data;ModHelper.SetModData(Id,RokaGiftKey,data);
            if (ModHelper.GetModData(Id,RokaGiftKey,"")!=data) throw new InvalidOperationException("Unique drink receipt was not saved");
        }
        private void TryDeliverRokaGift(bool atTavern=false)
        {
            if (!enabled || rokaGiftBusy || !PlayerStore.HasInstance || EmporiumEntry.Instance==null || StoreStation.Instance==null) return;
            if (atTavern ? !AtTavern : ModHelper.IsOutsideStore()) return;
            var visit=ReadStory().Visits.FirstOrDefault(v => v.Index==12);
            if (visit==null || !atTavern && StoreStation.Instance.dayCounter<=visit.Day) return;
            var p=ReadRokaGift();if (p.Blocked) return;
            rokaGiftBusy=true;
            try {
                if (p.VisitDay<0) { p.VisitDay=visit.Day;SaveRokaGift(p); }
                if (p.Phase=="placing") {
                    bool found=EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i.uniqueId==p.ItemId)
                        || atTavern && CarriedSupplyItems().Any(i => i.uniqueId==p.ItemId);
                    p.Recover(found);SaveRokaGift(p);
                }
                if (p.Phase!="waiting") return;
                var gift=ModHelper.SpawnItem(LuxuryDrinkCatalog.Roka.ItemId);if (gift==null) return;
                gift.ModifyTag("XIAOMAN_BATCH_SOURCE",t => t.Enable().SetString(RokaGiftSource));
                if (!p.Prepare(gift.GetUniqueID())) { gift.Destroy();return; }
                SaveRokaGift(p); // Both collection channels share this exact physical identity.
                bool placed;
                if (atTavern) {
                    ModHelper.SetItemOwnerShip(gift,true);
                    GraphUtils.TryAcceptAll(EmporiumEntry.Instance.afterhourInventory,gift);
                    placed=gift.parentInventory!=null;
                    if (!placed) gift.Destroy();
                } else placed=TryPlaceGift(gift);
                p.Placed(placed);SaveRokaGift(p);
                if (placed) TavernNotice("ui_w12_gift");
            }
            catch (Exception e) { Log.Warning("Dew Blossom gift remains pending: "+e.Message); }
            finally { rokaGiftBusy=false; }
        }
        private void OfferRoka()
        {
            if (!TavernMayAct || TavernDialogueBusy || ReadStory().VisitIndex!=12) return;
            CloseTavernMenu();
            QueueTavernSpeech(TavernDialogue.ChatSequence("xm_tavern_12_roka_01"),() => TryDeliverRokaGift(true));
        }
    }
}
