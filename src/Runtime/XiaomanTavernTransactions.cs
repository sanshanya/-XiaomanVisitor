using System;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private TavernProgress ReadTavern()
        { return TavernProgress.Restore(ModHelper.GetModData(Id,TavernRules.SaveKey,""),StoreStation.Instance.dayCounter); }

        private void SaveTavern(TavernProgress progress)
        {
            string data=progress.Data;
            ModHelper.SetModData(Id,TavernRules.SaveKey,data);
            if (ModHelper.GetModData(Id,TavernRules.SaveKey,"") != data)
                throw new InvalidOperationException("Tavern record was not saved; payment is blocked");
        }

        private void RecoverTavernDrink()
        {
            if (!TavernMapEnabled || !PlayerStore.HasInstance || tavernActionBusy) return;
            var progress=ReadTavern();
            var record=progress.Pending;
            if (record == null) return;
            tavernActionBusy=true;
            try {
                TavernRecovery recovery=progress.Recover(StoreStation.Instance.dayCounter,PlayerStore.Instance.GetCash(),ReadDrinkBuffs().Data);
                if (recovery == TavernRecovery.Cancel) { record.Status="cancelled"; SaveTavern(progress);
                    var supply=ReadSupply();supply.CancelBar(record.Id);SaveSupply(supply); }
                else if (recovery == TavernRecovery.ApplyBuff || recovery == TavernRecovery.Finish)
                    CommitTavernBuff(progress,record,recovery == TavernRecovery.ApplyBuff);
                else if (recovery == TavernRecovery.Hold) { record.Status="uncertain"; SaveTavern(progress); }
            }
            catch (Exception error) { Log.Warning("Tavern receipt remains pending: "+error.Message); }
            finally { tavernActionBusy=false; }
        }

        private void CommitTavernBuff(TavernProgress progress,TavernDrinkRecord record,bool apply)
        {
            if (apply) {
                record.Status="charged"; SaveTavern(progress);
                SaveDrinkBuffs(DrinkBuffCollection.Restore(record.AfterBuffs));
                if (ReadDrinkBuffs().Data != record.AfterBuffs) throw new InvalidOperationException("Drink effect was not saved");
            }
            record.Status="complete"; SaveTavern(progress);
            // Optional native relief/sound occurs only after the durable completed receipt.
            // Recovery never repeats a completed receipt or creates an inventory item.
            var supply=ReadSupply();supply.SettleBar(record.Id,SupplySettlementRevenue(record,supply));SaveSupply(supply);
            CocktailConsumed(CocktailCatalog.Find(record.Key));
        }

        private void BuyTavernDrink(string key)
        {
            if (!TavernMayAct || !TavernTrading || tavernActionBusy || TavernDialogueBusy) return;
            var progress=ReadTavern();
            int day=StoreStation.Instance.dayCounter;
            var spec=CocktailCatalog.Find(key);
            if (spec == null || !LuxuryDrinkCatalog.OnMenu(key,day)) return;
            if (LuxuryDrinkCatalog.IsStandard(key)) PrepareLuxuryStock();
            if (progress.RecoveryBlocked || progress.Pending != null) { TavernNotice("ui_tavern_payment_pending"); return; }
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
                SaveTavern(progress); // Verified before touching the native wallet.
                PlayerStore.Instance.PayMoney(record.Paid);
                if (progress.Recover(day,PlayerStore.Instance.GetCash(),ReadDrinkBuffs().Data) != TavernRecovery.ApplyBuff)
                    throw new InvalidOperationException("Payment snapshots do not match; receipt held for recovery");
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
