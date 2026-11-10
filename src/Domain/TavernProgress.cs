using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    public static class TavernMapRules
    {
        public const string LocationId = XiaomanMod.Id + ".tavern";
        public const string PreviewKey = "xiaoman_tavern_map";
        public const string ImageKey = XiaomanMod.Id + ":" + PreviewKey;

        public static bool UsesRenovationLabel(string locationId, bool enabled)
        { return enabled && locationId == LocationId; }
    }

    public static class TavernRules
    {
        public const string ExperimentalConfigKey = "experimental_tavern";
        public const bool ExperimentalDefault = false;
        public const int OpeningDay = 8;
        public const int DiscoveryDay = 3;
        public const string SaveKey = "tavern_v1_progress";
        public const string SceneFolder = "Scenes/Tavern/";
        public static bool IsDiscovered(bool enabled, bool hasRun, int day)
        { return enabled && hasRun && day >= DiscoveryDay; }
        public static bool IsOpen(bool enabled, bool hasRun, int day, bool departed)
        { return enabled && hasRun && day >= OpeningDay && !departed; }
        public static string OpeningLineKey(int visit, string key, int day)
        {
            if (visit==5 && day>29 && new[]{"xm_05_01","xm_05_02","xm_05_03"}.Contains(key)) return key+"_late";
            if (visit==7 && day>43 && key=="xm_07_03" || visit==8 && day>50 && key=="xm_08_02") return key+"_late";
            if (visit==9 && day>57 && key=="xm_09_02") return key+"_late";
            if (visit==10 && day>64 && key=="xm_10_01") return key+"_late";
            return visit==2 && key=="xm_02_02" && day>OpeningDay ? "xm_02_02_late" : key;
        }
        public static bool OpeningWeek(int day) { return day >= OpeningDay && day <= 14; }
        public static string MusicKey(int visits)
        { return StoryCatalog.Episodes[Math.Max(1,Math.Min(StoryCatalog.Episodes.Length,visits))-1].TrackKey; }
        public static string StatusKey(string id, bool enabled, bool hasRun, int day, bool departed)
        {
            if (!enabled || id != TavernMapRules.LocationId) return null;
            if (departed) return "ui_tavern_closed";
            return IsOpen(enabled, hasRun, day, departed) ? null : "ui_tavern_renovating";
        }
        // The author's reference canvas is shared by every layer.
        public static float IdleLift(float elapsed, bool animate)
        {
            if (!animate || elapsed < 0 || float.IsNaN(elapsed) || float.IsInfinity(elapsed)) return 0;
            double phase = (elapsed % TavernSceneLayout.BreathPeriod) / TavernSceneLayout.BreathPeriod;
            return TavernSceneLayout.BreathLift * .5f * (1f - (float)Math.Cos(2 * Math.PI * phase));
        }

        public static double FitScale(double width, double height)
        {
            if (double.IsNaN(width) || double.IsNaN(height) || double.IsInfinity(width)
                || double.IsInfinity(height) || width <= 0 || height <= 0) return 0;
            return Math.Min(width / (double)TavernSceneLayout.CanvasWidth, height / (double)TavernSceneLayout.CanvasHeight);
        }
        public static int FrameIndex(Mood mood, int mouth)
        { return Math.Max(0, Math.Min(5, (int)mood)) * 3 + Math.Max(0, Math.Min(2, mouth)); }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TavernEntry
    {
        [JsonProperty] public int Day;
        [JsonProperty] public string Action;
        [JsonProperty] public int Paid;
    }

    public enum TavernRecovery { None, Cancel, ApplyBuff, Finish, Hold }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TavernDrinkRecord
    {
        [JsonProperty] public string Id, Key, Status, BeforeBuffs, AfterBuffs;
        [JsonProperty] public int Day, Paid, BeforeCash;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TavernProgress
    {
        [JsonProperty] public int Schema = 2;
        [JsonProperty] public int OpenedOn = TavernRules.OpeningDay;
        [JsonProperty] public readonly List<TavernEntry> Entries = new List<TavernEntry>();
        [JsonProperty] public readonly List<TavernDrinkRecord> Drinks = new List<TavernDrinkRecord>();
        [JsonProperty] public readonly List<string> ChatSeen = new List<string>();
        [JsonProperty] public bool IntroSeen, UnknownIntroSeen, RainIntroSeen, TapeIntroSeen, OpeningReceptionHelped, RecoveryBlocked;
        [JsonProperty] public int LastEntryDay = -1, EntriesToday;
        [JsonProperty] public string SelectedTrack = "title", LegacyData;
        public TavernDrinkRecord Pending { get { return Drinks.FirstOrDefault(d => d.Status != "complete" && d.Status != "cancelled"); } }
        public bool Drank(int day) { return Drinks.Any(d => d.Day == day && d.Status != "cancelled"); }
        public bool MayDrink(int day) { return day >= TavernRules.OpeningDay && !RecoveryBlocked && Pending == null && !Drank(day); }
        public bool MayHelp(int day) { return TavernRules.OpeningWeek(day) && !OpeningReceptionHelped; }
        public bool Help(int day)
        {
            if (!MayHelp(day)) return false;
            OpeningReceptionHelped = true;
            return true;
        }
        public string[] Enter(int day, bool known)
        {
            if (day < TavernRules.OpeningDay) return new string[0];
            EntriesToday = LastEntryDay == day ? Math.Min(int.MaxValue-1,EntriesToday)+1 : 1;
            LastEntryDay = day;
            if (known && !IntroSeen) return new[] { "xm_tavern_w02_entry_01", "xm_tavern_w02_entry_02", "xm_tavern_w02_entry_03" };
            if (!known && !UnknownIntroSeen) return new[] { "xm_tavern_greeting_unknown" };
            return new[] { known ? EntriesToday == 2 ? "xm_tavern_w02_reentry" : "xm_tavern_greeting" : "xm_tavern_greeting_plain" };
        }
        public static string ChatReceipt(string key,int day)
        { return key=="xm_tavern_w03_chat_after_work" ? key+"@"+day : key; }
        public string NextChat(int day,bool workedToday=false,int visits=0,int encounterDay=-1)
        {
            var weeklyPool=TavernDialogue.WeeklyChatPool(day,visits);
            if (weeklyPool!=null) return weeklyPool.FirstOrDefault(k => !ChatSeen.Contains(k)
                && TavernDialogue.ChatReady(k,day,encounterDay)) ?? "xm_tavern_chat_rest";
            if (day>=15) {
                string after="xm_tavern_w03_chat_after_work";
                if (workedToday && !ChatSeen.Contains(ChatReceipt(after,day))) return after;
                return new[]{"xm_tavern_w03_chat_01","xm_tavern_w03_chat_02"}.FirstOrDefault(k => !ChatSeen.Contains(k)) ?? "xm_tavern_chat_rest";
            }
            string[] pool = day == 8 ? new[]{"xm_tavern_w02_chat_01"}
                : day >= 9 && day <= 11 ? new[]{"xm_tavern_w02_chat_02","xm_tavern_w02_chat_03"}
                : day >= 12 && day <= 14 ? new[]{"xm_tavern_w02_chat_04","xm_tavern_w02_chat_03"} : new string[0];
            return pool.FirstOrDefault(key => !ChatSeen.Contains(key)) ?? "xm_tavern_chat_rest";
        }
        public TavernDrinkRecord PrepareDrink(int day, string key, int cash, string beforeBuffs)
        {
            CocktailSpec spec = CocktailCatalog.Find(key);
            if (spec == null || !LuxuryDrinkCatalog.OnMenu(key,day) || !MayDrink(day) || cash < TavernPrices.For(spec)) return null;
            var buffs = DrinkBuffCollection.Restore(beforeBuffs);
            buffs.Replace(spec,day);
            var record = new TavernDrinkRecord { Id = "tavern_drink_"+day, Day=day, Key=key, Paid=TavernPrices.For(spec),
                Status="prepared", BeforeCash=cash, BeforeBuffs=beforeBuffs ?? "", AfterBuffs=buffs.Data };
            Drinks.RemoveAll(d => d.Day == day && d.Status == "cancelled");
            Drinks.Add(record);
            return record;
        }
        public TavernRecovery Recover(int today, int cash, string buffs)
        {
            var pending = Pending;
            if (pending == null) return TavernRecovery.None;
            if (RecoveryBlocked || pending.Status == "uncertain" || pending.Day != today) return TavernRecovery.Hold;
            bool before = (buffs ?? "") == pending.BeforeBuffs, after = (buffs ?? "") == pending.AfterBuffs;
            if (pending.Status == "prepared" && cash == pending.BeforeCash && before) return TavernRecovery.Cancel;
            if (cash != pending.BeforeCash-pending.Paid) return TavernRecovery.Hold;
            return after ? TavernRecovery.Finish : before ? TavernRecovery.ApplyBuff : TavernRecovery.Hold;
        }
        public string Data { get { return JsonConvert.SerializeObject(this); } }
        public static TavernProgress Restore(string data, int today)
        {
            var clean = new TavernProgress();
            if (string.IsNullOrEmpty(data)) return clean;
            TavernProgress loaded;
            try { loaded = JsonConvert.DeserializeObject<TavernProgress>(data); }
            catch (JsonException) { return Blocked(data); }
            if (loaded == null || loaded.Entries == null || loaded.Drinks == null || loaded.ChatSeen == null) return Blocked(data);
            if (loaded.Schema == 1) {
                clean.LegacyData = data; // Exact prototype history, including original day-one timestamps.
                clean.Entries.AddRange(loaded.Entries.Where(e => e != null));
                return clean; // A pickup_point is never the new opening event; old cups are not new drinks.
            }
            if (loaded.Schema != 2 || loaded.LastEntryDay > today || loaded.EntriesToday < 0) return Blocked(data);
            if (loaded.Drinks.Any(d => d == null || d.Day < 8 || d.Day > today || d.Paid < 1 || d.BeforeCash < d.Paid
                || d.Id != "tavern_drink_"+d.Day || CocktailCatalog.Find(d.Key) == null || d.BeforeBuffs == null || d.AfterBuffs == null
                || !new[]{"prepared","charged","complete","cancelled","uncertain"}.Contains(d.Status))
                || loaded.Drinks.GroupBy(d => d.Day).Any(g => g.Count() > 1)) return Blocked(data);
            foreach (var record in loaded.Drinks) {
                var expected=DrinkBuffCollection.Restore(record.BeforeBuffs);
                expected.Replace(CocktailCatalog.Find(record.Key),record.Day);
                if (expected.Data != record.AfterBuffs) return Blocked(data);
            }
            loaded.OpenedOn = TavernRules.OpeningDay;
            if (CassetteCollection.Find(loaded.SelectedTrack) == null) loaded.SelectedTrack = "title";
            return loaded;
        }
        private static TavernProgress Blocked(string data)
        { return new TavernProgress { RecoveryBlocked=true, LegacyData=data }; }
    }
}
