using System;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private bool weekFiveSixBusy,improvGiftBusy;
        private ReferralProgress ReadReferrals()
        { return ReferralProgress.Restore(ModHelper.GetModData(Id,ReferralProgress.SaveKey,""),WorkRunIdentity,StoreStation.Instance.dayCounter); }
        private void SaveReferrals(ReferralProgress p)
        {
            string data=p.Data;ModHelper.SetModData(Id,ReferralProgress.SaveKey,data);
            if (ModHelper.GetModData(Id,ReferralProgress.SaveKey,"")!=data) throw new InvalidOperationException("Referral record was not saved");
        }
        private void EnableWeekFiveSix()
        {
            ModHook.OnGameLoadedLate+=TickWeekFiveSix;ModHook.OnWakingUpLate+=TickWeekFiveSix;
            ModHook.OnShutterOpenedLate+=TickWeekFiveSix;ModHook.OnReturningStoreLate+=TickWeekFiveSix;
            ModHook.OnGenerateCustomerVeryLate+=QueueReferralVisitor;
        }
        private void DisableWeekFiveSix()
        {
            ModHook.OnGameLoadedLate-=TickWeekFiveSix;ModHook.OnWakingUpLate-=TickWeekFiveSix;
            ModHook.OnShutterOpenedLate-=TickWeekFiveSix;ModHook.OnReturningStoreLate-=TickWeekFiveSix;
            ModHook.OnGenerateCustomerVeryLate-=QueueReferralVisitor;
            if (PlayerStore.HasInstance) PlayerStore.Instance.storeClientManager?.clientStack.RemoveAll(c => c?.identifier?.StartsWith(ReferralProgress.Prefix)==true);
        }
        private void TickWeekFiveSix()
        {
            if (!enabled || !TavernMapEnabled || !PlayerStore.HasInstance || StoreStation.Instance==null || weekFiveSixBusy) return;
            weekFiveSixBusy=true;
            try {
                var supply=ReadSupply();if (supply.RecordPreview(StoreStation.Instance.dayCounter)) SaveSupply(supply);
                if (TavernTrading) {
                    var p=ReadReferrals();if (p.Draw(StoreStation.Instance.dayCounter)) SaveReferrals(p);
                    if (PlayerStore.Instance.storeState==PlayerStore.StoreState.OPEN) QueueReferralVisitor(PlayerStore.Instance.storeClientManager);
                }
            } finally { weekFiveSixBusy=false; }
        }
        private void PrepareLuxuryStock()
        {
            if (!TavernMapEnabled || !PlayerStore.HasInstance || !TavernTrading) return;
            int day=StoreStation.Instance.dayCounter;
            if (day<LuxuryConfig.UnlockDay) return;
            var support=ReadSupport();if (support.Advance(day)) SaveSupport(support);
            var supply=ReadSupply();bool changed=supply.RecordPreview(day);
            if (supply.ProduceLuxury(day,support.LuxuryChannelAvailable(day),false)>0) changed=true;
            if (changed) SaveSupply(supply);
        }
        private void QueueReferralVisitor(StoreClientManager manager)
        {
            if (!enabled || !TavernMapEnabled || !PlayerStore.HasInstance || StoreStation.Instance==null || manager?.clientStack==null || TavernStoryFinished) return;
            int day=StoreStation.Instance.dayCounter;
            var p=ReadReferrals();if (TavernTrading && p.Draw(day)) SaveReferrals(p);
            var due=p.Due(day);
            // A loaded queue may still contain yesterday's generated blueprint.
            // Withdraw only our stale entries; the saved event remains pending.
            manager.clientStack.RemoveAll(c => c?.identifier?.StartsWith(ReferralProgress.Prefix)==true && (due==null || c.identifier!=due.Id));
            if (due==null) return;
            if (PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()?.identifier?.StartsWith(ReferralProgress.Prefix)==true) return;
            var queued=manager.clientStack.FirstOrDefault(c => c?.identifier?.StartsWith(ReferralProgress.Prefix)==true);
            if (queued!=null) { CustomerQueuePolicy.MoveToTail(manager.clientStack,queued);return; }
            // Native factories own preferences, faction, negotiation and valuation.
            var c=due.Upper?StoreClientList.CreateUpperLowerVisitorLuxury():StoreClientList.CreateCluelessTouristBuyer();
            if (!due.ProfileReady) { due.Budget=c.GetBudget();due.Sprite=c.spriteName;due.ProfileReady=true;SaveReferrals(p); }
            else { c.SetBudget(due.Budget);c.spriteName=due.Sprite; }
            c.identifier=due.Id;
            var introduction=new Dialogue().SetText(c.displayName,Text(due.Upper?"xm_w06_referral_upper":"xm_w06_referral_tourist"));
            introduction.nextDialogue=c.mainDialogue;c.mainDialogue=introduction;
            introduction.startAction+=() => {
                if (!enabled || !TavernMapEnabled || StoreStation.Instance==null) return;
                var state=ReadReferrals();if (state.Arrive(c.identifier,StoreStation.Instance.dayCounter)) SaveReferrals(state);
            };
            ModHelper.SetClientSource(c,Id);manager.AddClient(c);
        }
        private ImprovGiftProgress ReadImprovGift()
        { return ImprovGiftProgress.Restore(ModHelper.GetModData(Id,ImprovGiftProgress.SaveKey,"")); }
        private void SaveImprovGift(ImprovGiftProgress p)
        {
            string data=p.Data;ModHelper.SetModData(Id,ImprovGiftProgress.SaveKey,data);
            if (ModHelper.GetModData(Id,ImprovGiftProgress.SaveKey,"")!=data) throw new InvalidOperationException("Improvised gift record was not saved");
        }
        private void TryDeliverImprovGift()
        {
            if (!enabled || improvGiftBusy || !PlayerStore.HasInstance || EmporiumEntry.Instance==null || ModHelper.IsOutsideStore()) return;
            var p=ReadImprovGift();if (p.Blocked) return;
            var visit=ReadStory().Visits.FirstOrDefault(v => v.Index==5);
            if (visit==null) return;
            improvGiftBusy=true;
            try {
                if (p.VisitDay<0) { p.VisitDay=visit.Day;SaveImprovGift(p); }
                if (p.Phase=="placing") { p.Recover(EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i.uniqueId==p.ItemId));SaveImprovGift(p); }
                if (p.Phase!="waiting") return;
                var gift=ModHelper.SpawnItem(LuxuryDrinkCatalog.Improv.ItemId);if (gift==null) return;
                gift.ModifyTag("XIAOMAN_BATCH_SOURCE",tag => tag.Enable().SetString(p.Source));
                if (!p.Prepare(gift.GetUniqueID())) { gift.Destroy();return; }
                SaveImprovGift(p);
                bool placed=TryPlaceGift(gift);p.Placed(placed);SaveImprovGift(p);
                if (placed && ReputationUIManager.Instance!=null) StoreUIManager.Instance.Notify(Text("ui_w05_gift"));
            } catch (Exception error) { Log.Warning("Improvised gift remains pending: "+error.Message); }
            finally { improvGiftBusy=false; }
        }
        private void OpenLuxuryMenu()
        {
            if (!TavernMayAct || StoreStation.Instance.dayCounter<LuxuryConfig.UnlockDay) return;
            PrepareLuxuryStock();var p=ReadSupply();
            if (p.LuxuryIntroSeen) { ShowTavernMenu("luxury");return; }
            CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_05_new_01","xm_tavern_05_new_02","xm_tavern_05_new_03"},() => {
                var state=ReadSupply();state.LuxuryIntroSeen=true;SaveSupply(state);ShowTavernMenu("luxury");
            });
        }
    }
}
