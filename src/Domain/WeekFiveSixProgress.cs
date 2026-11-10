using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    // Separate from next-day story gifts and the six-drink random reward pool.
    public sealed class ImprovGiftProgress
    {
        public const string SaveKey="w05_improv_gift_v1";
        public int Schema=1, VisitDay=-1, ItemId;
        public string Phase="waiting", PreservedData="", Source="private_improv_visit_5";
        public bool Blocked;
        public bool Prepare(int item)
        { if (Blocked || VisitDay<1 || Phase!="waiting" || item<=0) return false;ItemId=item;Phase="placing";return true; }
        public void Placed(bool placed) { if (Phase=="placing") { Phase=placed?"complete":"waiting";if (!placed) ItemId=0; } }
        public void Recover(bool found) { if (Phase=="placing") Phase=found?"complete":"uncertain"; }
        [JsonIgnore] public string Data { get { return JsonConvert.SerializeObject(this); } }
        public static ImprovGiftProgress Restore(string raw,string source="private_improv_visit_5")
        {
            if (string.IsNullOrEmpty(raw)) return new ImprovGiftProgress { Source=source };
            try {
                var p=JsonConvert.DeserializeObject<ImprovGiftProgress>(raw);
                if (p!=null && p.Schema==1 && (p.VisitDay==-1 || p.VisitDay>=1) && p.ItemId>=0 && p.Source==source
                    && new[]{"waiting","placing","complete","uncertain"}.Contains(p.Phase)
                    && (p.Phase=="waiting" || p.ItemId>0 && p.VisitDay>0)) return p;
            } catch (JsonException) { }
            return new ImprovGiftProgress { Blocked=true,PreservedData=raw };
        }
    }

    public sealed class ReferralDay
    {
        public int Day, Roll, ArrivedDay=-1, Budget;
        public bool Won, Guaranteed, Upper, ProfileReady;
        public string Sprite="";
        public string Id { get { return ReferralProgress.Prefix+Day; } }
    }
    public sealed class ReferralProgress
    {
        public const string SaveKey="w06_referrals_v1", Prefix=XiaomanMod.Id+".referral.";
        public int Schema=1;
        public string Run="", PreservedData="";
        public bool Blocked;
        public List<ReferralDay> Days=new List<ReferralDay>();
        public static bool Eligible(int day) { return day>=ReferralConfig.UnlockDay && !StoryRules.IsMonday(day); }
        public bool Draw(int day)
        {
            if (Blocked || !Eligible(day) || Days.Any(d => d.Day==day)) return false;
            int roll=TavernWorkProgress.StableRoll(Run,day.ToString(),"referral-chance")%100;
            bool sunday=day%7==0, guarantee=sunday && !Days.Any(d => SupplyProgress.Week(d.Day)==SupplyProgress.Week(day) && d.Won);
            Days.Add(new ReferralDay { Day=day,Roll=roll,Guaranteed=guarantee,Won=guarantee || roll<ReferralConfig.DailyPercent,
                Upper=TavernWorkProgress.StableRoll(Run,day.ToString(),"referral-kind")%2==1 });
            return true;
        }
        public ReferralDay Due(int day)
        {
            return Blocked || !Eligible(day) || Days.Any(d => d.ArrivedDay==day) ? null
                : Days.Where(d => d.Won && d.ArrivedDay<0 && d.Day<=day).OrderBy(d => d.Day).FirstOrDefault();
        }
        public bool Arrive(string id,int day)
        {
            var due=Due(day);if (due==null || due.Id!=id) return false;
            due.ArrivedDay=day;return true;
        }
        [JsonIgnore] public string Data { get { return JsonConvert.SerializeObject(this); } }
        public static ReferralProgress Restore(string raw,string run,int today)
        {
            if (string.IsNullOrEmpty(raw)) return new ReferralProgress { Run=run };
            try {
                var p=JsonConvert.DeserializeObject<ReferralProgress>(raw);
                if (p!=null && p.Schema==1 && p.Run==run && p.Days!=null
                    && p.Days.All(d => d!=null && Eligible(d.Day) && d.Day<=today && d.Roll>=0 && d.Roll<100
                        && d.Budget>=0 && d.Sprite!=null && (!d.ProfileReady || !string.IsNullOrEmpty(d.Sprite))
                        && (d.ArrivedDay==-1 || d.Won && d.ArrivedDay>=d.Day && d.ArrivedDay<=today && Eligible(d.ArrivedDay)))
                    && p.Days.GroupBy(d => d.Day).All(g => g.Count()==1)
                    && p.Days.Where(d => d.ArrivedDay>=0).GroupBy(d => d.ArrivedDay).All(g => g.Count()==1)) return p;
            } catch (JsonException) { }
            return new ReferralProgress { Run=run,Blocked=true,PreservedData=raw };
        }
    }
}
