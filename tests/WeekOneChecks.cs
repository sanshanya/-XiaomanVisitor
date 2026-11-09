using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
            "xm_01_01", "xm_01_02", "xm_01_03", "xm_01_04", "xm_01_05", "xm_01_shop" }),
            "new cassette remarks follow the original voiced discovery before shopping");
        var pack = JsonConvert.DeserializeObject<VoicePackDocument>(File.ReadAllText(Path.Combine(package, "Voices/index.json")));
        var voices = new VoiceCatalog(pack.lines);
        check(pack.pending_lines >= 2 && voices.Preview("xm_01_04", "ja") == null && voices.Preview("xm_01_05", "ja") == null,
            "the two first-week additions remain explicitly deferred");
        check(new[] { "intro_1", "xm_01_02", "xm_01_03", "xm_01_shop", "xm_01_accept", "xm_01_bye" }
            .All(k => voices.Preview(k, "ja") != null), "all six unchanged first-visit recordings remain available");
        var reward = new VisitTradeSummary(); reward.Record(1, 100, 100, 100, false);
        check(StoryRules.AllowsTradeDrink(1) && DrinkRewardRules.Select(reward) == "sunset_blue", "first-week qualifying trades still earn a drink");

        var original = new List<StoreEvent> { new StoreEvent { identifier = "native_story" } };
        var article = new StoreEvent { identifier = WeekOneRules.NewsId };
        var shown = XiaomanNewspaper.Append(original, article);
        check(original.Count == 1 && shown.Count == 2 && ReferenceEquals(shown[0], original[0]), "news appends to a view copy while preserving native stories and their order");
        check(ReferenceEquals(shown, XiaomanNewspaper.Append(shown, article)), "repeated population cannot duplicate the article");
        var full = Enumerable.Range(0, 4).Select(i => new StoreEvent { identifier = "native_" + i }).ToList();
        check(ReferenceEquals(full, XiaomanNewspaper.Append(full, article)), "full native newspaper never loses an existing article");

        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var rendering = typeof(XiaomanNewspaper).GetField("Rendering", flags);
        object[] before = { false };
        typeof(XiaomanNewspaperScopePatch).GetMethod("Prefix", flags).Invoke(null, before);
        var error = new InvalidOperationException("simulated newspaper failure");
        object result = typeof(XiaomanNewspaperScopePatch).GetMethod("Finalizer", flags).Invoke(null, new[] { (object)error, before[0] });
        check(!(bool)rendering.GetValue(null) && ReferenceEquals(result, error), "failed newspaper rendering clears its scope without swallowing the native error");
        Console.WriteLine("PASS: W01 cassette dialogue, reused voice, deferred subtitles, retained drink rewards, day-three renovation and non-destructive newspaper scope.");
    }
}
