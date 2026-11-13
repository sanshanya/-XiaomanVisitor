using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    public sealed class WorkOrder
    {
        public string Id, Source, RevenueOwner="";
        public int Day, Revenue, Material;
    }
    public sealed class WorkDemandBatch
    {
        public int Day;
        public readonly List<WorkOrder> Orders=new List<WorkOrder>();
    }
    public sealed class PaidWorkShift
    {
        public string Id, Status;
        public int Day, BeforeCash, Wage, Revenue, Material, Net;
        public string[] OrderIds;
    }
    public sealed class WorkThanksReceipt
    {
        public bool Drawn, Won, Seen, Merged, ProcurementClosed;
        public int Roll=-1, DueDay, GiftItemId, PrivateSpent, Week=-1;
        public string ShiftId="";
        public string DrinkKey="", GiftPhase="waiting";
        public readonly List<int> PurchasedItemIds=new List<int>();
        public bool GiftComplete { get { return GiftPhase=="complete"; } }
    }
    public enum WageRecovery { None, Cancel, Finish, Hold }

    // Incremental paid work only. Not the tavern's full cash-flow/debt model.
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TavernWorkProgress
    {
        public const string SaveKey="tavern_paid_work_v1";
        [JsonProperty] public int Schema=3;
        [JsonProperty] public string RunIdentity, PreservedData;
        [JsonProperty] public bool RecoveryBlocked, WorkIntroSeen, OpeningCallbackSeen, ShopkeeperKnown;
        [JsonProperty] public readonly List<WorkDemandBatch> Demand=new List<WorkDemandBatch>();
        [JsonProperty] public readonly List<PaidWorkShift> Shifts=new List<PaidWorkShift>();
        [JsonProperty] public WorkThanksReceipt Thanks=new WorkThanksReceipt();
        [JsonProperty] public List<WorkThanksReceipt> ThanksHistory=new List<WorkThanksReceipt>();
        [JsonProperty] public List<int> ThanksVisitWeeks=new List<int>();
        public bool HasWorked { get { return Shifts.Any(s => s.Status=="complete"); } }
        public long IncrementalNet { get { return Shifts.Where(s => s.Status=="complete").Sum(s => (long)s.Net); } }
        public PaidWorkShift Pending { get { return Shifts.FirstOrDefault(s => s.Status!="complete" && s.Status!="cancelled"); } }
        public bool WorkedOn(int day) { return Shifts.Any(s => s.Day==day && s.Status=="complete"); }
        public bool ThanksDue(int day) { return !RecoveryBlocked && Thanks.Drawn && Thanks.Won && !Thanks.Seen && day>=Thanks.DueDay && !ThanksVisitWeeks.Contains(SupplyProgress.Week(day)); }
        public int PurchaseRemaining { get { return Thanks.Merged ? 0 : Math.Max(0,TavernWorkConfig.PrivateReserve-Thanks.PrivateSpent); } }

        public bool EnsureDemand(int day,int quantity)
        {
            if (RecoveryBlocked || day<TavernWorkConfig.UnlockDay || Demand.Any(d => d.Day==day)) return false;
            var batch=new WorkDemandBatch { Day=day };
            for (int i=0;i<Math.Max(0,Math.Min(30,quantity));i++) batch.Orders.Add(new WorkOrder {
                Id=TavernWorkConfig.Source+":"+day+":"+i,Source=TavernWorkConfig.Source,Day=day,
                Revenue=TavernWorkConfig.OrderRevenue,Material=TavernWorkConfig.OrderMaterial });
            Demand.Add(batch); return true;
        }
        private WorkOrder[] Waiting(int day)
        { return Demand.Where(d => d.Day==day).SelectMany(d => d.Orders).Where(o => string.IsNullOrEmpty(o.RevenueOwner)).ToArray(); }
        public string[] WaitingIds(int day)
        { return Waiting(day).Take(TavernWorkConfig.OrdersPerShift).Select(o => o.Id).ToArray(); }
        public bool MayWork(int day)
        { return !RecoveryBlocked && day>=TavernWorkConfig.UnlockDay && Pending==null && !WorkedOn(day)
            && Waiting(day).Length>=TavernWorkConfig.OrdersPerShift; }
        // Future consumers must claim the same ID, not count an order again as L1/baseline traffic.
        public bool TryAssignOrder(string id,string owner)
        {
            var order=Demand.SelectMany(d => d.Orders).FirstOrDefault(o => o.Id==id);
            if (order==null || string.IsNullOrEmpty(owner) || !string.IsNullOrEmpty(order.RevenueOwner)) return false;
            order.RevenueOwner=owner; return true;
        }
        public PaidWorkShift PrepareShift(int day,int cash,string[] expectedIds=null)
        {
            if (!MayWork(day) || cash>int.MaxValue-TavernWorkConfig.Wage) return null;
            var orders=Waiting(day).Take(TavernWorkConfig.OrdersPerShift).ToArray();
            if (expectedIds!=null && !expectedIds.SequenceEqual(orders.Select(o => o.Id))) return null;
            int revenue=orders.Sum(o => o.Revenue),material=orders.Sum(o => o.Material);
            if (revenue-material<TavernWorkConfig.Wage) return null;
            var shift=new PaidWorkShift { Id="paid_shift_"+day, Day=day, Status="prepared", BeforeCash=cash,
                Wage=TavernWorkConfig.Wage,Revenue=revenue,Material=material,Net=revenue-material-TavernWorkConfig.Wage,
                OrderIds=orders.Select(o => o.Id).ToArray() };
            Shifts.RemoveAll(s => s.Day==day && s.Status=="cancelled");
            Shifts.Add(shift); return shift;
        }
        public WageRecovery Recover(int today,int cash)
        {
            var p=Pending;
            if (p==null) return WageRecovery.None;
            if (RecoveryBlocked || p.Status=="uncertain" || p.Day!=today) return WageRecovery.Hold;
            if (p.Status=="prepared" && cash==p.BeforeCash) return WageRecovery.Cancel;
            return cash==(long)p.BeforeCash+p.Wage ? WageRecovery.Finish : WageRecovery.Hold;
        }
        public bool Complete(PaidWorkShift shift)
        {
            if (shift==null || !ReferenceEquals(shift,Pending)) return false;
            var orders=Demand.SelectMany(d => d.Orders).Where(o => shift.OrderIds.Contains(o.Id)).ToArray();
            if (orders.Length!=shift.OrderIds.Length || orders.Any(o => !string.IsNullOrEmpty(o.RevenueOwner))) return false;
            foreach (var order in orders) order.RevenueOwner=shift.Id;
            shift.Status="complete";
            int week=SupplyProgress.Week(shift.Day);
            bool pendingThanks=Thanks.Won && (!Thanks.Seen || !Thanks.GiftComplete || !Thanks.Merged && !Thanks.ProcurementClosed);
            if (!pendingThanks && !ThanksHistory.Concat(new[]{Thanks}).Any(t => t.Drawn && t.Week==week && t.Won)) {
                if (Thanks.Drawn) ThanksHistory.Add(Thanks);
                Thanks=new WorkThanksReceipt { Drawn=true,Week=week,ShiftId=shift.Id,
                    Roll=StableRoll(RunIdentity,shift.Id,"visit")%100,DueDay=shift.Day+1 };
                Thanks.Won=Thanks.Roll<TavernWorkConfig.ThanksPercent;
                Thanks.DrinkKey=StoryRules.BasicDrinks[StableRoll(RunIdentity,shift.Id,"drink")%StoryRules.BasicDrinks.Length];
            }
            return true;
        }
        public bool BeginThanks(int day,bool merged)
        {
            if (!ThanksDue(day)) return false;
            Thanks.Seen=true; Thanks.Merged=merged; ThanksVisitWeeks.Add(SupplyProgress.Week(day)); ShopkeeperKnown=true; return true;
        }
        public bool RecordPurchase(int itemId,int credits)
        {
            if (RecoveryBlocked || !Thanks.Seen || Thanks.Merged || Thanks.ProcurementClosed || itemId<=0 || credits<0
                || credits>PurchaseRemaining || Thanks.PurchasedItemIds.Contains(itemId)) return false;
            Thanks.PurchasedItemIds.Add(itemId); Thanks.PrivateSpent+=credits; return true;
        }
        public bool PrepareGift(int itemId)
        {
            if (RecoveryBlocked || !Thanks.Won || !Thanks.Seen || Thanks.GiftPhase!="waiting" || itemId<=0) return false;
            Thanks.GiftItemId=itemId; Thanks.GiftPhase="placing"; return true;
        }
        public void GiftPlacementResult(bool placed)
        {
            if (Thanks.GiftPhase!="placing") return;
            Thanks.GiftPhase=placed ? "complete" : "waiting";
            if (!placed) Thanks.GiftItemId=0;
        }
        public void RecoverGift(bool itemFound)
        {
            if (Thanks.GiftPhase=="placing") Thanks.GiftPhase=itemFound ? "complete" : "uncertain";
        }
        public static int StableRoll(string identity,string shift,string purpose)
        {
            uint hash=2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes((identity??"")+"|"+shift+"|"+purpose)) hash=unchecked((hash^b)*16777619);
            return (int)(hash&0x7fffffff);
        }
        public string Data { get { return JsonConvert.SerializeObject(this); } }
        public static TavernWorkProgress Restore(string data,string identity,int today)
        {
            if (string.IsNullOrEmpty(data)) return new TavernWorkProgress { RunIdentity=identity };
            TavernWorkProgress p;
            try { p=JsonConvert.DeserializeObject<TavernWorkProgress>(data); }
            catch (JsonException) { return Blocked(data,identity); }
            if (p==null || p.Schema<1 || p.Schema>3 || p.Demand==null || p.Shifts==null || p.Thanks==null
                || p.Thanks.PurchasedItemIds==null || string.IsNullOrEmpty(p.RunIdentity)) return Blocked(data,identity);
            if (p.Schema==1) {
                if (p.Thanks.Drawn) {
                    var first=p.Shifts.Where(s => s.Status=="complete").OrderBy(s => s.Day).FirstOrDefault();
                    if (first==null) return Blocked(data,identity);
                    p.Thanks.Week=SupplyProgress.Week(first.Day);p.Thanks.ShiftId=first.Id;
                    if (p.Thanks.Seen) p.ThanksVisitWeeks.Add(SupplyProgress.Week(today));
                }
                p.Schema=2;
            }
            if (p.ThanksHistory==null || p.ThanksVisitWeeks==null) return Blocked(data,identity);
            if (p.Demand.Any(d => d==null || d.Day<TavernWorkConfig.UnlockDay || d.Day>today || d.Orders==null
                    || d.Orders.Any(o => o==null || o.Day!=d.Day))
                || p.Demand.GroupBy(d => d.Day).Any(g => g.Count()>1)) return Blocked(data,identity);
            var orders=p.Demand.SelectMany(d => d.Orders).ToArray();
            if (orders.Any(o => o==null || string.IsNullOrEmpty(o.Id) || o.Source!=TavernWorkConfig.Source || o.Revenue<0 || o.Material<0)
                || orders.GroupBy(o => o.Id).Any(g => g.Count()>1)) return Blocked(data,identity);
            if (p.Shifts.GroupBy(s => s==null ? -1:s.Day).Any(g => g.Count()>1)) return Blocked(data,identity);
            foreach (var s in p.Shifts) {
                if (s==null || s.Id!="paid_shift_"+s.Day || s.Day<TavernWorkConfig.UnlockDay || s.Day>today || s.Wage<1
                    || s.BeforeCash>int.MaxValue-s.Wage || s.OrderIds==null || s.OrderIds.Length<1 || s.OrderIds.Length>30
                    || s.OrderIds.Distinct().Count()!=s.OrderIds.Length || !new[]{"prepared","credited","complete","cancelled","uncertain"}.Contains(s.Status)) return Blocked(data,identity);
                var own=orders.Where(o => s.OrderIds.Contains(o.Id)).ToArray();
                if (own.Length!=s.OrderIds.Length || own.Any(o => o.Day!=s.Day) || own.Sum(o => (long)o.Revenue)!=s.Revenue
                    || own.Sum(o => (long)o.Material)!=s.Material || s.Net!=(long)s.Revenue-s.Material-s.Wage || s.Net<0
                    || (s.Status=="complete" && own.Any(o => o.RevenueOwner!=s.Id))) return Blocked(data,identity);
            }
            if (p.Shifts.Count(s => s.Status!="complete" && s.Status!="cancelled")>1) return Blocked(data,identity);
            if (p.Thanks.Drawn && (p.Thanks.Roll<0 || p.Thanks.Roll>=100 || !StoryRules.BasicDrinks.Contains(p.Thanks.DrinkKey))
                || p.Thanks.Drawn && !p.HasWorked || p.Thanks.PrivateSpent<0 || p.Thanks.PrivateSpent>TavernWorkConfig.PrivateReserve
                || !new[]{"waiting","placing","complete","uncertain"}.Contains(p.Thanks.GiftPhase)
                || p.Thanks.PurchasedItemIds.Any(id => id<=0) || p.Thanks.PurchasedItemIds.Distinct().Count()!=p.Thanks.PurchasedItemIds.Count
                || (p.Thanks.Seen && !p.Thanks.Won) || (p.Thanks.Merged && p.Thanks.PrivateSpent!=0)
                || (p.Thanks.GiftPhase!="waiting" && (!p.Thanks.Seen || p.Thanks.GiftItemId<=0))) return Blocked(data,identity);
            if (p.Schema==2) {
                // Upgrade only today's old three-order batch, once. Frozen
                // shift identities/wages remain intact; quiet days stay quiet.
                var batch=p.Demand.FirstOrDefault(d => d.Day==today && d.Orders.Count==3);
                if (batch!=null) for (int i=3;i<TavernWorkConfig.DailyOrders;i++) batch.Orders.Add(new WorkOrder {
                    Id=TavernWorkConfig.Source+":"+today+":balance:"+i,Source=TavernWorkConfig.Source,Day=today,
                    Revenue=TavernWorkConfig.OrderRevenue,Material=TavernWorkConfig.OrderMaterial });
                p.Schema=3;
            }
            return p;
        }
        private static TavernWorkProgress Blocked(string raw,string identity)
        { return new TavernWorkProgress { RecoveryBlocked=true,PreservedData=raw,RunIdentity=identity }; }
    }
}
