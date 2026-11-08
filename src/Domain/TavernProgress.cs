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
        // Development test access; the planned release opening remains day 3.
        public const int OpeningDay = 1;
        public const int CupPrice = 35;
        public const string SaveKey = "tavern_v1_progress";
        public const string SceneFolder = "Scenes/Tavern/";
        public static bool IsOpen(bool enabled, bool hasRun, int day, bool departed)
        { return enabled && hasRun && day >= OpeningDay && !departed; }
        public static string StatusKey(string id, bool enabled, bool hasRun, int day, bool departed)
        {
            if (!enabled || id != TavernMapRules.LocationId) return null;
            if (departed) return "ui_tavern_closed";
            return !hasRun || day < OpeningDay ? "ui_tavern_renovating" : null;
        }
        // The author's reference canvas is shared by every layer.
        public static double FitScale(double width, double height)
        {
            if (double.IsNaN(width) || double.IsNaN(height) || double.IsInfinity(width)
                || double.IsInfinity(height) || width <= 0 || height <= 0) return 0;
            return Math.Min(width / 5016.0, height / 2823.0);
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

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TavernProgress
    {
        [JsonProperty] public int Schema = 1;
        [JsonProperty] public int OpenedOn;
        [JsonProperty] public readonly List<TavernEntry> Entries = new List<TavernEntry>();
        public int Cups { get { return Entries.Count(e => e.Action == "cup"); } }
        public long Receipts { get { return Entries.Sum(e => (long)e.Paid); } }
        public bool PickupPointReady { get { return Entries.Any(e => e.Action == "pickup_point"); } }
        public bool MayAct(int day)
        { return day >= OpenedOn && day >= TavernRules.OpeningDay && !Entries.Any(e => e.Day == day); }
        public bool MayBuy(int day, int cash) { return MayAct(day) && cash >= TavernRules.CupPrice; }
        public bool MayHelp(int day) { return MayAct(day) && !PickupPointReady; }
        public bool Buy(int day, int cash)
        {
            if (!MayBuy(day, cash)) return false;
            Entries.Add(new TavernEntry { Day = day, Action = "cup", Paid = TavernRules.CupPrice });
            return true;
        }
        public bool Help(int day)
        {
            if (!MayHelp(day)) return false;
            Entries.Add(new TavernEntry { Day = day, Action = "pickup_point", Paid = 0 });
            return true;
        }
        public string Data { get { return JsonConvert.SerializeObject(this); } }
        public static TavernProgress Restore(string data, int today)
        {
            // Existing runs open on installation day, without retroactive debt.
            var clean = new TavernProgress { OpenedOn = Math.Max(TavernRules.OpeningDay, today) };
            if (string.IsNullOrEmpty(data)) return clean;
            TavernProgress loaded;
            try { loaded = JsonConvert.DeserializeObject<TavernProgress>(data); }
            catch (JsonException) { return clean; }
            if (loaded == null || loaded.Entries == null || loaded.Schema != 1 || loaded.OpenedOn < TavernRules.OpeningDay
                || loaded.OpenedOn > Math.Max(TavernRules.OpeningDay, today)) return clean;
            clean.OpenedOn = loaded.OpenedOn;
            foreach (TavernEntry entry in loaded.Entries)
            {
                if (entry == null || entry.Day < clean.OpenedOn || entry.Day > today
                    || clean.Entries.Any(e => e.Day == entry.Day)) continue;
                if (entry.Action == "cup" && entry.Paid == TavernRules.CupPrice
                    || entry.Action == "pickup_point" && entry.Paid == 0 && !clean.PickupPointReady)
                    clean.Entries.Add(entry);
            }
            clean.Entries.Sort((a, b) => a.Day.CompareTo(b.Day));
            return clean;
        }
    }
}
