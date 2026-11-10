using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    public sealed class SupportProgress
    {
        public const string SaveKey="w04_support_v1";
        public int Schema=1, AppliedDay=-1, AuditorDay=-1, NoticeItemId, GroupContractEffectiveDay=-1;
        public long PhoneNumber;
        public bool NewsSeen, AuditorSeen, AuditorComplete, DecisionGenerated, DecisionSent, WangSeen, Blocked;
        public string ApplicationId="", NoticePhase="waiting", PreservedData="";
        public static bool RegistrationOpen(int day) { return day>=22 && day<=24; }
        public bool Apply(int day,string run)
        {
            if (Blocked || !RegistrationOpen(day) || AppliedDay>=0) return false;
            AppliedDay=day;AuditorDay=day+1;ApplicationId=run+":tenquin_application";return true;
        }
        public bool AuditorDue(int day) { return !Blocked && AppliedDay>=22 && !AuditorSeen && day>=AuditorDay && day<=Math.Min(25,AuditorDay+1); }
        public bool WangDue(int day) { return !Blocked && AppliedDay>=22 && DecisionGenerated && !WangSeen && day>=27; }
        public bool LuxuryChannelAvailable(int day) { return !Blocked && GroupContractEffectiveDay>=25 && day>=29; }
        public bool Advance(int day)
        {
            if (Blocked) return false;bool changed=false;
            if (day>=25 && GroupContractEffectiveDay<0) { GroupContractEffectiveDay=25;changed=true; }
            if (AppliedDay>=22 && day>=26 && !DecisionGenerated) { DecisionGenerated=true;changed=true; }
            return changed;
        }
        public static SupportProgress Restore(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new SupportProgress();
            try {
                var p=JsonConvert.DeserializeObject<SupportProgress>(raw);
                if (p!=null && p.Schema==1 && (p.AppliedDay==-1 || RegistrationOpen(p.AppliedDay) && p.AuditorDay==p.AppliedDay+1)) return p;
            } catch (JsonException) { }
            return new SupportProgress { Blocked=true,PreservedData=raw };
        }
        [JsonIgnore] public string Data { get { return JsonConvert.SerializeObject(this); } }
    }

    public sealed class SupplyNoteSnapshot
    { public int Day,WaterPrice,JuicePrice,Wholesale; }

    public sealed class SupplyBatch
    {
        public string Id, Key;
        public int Day, Produced, PlayerInitial, StoreInitial, PlayerSold, StoreSold, IngredientCost, MakingCost;
        public int PlayerRemaining { get { return PlayerInitial-PlayerSold; } }
        public int StoreRemaining { get { return StoreInitial-StoreSold; } }
    }
    public sealed class SupplyTrade
    {
        public string Id, Direction, Key, BatchId="", Status="prepared";
        public int Day, ItemId, Count, Price, BeforeCash;
    }
    public sealed class BarStockUse { public string Id, BatchId;public bool Cancelled, Settled; }

    // Own ledger only: no native rent, debt, reputation or W03 order revenue.
    public sealed class SupplyProgress
    {
        public const string SaveKey="tavern_supply_v1";
        public int Schema=1,SignedDay=-1,Cash=SupplyConfig.InitialCash,Water,Juice;
        public bool IntroSeen,Blocked;
        public string PreservedData="";
        public List<SupplyBatch> Batches=new List<SupplyBatch>();
        public List<SupplyTrade> Trades=new List<SupplyTrade>();
        public List<BarStockUse> BarUses=new List<BarStockUse>();
        public static int Week(int day) { return (day-1)/7; }
        public bool Signed { get { return SignedDay>=SupplyConfig.UnlockDay; } }
        public bool SignedBy(int day) { return Signed && SignedDay<=day; }
        public SupplyTrade Pending { get { return Trades.FirstOrDefault(t => t.Status!="complete" && t.Status!="cancelled"); } }
        public bool Sign(int day) { if (Blocked || Signed || day<SupplyConfig.UnlockDay) return false;SignedDay=day;return true; }
        public int BoughtThisWeek(int day,string key=null)
        { return Trades.Where(t => t.Status=="complete" && t.Direction=="in" && Week(t.Day)==Week(day) && (key==null || t.Key==key)).Sum(t => key==null ? t.Price : t.Count); }
        public int InputPrice(string key) { return key==SupplyConfig.WaterItem ? SupplyConfig.WaterPrice : key==SupplyConfig.JuiceItem ? SupplyConfig.JuicePrice : 0; }
        public bool MayReceive(int day,string key,int count)
        {
            int price=InputPrice(key),limit=key==SupplyConfig.WaterItem ? SupplyConfig.WaterLimit : SupplyConfig.JuiceLimit;
            return !Blocked && Signed && Pending==null && count>0 && price>0 && (long)price*count<=Cash
                && (long)BoughtThisWeek(day)+price*count<=SupplyConfig.WeeklyBudget && BoughtThisWeek(day,key)+count<=limit;
        }
        public SupplyTrade PrepareInput(string run,int day,string key,int item,int count,int cash)
        {
            string id=run+":supply_in:"+day+":"+item;
            if (item<=0 || Trades.Any(t => t.Id==id) || !MayReceive(day,key,count) || cash>int.MaxValue-InputPrice(key)*count) return null;
            var t=new SupplyTrade { Id=id,Day=day,Direction="in",Key=key,ItemId=item,Count=count,Price=InputPrice(key)*count,BeforeCash=cash };
            Trades.Add(t);return t;
        }
        public SupplyTrade PrepareOutput(string run,int day,string batchId,int item,int cash)
        {
            var b=Batches.FirstOrDefault(v => v.Id==batchId);
            if (Blocked || !Signed || Pending!=null || item<=0 || cash<SupplyConfig.Wholesale || b==null || b.PlayerRemaining<=0) return null;
            var t=new SupplyTrade { Id=run+":supply_out:"+day+":"+item,Day=day,Direction="out",Key=b.Key,BatchId=b.Id,
                ItemId=item,Count=1,Price=SupplyConfig.Wholesale,BeforeCash=cash };
            if (Trades.Any(v => v.Id==t.Id)) return null;
            Trades.Add(t);return t;
        }
        public bool Complete(SupplyTrade t)
        {
            if (t==null || !ReferenceEquals(t,Pending) || t.Status=="uncertain") return false;
            if (t.Direction=="in") {
                if (Cash<t.Price) return false;
                Cash-=t.Price;
                if (t.Key==SupplyConfig.WaterItem) Water+=t.Count;else Juice+=t.Count;
            } else {
                var batch=Batches.FirstOrDefault(b => b.Id==t.BatchId);
                if (batch==null || batch.PlayerRemaining<1 || Cash>int.MaxValue-t.Price) return false;
                batch.PlayerSold++;Cash+=t.Price;
            }
            t.Status="complete";return true;
        }
        public int Produce(int day)
        {
            if (Blocked || Pending!=null || !Signed) return 0;
            int made=0;
            while (Water>=SupplyConfig.RecipeWater && Juice>=SupplyConfig.RecipeJuice && Cash>=SupplyConfig.MakingCost) {
                Water-=SupplyConfig.RecipeWater;Juice-=SupplyConfig.RecipeJuice;Cash-=SupplyConfig.MakingCost;
                int player=SupplyConfig.Output*SupplyConfig.PlayerNumerator/SupplyConfig.Denominator;
                Batches.Add(new SupplyBatch { Id=SupplyConfig.Source+":"+Batches.Count,Day=day,Key=StoryRules.BasicDrinks[Batches.Count%3],
                    Produced=SupplyConfig.Output,PlayerInitial=player,StoreInitial=SupplyConfig.Output-player,
                    IngredientCost=SupplyConfig.RecipeWater*SupplyConfig.WaterPrice+SupplyConfig.RecipeJuice*SupplyConfig.JuicePrice,MakingCost=SupplyConfig.MakingCost });
                made++;
            }
            return made;
        }
        public bool ReserveBar(string receipt,string key)
        {
            var old=BarUses.FirstOrDefault(u => u.Id==receipt);
            if (old!=null && !old.Cancelled) return true;
            if (old!=null) BarUses.Remove(old);
            var b=Batches.FirstOrDefault(v => v.Key==key && v.StoreRemaining>0);
            if (b==null || Blocked) return false;
            b.StoreSold++;BarUses.Add(new BarStockUse { Id=receipt,BatchId=b.Id });return true;
        }
        public void SettleBar(string receipt,int revenue)
        {
            var use=BarUses.FirstOrDefault(u => u.Id==receipt && !u.Cancelled);
            if (use==null || use.Settled || revenue<0 || Cash>int.MaxValue-revenue) return;
            Cash+=revenue;use.Settled=true;
        }
        public void CancelBar(string receipt)
        {
            var use=BarUses.FirstOrDefault(u => u.Id==receipt && !u.Cancelled);
            if (use==null) return;
            Batches.First(b => b.Id==use.BatchId).StoreSold--;use.Cancelled=true;
        }
        public static SupplyProgress Restore(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new SupplyProgress();
            try {
                var p=JsonConvert.DeserializeObject<SupplyProgress>(raw);
                if (p!=null && p.Schema==1 && p.Cash>=0 && p.Water>=0 && p.Juice>=0 && p.Trades!=null && p.Batches!=null && p.BarUses!=null
                    && p.Trades.All(t => t!=null && t.Count>0 && t.Price>0 && t.ItemId>0 && new[]{"prepared","moving","paid","complete","cancelled","uncertain"}.Contains(t.Status))
                    && p.Trades.GroupBy(t => t.Id).All(g => g.Count()==1) && p.Trades.Count(t => t.Status!="complete" && t.Status!="cancelled")<=1
                    && p.Batches.All(b => b!=null && !string.IsNullOrEmpty(b.Id)) && p.BarUses.All(u => u!=null && !string.IsNullOrEmpty(u.Id))
                    && p.Batches.GroupBy(b => b.Id).All(g => g.Count()==1)
                    && p.Batches.All(b => b!=null && b.Produced==b.PlayerInitial+b.StoreInitial && b.PlayerRemaining>=0 && b.StoreRemaining>=0 && b.PlayerSold>=0 && b.StoreSold>=0)
                    && p.BarUses.GroupBy(u => u.Id).All(g => g.Count()==1)) return p;
            } catch (JsonException) { }
            return new SupplyProgress { Blocked=true,PreservedData=raw };
        }
        [JsonIgnore] public string Data { get { return JsonConvert.SerializeObject(this); } }
    }
}
