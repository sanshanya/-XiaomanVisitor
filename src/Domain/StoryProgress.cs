using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    public sealed class StoryLineSpec
    {
        public readonly string Key;
        public readonly Mood Mood;
        public StoryLineSpec(string key, Mood mood) { Key = key; Mood = mood; }
    }

    public sealed class StoryEpisode
    {
        public readonly int Index, Credits;
        public readonly XiaomanDemand Demand;
        public readonly string TrackKey, AcceptKey, GoodbyeKey, CocktailKey;
        public readonly StoryLineSpec[] Opening;
        public string NoteId { get { return "story_note_" + Index.ToString("00"); } }
        public bool IsFinal { get { return Index == 14; } }
        public StoryEpisode(int index, int credits, XiaomanDemand demand, string track, string accept,
            string goodbye, string cocktail, StoryLineSpec[] opening)
        { Index = index; Credits = credits; Demand = demand; TrackKey = track; AcceptKey = accept;
            GoodbyeKey = goodbye; CocktailKey = cocktail; Opening = opening; }
    }

    public sealed class StoryActionSpec
    {
        public readonly string NativeKey, Key;
        public readonly Mood Mood;
        public readonly bool IsSfx;
        public readonly string[] OriginalTexts;
        public StoryActionSpec(string native, string key, Mood mood, bool sfx, string[] originals)
        { NativeKey = native; Key = key; Mood = mood; IsSfx = sfx; OriginalTexts = originals; }
    }

    public static class StoryRules
    {
        // The current story has a fixed ending. This is encounter progress, not day / 7.
        public static bool BusinessOpen(int visits) { return visits<13; }
        public static readonly string[] BasicDrinks = { "whale_dream", "sunset_blue", "rainy_blue" };
        public static readonly string[] FinalDrinks = { "bluebird", "cosmo_zone", "sky_after_rain" };
        public static bool IsMonday(int day) { return day >= 1 && day % 7 == 1; }
        public static StoryEpisode Episode(int index)
        { return index >= 1 && index <= 14 ? StoryCatalog.Episodes[index - 1] : null; }
        public static string[] SaleOffer(int index, int chanceRoll, int pairRoll)
        {
            if (index < 4 || index == 6 || index >= 13 || (index != 4 && (uint)chanceRoll % 100 >= 30)) return new string[0];
            int excluded = (int)((uint)pairRoll % 3);
            return BasicDrinks.Where((key, i) => i != excluded).ToArray();
        }
        public static StoryActionSpec ActionForText(string text)
        {
            if (text == null) return null;
            return StoryCatalog.Actions.FirstOrDefault(a => a.OriginalTexts.Contains(text, StringComparer.Ordinal));
        }
        public static string GoodbyeKey(int index, bool actualDiscount)
        { return index >= 13 ? Episode(index).GoodbyeKey : actualDiscount ? "goodbye_discount" : Episode(index).GoodbyeKey; }
        public static bool AllowsTradeDrink(int index) { return index != 6 && index < 13; }
        public static string FixedDrink(int index, int roll)
        { return index == 6 ? FinalDrinks[(int)((uint)roll % (uint)FinalDrinks.Length)] : ""; }
    }

    // Multiple item callbacks may precede a single batch's dialogue. Only actual
    // display of the acceptance line consumes the visit's first-demand response.
    public sealed class StoryTradeFlow
    {
        public bool DemandAcceptancePlayed { get; private set; }
        public bool LastWasDemand { get; private set; }
        public bool SelectDemand() { LastWasDemand = true; return !DemandAcceptancePlayed; }
        public void SelectCocktail() { LastWasDemand = false; }
        public void DemandAcceptanceDisplayed() { DemandAcceptancePlayed = true; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StoryVisitRecord
    {
        [JsonProperty] public int Index;
        [JsonProperty] public int Day;
        [JsonProperty] public int GiftMask;
        [JsonProperty] public string[] SaleKeys = new string[0];
        [JsonProperty] public string GiftDrinkKey = "";
        [JsonProperty] public string NoteVariant = "";
        [JsonProperty] public SupplyNoteSnapshot SupplySnapshot;
        public bool HasFixedDrink { get { return Index == 6 && StoryRules.FinalDrinks.Contains(GiftDrinkKey); } }
        public int RequiredMask { get { return Index == 14 ? 31 : HasFixedDrink ? 7 : 3; } }
        public bool GiftComplete { get { return GiftMask == RequiredMask; } }
        public int PartCount { get { return Index == 14 ? 5 : HasFixedDrink ? 3 : 2; } }
        public bool HasPart(int part) { return (GiftMask & (1 << part)) != 0; }
    }

    // Story visits are committed by the first displayed chapter line, never by a trade.
    // A single persisted mask records each physical gift separately, including partial retries.
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StoryProgress
    {
        [JsonProperty] public readonly List<StoryVisitRecord> Visits = new List<StoryVisitRecord>();
        [JsonProperty] public int LastCompletedGiftDay = -1;
        public int VisitIndex { get { return Visits.Count; } }
        public bool Finished { get { return VisitIndex == 14; } }
        public int LastVisitDay { get { return Visits.Count == 0 ? -1 : Visits[Visits.Count - 1].Day; } }
        public bool MayVisit(int day) { return StoryRules.IsMonday(day) && !Finished && day > LastVisitDay; }

        public bool BeginVisit(int day, int chanceRoll, int pairRoll, out StoryVisitRecord visit)
        {
            visit = null;
            if (!MayVisit(day)) return false;
            int index = VisitIndex + 1;
            visit = new StoryVisitRecord { Index = index, Day = day, SaleKeys = StoryRules.SaleOffer(index, chanceRoll, pairRoll),
                GiftDrinkKey = StoryRules.FixedDrink(index, pairRoll) };
            Visits.Add(visit);
            return true;
        }

        public StoryVisitRecord DueGift(int today)
        {
            if (today <= LastCompletedGiftDay) return null;
            return Visits.FirstOrDefault(v => !v.GiftComplete && today > v.Day);
        }

        public bool MarkPartDelivered(int index, int part, int today)
        {
            StoryVisitRecord record = DueGift(today);
            if (record == null || record.Index != index || part < 0 || part >= record.PartCount || record.HasPart(part)) return false;
            record.GiftMask |= 1 << part;
            if (record.GiftComplete) LastCompletedGiftDay = today;
            return true;
        }

        public bool FreezeThirdNote(int today,Func<int,bool> paidOnDay)
        {
            var visit=Visits.FirstOrDefault(v => v.Index==3);
            if (visit==null || today<=visit.Day || !string.IsNullOrEmpty(visit.NoteVariant)) return false;
            visit.NoteVariant=visit.HasPart(0) ? "A" : paidOnDay(visit.Day) ? "B" : "A";
            return true;
        }

        public bool FreezeFourthNote(int today,Func<int,bool> signedBy)
        {
            var v=Visits.FirstOrDefault(x => x.Index==4);
            if (v==null || today<=v.Day || !string.IsNullOrEmpty(v.NoteVariant)) return false;
            v.NoteVariant=v.HasPart(0) ? "A" : signedBy(v.Day) ? "B" : "A";return true;
        }

        public string Data { get { return JsonConvert.SerializeObject(this); } }
        public static StoryProgress Restore(string data)
        {
            if (string.IsNullOrEmpty(data)) return new StoryProgress();
            StoryProgress loaded;
            try { loaded = JsonConvert.DeserializeObject<StoryProgress>(data); }
            catch (JsonException) { return new StoryProgress(); }
            if (loaded == null) return new StoryProgress();
            var result = new StoryProgress();
            foreach (StoryVisitRecord visit in loaded.Visits.Take(14))
            {
                if (visit == null || visit.Index != result.Visits.Count + 1 || !StoryRules.IsMonday(visit.Day)
                    || visit.Day <= result.LastVisitDay) break;
                // Old completed sixth-visit bundles stay completed. Pending ones
                // gain a deterministic fixed drink once, without rerolling on load.
                if (visit.Index == 6)
                {
                    if (!StoryRules.FinalDrinks.Contains(visit.GiftDrinkKey))
                        visit.GiftDrinkKey = (visit.GiftMask & 3) == 3 ? "" : StoryRules.FixedDrink(6, visit.Day);
                }
                else visit.GiftDrinkKey = "";
                if (visit.Index==3 || visit.Index==4) {
                    if (visit.NoteVariant!="A" && visit.NoteVariant!="B") visit.NoteVariant=visit.HasPart(0)?"A":"";
                }
                else visit.NoteVariant="";
                visit.GiftMask &= visit.RequiredMask;
                visit.SaleKeys = visit.SaleKeys ?? new string[0];
                if (visit.SaleKeys.Length != 0 && (visit.SaleKeys.Length != 2 || visit.SaleKeys.Distinct().Count() != 2
                    || visit.SaleKeys.Any(key => !StoryRules.BasicDrinks.Contains(key)))) visit.SaleKeys = new string[0];
                if (visit.Index < 4 || visit.Index == 6 || visit.Index >= 13) visit.SaleKeys = new string[0];
                result.Visits.Add(visit);
            }
            result.LastCompletedGiftDay = loaded.LastCompletedGiftDay;
            return result;
        }
    }
}
