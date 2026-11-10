using System;
using System.Collections.Generic;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private bool supplyBusy;
        private SupplyProgress ReadSupply() { return SupplyProgress.Restore(ModHelper.GetModData(Id,SupplyProgress.SaveKey,"")); }
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
                +Text("ui_w04_prices",snap?.WaterPrice??SupplyConfig.WaterPrice,snap?.JuicePrice??SupplyConfig.JuicePrice,snap?.Wholesale??SupplyConfig.Wholesale);
        }
        private IEnumerable<GameItem> CarriedSupplyItems()
        {
            var e=EmporiumEntry.Instance;
            return new GameInventory[]{e.afterhourInventory,e.afterhourPocketSlotInvLeft,e.afterhourPocketSlotInvBackpack,e.afterhourPocketSlotInvRight}
                .Where(i => i!=null).SelectMany(i => GraphUtils.FindAllChildrenType<GameItem>(i)).Where(i => GeneralHelper.IsItemOwned(i)).Distinct();
        }
        private bool AcceptSupplyItem(GameItem item)
        {
            if (item==null || !GeneralHelper.IsItemOwned(item) || item.unitCount<1 || item.IsTag("CONTRABAND")) return false;
            return item.identifier==SupplyConfig.WaterItem ? IsSafeDrinkingWater(item) && WaterHelper.IsFull(item)
                : item.identifier==SupplyConfig.JuiceItem && item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt()>=450;
        }
        private void RequestSupply()
        {
            if (!TavernMayAct || TavernDialogueBusy || StoreStation.Instance.dayCounter<22) return;
            CloseTavernMenu();var p=ReadSupply();
            if (p.Signed) { QueueTavernSpeech(new[]{"xm_tavern_w04_supply_existing"},() => ShowTavernMenu("supply"));return; }
            if (p.IntroSeen) { ShowTavernMenu("supply");return; }
            string intro=ReadStory().VisitIndex>=4 ? "xm_tavern_w04_supply_01" : "xm_tavern_w04_supply_early";
            QueueTavernSpeech(new[]{intro,"xm_tavern_w04_supply_02","xm_tavern_w04_supply_03"},() => {
                var state=ReadSupply();state.IntroSeen=true;SaveSupply(state);ShowTavernMenu("supply");
            });
        }
        private void BuildSupplyMenu(CustomUIBuilder b,string page)
        {
            PrepareLuxuryStock();var p=ReadSupply();int day=StoreStation.Instance.dayCounter;
            Action<string,Action> button=(label,click) => { b.AddButton(label,() => { if (TavernMayAct && !TavernDialogueBusy && !supplyBusy) click(); });b.Last.SetPreferredSize(360,38); };
            b.BeginScroll(490);
            if (p.Blocked || p.Pending!=null) b.AddWarning(Text("ui_w04_pending"));
            if (page=="supply") {
                b.AddLabel(Text("ui_w04_recipe",SupplyConfig.RecipeWater,SupplyConfig.RecipeJuice,SupplyConfig.Output,SupplyConfig.MakingCost));
                b.AddLabel(Text("ui_w04_ratio",SupplyConfig.PlayerNumerator,SupplyConfig.Denominator));
                b.AddLabel(Text("ui_w04_prices",SupplyConfig.WaterPrice,SupplyConfig.JuicePrice,SupplyConfig.Wholesale));
                if (day>=LuxuryConfig.UnlockDay) b.AddLabel(Text("ui_w05_recipe",LuxuryConfig.Output,LuxuryConfig.RareCost,LuxuryConfig.MakingCost,LuxuryConfig.Wholesale,LuxuryConfig.Onsite));
                b.AddLabel(Text("ui_w04_limits",SupplyConfig.WaterLimit,SupplyConfig.JuiceLimit,SupplyConfig.WeeklyBudget));
                b.AddLabel(Text("ui_w04_test"));
                if (!p.Signed) {
                    button(Text("ui_w04_sign"),() => {
                        var state=ReadSupply();if (!state.Sign(StoreStation.Instance.dayCounter)) return;
                        SaveSupply(state);CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_w04_supply_yes"});
                    });
                    button(Text("ui_w04_later"),() => { CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_w04_supply_later"}); });
                } else {
                    button(Text("ui_w04_deliver"),() => ShowTavernMenu("supply_in"));
                    button(Text("ui_w04_buy"),() => ShowTavernMenu("supply_out"));
                    button(Text("ui_w04_ledger"),() => ShowTavernMenu("supply_ledger"));
                }
                if (day>=SupplyConfig.ContractDay) button(Text("ui_w04_group"),() => ModHelper.OpenPaper(Id,"w04_group_contract"));
                button(Text("ui_w04_news"),() => ShowTavernMenu("support_news"));
            }
            else if (page=="supply_in") {
                var goods=CarriedSupplyItems().Where(AcceptSupplyItem).Take(12).ToArray();
                if (goods.Length==0) b.AddLabel(Text("ui_w04_empty"));
                foreach (GameItem item in goods) {
                    var captured=item;int count=item.unitCount,price=p.InputPrice(item.identifier)*count;
                    button(Text("ui_w04_supply_offer",item.name,count,price),() => ConfirmSupplyInput(captured.GetUniqueID(),count,price));
                }
                b.AddLabel(Text("ui_w04_balance",p.Cash,p.BoughtThisWeek(day)));
            }
            else if (page=="supply_out") {
                var available=p.Batches.Where(v => v.PlayerRemaining>0).GroupBy(v => v.Key).ToArray();
                if (available.Length==0) b.AddLabel(Text("ui_w04_soldout"));
                foreach (var group in available) {
                    string batch=group.First().Id,key=group.Key;
                    button(Text("ui_w04_take_offer",Text("cocktail_"+key+"_name"),group.First().WholesalePrice,group.Sum(v => v.PlayerRemaining)),() => ConfirmSupplyOutput(batch));
                }
            }
            else if (page=="support_news") {
                b.AddLabel(Text("xm_w04_news_title"));b.AddLabel(Text("xm_w04_news_body"));
                b.AddLabel(Text("ui_w04_phone",ReadSupport().PhoneNumber));
            }
            else {
                b.AddLabel(Text("ui_w04_balance",p.Cash,p.BoughtThisWeek(day)));
                b.AddLabel(Text("ui_w04_stock",p.Water,p.Juice,p.Batches.Sum(v => v.PlayerRemaining),p.Batches.Sum(v => v.StoreRemaining)));
                b.AddLabel(Text("ui_w04_totals",p.Trades.Where(t => t.Status=="complete" && t.Direction=="in").Sum(t => t.Price),p.Batches.Sum(v => v.MakingCost),p.Trades.Where(t => t.Status=="complete" && t.Direction=="out").Sum(t => t.Price)));
                if (p.CompanyLots.Count>0) {
                    b.AddLabel(Text("ui_w05_company_total",p.CompanyLots.Sum(l => l.Paid)));
                    foreach (var lot in p.CompanyLots.AsEnumerable().Reverse().Take(2))
                        b.AddLabel(Text("ui_w05_lot",lot.Day,Text("cocktail_"+lot.Key+"_name"),lot.Paid,lot.WaterFromPlayer,lot.JuiceFromPlayer));
                }
                foreach (var t in p.Trades.Where(t => t.Status=="complete").Reverse().Take(7))
                    b.AddLabel(Text("ui_w04_receipt",t.Day,Text(t.Direction=="in"?"ui_w04_deliver":"ui_w04_buy"),t.Count,t.Price));
            }
            b.End();
            button(Text("ui_tavern_back"),() => ShowTavernMenu(page=="supply"?"interaction":"supply"));
        }
        private void ConfirmSupplyInput(int itemId,int count,int quote)
        {
            CloseTavernMenu();
            tavernStage.ShowChoices(new[]{new TavernChoice(Text("ui_w04_confirm")+" · "+quote+" CR",() => TradeSupplyInput(itemId,count,quote)),
                new TavernChoice(Text("ui_w04_cancel"),() => ShowTavernMenu("supply_in"))});
        }
        private void TradeSupplyInput(int id,int count,int quote)
        {
            if (!TavernMayAct || supplyBusy || TavernDialogueBusy) return;
            supplyBusy=true;
            try {
                var item=CarriedSupplyItems().FirstOrDefault(i => i.uniqueId==id);
                PrepareLuxuryStock();var p=ReadSupply();int day=StoreStation.Instance.dayCounter;
                if (!AcceptSupplyItem(item) || item.unitCount!=count || p.InputPrice(item.identifier)*count!=quote || !p.MayReceive(day,item.identifier,count)) { TavernNotice("ui_w04_no_need");return; }
                var t=p.PrepareInput(WorkRunIdentity,day,item.identifier,id,count,PlayerStore.Instance.GetCash());if (t==null) return;
                SaveSupply(p);item.Destroy();t.Status="moving";SaveSupply(p);
                ModHelper.AddCredit(t.Price,false);
                if (PlayerStore.Instance.GetCash()!=t.BeforeCash+t.Price) throw new InvalidOperationException("Input payment snapshot differs");
                t.Status="paid";SaveSupply(p);
                if (!p.Complete(t)) throw new InvalidOperationException("Supply input could not be committed");
                p.ProduceLuxury(day,ReadSupport().LuxuryChannelAvailable(day));p.Produce(day);SaveSupply(p);TavernNotice("ui_w04_done");ShowTavernMenu("supply_in");
            } catch (Exception e) { Log.Warning("Supply input held: "+e.Message);TavernNotice("ui_w04_pending"); }
            finally { supplyBusy=false; }
        }
        private void ConfirmSupplyOutput(string batch)
        {
            CloseTavernMenu();tavernStage.ShowChoices(new[]{new TavernChoice(Text("ui_w04_confirm")+" · "+(ReadSupply().Batches.FirstOrDefault(v => v.Id==batch)?.WholesalePrice??0)+" CR",() => TradeSupplyOutput(batch)),
                new TavernChoice(Text("ui_w04_cancel"),() => ShowTavernMenu("supply_out"))});
        }
        private void TradeSupplyOutput(string batch)
        {
            if (!TavernMayAct || supplyBusy || TavernDialogueBusy) return;
            supplyBusy=true;
            try {
                var p=ReadSupply();var stock=p.Batches.FirstOrDefault(b => b.Id==batch);
                if (p.Pending!=null || stock==null || stock.PlayerRemaining<1) { TavernNotice("ui_w04_soldout");return; }
                if (PlayerStore.Instance.GetCash()<stock.WholesalePrice) { QueueTavernSpeech(new[]{"xm_tavern_no_money"});return; }
                var item=ModHelper.SpawnItem(CocktailCatalog.Find(stock.Key).ItemId);
                if (item==null) return;
                item.ModifyTag("XIAOMAN_BATCH_SOURCE",tag => tag.Enable().SetString(stock.Id));
                var t=p.PrepareOutput(WorkRunIdentity,StoreStation.Instance.dayCounter,batch,item.GetUniqueID(),PlayerStore.Instance.GetCash());
                if (t==null) { item.Destroy();return; }SaveSupply(p);
                ModHelper.SetItemOwnerShip(item,true);GraphUtils.TryAcceptAll(EmporiumEntry.Instance.afterhourInventory,item);
                if (item.parentInventory==null) { item.Destroy();t.Status="cancelled";SaveSupply(p);TavernNotice("ui_w04_full");return; }
                t.Status="moving";SaveSupply(p);PlayerStore.Instance.PayMoney(t.Price);
                if (PlayerStore.Instance.GetCash()!=t.BeforeCash-t.Price) throw new InvalidOperationException("Output payment snapshot differs");
                t.Status="paid";SaveSupply(p);
                if (!p.Complete(t)) throw new InvalidOperationException("Supply output could not be committed");
                SaveSupply(p);TavernNotice("ui_w04_done");ShowTavernMenu("supply_out");
            } catch (Exception e) { Log.Warning("Supply output held: "+e.Message);TavernNotice("ui_w04_pending"); }
            finally { supplyBusy=false; }
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
            if (!TavernMapEnabled || !PlayerStore.HasInstance || EmporiumEntry.Instance==null || supplyBusy) return;
            var p=ReadSupply();var t=p.Pending;if (t==null || p.Blocked || t.Status=="uncertain") return;
            bool owned=EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i.uniqueId==t.ItemId);
            int cash=PlayerStore.Instance.GetCash(),after=t.Direction=="in"?t.BeforeCash+t.Price:t.BeforeCash-t.Price;
            if (t.Status=="prepared" && cash==t.BeforeCash && (t.Direction=="in"?owned:!owned)) t.Status="cancelled";
            else if (cash==after && (t.Direction=="in"?!owned:owned)) {
                if (!p.Complete(t)) t.Status="uncertain";else { p.ProduceLuxury(StoreStation.Instance.dayCounter,ReadSupport().LuxuryChannelAvailable(StoreStation.Instance.dayCounter));p.Produce(StoreStation.Instance.dayCounter); }
            } else t.Status="uncertain";
            SaveSupply(p);
        }
    }
}
