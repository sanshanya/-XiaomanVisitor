using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using XiaomanVisitor;

internal static class WeekOneChecks
{
    internal static void Run(string package, Action<bool, string> check)
    {
        check(!TavernRules.IsDiscovered(true, true, 1) && !TavernRules.IsDiscovered(true, true, 2)
            && TavernRules.IsDiscovered(true, true, 3) && TavernRules.IsDiscovered(true, true, 52),
            "map first appears on calendar day three, including existing runs");
        check(!TavernRules.IsDiscovered(false, true, 3) && !TavernRules.IsDiscovered(true, false, 3),
            "day-three discovery still respects the default-off setting and loaded-run gate");
        check(!WeekOneRules.HasRenovationNews(2) && WeekOneRules.HasRenovationNews(3)
            && WeekOneRules.HasRenovationNews(7) && !WeekOneRules.HasRenovationNews(8), "anonymous renovation news has a calendar-only first-week window");
        check(StoryRules.Episode(1).Opening.Select(l => l.Key).SequenceEqual(new[] {
            "xm_01_01", "xm_01_02", "xm_01_03", "xm_01_04", "xm_01_shop" }),
            "master merges cassette discovery and moving tapes before the shopping line");
        var pack = JsonConvert.DeserializeObject<VoicePackDocument>(File.ReadAllText(Path.Combine(package, "Voices/index.json")));
        var voices = new VoiceCatalog(pack.lines);
        check(pack.pending_lines >= 2 && voices.Preview("xm_01_03", "ja") == null && voices.Preview("xm_01_04", "ja") == null,
            "rewritten discovery and follow-up never play mismatched older recordings");
        check(new[] { "intro_1", "xm_01_accept", "xm_01_bye" }
            .All(k => voices.Preview(k, "ja") != null), "unchanged first-visit recordings remain available");
        var reward = new VisitTradeSummary(); reward.Record(1, 100, 100, 100, false);
        check(StoryRules.AllowsTradeDrink(1) && DrinkRewardRules.Select(reward) == "sunset_blue", "first-week qualifying trades still earn a drink");

        var original = new List<StoreEvent> { new StoreEvent { identifier = "native_story" } };
        var article = new StoreEvent { identifier = WeekOneRules.NewsId };
        var shown = XiaomanNewspaper.Append(original, article);
        check(original.Count == 1 && shown.Count == 2 && ReferenceEquals(shown[0], original[0]), "news appends to a view copy while preserving native stories and their order");
        check(ReferenceEquals(shown, XiaomanNewspaper.Append(shown, article)), "repeated population cannot duplicate the article");
        var full = Enumerable.Range(0, 4).Select(i => new StoreEvent { identifier = "native_" + i }).ToList();
        check(ReferenceEquals(full, XiaomanNewspaper.Append(full, article)), "full native newspaper never loses an existing article");

        Exception failure=null;
        check(ReferenceEquals(original,XiaomanNewspaper.DisplayEvents(original,() => { throw new InvalidOperationException("article unavailable"); },e => failure=e))
            && failure!=null && original.Count==1,"mod article failures preserve the complete native newspaper input");
        var quiet=new List<StoreEvent>();
        check(ReferenceEquals(quiet,XiaomanNewspaper.DisplayEvents(quiet,() => new StoreEvent[]{null})),
            "expired mod news leaves an empty native input intact for the game's AllQuiet fallback");
        var read=typeof(StoreEventManager).GetMethod(nameof(StoreEventManager.GetActiveEvents));
        var display=typeof(XiaomanNewspaper).GetMethod("GetDisplayEvents",BindingFlags.NonPublic|BindingFlags.Static);
        var instructions=PatchProcessor.GetOriginalInstructions(typeof(NewsUIManager).GetMethod(nameof(NewsUIManager.PopulateUI))).ToArray();
        var patched=XiaomanNewspaperPopulatePatch.Transpiler(instructions).ToArray();
        check(instructions.Count(i => i.Calls(read))==1 && patched.Count(i => i.Calls(display))==1 && !patched.Any(i => i.Calls(read))
            && patched.Length==instructions.Length,"installed native PopulateUI changes exactly its own event read, retaining its click, layout and AllQuiet flow");
        check(!typeof(XiaomanMod).Assembly.GetTypes().Where(t => t.IsDefined(typeof(HarmonyPatch),false))
            .Select(t => ((HarmonyPatch)t.GetCustomAttributes(typeof(HarmonyPatch),false).Single()).info)
            .Any(p => p.declaringType==typeof(StoreEventManager) && p.methodName==nameof(StoreEventManager.GetActiveEvents)),
            "newspaper has no process-wide GetActiveEvents hook or leaked rendering flag");
        Console.WriteLine("PASS: W01 dialogue, native newspaper read isolation, exception fallback, expiration and vanilla click/layout preservation.");
    }
}
