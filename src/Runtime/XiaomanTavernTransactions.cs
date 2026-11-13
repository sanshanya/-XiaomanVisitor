using System;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private TavernProgress ReadTavern()
        { return TavernProgress.Restore(ModHelper.GetModData(Id,TavernRules.SaveKey,""),StoreStation.Instance.dayCounter); }

        private void SaveTavern(TavernProgress progress)
        { ModHelper.SetModData(Id,TavernRules.SaveKey,progress.Data); }

        private void RecoverTavernDrink()
        {
            if (!TavernMapEnabled || !PlayerStore.HasInstance || tavernActionBusy) return;
            var progress=ReadTavern();
            var record=progress.Pending;
            if (progress.RecoveryBlocked) return;
            tavernActionBusy=true;
            try {
                TavernRecovery recovery=progress.Recover(StoreStation.Instance.dayCounter,PlayerStore.Instance.GetCash(),ReadDrinkBuffs().Data);
                if (recovery == TavernRecovery.Cancel) { record.Status="cancelled"; SaveTavern(progress); }
                else if (recovery == TavernRecovery.ApplyBuff || recovery == TavernRecovery.Finish)
                    CommitTavernBuff(progress,record,recovery == TavernRecovery.ApplyBuff);
                else if (recovery == TavernRecovery.Hold) { record.Status="uncertain"; SaveTavern(progress); }
                ReconcileTavernStock(progress);
            }
            catch (Exception error) { Log.Warning("Tavern receipt remains pending: "+error.Message); }
            finally { tavernActionBusy=false; }
        }

        private void CommitTavernBuff(TavernProgress progress,TavernDrinkRecord record,bool apply)
        {
            if (record.Status!="charged") { record.Status="charged";SaveTavern(progress); }
            if (apply) {
                var buffs=ReadDrinkBuffs();
                buffs.Replace(CocktailCatalog.Find(record.Key),record.Day);
                buffs.Expire(StoreStation.Instance.dayCounter);
                SaveDrinkBuffs(buffs);
            }
            record.Status="complete"; SaveTavern(progress);
            if (record.Day==StoreStation.Instance.dayCounter) CocktailConsumed(CocktailCatalog.Find(record.Key));
            ReconcileTavernStock(progress);
        }

        private void ReconcileTavernStock(TavernProgress progress)
        {
            var supply=ReadSupply();bool changed=false;
            foreach (var use in supply.BarUses.Where(u => !u.Cancelled && !u.Settled)) {
                var record=progress.Drinks.FirstOrDefault(d => d.Id==use.Id);
                if (record==null) continue;
                if (record.Status=="complete") { supply.SettleBar(record.Id,SupplySettlementRevenue(record,supply));changed|=use.Settled; }
                else if (record.Status=="cancelled") { supply.CancelBar(record.Id);changed=true; }
            }
            if (changed) SaveSupply(supply);
        }

        private void BuyTavernDrink(string key)
        {
            if (!TavernMayAct || !TavernTrading || tavernActionBusy || TavernDialogueBusy) return;
            RecoverTavernDrink();
            var progress=ReadTavern();
            int day=StoreStation.Instance.dayCounter;
            var spec=CocktailCatalog.Find(key);
            if (spec == null || !LuxuryDrinkCatalog.OnMenu(key,day)) return;
            if (LuxuryDrinkCatalog.IsStandard(key)) PrepareLuxuryStock();
            if (progress.RecoveryBlocked) { TavernNotice("ui_tavern_receipt_blocked");return; }
            if (progress.Pending != null || progress.Drinks.Any(d => d.Day==day && d.Status=="uncertain"))
                { TavernNotice("ui_tavern_payment_pending"); return; }
            if (progress.Drank(day)) { QueueTavernSpeech(new[]{"xm_tavern_drink_limit"}); return; }
            if (PlayerStore.Instance.GetCash() < TavernPrices.For(spec)) { QueueTavernSpeech(new[]{"xm_tavern_no_money"}); return; }
            tavernActionBusy=true;
            try {
                string before=ReadDrinkBuffs().Data;
                var record=progress.PrepareDrink(day,key,PlayerStore.Instance.GetCash(),before);
                if (record == null) return;
                var supply=ReadSupply();
                if (LuxuryDrinkCatalog.IsStandard(key) || supply.Batches.Any(b => b.Key==key)) {
                    if (!supply.ReserveBar(record.Id,key)) {
                        if (LuxuryDrinkCatalog.IsStandard(key)) { CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_05_soldout"}); }
                        else TavernNotice("ui_w04_pool_empty");
                        return;
                    }
                    SaveSupply(supply);
                }
                SaveTavern(progress); // Record intent before touching the native wallet.
                PlayerStore.Instance.PayMoney(record.Paid);
                if (PlayerStore.Instance.GetCash()!=record.BeforeCash-record.Paid)
                    throw new InvalidOperationException("Payment snapshot does not match; receipt held for recovery");
                CommitTavernBuff(progress,record,true);
                CloseTavernMenu();
                QueueTavernSpeech(new[]{"xm_tavern_order","xm_tavern_serve"});
            }
            catch (Exception error) {
                Log.Warning("Tavern payment stopped safely: "+error.Message);
                TavernNotice("ui_tavern_payment_pending");
            }
            finally { tavernActionBusy=false; }
        }
    }
}
