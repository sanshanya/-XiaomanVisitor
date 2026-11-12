using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    public enum BrewingCollectionState { Ready, Blocked, Processing, WrongDay, Closed, NoAppointment, Collected, AwaitingArrival }
    public sealed class BrewingMaterial
    {
        public int ItemId, Count;
        public string Key;
        public long ValueCents;
    }
    public sealed class BrewingBatch
    {
        public string Id, Phase = "prepared";
        public int Day, DueDay, AgreementRevision;
        public List<BrewingMaterial> Materials = new List<BrewingMaterial>();
        public long InputCents, ReturnMilli;
        public bool Credited, RenewalAnswered;
    }
    public sealed class BrewingBottle
    {
        public string Id, Phase = "placing";
        public int ItemId, Value;
    }
    public sealed class BrewingProgress
    {
        public const string SaveKey = "tavern_brewing_v1";
        public const string ClientId = XiaomanMod.ClientId + ".brewing";
        public const string SampleId = XiaomanMod.Id + ".brewing.sample", BottleId = XiaomanMod.Id + ".brewing.bottle";
        public int Schema = 3, LastVisitDay = -1, NextCollectionDay = -1, CapacityStartWeek = -1, AgreementRevision;
        public bool Blocked, AgreementActive;
        public string PreservedData = "";
        public long CreditMilli;
        public List<BrewingBatch> Batches = new List<BrewingBatch>();
        public List<BrewingBottle> Bottles = new List<BrewingBottle>();
        [JsonIgnore] public BrewingBatch Pending { get { return Batches.FirstOrDefault(b => b.Phase == "prepared"); } }
        [JsonIgnore] public BrewingBottle Placing { get { return Bottles.FirstOrDefault(b => b.Phase == "placing"); } }
        [JsonIgnore] public BrewingBatch Renewal { get { return !Blocked && Placing==null && CreditMilli<BrewingConfig.SampleValue*1000L
            ? Batches.LastOrDefault(b => b.Phase=="complete" && b.Credited && !b.RenewalAnswered && b.AgreementRevision==AgreementRevision) : null; } }
        public static bool Wednesday(int day) { return day >= 3 && (day - 1) % 7 == 2; }
        public static bool Friday(int day) { return day >= 5 && (day - 1) % 7 == 4; }
        public static bool VisitDay(int day) { return Wednesday(day) || Friday(day); }
        public static int NextWednesday(int day) { int next=day+1;while (!Wednesday(next)) next++;return next; }
        public bool Sign(int day)
        {
            if (Blocked || day<BrewingConfig.UnlockDay || (AgreementActive && NextCollectionDay>day)) return false;
            AgreementActive=true;NextCollectionDay=NextWednesday(day);
            if (CapacityStartWeek<0) CapacityStartWeek=(NextCollectionDay-1)/7;
            AgreementRevision++;return true;
        }
        public bool Pause()
        {
            if (Blocked || !AgreementActive) return false;
            AgreementActive=false;NextCollectionDay=-1;AgreementRevision++;return true;
        }
        public bool ChangeAgreement(int day,int expectedRevision,bool active)
        {
            if (day<BrewingConfig.UnlockDay || expectedRevision!=AgreementRevision) return false;
            return active?Sign(day):Pause();
        }
        // The contract menu is visited at night; today's daytime appointment
        // has passed. A missed appointment stays valid for the next Wednesday.
        public int NextCollectionAfter(int day)
        {
            if (!AgreementActive || NextCollectionDay<BrewingConfig.UnlockDay) return -1;
            return NextCollectionDay>day?NextCollectionDay:NextWednesday(day);
        }
        public bool MayCollect(int day)
        { return !Blocked && AgreementActive && NextCollectionDay>=BrewingConfig.UnlockDay && day>=NextCollectionDay && Wednesday(day); }
        public int MaterialLimit(int day)
        {
            long weeks=CapacityStartWeek<0?0:Math.Max(0,(day-1)/7-CapacityStartWeek);
            return (int)Math.Min(BrewingConfig.MaximumMaterialLimit,BrewingConfig.InitialMaterialLimit+weeks*BrewingConfig.WeeklyMaterialIncrease);
        }
        public bool AcceptsValue(decimal cents,int day)
        { return cents>0 && cents<=MaterialLimit(day)*100m; }
        public BrewingCollectionState CollectionState(int day,bool businessOpen)
        {
            if (Blocked) return BrewingCollectionState.Blocked;
            if (day<BrewingConfig.UnlockDay || !Wednesday(day)) return BrewingCollectionState.WrongDay;
            if (!businessOpen) return BrewingCollectionState.Closed;
            if (Pending!=null) return BrewingCollectionState.Processing;
            if (Batches.Any(b => b.Day==day && b.Phase!="cancelled")) return BrewingCollectionState.Collected;
            if (!MayCollect(day)) return BrewingCollectionState.NoAppointment;
            return LastVisitDay==day?BrewingCollectionState.Ready:BrewingCollectionState.AwaitingArrival;
        }
        public bool CanPrepare(int day,List<BrewingMaterial> materials)
        {
            if (CollectionState(day,true)!=BrewingCollectionState.Ready || materials==null || materials.Count==0
                || materials.Any(m => m==null || m.ItemId<=0 || m.Count<=0 || m.ValueCents<=0)
                || materials.Select(m => m.ItemId).Distinct().Count()!=materials.Count) return false;
            decimal total=materials.Sum(m => (decimal)m.ValueCents);
            return AcceptsValue(total,day) && total<=long.MaxValue/(100+BrewingConfig.BonusPercent);
        }
        public bool ContinueAfterDelivery(string batchId,int revision,int day,bool yes)
        {
            if (!Friday(day) || revision!=AgreementRevision || Renewal?.Id!=batchId) return false;
            if (!ChangeAgreement(day,revision,yes)) return false;
            foreach (var batch in Batches.Where(b => b.Credited)) batch.RenewalAnswered=true;
            return true;
        }
        public bool MayVisit(int day, bool businessOpen)
        {
            return !Blocked && day>=BrewingConfig.UnlockDay && LastVisitDay!=day
                && (businessOpen && MayCollect(day) || Friday(day) && (CreditMilli>=BrewingConfig.SampleValue*1000L
                    || Batches.Any(b => b.Phase=="complete" && !b.Credited && b.DueDay<=day)));
        }
        public bool Arrive(int day)
        {
            if (Blocked || !VisitDay(day) || LastVisitDay == day || Wednesday(day) && !MayCollect(day)) return false;
            LastVisitDay = day; CreditDue(day); return true;
        }
        public BrewingBatch Prepare(string run, int day, List<BrewingMaterial> materials)
        {
            if (!CanPrepare(day,materials)) return null;
            long input=(long)materials.Sum(m => (decimal)m.ValueCents);
            var batch = new BrewingBatch { Id = run + ":brew:" + day + ":" + Batches.Count, Day = day, DueDay = day + BrewingConfig.DeliveryDays,
                AgreementRevision=AgreementRevision,
                Materials = materials, InputCents = input,
                ReturnMilli = checked(input * (100 + BrewingConfig.BonusPercent) / 10) };
            Batches.Add(batch); return batch;
        }
        public bool RecoverInput(IEnumerable<int> ownedIds)
        {
            var batch = Pending; if (Blocked || batch == null) return false;
            var owned = new HashSet<int>(ownedIds);
            int retained = batch.Materials.Count(m => owned.Contains(m.ItemId));
            batch.Phase = retained == 0 ? "complete" : retained == batch.Materials.Count ? "cancelled" : "uncertain";
            if (batch.Phase == "uncertain") Blocked = true;
            if (batch.Phase == "complete" && batch.AgreementRevision==AgreementRevision) NextCollectionDay=-1;
            return true;
        }
        public void CreditDue(int day, bool finalSettlement = false)
        {
            if (Blocked || !finalSettlement && !Friday(day)) return;
            foreach (var batch in Batches.Where(b => b.Phase == "complete" && !b.Credited && (finalSettlement || b.DueDay <= day))) {
                if (CreditMilli > long.MaxValue - batch.ReturnMilli) { Blocked = true; return; }
                CreditMilli += batch.ReturnMilli; batch.Credited = true;
            }
        }
        public BrewingBottle PrepareBottle(int itemId)
        {
            if (Blocked || Placing != null || itemId <= 0 || CreditMilli < BrewingConfig.SampleValue * 1000L) return null;
            var bottle = new BrewingBottle { Id = "brew_gift:" + Bottles.Count, ItemId = itemId,
                Value = CreditMilli >= BrewingConfig.BottleValue * 1000L ? BrewingConfig.BottleValue : BrewingConfig.SampleValue };
            Bottles.Add(bottle); return bottle;
        }
        public bool BottlePlaced(bool success)
        {
            var bottle = Placing; if (Blocked || bottle == null) return false;
            if (success) CreditMilli -= bottle.Value * 1000L;
            bottle.Phase = success ? "complete" : "cancelled"; return true;
        }
        public bool RecoverBottle(bool owned)
        {
            if (Placing == null || Blocked) return false;
            if (owned) return BottlePlaced(true);
            // Missing after a saved placing marker is ambiguous; never spawn twice.
            Placing.Phase = "uncertain"; Blocked = true; return true;
        }
        public static BrewingProgress Restore(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new BrewingProgress();
            try {
                var p = JsonConvert.DeserializeObject<BrewingProgress>(raw);
                if (p!=null && p.Schema==1 && p.Batches!=null) {
                    if (p.Batches.Any(b => b==null || b.DueDay!=b.Day+7)) return new BrewingProgress { Blocked=true,PreservedData=raw };
                    p.Schema=2;p.AgreementActive=false;p.NextCollectionDay=-1;p.AgreementRevision=0;
                    foreach (var b in p.Batches) { b.DueDay=b.Day+BrewingConfig.DeliveryDays;b.RenewalAnswered=true;b.AgreementRevision=0; }
                }
                if (p!=null && p.Schema==2 && p.Batches!=null) {
                    if (p.Batches.Any(b => b==null)) return new BrewingProgress { Blocked=true,PreservedData=raw };
                    p.Schema=3;
                    p.CapacityStartWeek=p.Batches.Count>0?(p.Batches.Min(b => b.Day)-1)/7:p.NextCollectionDay>=0?(p.NextCollectionDay-1)/7:-1;
                }
                if (p != null && p.Schema == 3 && p.CreditMilli >= 0 && p.Batches != null && p.Bottles != null && p.AgreementRevision>=0
                    && p.CapacityStartWeek>=-1 && (!p.AgreementActive || p.CapacityStartWeek>=0)
                    && (p.LastVisitDay == -1 || VisitDay(p.LastVisitDay))
                    && (p.NextCollectionDay==-1 || p.AgreementActive && p.NextCollectionDay>=BrewingConfig.UnlockDay && Wednesday(p.NextCollectionDay))
                    && p.Batches.All(b => b != null && !string.IsNullOrEmpty(b.Id) && Wednesday(b.Day) && b.DueDay == b.Day + BrewingConfig.DeliveryDays
                        && b.AgreementRevision>=0 && b.AgreementRevision<=p.AgreementRevision
                        && b.InputCents > 0 && b.ReturnMilli > 0 && b.Materials != null && b.Materials.Count > 0
                        && b.Materials.All(m => m != null && m.ItemId > 0 && m.Count > 0 && m.ValueCents > 0)
                        && b.Materials.Sum(m => m.ValueCents) == b.InputCents
                        && new[] { "prepared", "complete", "cancelled", "uncertain" }.Contains(b.Phase)
                        && (!b.Credited || b.Phase == "complete"))
                    && p.Batches.Select(b => b.Id).Distinct().Count() == p.Batches.Count && p.Batches.Count(b => b.Phase == "prepared") <= 1
                    && p.Bottles.All(b => b != null && !string.IsNullOrEmpty(b.Id) && b.ItemId > 0 && (b.Value == 10 || b.Value == 30 || b.Value == 150)
                        && new[] { "placing", "complete", "cancelled", "uncertain" }.Contains(b.Phase))
                    && p.Bottles.Select(b => b.Id).Distinct().Count() == p.Bottles.Count && p.Bottles.Count(b => b.Phase == "placing") <= 1
                    && p.Bottles.Where(b => b.Phase == "complete").Sum(b => b.Value * 1000L) + p.CreditMilli
                        == p.Batches.Where(b => b.Credited).Sum(b => b.ReturnMilli)) return p;
            } catch (Exception e) when (e is JsonException || e is OverflowException) { }
            return new BrewingProgress { Blocked = true, PreservedData = raw };
        }
        [JsonIgnore] public string Data { get { return JsonConvert.SerializeObject(this); } }
    }
}
