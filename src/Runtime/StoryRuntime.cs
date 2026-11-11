using System;
using System.Linq;
using HarmonyLib;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private sealed class StorySession
        {
            public StoreClient Client;
            public StoryEpisode Episode;
            public int Day, ChanceRoll, PairRoll;
            public string[] SaleKeys;
            public bool[] Placed;
            public bool Started, OpeningFinished, FarewellShown;
            public readonly StoryTradeFlow Flow = new StoryTradeFlow();
            public float NextStockRetry;
        }
        private StorySession storySession;

        private StoryProgress ReadStory()
        { return StoryProgress.Restore(ModHelper.GetModData(Id, "story_v34_progress", "")); }
        private void SaveStory(StoryProgress progress)
        { ModHelper.SetModData(Id, "story_v34_progress", progress.Data); }

        private Dialogue StoryLine(StoreClient client, string key, Mood expression, params object[] args)
        {
            Dialogue line = Line(client, key, expression, args);
            if (key == "xm_01_01") BindVoice(line, "intro_1", Text(key));
            return line;
        }

        private StoreClient CreateStoryVisitor(int index)
        {
            StoryEpisode episode = StoryRules.Episode(index);
            var session = new StorySession { Episode = episode, Day = StoreStation.Instance.dayCounter,
                ChanceRoll = UnityEngine.Random.Range(0, 100), PairRoll = UnityEngine.Random.Range(0, 3) };
            bool mergedGift=TavernMapEnabled && ReadWork().ThanksDue(session.Day);
            session.SaleKeys = mergedGift?new string[0]:StoryRules.SaleOffer(index, session.ChanceRoll, session.PairRoll);
            session.Placed = new bool[session.SaleKeys.Length];
            var client = new StoreClient { identifier = ClientId, displayName = Text("name"), realName = Text("name"),
                spriteName = SpriteKey, clientFaction = StoreClient.FACTION_LOWER,
                clientIntent = session.SaleKeys.Length > 0 ? StoreClient.ClientIntent.SELLNBUY : StoreClient.ClientIntent.BUY,
                cooldown = 0, arrestable = false, nonShootable = true, noAcceptingContraband = true };
            session.Client = client;
            ConfigureGunReaction(client);
            client.SetBudget(episode.Credits);
            client.willAcceptItem = item => IsChapterDemand(item, episode.Demand);
            Dialogue tail = null;
            foreach (StoryLineSpec spec in episode.Opening)
            {
                string key=TavernRules.OpeningLineKey(index,spec.Key,session.Day);
                if (key=="xm_04_03" && ReadSupply().Signed) key="xm_04_03_existing";
                if (key=="xm_03_04" && ReadWork().HasWorked) key="xm_03_04_already_worked";
                Dialogue next = StoryLine(client,key,spec.Mood,episode.Credits);
                if (key=="xm_05_gift") next.startAction+=TryDeliverImprovGift;
                if (tail == null) client.mainDialogue = next;
                else tail.nextDialogue = next;
                tail = next;
                if (spec==episode.Opening[0] && TavernMapEnabled && ReadWork().ThanksDue(session.Day))
                    tail=AppendWorkThanks(tail,client,true);
            }
            if (!string.IsNullOrEmpty(episode.CocktailKey) && session.SaleKeys.Length > 0)
            {
                tail.nextDialogue = StoryLine(client, episode.CocktailKey, Mood.Soft);
                tail = tail.nextDialogue;
            }
            client.mainDialogue.startAction += () => BeginStorySession(session);
            tail.SetEndAction(() => { client.OnIntroduced(); session.OpeningFinished = true; TickStoryStock(); SetMood(client, Mood.Neutral); });
            client.acceptDealDialogue = StoryLine(client, episode.AcceptKey, episode.AcceptKey == "accept_return" ? Mood.Soft : Mood.Smile);
            client.acceptDealDialogue.startAction += () => session.Flow.DemandAcceptanceDisplayed();
            client.placedWrongItemWhenSellingToDialogue = Line(client, "wrong_item", Mood.Worried);
            client.placeRightItemWhenSellingToDialogue = Line(client, "right_item", Mood.Soft);
            string repeat = episode.Demand == XiaomanDemand.FoodOrWater ? "xm_repeat_first"
                : episode.Demand == XiaomanDemand.Food ? "repeat_food" : episode.Demand == XiaomanDemand.Household ? "repeat_household" : "repeat";
            client.repeatDialogue = Line(client, repeat, Mood.Soft);
            AttachStoryTrading(session);
            client.OnArrival = () => { storySession = session; SetMood(client, Mood.Neutral); };
            client.OnDismiss = () => { Voice?.Stop(); ResetGunReaction(); SetMood(client, Mood.Neutral); if (storySession == session) storySession = null; };
            client.CompleteClientCreation();
            client.noAcceptingContraband = true;
            return client;
        }

        private void BeginStorySession(StorySession session)
        {
            if (session.Started || StoreStation.Instance.dayCounter != session.Day) return;
            StoryProgress progress = ReadStory();
            StoryVisitRecord record;
            if (!progress.BeginVisit(session.Day, session.ChanceRoll, session.PairRoll, out record)
                || record.Index != session.Episode.Index) return;
            session.Started = true;
            storySession = session;
            record.SaleKeys=session.SaleKeys;
            SaveStory(progress);
            ModHelper.SetModDataInt(Id, "last_visit_day", session.Day);
            if (session.Episode.IsFinal)
            {
                DrinkRewardProgress drinks = ReadDrinkRewards();
                drinks.Pending.Clear();
                SaveDrinkRewards(drinks);
            }
            Log.Log("Story visit " + record.Index + "/14 began on day " + record.Day + "; "
                + session.Episode.Credits + " opening credits; fixed gifts due tomorrow, independently of trade.");
        }

        internal void TickStoryStock()
        {
            StorySession session = storySession;
            if (!enabled || session == null || !session.Started || !session.OpeningFinished || !PlayerStore.HasInstance
                || PlayerStore.Instance.currentClientInstance == null
                || PlayerStore.Instance.currentClientInstance.GetClientBlueprint() != session.Client
                || UnityEngine.Time.unscaledTime < session.NextStockRetry) return;
            session.NextStockRetry = UnityEngine.Time.unscaledTime + 1f;
            for (int i = 0; i < session.SaleKeys.Length; i++)
            {
                if (session.Placed[i]) continue;
                GameItem item = ModHelper.SpawnItem(CocktailCatalog.Find(session.SaleKeys[i]).ItemId);
                item.ModifyTag("XIAOMAN_BATCH_SOURCE",tag => tag.Enable().SetString("private_visit_sample:"+session.Day+":"+session.SaleKeys[i]));
                session.Placed[i] = PlayerStore.Instance.TryAddDirectSellingItemToTable(item);
            }
        }

        private bool HasSaleStock(StorySession session)
        {
            return session.Placed.Any(p => !p) || EmporiumEntry.Instance.GetAllNonOwnedItem()
                .Any(item => session.SaleKeys.Any(key => item.identifier == CocktailCatalog.Find(key).ItemId));
        }

        private Dialogue StoryGoodbye(StorySession session)
        {
            bool discount = ReadVisitSummary(session.Day).HasDiscount;
            string key = StoryRules.GoodbyeKey(session.Episode.Index, discount);
            Dialogue result = StoryLine(session.Client, key, session.Episode.IsFinal ? Mood.Soft : discount ? Mood.Shy
                : session.Episode.Index == 1 ? Mood.Smile : Mood.Soft);
            result.startAction += () => session.FarewellShown = true;
            result.SetEndAction(() => {
                if (PlayerStore.HasInstance && PlayerStore.Instance.currentClientInstance != null
                    && PlayerStore.Instance.currentClientInstance.GetClientBlueprint() == session.Client)
                    PlayerStore.Instance.DismissCurrentClient(false);
            });
            return result;
        }

        private void AttachStoryTrading(StorySession session)
        {
            StoreClient client = session.Client;
            client.allDoneDialogue = StoryGoodbye(session);
            client.tryInterceptDismiss = () => session.Started && !session.FarewellShown
                && DialogUIManager.Instance.StartDialogue(StoryGoodbye(session));
            client.onItemBought += (item, cost) => {
                if (!enabled || item == null || !session.Started || !IsChapterDemand(item, session.Episode.Demand)) return;
                VisitTradeSummary visit = ReadVisitSummary(session.Day);
                long quote = item.GetNegociatedValue(), fair = quote;
                ItemFeature discount = item.FindItemFeatureByID("bargainDiscount");
                bool voluntary = discount != null && !discount.isDisabled && discount.GetCurrentValueMod() < 0;
                if (voluntary)
                {
                    discount.isDisabled = true;
                    try { fair = item.GetNegociatedValue(); } finally { discount.isDisabled = false; }
                }
                visit.Record(Math.Max(1, item.unitCount), Math.Max(0, cost), quote, fair, voluntary,
                    voluntary ? -discount.GetCurrentValueMod() : 0);
                ModHelper.SetModDataInt(Id, "story_trade_day", session.Day);
                ModHelper.SetModData(Id, "story_trade_summary", visit.Data);
                bool needsAccept = session.Flow.SelectDemand();
                client.acceptDealDialogue = TradeAcceptance(session, session.Episode.AcceptKey, true);
                client.acceptDealDialoguePlayed = !needsAccept;
                if (StoryRules.AllowsTradeDrink(session.Episode.Index))
                {
                    DrinkRewardProgress drinks = ReadDrinkRewards();
                    drinks.Update(session.Day, visit);
                    SaveDrinkRewards(drinks);
                }
                client.allDoneDialogue = StoryGoodbye(session);
            };
            client.onItemSold += (item, price) => {
                session.Flow.SelectCocktail();
                client.acceptDealDialogue = TradeAcceptance(session, "xm_cocktail_accept", false);
                client.acceptDealDialoguePlayed = false;
            };
        }

        private Dialogue TradeAcceptance(StorySession session, string key, bool demand)
        {
            Dialogue line = StoryLine(session.Client, key, key == "accept_return" ? Mood.Soft : Mood.Smile);
            line.startAction += () => {
                if (demand) session.Flow.DemandAcceptanceDisplayed();
                session.Client.OnDealAccepted(line);
            };
            return line;
        }

        internal bool HandleStoryDeal(StoreClient client, Dialogue hostLine)
        {
            StorySession session = storySession;
            if (session == null || session.Client != client || !session.Started) return false;
            client.onDealAccepted?.Invoke();
            Dialogue next = null;
            if (client.GetBudget() <= 0 && !HasSaleStock(session)) next = StoryGoodbye(session);
            else if (session.Flow.LastWasDemand)
                next = Line(client, client.GetBudget() < 10 ? "xm_act_after_sale_low_budget_01" : "xm_act_after_sale_continue_01", Mood.Soft);
            if (hostLine != null) hostLine.SetNextDialogue(next);
            else if (next != null) DialogUIManager.Instance.StartDialogue(next);
            return true;
        }

        internal void RewriteStoryAction(Dialogue line, ref string speaker, ref string text)
        {
            if (!enabled || !CurrentClientIsXiaoman() || (speaker != "CLIENT" && speaker != Text("name"))) return;
            StoryActionSpec spec = StoryRules.ActionForText(text);
            if (spec == null) return;
            speaker = Text("name");
            text = Text(spec.Key);
            BindVoice(line, spec.Key, text, spec.Mood, !spec.IsSfx);
            line.startAction += () => {
                StoreClient current = PlayerStore.Instance.currentClientInstance?.GetClientBlueprint();
                if (current != null) SetMood(current, spec.Mood);
                if (spec.IsSfx) ModHelper.PlaySound3(Id, "xm_shutter_knock");
            };
        }
    }

    [HarmonyPatch(typeof(Dialogue), nameof(Dialogue.SetText), new Type[] { typeof(string), typeof(string) })]
    public static class XiaomanStoryActionPatch
    {
        private static void Prefix(Dialogue __instance, ref string __0, ref string __1)
        { XiaomanMod.Instance?.RewriteStoryAction(__instance, ref __0, ref __1); }
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.OnDealAccepted))]
    public static class XiaomanStoryDealPatch
    {
        private static bool Prefix(StoreClient __instance, Dialogue hostLine)
        { return !(XiaomanMod.Instance?.HandleStoryDeal(__instance, hostLine) ?? false); }
    }
}
