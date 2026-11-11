using System;
using System.Collections.Generic;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        // Keep the original ledger and unfinished payments; new materials use BrewingProgress.
        private SupplyProgress ReadSupply() { return SupplyProgress.Restore(ModHelper.GetModData(Id,SupplyProgress.SaveKey,"")); }
        private IEnumerable<GameItem> CarriedSupplyItems()
        {
            var e=EmporiumEntry.Instance;
            return new GameInventory[]{e.afterhourInventory,e.afterhourPocketSlotInvLeft,e.afterhourPocketSlotInvBackpack,e.afterhourPocketSlotInvRight}
                .Where(i => i!=null).SelectMany(i => GraphUtils.FindAllChildrenType<GameItem>(i)).Where(i => GeneralHelper.IsItemOwned(i)).Distinct();
        }
        private void SaveSupply(SupplyProgress p)
        {
            string raw=p.Data;ModHelper.SetModData(Id,SupplyProgress.SaveKey,raw);
            if (ModHelper.GetModData(Id,SupplyProgress.SaveKey,"")!=raw) throw new InvalidOperationException("Supply receipt could not be saved");
        }
        internal string FourthSupplySnapshot()
        {
            var visit=ReadStory().Visits.FirstOrDefault(v => v.Index==4);
            var snap=visit?.SupplySnapshot;
            return Text("ui_w04_supply_list")+"\n\n"+Text("ui_w04_snapshot",snap?.Day??visit?.Day??22)+"\n\n"
                +(snap!=null && snap.ContractSchema<2?Text("ui_w04_prices",snap.WaterPrice,snap.JuicePrice,snap.Wholesale):BrewingTerms);
        }
        private void RequestSupply()
        {
            if (!TavernMayAct || TavernDialogueBusy || StoreStation.Instance.dayCounter<22) return;
            CloseTavernMenu();var p=ReadSupply();
            if (!TavernTrading) { ShowTavernMenu("supply_ledger");return; }
            if (p.Signed || p.IntroSeen) { ShowTavernMenu("supply");return; }
            string intro=ReadStory().VisitIndex>=4?"xm_tavern_w04_supply_01":"xm_tavern_w04_supply_early";
            QueueTavernSpeech(new[]{intro,"xm_tavern_w04_supply_02","xm_tavern_w04_supply_03"},() => {
                var state=ReadSupply();state.IntroSeen=true;SaveSupply(state);ShowTavernMenu("supply");
            });
        }
        private void BuildSupplyMenu(CustomUIBuilder b,string page)
        {
            var old=ReadSupply();var brewing=ReadBrewing();int day=StoreStation.Instance.dayCounter;
            Action<string,Action> button=(label,click) => { b.AddButton(label,() => { if (TavernMayAct && !TavernDialogueBusy) click(); });b.Last.SetPreferredSize(360,38); };
            b.BeginScroll(490);
            if (brewing.Blocked || brewing.Pending!=null || old.Blocked || old.Pending!=null) b.AddWarning(Text("ui_w04_pending"));
            if (page=="support_news") {
                b.AddLabel(Text("xm_w04_news_title"));b.AddLabel(Text("xm_w04_news_body"));b.AddLabel(Text("ui_w04_phone",ReadSupport().PhoneNumber));
            } else {
                b.AddLabel(BrewingTerms);
                b.AddLabel(Text("ui_brew_balance",(brewing.CreditMilli/1000m).ToString("0.###")));
                foreach (var batch in brewing.Batches.Where(v => v.Phase=="complete").Reverse().Take(5))
                    b.AddLabel(Text("ui_brew_receipt",batch.Day,(batch.InputCents/100m).ToString("0.##"),(batch.ReturnMilli/1000m).ToString("0.###"),batch.DueDay));
                if (old.Trades.Count>0 || old.Batches.Count>0) {
                    b.AddLabel(Text("ui_brew_legacy"));
                    b.AddLabel(Text("ui_w04_balance",old.Cash,old.BoughtThisWeek(day)));
                    b.AddLabel(Text("ui_w04_stock",old.Water,old.Juice,old.Batches.Sum(v => v.PlayerRemaining),old.Batches.Sum(v => v.StoreRemaining)));
                }
                if (TavernTrading && !old.Signed && page=="supply")
                    button(Text("ui_w04_sign"),() => { var p=ReadSupply();if (p.Sign(day)) SaveSupply(p);CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_w04_supply_yes"}); });
                if (day>=SupplyConfig.ContractDay) button(Text("ui_w04_group"),() => ModHelper.OpenPaper(Id,"w04_group_contract"));
            }
            b.End();button(Text("ui_tavern_back"),() => ShowTavernMenu("interaction"));
        }
        private void RecoverSupplyBarStock()
        {
            if (!TavernMapEnabled || !PlayerStore.HasInstance) return;
            var p=ReadSupply();if (p.BarUses.Count==0 || p.Blocked) return;
            var drinks=ReadTavern();bool changed=false;
            foreach (var use in p.BarUses.Where(u => !u.Cancelled && !u.Settled)) {
                var drink=drinks.Drinks.FirstOrDefault(d => d.Id==use.Id);
                if (drink==null || drink.Status=="cancelled") { p.CancelBar(use.Id);changed=true; }
                else if (drink.Status=="complete") { p.SettleBar(use.Id,drink.Paid);changed=true; }
            }
            if (changed) SaveSupply(p);
        }
        private void RecoverSupplyTrade()
        {
            if (!TavernMapEnabled || !PlayerStore.HasInstance || EmporiumEntry.Instance==null) return;
            var p=ReadSupply();var t=p.Pending;if (t==null || p.Blocked || t.Status=="uncertain") return;
            bool owned=EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i.uniqueId==t.ItemId);
            int cash=PlayerStore.Instance.GetCash(),after=t.Direction=="in"?t.BeforeCash+t.Price:t.BeforeCash-t.Price;
            if (t.Status=="prepared" && cash==t.BeforeCash && (t.Direction=="in"?owned:!owned)) t.Status="cancelled";
            else if (cash==after && (t.Direction=="in"?!owned:owned)) { if (!p.Complete(t)) t.Status="uncertain"; }
            else t.Status="uncertain";
            SaveSupply(p);
        }
    }
}
