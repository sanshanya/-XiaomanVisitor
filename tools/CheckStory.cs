using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XiaomanVisitor;

internal static class CheckStory
{
    public static void Run(string package, Action<bool, string> check)
    {
        CheckCustomerQueue(check);
        int[] budgets = { 150, 200, 250, 300, 350, 400, 450, 500, 450, 400, 350, 300, 250, 200 };
        check(StoryCatalog.Episodes.Length == 14 && StoryCatalog.Episodes.Select(e => e.Credits).SequenceEqual(budgets), "user's fourteen fixed opening budgets");
        var episodes = StoryCatalog.Episodes;
        check(episodes[0].Demand == XiaomanDemand.FoodOrWater && episodes[1].Demand == XiaomanDemand.Household
            && episodes[2].Demand == XiaomanDemand.Food && episodes[13].Demand == XiaomanDemand.Water, "specific chapter shopping preferences");
        check(episodes[0].TrackKey == "title" && episodes[1].TrackKey == "intro" && episodes[2].TrackKey == "nightfall"
            && episodes[6].TrackKey == "diary" && episodes[12].TrackKey == "outro" && episodes[13].TrackKey == "rainysound", "specified story tape slots");
        check(episodes.Select(e => e.TrackKey).Distinct().Count() == 14 && CassetteCollection.Tracks.Select(t => t.Key).SequenceEqual(episodes.Select(e => e.TrackKey)), "one fixed tape per chapter, collection numbers match story");
        check(CassetteCollection.Tracks.Select(t => t.SpriteKey).Distinct().Count() == 14
            && CassetteCollection.Tracks.All(t => File.Exists(Path.Combine(package, "Sounds", t.SoundKey + ".mp3"))), "fourteen distinct, existing cassette assets");
        check(StoryRules.GoodbyeKey(14, true) == "xm_14_bye" && StoryRules.GoodbyeKey(14, false) == "xm_14_bye"
            && StoryRules.GoodbyeKey(5, true) == "goodbye_discount", "final goodbye wins even after a discount");

        var postponed = new StoryProgress();
        StoryVisitRecord visit;
        check(!postponed.BeginVisit(0, 0, 0, out visit) && !postponed.BeginVisit(2, 0, 0, out visit), "no off-Monday encounter");
        check(postponed.MayVisit(1) && postponed.VisitIndex == 0 && postponed.DueGift(2) == null, "queue eligibility alone awards no unseen episode");
        check(postponed.BeginVisit(8, 0, 0, out visit) && visit.Index == 1 && visit.Day == 8, "missed first Monday retains opening");
        check(!postponed.BeginVisit(8, 99, 2, out visit), "reopening same day cannot advance");
        postponed = StoryProgress.Restore(postponed.Data);
        check(postponed.VisitIndex == 1 && !postponed.MayVisit(9) && postponed.MayVisit(22), "missed second Monday stays at episode two");
        check(postponed.BeginVisit(22, 0, 0, out visit) && visit.Index == 2, "calendar gaps never skip story chapters");

        for (int seed = 0; seed < 16; seed++)
        {
            var progress = new StoryProgress();
            var random = new Random(seed);
            for (int n = 1; n <= 14; n++)
            {
                int day = 1 + (n - 1) * 7;
                check(progress.BeginVisit(day, random.Next(100), random.Next(3), out visit) && visit.Index == n, "each seen Monday advances once without any trade");
                string[] offer = visit.SaleKeys.ToArray();
                progress = StoryProgress.Restore(progress.Data);
                check(progress.Visits.Last().SaleKeys.SequenceEqual(offer), "reload preserves selected stock");
                check(progress.DueGift(day) == null && progress.DueGift(day + 1).Index == n, "Tuesday rewards are due with zero purchases");
                int parts = n == 14 ? 5 : n == 6 ? 3 : 2;
                string fixedDrink = progress.Visits.Last().GiftDrinkKey;
                if (n == 6) check(StoryRules.FinalDrinks.Contains(fixedDrink) && visit.SaleKeys.Length == 0,
                    "sixth visit reserves one strong gift without sales or any transaction");
                for (int part = 0; part < parts; part++)
                {
                    check(progress.MarkPartDelivered(n, part, day + 1), "each physical gift has independent delivery receipt");
                    progress = StoryProgress.Restore(progress.Data);
                    check(progress.Visits.Last().GiftDrinkKey == fixedDrink, "reload cannot reroll the reserved chapter drink");
                    check(!progress.MarkPartDelivered(n, part, day + 1), "reload cannot duplicate a delivered item");
                    if (part + 1 < parts) check(progress.DueGift(day + 1).Index == n, "partial delivery survives retries");
                }
                check(progress.DueGift(day + 1) == null && progress.DueGift(day + 2) == null, "completed gift never respawns");
            }
            check(progress.Finished && !progress.MayVisit(99) && !progress.BeginVisit(106, 0, 0, out visit), "no fifteenth visit");
            check(progress.Visits.Count(v => v.GiftComplete) == 14, "complete story has fourteen delivered letters and tapes");
        }
        var blocked = new StoryProgress();
        blocked.BeginVisit(1, 0, 0, out visit);
        blocked.BeginVisit(8, 0, 0, out visit);
        check(blocked.DueGift(9).Index == 1 && !blocked.MarkPartDelivered(2, 0, 9), "blocked gifts retain chapter order");
        blocked.MarkPartDelivered(1, 0, 9);
        blocked = StoryProgress.Restore(blocked.Data);
        check(blocked.DueGift(10).HasPart(0) && !blocked.DueGift(10).HasPart(1), "letter delivery does not swallow blocked tape");
        blocked.MarkPartDelivered(1, 1, 10);
        check(blocked.DueGift(10) == null && blocked.DueGift(11).Index == 2, "backlog delivers at most one chapter bundle per day");

        int offered = 0;
        for (int roll = 0; roll < 100; roll++)
        {
            check(StoryRules.SaleOffer(4, roll, roll).Length == 2, "fourth visit guaranteed sale offer");
            string[] offer = StoryRules.SaleOffer(5, roll, roll);
            if (offer.Length > 0)
            {
                offered++;
                check(offer.Length == 2 && offer.Distinct().Count() == 2 && offer.All(k => CocktailCatalog.Find(k).Tier == 1), "sale has two different basic drinks");
            }
            check(StoryRules.SaleOffer(3, roll, 0).Length == 0 && StoryRules.SaleOffer(6, roll, roll).Length == 0
                && StoryRules.SaleOffer(14, roll, 0).Length == 0, "no early, sixth-week or final-week sale stock");
        }
        check(offered == 30, "later sale offer is exactly 30 percent");
        check(Enumerable.Range(0, 3).Select(roll => StoryRules.FixedDrink(6, roll)).Distinct().Count() == 3
            && Enumerable.Range(1, 14).Where(n => n != 6).All(n => StoryRules.FixedDrink(n, 0) == ""),
            "sixth-visit random gift reaches all three strong drinks only");
        check(!StoryRules.AllowsTradeDrink(6) && !StoryRules.AllowsTradeDrink(14) && StoryRules.AllowsTradeDrink(5),
            "fixed gift weeks do not also earn an ordinary transaction drink");
        var receipt = new DrinkRewardProgress();
        var earned = new VisitTradeSummary(); earned.Record(10, 300, 100, 200, true, 50);
        receipt.Update(29, earned);
        check(!receipt.CanDeliverStoryDrink(36, 36) && receipt.CanDeliverStoryDrink(36, 37), "special drink is next-day only");
        check(receipt.RecordStoryDrink(36, 37, StoryRules.FixedDrink(6, 0)) && receipt.Due(37) == null,
            "guaranteed sixth-week drink blocks a second ordinary backlog drink on the same day");
        receipt = DrinkRewardProgress.Restore(receipt.PendingData, receipt.DeliveredData, receipt.LastDeliveryDay, (int)receipt.LastDeliveredEffect);
        check(!receipt.RecordStoryDrink(36, 38, StoryRules.FixedDrink(6, 2)) && !receipt.Update(36, earned)
            && receipt.Due(38)?.TradeDay == 29, "saved special receipt prevents duplicates while retaining prior owed drinks");
        string oldCompleted = "{\"Visits\":[" + string.Join(",", Enumerable.Range(1, 6).Select(n =>
            "{\"Index\":" + n + ",\"Day\":" + (1 + (n-1)*7) + ",\"GiftMask\":3}")) + "]}";
        var oldProgress = StoryProgress.Restore(oldCompleted);
        check(oldProgress.VisitIndex == 6 && oldProgress.Visits.Last().GiftComplete && !oldProgress.Visits.Last().HasFixedDrink,
            "completed old sixth-week bundles do not receive a retroactive duplicate reward");
        var oldPending = StoryProgress.Restore(oldCompleted.Replace("\"Index\":6,\"Day\":36,\"GiftMask\":3", "\"Index\":6,\"Day\":36,\"GiftMask\":1"));
        string pendingKey = oldPending.Visits.Last().GiftDrinkKey;
        check(oldPending.Visits.Last().HasFixedDrink && oldPending.Visits.Last().PartCount == 3
            && StoryProgress.Restore(oldPending.Data).Visits.Last().GiftDrinkKey == pendingKey,
            "pending old sixth-week bundles adopt the gift once with a stable selection");
        check(StoryRules.FinalDrinks.Length == 3 && StoryRules.FinalDrinks.Select(k => CocktailCatalog.Find(k).Effect).Distinct().Count() == 3
            && StoryRules.FinalDrinks.All(k => CocktailCatalog.Find(k).Tier == 2), "final gift is one strong drink from each effect category");
        check(StoryCatalog.Actions.Length == 49 && StoryCatalog.Actions.Count(a => a.IsSfx) == 1, "48 spoken action slots plus one nonverbal SFX");
        foreach (StoryActionSpec action in StoryCatalog.Actions)
            foreach (string original in action.OriginalTexts)
                check(StoryRules.ActionForText(original)?.Key == action.Key, "all native language variants map to the intended dedicated slot");
        check(StoryRules.ActionForText("Unrelated NPC dialogue") == null, "unmapped dialogue is preserved");
        var batch = new StoryTradeFlow();
        for (int item = 0; item < 6; item++) check(batch.SelectDemand(), "six item callbacks do not consume the first batch's acceptance");
        batch.DemandAcceptanceDisplayed();
        check(!batch.SelectDemand(), "later demand transaction uses continuation instead of repeating first acceptance");
        batch.SelectCocktail();
        check(!batch.LastWasDemand && !batch.SelectDemand(), "buying her cocktail does not reset the demand acceptance");
        var cocktailFirst = new StoryTradeFlow();
        cocktailFirst.SelectCocktail();
        check(cocktailFirst.SelectDemand(), "buying her cocktail first does not suppress her first demand acceptance");
        Console.WriteLine("PASS: fourteen Monday chapters, missed weeks, fixed budgets, no-trade gifts, partial delivery/reload, 30% basic sales, free final gifts and 49 action mappings.");
    }

    private static void CheckCustomerQueue(Action<bool, string> check)
    {
        var xiaoman = new object();
        var mentor = new object(); var foodSeller = new object(); var foodBuyer = new object();
        var wantedIntroduction = new object(); var futureStoryClient = new object();
        var native = new[] { mentor, foodSeller, foodBuyer, wantedIntroduction };
        var opening = new List<object>(native);
        opening.Add(xiaoman); // Same append operation used by native AddClient.
        check(opening.Count == native.Length + 1 && opening.Take(native.Length).SequenceEqual(native)
            && opening.Last() == xiaoman, "first-night visitor is additional after all tutorial clients");
        check(!CustomerQueuePolicy.MoveToTail(opening, xiaoman) && opening.Count == native.Length + 1,
            "repeated generation/opening/shortcut cannot duplicate the queued visitor");
        opening.Add(futureStoryClient); // PlayerStore.HandleFutureClientQueue runs after generation.
        check(CustomerQueuePolicy.MoveToTail(opening, xiaoman)
            && opening.SequenceEqual(native.Concat(new[] { futureStoryClient, xiaoman })),
            "late opening callback preserves native future-story arrivals ahead of Xiaoman");
        var legacy = new List<object>(native); legacy.Insert(0, xiaoman);
        check(CustomerQueuePolicy.MoveToTail(legacy, xiaoman)
            && legacy.SequenceEqual(native.Concat(new[] { xiaoman })), "old front placement repairs without removing native NPCs");
        var absent = new List<object>(native);
        check(!CustomerQueuePolicy.MoveToTail(absent, xiaoman) && absent.SequenceEqual(native),
            "missing visitor does not alter native queue");
        check(!CustomerQueuePolicy.MoveToTail<object>(null, xiaoman)
            && !CustomerQueuePolicy.MoveToTail(absent, null), "uninitialized queue is left alone");
        for (int count = 0; count <= 12; count++)
        {
            object[] clients = Enumerable.Range(0, count).Select(_ => new object()).ToArray();
            for (int position = 0; position <= count; position++)
            {
                var queue = new List<object>(clients); queue.Insert(position, xiaoman);
                CustomerQueuePolicy.MoveToTail(queue, xiaoman);
                check(queue.Count == count + 1 && queue.Count(c => c == xiaoman) == 1
                    && queue.Last() == xiaoman && queue.Take(count).SequenceEqual(clients),
                    "every original customer and relative position survives moving only Xiaoman to the tail");
            }
        }
        Console.WriteLine("PASS: additional tail-only visitor, intact first-night tutorial order, late story arrivals, legacy repair and repeated invitations.");
    }
}
