using System;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private const string EconomyPhoneId=Id+".economy.phone", EconomyWindowId=Id+".economy.window";
        private const string EconomyLedgerConfigKey="show_economy_ledger";
        private const bool EconomyLedgerDefault=false;
        private ConfigEntry<bool> showEconomyLedger;
        internal bool EconomyLedgerVisible { get { return TavernMapEnabled && showEconomyLedger!=null && showEconomyLedger.Value; } }
        private TavernEconomyProgress economyCache;
        private string economyCacheData;
        private bool economyBusy;
        private CustomUIWindow economyWindow;
        private int economyConfirmAmount;
        private TavernEconomyProgress ReadEconomy()
        {
            string data=ModHelper.GetModData(Id,TavernEconomyProgress.SaveKey,"");
            if (economyCache==null || data!=economyCacheData) {
                economyCache=TavernEconomyProgress.Restore(data);economyCacheData=data;
            }
            return economyCache;
        }
        private void SaveEconomy(TavernEconomyProgress p)
        {
            string data=p.Data;ModHelper.SetModData(Id,TavernEconomyProgress.SaveKey,data);
            economyCache=p;economyCacheData=data;
        }
        private bool EconomyAvailable { get { return TavernMapEnabled && PlayerStore.HasInstance && !SideStories.BuybackStory.LineClosed; } }
        private void UpdateEconomyLedgerAccess()
        {
            if (!EconomyLedgerVisible) { CloseEconomyWindow();RemoveEconomyPhone(); }
            if (!TavernMapEnabled || !PlayerStore.HasInstance || StoreStation.Instance==null) return;
            if (AtTavern && (tavernMenuPage=="details" || tavernMenuPage=="interaction")) CloseTavernMenu();
            if (EconomyLedgerVisible) EnsureEconomyPhone(StoreStation.Instance.dayCounter);
        }
        private bool EconomyStoryApplies {
            get {
                if (!PlayerStore.HasInstance || SideStories.BuybackStory.LineClosed) return false;
                string phase=ReadEconomy().Phase;
                return phase!="new" && phase!="legacy";
            }
        }
        private void EnableEconomy()
        {
            ModHook.OnGameLoadedEarly+=CloseEconomyWindow;
            if (!TavernMapEnabled) { ModHook.OnGameLoadedLate+=RemoveEconomyPhone;RemoveEconomyPhone();return; }
            PhoneClientList.DialogFuncs[EconomyPhoneId]=EconomyCall;
            ModHook.OnGameLoadedLate+=ReloadEconomy;
            ModHook.OnWakingUpLate+=TickEconomy;
            ModHook.OnShutterOpenedLate+=OnEconomyBusinessDay;
            ModHook.OnGoingSleepEarly+=TickEconomy;
            ModHook.OnReturningStoreLate+=TickEconomy;
            UpdateEconomyLedgerAccess();
        }
        private Dialogue EconomyCall(StorePhoneClient owner)
        {
            long call=++phoneCallSequence;
            return new Dialogue().SetText(Text("ui_eco_phone"),Text("ui_eco_phone_line"))
                .SetEndAction(PhoneAction(owner,call,() => { PhoneUIManager.Instance.OnPhoneDialogEnd();if (EconomyAvailable) ShowEconomyWindow(); }));
        }
        private void DisableEconomy()
        {
            ModHook.OnGameLoadedEarly-=CloseEconomyWindow;
            ModHook.OnGameLoadedLate-=ReloadEconomy;ModHook.OnWakingUpLate-=TickEconomy;
            ModHook.OnShutterOpenedLate-=OnEconomyBusinessDay;ModHook.OnGoingSleepEarly-=TickEconomy;
            ModHook.OnReturningStoreLate-=TickEconomy;
            ModHook.OnGameLoadedLate-=RemoveEconomyPhone;
            PhoneClientList.DialogFuncs.Remove(EconomyPhoneId);
            RemoveEconomyPhone();
            CloseEconomyWindow();economyCache=null;economyCacheData=null;
        }
        private void RemoveEconomyPhone()
        {
            if (PlayerStore.HasInstance && PlayerStore.Instance.PhoneClientDict!=null)
                foreach (long key in PlayerStore.Instance.PhoneClientDict.Where(k => k.Value.dialogFuncId==EconomyPhoneId).Select(k => k.Key).ToArray())
                    PlayerStore.Instance.PhoneClientDict.Remove(key);
        }
        private void ReloadEconomy() { economyCache=null;economyCacheData=null;CloseEconomyWindow();TickEconomy(); }
        private void OnEconomyBusinessDay()
        {
            TickEconomy();
            if (!EconomyAvailable) return;
            var p=ReadEconomy();if (p.HandlingDay(StoreStation.Instance.dayCounter)) SaveEconomy(p);
        }
        private void TickEconomy()
        {
            if (!TavernMapEnabled || !PlayerStore.HasInstance || StoreStation.Instance==null || economyBusy) return;
            if (SideStories.BuybackStory.LineClosed) {
                CloseEconomyWindow();RemoveEconomyPhone();
                return;
            }
            economyBusy=true;
            try {
                int day=StoreStation.Instance.dayCounter;
                var p=ReadEconomy();
                if (p.Phase=="new") { p.Initialize(day,TavernRules.OpeningDay,ReadStory().Finished,ReadStory().VisitIndex>0);SaveEconomy(p); }
                if (p.Phase=="legacy") return;
                if (p.PendingPayment!=null) { p.RecoverPayment(day,PlayerStore.Instance.GetCash());SaveEconomy(p); }
                if (!p.Blocked) {
                    foreach (var visit in ReadStory().Visits.Where(v => v.Day<day || v.Day==day
                        && storySession!=null && storySession.Episode.Index==v.Index && storySession.OpeningFinished))
                        p.ObserveChapter(visit.Index,visit.Day);
                    if (p.PacingRecoveryPending) p.Advance(day); // Establish the resume date before importing old native receipts.
                    ImportEconomyReceipts(p);
                    p.Advance(day);SaveEconomy(p);
                }
                EnsureEconomyPhone(day);
                if (EconomyLedgerVisible && !p.Blocked && !p.Departing && (p.Reviewing || p.ReviewUnlocked && p.RefinanceDay<0) && !p.ReviewAnnounced
                    && StoreUIManager.Instance!=null && ReputationUIManager.Instance!=null) {
                    StoreUIManager.Instance.Notify(Text(p.Phase=="remedy"?"ui_eco_remedy_arrived":"ui_eco_review_arrived"));
                    p.ReviewAnnounced=true;SaveEconomy(p);
                }
                TryDeliverEconomyFarewell(p,day);
                TryDeliverLateStayLetter(p,day);
            }
            catch (Exception error) { Log.Warning("Tavern economy waits for review: "+error.Message); }
            finally { economyBusy=false; }
        }
        private void EnsureEconomyPhone(int day)
        {
            if (!EconomyLedgerVisible) { RemoveEconomyPhone();return; }
            if (day<TavernRules.OpeningDay || ReadStory().VisitIndex<1) return;
            var phones=PlayerStore.Instance.PhoneClientDict;if (phones==null || phones.Values.Any(v => v.dialogFuncId==EconomyPhoneId)) return;
            long number=83427;while (phones.ContainsKey(number)) number++;
            phones.Add(number,new StorePhoneClient { dialogFuncId=EconomyPhoneId,locID=EconomyPhoneId,
                displayName=Text("ui_eco_phone"),phoneState=StorePhoneClient.PhoneState.Regular,dialedBefore=true });
            if (StoreUIManager.Instance!=null) StoreUIManager.Instance.Notify(Text("ui_eco_open")+" · "+number);
        }
        private void ImportEconomyReceipts(TavernEconomyProgress p)
        {
            foreach (var batch in ReadBrewing().Batches) p.ImportBrewingProfit(batch);
            if (!p.Trading) return;
            // Existing systems own their physical transactions. Only completed
            // receipts are imported once; old records before the new lease stay out.
            var supply=ReadSupply();
            foreach (var r in ReadTavern().Drinks.Where(r => r.Status=="complete" && r.Day>=p.NextSettlement-7 && r.Day<p.NextSettlement)) {
                p.Record("onsite:"+r.Id,"onsite",r.Day,r.Paid,EconomyDrinkCost(r,supply));
            }
            foreach (var r in ReadWork().Shifts.Where(r => r.Status=="complete" && r.Day>=p.NextSettlement-7 && r.Day<p.NextSettlement))
                p.Record("work:"+r.Id,"overflow_orders",r.Day,r.Revenue,r.Material+r.Wage);
        }
        private int EconomyDrinkCost(TavernDrinkRecord r,SupplyProgress supply)
        {
            var use=supply.BarUses.FirstOrDefault(u => u.Id==r.Id && !u.Cancelled);
            var batch=use==null?null:supply.Batches.FirstOrDefault(b => b.Id==use.BatchId);
            if (batch!=null && batch.Produced>0) {
                int ordinal=supply.BarUses.TakeWhile(u => u.Id!=use.Id).Count(u => u.BatchId==batch.Id && u.Settled && !u.Cancelled);
                return TavernEconomyProgress.AllocatedUnitCost(batch.IngredientCost+batch.MakingCost,batch.Produced,ordinal);
            }
            var spec=CocktailCatalog.Find(r.Key);
            return TavernEconomyConfig.OrderMaterial*(spec!=null && spec.Tier==2?2:1);
        }
        private int SupplySettlementRevenue(TavernDrinkRecord r,SupplyProgress supply)
        {
            // The old supplier pool is restricted stock float. Reimburse only
            // its consumed cost; the new operating account owns the sale margin.
            return EconomyAvailable && ReadEconomy().StartDay>0 && r.Day>=ReadEconomy().StartDay
                ? EconomyDrinkCost(r,supply) : r.Paid;
        }
        internal string EconomyPickupLabel {
            get {
                if (!EconomyLedgerVisible || !EconomyAvailable) return null;
                var p=ReadEconomy();
                return p.Level>=2 || p.ContractReady("orders",StoreStation.Instance.dayCounter)
                    ? Text(TavernTrading?"ui_eco_pickup":"ui_eco_pickup_paused") : null;
            }
        }
        private StoryEpisode EconomyEpisode(int index)
        {
            var original=StoryRules.Episode(index);
            if (index<13 || !EconomyStoryApplies) return original;
            var p=ReadEconomy();if (p.Departing) return original;
            string branch=p.ChapterBranch;
            var opening=EconomyStory.Opening(branch,index).Select(k => new StoryLineSpec(k,EconomyStory.MoodFor(k,Mood.Soft))).ToList();
            opening.Add(new StoryLineSpec("xm_eco_shop_"+index,Mood.Neutral));
            return new StoryEpisode(index,original.Credits,original.Demand,original.TrackKey,original.AcceptKey,"xm_eco_bye","",opening.ToArray());
        }
        private string EconomyNote(StoryVisitRecord visit,string original)
        {
            return (visit.Index==13 || visit.Index==14) && (visit.EndingBranch=="review" || visit.EndingBranch=="stay")
                ? "eco_note_"+visit.Index+"_"+visit.EndingBranch : original;
        }
        private void StartEconomyProject()
        {
            if (!TavernMayAct || TavernDialogueBusy || !EconomyLedgerVisible || !EconomyAvailable) return;
            int day=StoreStation.Instance.dayCounter;var p=ReadEconomy();
            if (!p.MayProject(day) || ReadWork().WorkedOn(day) || workRunning) return;
            int level=p.Level+1;CloseEconomyWindow();CloseTavernMenu();
            QueueTavernSpeech(new[]{"xm_eco_project_"+level+"_offer","xm_eco_project_"+level+"_done"},() => {
                if (!TavernMayAct || StoreStation.Instance.dayCounter!=day) return;
                var state=ReadEconomy();if (state.Level+1==level && !ReadWork().WorkedOn(day) && state.FinishProject(day)) {
                    SaveEconomy(state);TavernNotice("ui_eco_saved");
                }
            });
        }
        private void PlayStayEnding()
        {
            if (!TavernMayAct || TavernDialogueBusy || !EconomyLedgerVisible || !EconomyAvailable) return;
            var p=ReadEconomy();if (!p.HasStayed || p.GoodEndingSeen) return;
            CloseEconomyWindow();CloseTavernMenu();
            var keys=new[]{"xm_eco_good_01","xm_eco_good_02","xm_eco_good_03","xm_eco_good_04","xm_eco_good_05","xm_eco_good_06"};
            if (p.Debt==0) keys=keys.Where(k => k!="xm_eco_good_03").ToArray();
            QueueTavernSpeech(keys,() => {
                var state=ReadEconomy();if (!state.HasStayed) return;
                state.GoodEndingSeen=true;SaveEconomy(state);ShowEconomyWindow("ending");
            });
        }
        private void PlayEconomyChat()
        {
            var p=ReadEconomy();
            if (p.HasStayed && p.Debt==0 && !p.DebtFreeSeen) {
                QueueTavernSpeech(new[]{"xm_eco_clear_01","xm_eco_clear_02","xm_eco_clear_03"},() => {
                    var state=ReadEconomy();state.DebtFreeSeen=true;SaveEconomy(state);TavernNotice("ui_eco_cleared_title");
                });return;
            }
            QueueTavernSpeech(new[]{"xm_eco_stable_chat_0"+(StoreStation.Instance.dayCounter%3+1)});
        }
        private void MakeEconomyPayment(int amount)
        {
            if (!enabled || !EconomyLedgerVisible || !EconomyAvailable || economyBusy || ModHelper.IsOutsideStore() && !AtTavern) return;
            economyBusy=true;
            try {
                var p=ReadEconomy();int cash=PlayerStore.Instance.GetCash();
                if (amount>Math.Max(0,cash-p.PlayerReserve)) return;
                var r=p.PreparePayment(StoreStation.Instance.dayCounter,cash,amount);if (r==null) return;
                SaveEconomy(p);PlayerStore.Instance.PayMoney(r.Amount);
                if (!p.ConfirmPayment(PlayerStore.Instance.GetCash())) { p.Blocked=true;r.Phase="uncertain"; }
                SaveEconomy(p);
            }
            catch (Exception error) { Log.Warning("Voluntary repayment held for reconciliation: "+error.Message); }
            finally { economyBusy=false; }
            ShowEconomyWindow("repay");
        }
        private void TryDeliverEconomyFarewell(TavernEconomyProgress p,int today)
        {
            var final=ReadStory().Visits.FirstOrDefault(v => v.Index==14);
            if (!p.Departing || p.DepartureDay<0 || final==null || final.EndingBranch=="" || final.EndingBranch=="leave"
                || today<=p.DepartureDay || ModHelper.IsOutsideStore() || EmporiumEntry.Instance==null) return;
            if (p.FarewellPhase=="placing") {
                p.FarewellPhase=EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i.uniqueId==p.FarewellItemId)?"complete":"uncertain";
                SaveEconomy(p);
            }
            if (p.FarewellPhase!="waiting") return;
            var item=ModHelper.SpawnPaperItem(Id,"story_note_14");if (item==null) return;
            p.FarewellItemId=item.GetUniqueID();p.FarewellPhase="placing";SaveEconomy(p);
            bool placed=TryPlaceStoryGift(item);p.FarewellPhase=placed?"complete":"waiting";SaveEconomy(p);
        }
        private void TryDeliverLateStayLetter(TavernEconomyProgress p,int today)
        {
            var final=ReadStory().Visits.FirstOrDefault(v => v.Index==14);
            if (!p.HasStayed || final==null || final.EndingBranch!="review" || today<=p.StableDay
                || ModHelper.IsOutsideStore() || EmporiumEntry.Instance==null) return;
            if (p.StayLetterPhase=="placing") {
                p.StayLetterPhase=EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i.uniqueId==p.StayLetterItemId)?"complete":"uncertain";
                SaveEconomy(p);
            }
            if (p.StayLetterPhase!="waiting") return;
            var item=ModHelper.SpawnPaperItem(Id,"eco_note_14_stay");if (item==null) return;
            p.StayLetterItemId=item.GetUniqueID();p.StayLetterPhase="placing";SaveEconomy(p);
            bool placed=TryPlaceStoryGift(item);p.StayLetterPhase=placed?"complete":"waiting";SaveEconomy(p);
        }
    }
}
