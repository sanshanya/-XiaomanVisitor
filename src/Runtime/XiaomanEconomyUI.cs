using System;
using System.Linq;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private void CloseEconomyWindow()
        {
            var closing=economyWindow;
            // Destroying native inputs can emit onEndEdit. Invalidate ownership
            // before closing so those old callbacks cannot write another page/save.
            economyWindow=null;
            if (CustomUIManager.Instance!=null) CustomUIManager.Instance.CloseWindow(EconomyWindowId);
            if (closing!=null && AtTavern) tavernStage?.AttachPanel(tavernPanel?.Rect);
        }
        internal static bool MayUseEconomyWindow(CustomUIWindow owner,CustomUIWindow current,string openedRun,string currentRun,int openedDay,int currentDay)
        {
            return owner!=null && ReferenceEquals(owner,current) && !string.IsNullOrEmpty(openedRun)
                && openedRun==currentRun && openedDay==currentDay;
        }
        private bool EconomyWindowCanAct(CustomUIWindow owner,string run,int day)
        {
            return enabled && EconomyLedgerVisible && EconomyAvailable && StoreStation.Instance!=null
                && !TavernDialogueBusy && !economyBusy && (!ModHelper.IsOutsideStore() || AtTavern)
                && MayUseEconomyWindow(owner,economyWindow,run,WorkRunIdentity,day,StoreStation.Instance.dayCounter) && owner.IsAlive;
        }
        private void ShowEconomyWindow(string page="ledger")
        {
            if (!enabled || !EconomyLedgerVisible || !EconomyAvailable || CustomUIManager.Instance==null || TavernDialogueBusy
                || ModHelper.IsOutsideStore() && !AtTavern) return;
            TickEconomy();CloseEconomyWindow();
            if (AtTavern) CloseTavernMenu();
            var b=CustomUIManager.Window(EconomyWindowId,Text("ui_eco_title"),"meta");economyWindow=b.Handle;
            var opened=b.Handle;int day=StoreStation.Instance.dayCounter;string run=WorkRunIdentity;
            Func<bool> current=() => EconomyWindowCanAct(opened,run,day);
            b.SetSize(620,690).SetDraggable(true);
            b.OnClose(() => {
                if (!ReferenceEquals(economyWindow,opened)) return;
                economyWindow=null;if (AtTavern) tavernStage?.AttachPanel(tavernPanel?.Rect);
            });
            b.BeginRow(6);
            foreach (string tab in new[]{"ledger","contracts","projects","loan"}) {
                string captured=tab;b.AddButton(Text("ui_eco_"+tab),() => { if (current()) ShowEconomyWindow(captured); });
            }
            b.End();
            var p=ReadEconomy();
            b.AddLabel(Text(p.Blocked?"ui_eco_blocked":"ui_eco_phase_"+p.Phase));
            b.BeginScroll(500);
            Action<string,Action> button=(key,action) => b.AddButton(Text(key),() => {
                if (!current()) return;
                action();
            });
            Action<Func<TavernEconomyProgress,bool>> change=action => {
                if (!current()) return;
                var state=ReadEconomy();if (action(state)) { SaveEconomy(state);TavernNotice("ui_eco_saved"); }
                else TavernNotice("ui_eco_unavailable");
                ShowEconomyWindow(page);
            };
            if (p.Blocked) b.AddLabel(Text("ui_eco_blocked"));
            else if (p.Phase=="migration") {
                b.AddLabel(Text("ui_eco_migration"));b.AddLabel(Text("ui_eco_lease"));
                button("ui_eco_accept_lease",() => change(s => s.AcceptMigration(day)));
            }
            else if (page=="loan") {
                b.AddLabel(Text("ui_eco_lease"));
                b.AddLabel(Text("ui_eco_close_credit_terms"));
                if (p.Trading && p.FullRentFromPeriod<0 && p.RefinanceDay<0 && p.OperatingWeeks<7)
                    button("ui_eco_close_credit",() => change(s => s.StopRentAdvances()));
                if (p.FullRentFromPeriod>0) b.AddLabel(Text("ui_eco_full_rent",p.FullRentFromPeriod));
            }
            else if (page=="ledger" || page=="history") {
                b.AddLabel(Text("ui_eco_balance",p.Cash,TavernEconomyConfig.Reserve,p.Principal,p.Fees,p.Bills));
                b.AddLabel(Text("ui_eco_dates",p.StartDay,p.OperatingWeeks,p.NextSettlement));
                var unsettled=p.Receipts.Where(r => r.Day>=p.NextSettlement-7 && r.Day<p.NextSettlement).ToArray();
                if (unsettled.Length>0) b.AddLabel(Text("ui_eco_unsettled",unsettled.Sum(r => r.Revenue),unsettled.Sum(r => r.Cost)));
                foreach (var w in p.Weeks.Skip(page=="history"?0:Math.Max(0,p.Weeks.Count-3))) {
                    b.AddLabel(Text("ui_eco_week",w.Number,w.EndDay,w.Revenue,w.Material,w.Fixed,w.Rent,w.Interest+w.OneOffFee,w.RentAdvance,w.Repaid));
                    if (w.ActiveDays<7) b.AddLabel(Text("ui_eco_partial_week",w.ActiveDays));
                    if (w.FixedOrders>0) b.AddLabel(Text("ui_eco_deliveries",w.EndDay,w.FixedOrders,w.EndDay));
                }
                if (p.Weeks.Count==0) b.AddLabel(Text("ui_eco_empty"));
                foreach (var paid in p.PaymentHistory.Skip(Math.Max(0,p.PaymentHistory.Count-5)))
                    b.AddLabel(Text("ui_eco_repayment_receipt",paid.Day,paid.Amount));
                if (page=="ledger" && p.Weeks.Count>3) button("ui_eco_history",() => ShowEconomyWindow("history"));
                if (p.HasStayed && !p.GoodEndingSeen && AtTavern) button("ui_eco_good_event",PlayStayEnding);
                if (p.Debt==0 && p.HasStayed) b.AddLabel(Text("ui_eco_future"));
                if ((!p.Departing || p.GuaranteeDue>0) && p.Active) button("ui_eco_repay",() => ShowEconomyWindow("repay"));
                if (p.GuaranteeDue>0) b.AddLabel(Text("ui_eco_guarantee_due",p.GuaranteeDue));
                if (p.Trading && p.RefinanceDay>=0 && !p.Guaranteed && p.Principal>0 && PlayerStore.Instance.IsPropertyPaid && StoryRules.IsMonday(day))
                    button("ui_eco_guarantee",() => ShowEconomyWindow("guarantee"));
            }
            else if (page=="projects") {
                b.AddLabel(Text("ui_eco_project_status",p.Level,p.Regulars));
                foreach (var project in p.Projects) b.AddLabel(Text("ui_eco_project_"+project.Level)+" · "+project.Day);
                if (AtTavern && p.MayProject(day) && !ReadWork().WorkedOn(day) && !workRunning)
                    button("ui_eco_project_"+(p.Level+1),StartEconomyProject);
                else b.AddLabel(Text(AtTavern?"ui_eco_project_wait":"ui_eco_night_only"));
            }
            else if (page=="contracts") {
                if (p.Trading && p.RefinanceDay<0 && p.ReviewUnlocked) {
                    b.AddLabel(Text("ui_eco_review_available"));
                    if (p.ReviewRequestedDay<0) button("ui_eco_request_review",() => change(s => s.Acknowledge(day)));
                    else b.AddLabel(Text("ui_eco_review_scheduled",p.NextSettlement));
                }
                if (p.Reviewing) {
                    b.AddLabel(Text("ui_eco_review"));
                    b.AddLabel(Text("ui_eco_balance",p.Cash,TavernEconomyConfig.Reserve,p.Principal,p.Fees,p.Bills));
                    b.AddLabel(Text(p.Phase=="remedy"?"ui_eco_remedy_deadline":"ui_eco_deadline",p.NoticeDay,p.HandlingDays.Count));
                    if (p.NoticeDay<0) button("ui_eco_ack",() => change(s => s.Acknowledge(day)));
                }
                if (!p.ReviewUnlocked && p.RefinanceDay<0) b.AddLabel(Text("ui_eco_contracts_wait"));
                if (p.ReviewUnlocked || p.RefinanceDay>=0) b.AddLabel(Text("ui_eco_orders_terms"));
                if (p.ReviewUnlocked && !p.Contracts.Any(c => c.Kind=="orders") && p.RefinanceDay<0 && !p.Departing)
                    button("ui_eco_sign_orders",() => change(s => s.SignContract("orders",day)));
                if (p.ReviewUnlocked || p.RefinanceDay>=0) b.AddLabel(Text("ui_eco_sublet_terms"));
                if (p.ReviewUnlocked && !p.Contracts.Any(c => c.Kind=="sublet") && p.RefinanceDay<0 && !p.Departing)
                    button("ui_eco_sign_sublet",() => change(s => s.SignContract("sublet",day)));
                foreach (var contract in p.Contracts)
                    b.AddLabel(Text("ui_eco_contract_status",Text(contract.Kind=="orders"?"ui_eco_orders_name":"ui_eco_sublet_name"),contract.SignedDay,contract.EffectiveDay));
                if (p.PaperDay==day) b.AddLabel(Text("ui_eco_one_document"));
                if (p.ReviewUnlocked && p.RefinanceDay<0 && !p.Departing && p.Active) {
                    long minimum=TavernEconomyProgress.Minimum(p.RefinanceQuote,(int)TavernEconomyProgress.Up(p.RefinanceQuote,TavernEconomyConfig.NewTerm),TavernEconomyConfig.NewRateBp);
                    b.AddLabel(Text("ui_eco_refinance_quote",p.RefinanceQuote,minimum));
                    if (p.Reviewing && p.NoticeDay>=0 && p.ContractReady("orders",day) && p.ContractReady("sublet",day)) {
                        button("ui_eco_refinance",() => change(s => s.Refinance(day,false,false)));
                        if (PlayerStore.Instance.IsPropertyPaid) button("ui_eco_guarantee",() => ShowEconomyWindow("guarantee"));
                    }
                    b.AddLabel(Text("ui_eco_extension",p.FrozenOperatingRepaid,p.FrozenExtensions-p.ExtensionsUsed,p.FrozenExtensions));
                    if (p.Reviewing && p.NoticeDay>=0 && p.ExtensionsUsed<p.FrozenExtensions)
                        b.AddButton(Text("ui_eco_extend",TavernEconomyConfig.ExtensionFees[p.ExtensionsUsed]),() => change(s => s.Extend(day)));
                }
                else if (p.RefinanceDay>=0 && !p.HasStayed) b.AddLabel(Text("ui_eco_restructured"));
                if (p.Phase=="remedy") {
                    b.AddLabel(Text("ui_eco_remedy"));button("ui_eco_repay",() => ShowEconomyWindow("repay"));
                    button("ui_eco_resume",() => change(s => s.ResumeAfterRemedy(day)));
                }
                if (p.Reviewing && p.NoticeDay>=0) button("ui_eco_leave",() => ShowEconomyWindow("leave"));
            }
            else if (page=="guarantee") {
                b.AddLabel(Text("ui_eco_guarantee_terms"));
                button("ui_eco_confirm_guarantee",() => change(s => s.RefinanceDay<0
                    ? s.Refinance(day,true,PlayerStore.Instance.IsPropertyPaid) : s.UpgradeGuarantee(day,PlayerStore.Instance.IsPropertyPaid)));
            }
            else if (page=="repay") {
                b.AddLabel(Text("ui_eco_repay_terms"));
                b.AddLabel(Text("ui_eco_balance",p.Cash,TavernEconomyConfig.Reserve,p.Principal,p.Fees,p.Bills));
                b.AddLabel(Text("ui_eco_player_reserve",p.PlayerReserve,PlayerStore.Instance.GetCash()));
                b.AddInput(Text("ui_eco_keep_cash"),p.PlayerReserve.ToString(),value => {
                    if (!current()) return;
                    int keep;if (int.TryParse(value,out keep) && keep>=0) { var state=ReadEconomy();state.PlayerReserve=keep;SaveEconomy(state); }
                });
                b.AddInput(Text("ui_eco_repay"),"100",value => { if (!current()) return;int amount;if (int.TryParse(value,out amount) && amount>0) economyConfirmAmount=amount; });
                economyConfirmAmount=100;
                button("ui_eco_confirm",() => {
                    var state=ReadEconomy();int available=Math.Max(0,PlayerStore.Instance.GetCash()-state.PlayerReserve);
                    economyConfirmAmount=(int)Math.Min(state.Debt,Math.Min(economyConfirmAmount,available));
                    if (economyConfirmAmount>0) ShowEconomyWindow("repay_confirm");
                });
            }
            else if (page=="repay_confirm") {
                int amount=economyConfirmAmount;b.AddLabel(Text("ui_eco_repay_confirm",amount));
                bool submitted=false;
                button("ui_eco_confirm",() => { if (submitted) return;submitted=true;MakeEconomyPayment(amount); });
                button("ui_eco_back",() => ShowEconomyWindow("repay"));
            }
            else if (page=="leave") {
                b.AddLabel(Text("ui_eco_leave_terms"));
                button("ui_eco_confirm",() => change(s => s.Leave(day)));
                button("ui_eco_back",() => ShowEconomyWindow("contracts"));
            }
            else if (page=="ending") {
                economyWindow.SetTitle(Text("ui_eco_good_title"));
                b.AddHeader(Text("ui_eco_good_title"));b.AddLabel(Text("ui_eco_good_epilogue"));
            }
            b.End();button("ui_eco_close",CloseEconomyWindow);b.Show();
            if (AtTavern) tavernStage?.AttachPanel(economyWindow.Rect);
        }
    }
}
