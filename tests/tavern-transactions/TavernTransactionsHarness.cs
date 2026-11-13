using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using XiaomanVisitor;

// The production transaction adapter is compiled unchanged beside these stubs.
// PayMoney mirrors the verified native method: one synchronous cash subtraction.
public sealed class PlayerStore
{
    public static PlayerStore Instance=new PlayerStore();
    public static bool HasInstance { get { return Instance!=null; } }
    public int Cash=1000, Payments;
    public Action AfterDebit;
    public int GetCash() { return Cash; }
    public void PayMoney(int amount) { Cash-=amount;Payments++;AfterDebit?.Invoke(); }
}
public sealed class StoreStation
{
    public static StoreStation Instance=new StoreStation();
    public int dayCounter=15;
}
public static class ModHelper
{
    private static readonly Dictionary<string,string> data=new Dictionary<string,string>();
    public static Func<string,string,bool> FailWrite;
    public static string GetModData(string id,string key,string fallback)
    { string value;return data.TryGetValue(id+":"+key,out value)?value:fallback; }
    public static void SetModData(string id,string key,string value)
    {
        if (FailWrite!=null && FailWrite(key,value)) { FailWrite=null;throw new InvalidOperationException("injected write failure"); }
        data[id+":"+key]=value;
    }
    public static void Reset() { data.Clear();FailWrite=null; }
}
namespace XiaomanVisitor
{
    public sealed class TransactionLog { public readonly List<string> Warnings=new List<string>();public void Warning(string value) { Warnings.Add(value); } }
    public sealed partial class XiaomanMod
    {
        public const string Id="sansm.xiaoman.visitor";
        private bool tavernActionBusy;
        private bool TavernMapEnabled { get { return true; } }
        private bool TavernMayAct { get { return true; } }
        private bool TavernTrading { get { return true; } }
        private bool TavernDialogueBusy { get { return false; } }
        public readonly TransactionLog Log=new TransactionLog();
        public readonly List<string> Notices=new List<string>();
        public int Consumed, ClosedMenus;
        private DrinkBuffCollection ReadDrinkBuffs() { return DrinkBuffCollection.Restore(ModHelper.GetModData(Id,"drink_buffs_active","")); }
        private void SaveDrinkBuffs(DrinkBuffCollection p) { ModHelper.SetModData(Id,"drink_buffs_active",p.Data); }
        private SupplyProgress ReadSupply() { return SupplyProgress.Restore(ModHelper.GetModData(Id,SupplyProgress.SaveKey,"")); }
        private void SaveSupply(SupplyProgress p) { ModHelper.SetModData(Id,SupplyProgress.SaveKey,p.Data); }
        private int SupplySettlementRevenue(TavernDrinkRecord r,SupplyProgress p) { return r.Paid; }
        private void PrepareLuxuryStock() { }
        private void CloseTavernMenu() { ClosedMenus++; }
        private void QueueTavernSpeech(string[] keys) { }
        private void TavernNotice(string key) { Notices.Add(key); }
        private void CocktailConsumed(CocktailSpec spec) { Consumed++; }
        public TavernProgress Progress { get { return ReadTavern(); } }
        public DrinkBuffCollection Buffs { get { return ReadDrinkBuffs(); } }
        public SupplyProgress Stock { get { return ReadSupply(); } }
        public void Put(TavernProgress p) { SaveTavern(p); }
        public void Put(DrinkBuffCollection p) { SaveDrinkBuffs(p); }
        public void Put(SupplyProgress p) { SaveSupply(p); }
        public void Buy(string key="cosmo_zone") { BuyTavernDrink(key); }
        public void Recover() { RecoverTavernDrink(); }
    }
}
internal static class TavernTransactionsHarness
{
    private static int checks;
    private static void Check(bool ok,string label)
    { if (!ok) throw new InvalidOperationException(label);checks++; }
    private static XiaomanMod Fresh()
    { ModHelper.Reset();PlayerStore.Instance=new PlayerStore();StoreStation.Instance=new StoreStation();return new XiaomanMod(); }
    private static TavernDrinkRecord Receipt(XiaomanMod mod,string status,int day=15)
    {
        var p=mod.Progress;var r=p.PrepareDrink(day,"cosmo_zone",1000,mod.Buffs.Data);r.Status=status;mod.Put(p);return r;
    }
    private static void PutStock(XiaomanMod mod)
    {
        var s=new SupplyProgress { Cash=1000 };
        s.Batches.Add(new SupplyBatch { Id="stock",Key="cosmo_zone",Day=15,Produced=2,StoreInitial=2 });mod.Put(s);
    }
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e) => {
            string name=new AssemblyName(e.Name).Name+".dll";
            foreach (string directory in args) { string path=Path.Combine(directory,name);if (File.Exists(path)) return Assembly.LoadFrom(path); }
            return null;
        };
        Run();Console.WriteLine("PASS: "+checks+" assertions against production tavern purchase/recovery methods; no game launched.");return 0;
    }
    private static void Run()
    {
        var mod=Fresh();var buffs=new DrinkBuffCollection();buffs.Replace(CocktailCatalog.Find("cosmo_zone"),15);
        buffs.Replace(CocktailCatalog.Find("sunset_blue"),15);mod.Put(buffs);string original=buffs.Data;
        mod.Buy();
        Check(PlayerStore.Instance.Cash==810 && PlayerStore.Instance.Payments==1 && mod.Progress.Drinks.Single().Status=="complete",
            "already-identical cosmo_zone effect must still complete exactly one paid purchase");
        Check(mod.Buffs.Data==original && mod.Consumed==1 && mod.Notices.Count==0,"identical effect neither extends expiry nor reports failed payment");
        mod.Buy();mod.Recover();Check(PlayerStore.Instance.Payments==1 && mod.Consumed==1,"repeat clicks/recovery never repeat debit or consumption");

        foreach (var spec in CocktailCatalog.Drinks.Concat(LuxuryDrinkCatalog.Standard)) {
            mod=Fresh();StoreStation.Instance.dayCounter=29;
            buffs=new DrinkBuffCollection();buffs.Replace(spec,29);mod.Put(buffs);original=buffs.Data;
            if (LuxuryDrinkCatalog.IsStandard(spec.Key)) {
                var supply=new SupplyProgress { Cash=10000 };supply.ProduceLuxury(29,true,false);mod.Put(supply);
            }
            mod.Buy(spec.Key);
            Check(PlayerStore.Instance.Cash==1000-TavernPrices.For(spec) && PlayerStore.Instance.Payments==1
                && mod.Progress.Drinks.Single().Status=="complete" && mod.Notices.Count==0,
                "every menu drink accepts an already-identical effect and completes its actual price: "+spec.Key);
            original=mod.Buffs.Data;mod.Recover();mod.Buy(spec.Key);
            Check(mod.Buffs.Data==original && PlayerStore.Instance.Payments==1 && mod.Consumed==1
                && (!LuxuryDrinkCatalog.IsStandard(spec.Key) || mod.Stock.BarUses.Single().Settled),
                "every menu drink preserves one daily payment, effect and stock settlement: "+spec.Key);
        }

        mod=Fresh();Receipt(mod,"uncertain");string old=mod.Progress.Data;mod.Buy();
        Check(PlayerStore.Instance.Payments==0 && mod.Notices.Last()=="ui_tavern_payment_pending" && mod.Progress.Data==old,
            "an uncertain current-day receipt retains its evidence and prevents a second charge");
        StoreStation.Instance.dayCounter=16;mod.Buy();
        Check(PlayerStore.Instance.Payments==1 && mod.Progress.Drinks[0].Status=="uncertain"
            && mod.Progress.Drinks[1].Day==16 && mod.Progress.Drinks[1].Status=="complete",
            "legacy uncertain day15 cannot block day16, refund it, or fabricate old consumption");

        mod=Fresh();Receipt(mod,"prepared");StoreStation.Instance.dayCounter=16;PlayerStore.Instance.Cash=743;mod.Buy();
        Check(mod.Progress.Drinks[0].Status=="uncertain" && mod.Progress.Drinks[1].Status=="complete"
            && PlayerStore.Instance.Cash==553 && PlayerStore.Instance.Payments==1,"a stale unproven intent is isolated before the next purchase");

        mod=Fresh();var receipt=Receipt(mod,"charged");StoreStation.Instance.dayCounter=16;PlayerStore.Instance.Cash=930;
        buffs=new DrinkBuffCollection();buffs.Replace(CocktailCatalog.Find("sunset_blue"),16);mod.Put(buffs);mod.Recover();
        Check(mod.Progress.Drinks.Single().Status=="complete" && PlayerStore.Instance.Cash==930 && PlayerStore.Instance.Payments==0,
            "saved debit proof survives later wages and recovery never charges again");
        Check(mod.Buffs.ActiveAmount(DrinkEffect.Scavenging,17)==4 && mod.Buffs.ActiveAmount(DrinkEffect.Scavenging,18)==0
            && mod.Buffs.ActiveAmount(DrinkEffect.Luck,18)==5 && mod.Consumed==0,
            "cross-day recovery uses original expiry, retains unrelated effects and does not replay native consumption");
        string recovered=mod.Buffs.Data;mod.Recover();Check(mod.Buffs.Data==recovered,"a restored completion is idempotent");

        mod=Fresh();Receipt(mod,"charged");StoreStation.Instance.dayCounter=19;PlayerStore.Instance.Cash=123;
        buffs=new DrinkBuffCollection();buffs.Replace(CocktailCatalog.Find("bluebird"),19);mod.Put(buffs);original=buffs.Data;mod.Recover();
        Check(mod.Progress.Drinks.Single().Status=="complete" && mod.Buffs.Data==original && mod.Consumed==0,
            "expired paid drinks complete their receipt without granting a fresh buff");

        mod=Fresh();Receipt(mod,"charged");StoreStation.Instance.dayCounter=16;
        buffs=new DrinkBuffCollection();buffs.Replace(CocktailCatalog.Find("whale_dream"),16);mod.Put(buffs);original=buffs.Data;mod.Recover();
        Check(mod.Progress.Drinks.Single().Status=="complete" && mod.Buffs.Data==original,"an old paid receipt cannot replace a later chosen drink of the same category");

        mod=Fresh();PutStock(mod);ModHelper.FailWrite=(key,value) => key=="drink_buffs_active";mod.Buy();
        Check(PlayerStore.Instance.Cash==810 && mod.Progress.Pending?.Status=="charged" && mod.Buffs.Count==0,
            "failure before applying the buff retains verified debit proof");
        StoreStation.Instance.dayCounter=16;PlayerStore.Instance.Cash+=120;mod.Recover();
        Check(mod.Progress.Drinks.Single().Status=="complete" && mod.Buffs.ActiveAmount(DrinkEffect.Scavenging,16)==4
            && mod.Stock.BarUses.Single().Settled && PlayerStore.Instance.Payments==1,"later recovery applies only the owed buff and settles reserved stock once");

        mod=Fresh();ModHelper.FailWrite=(key,value) => key==TavernRules.SaveKey && value.Contains("\"Status\":\"complete\"");mod.Buy();
        Check(mod.Progress.Pending?.Status=="charged" && mod.Buffs.ActiveAmount(DrinkEffect.Scavenging,15)==4,"failure after buff save remains a provably paid receipt");
        original=mod.Buffs.Data;PlayerStore.Instance.Cash+=120;mod.Recover();
        Check(mod.Progress.Drinks.Single().Status=="complete" && mod.Buffs.Data==original && PlayerStore.Instance.Payments==1,
            "changed cash after buff application cannot strand or reapply the completed effect");

        mod=Fresh();PutStock(mod);int stockWrites=0;
        ModHelper.FailWrite=(key,value) => key==SupplyProgress.SaveKey && ++stockWrites==2;mod.Buy();
        Check(mod.Progress.Drinks.Single().Status=="complete" && !mod.Stock.BarUses.Single().Settled,
            "interrupted stock settlement keeps an actual completed drink receipt");
        mod.Recover();mod.Recover();
        Check(mod.Stock.BarUses.Single().Settled && mod.Stock.Cash==1190 && mod.Stock.Batches.Single().StoreSold==1
            && PlayerStore.Instance.Payments==1,"completed receipt reconciles stock exactly once even with no pending drink");

        mod=Fresh();PlayerStore.Instance.AfterDebit=() => { PlayerStore.Instance.AfterDebit=null;throw new InvalidOperationException("after debit"); };mod.Buy();
        Check(mod.Progress.Pending?.Status=="prepared" && PlayerStore.Instance.Cash==810,"debit interruption does not invent a charged marker");
        mod.Recover();Check(mod.Progress.Drinks.Single().Status=="complete" && PlayerStore.Instance.Payments==1,
            "an exact same-day interrupted debit recovers without another payment");

        mod=Fresh();Receipt(mod,"prepared");PlayerStore.Instance.Cash=810;
        buffs=new DrinkBuffCollection();buffs.Replace(CocktailCatalog.Find("sunset_blue"),15);mod.Put(buffs);mod.Recover();
        Check(mod.Progress.Drinks.Single().Status=="complete" && mod.Buffs.ActiveAmount(DrinkEffect.Luck,15)==5
            && mod.Buffs.ActiveAmount(DrinkEffect.Scavenging,15)==4 && PlayerStore.Instance.Payments==0,
            "unrelated categories do not invalidate a proven debit or get overwritten by recovery");
        mod=Fresh();Receipt(mod,"prepared");PlayerStore.Instance.Cash=777;mod.Recover();
        Check(mod.Progress.Drinks.Single().Status=="uncertain" && mod.Progress.Pending==null
            && !mod.Progress.MayDrink(15) && mod.Progress.MayDrink(16) && PlayerStore.Instance.Cash==777,
            "an ambiguous wallet change isolates that day without a refund, effect or permanent lock");
        StoreStation.Instance.dayCounter=16;Receipt(mod,"charged",16);mod.Recover();
        Check(mod.Progress.Drinks[0].Status=="uncertain" && mod.Progress.Drinks[1].Status=="complete",
            "an archived uncertain receipt cannot hide a later active receipt from recovery");

        mod=Fresh();PutStock(mod);var stock=mod.Stock;stock.ReserveBar("tavern_drink_15","cosmo_zone");mod.Put(stock);
        Receipt(mod,"prepared");mod.Recover();mod.Recover();
        Check(mod.Stock.BarUses.Single().Cancelled && mod.Stock.Batches.Single().StoreSold==0 && mod.Stock.Cash==1000,
            "verified untouched cancellation releases only its reservation, once, without native money changes");

        mod=Fresh();Receipt(mod,"prepared");mod.Recover();
        Check(mod.Progress.Drinks.Single().Status=="cancelled" && mod.Progress.MayDrink(15) && PlayerStore.Instance.Payments==0,
            "only a verified untouched intent may cancel and release today's allowance");
        mod=Fresh();ModHelper.SetModData(XiaomanMod.Id,TavernRules.SaveKey,"{broken");mod.Buy();
        Check(PlayerStore.Instance.Payments==0 && mod.Notices.Last()=="ui_tavern_receipt_blocked"
            && ModHelper.GetModData(XiaomanMod.Id,TavernRules.SaveKey,"")=="{broken","malformed ledgers are preserved, not reset to make purchases look healthy");
    }
}
