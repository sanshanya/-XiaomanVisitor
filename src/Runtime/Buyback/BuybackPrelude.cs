using System;
using System.Linq;
using HarmonyLib;

// Fields below are populated by the runtime JSON parser.
#pragma warning disable CS0649
namespace XiaomanVisitor.SideStories
{
    internal sealed class BuybackPreludeText
    {
        public int schema;
        public string[] first_accept, second_accept, first_return, second_return;
        public string late_return, repeat, accept_clean, goodbye;
        internal void Validate()
        {
            if (schema != 1 || new[] { first_accept, second_accept, first_return, second_return }
                    .Any(a => a == null || a.Length == 0 || a.Any(string.IsNullOrWhiteSpace))
                || new[] { late_return, repeat, accept_clean, goodbye }.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Missing prelude dialogue");
        }
    }
#pragma warning restore CS0649
    internal sealed partial class BuybackStory
    {
        private const string PreludeKey = "buyback_water_receipts_v1";
        private XiaomanMod visitor;
        private BuybackPreludeText preludeText;
        internal static bool OwnsVisitor(StoreClient client)
        { return client != null && client.sourceModId == Id && CustomerQueuePolicy.IsXiaoman(client.identifier); }
        internal static bool LineClosed { get {
            if (Instance == null || !Instance.enabled || !PlayerStore.HasInstance) return false;
            try { return Instance.ReadDoorProgress().refusals == 3; }
            catch (Exception error) { Instance.log.Warning("Invalid refusal state; Xiaoman stays unavailable: " + error.Message); return true; }
        } }
        private BuybackProgress ReadPrelude() { return BuybackProgress.Restore(ModHelper.GetModData(Id, PreludeKey, "")); }
        private void SavePrelude(BuybackProgress progress)
        { ModHelper.SetModData(Id, PreludeKey, progress.Data); }
        private BuybackPreludeText PreludeText()
        {
            if (preludeText == null) {
                var loaded = BuybackStage.ParseData<BuybackPreludeText>(ModHelper.ReadModFileText(Id, "Scenes/Buyback/prelude.json"));
                if (loaded == null) throw new InvalidOperationException("Prelude text missing");
                loaded.Validate(); preludeText = loaded;
            }
            return preludeText;
        }
        internal static bool IsDrugWater(GameItem item)
        {
            if (item == null || !item.IsTag("LIQUID_CONTAINER_TAG") || WaterHelper.IsEmpty(item)
                || item.IsTag("ALCOHOL") || item.IsTag("CHEMICAL_PRODUCT_TAG") || item.IsTag("WINE_TAG")) return false;
            // Composition, never its label, item name or a generic contraband tag.
            return item.GetTagReadonly(Liquid.GetCurrentPartFromId(Liquid.LIQUID_WATER)).GetInt() > 0
                && (item.GetTagReadonly(Liquid.GetCurrentPartFromId(Liquid.LIQUID_DREAM_CAP_MUSH)).GetInt() > 0
                    || item.GetTagReadonly(Liquid.GetCurrentPartFromId(Liquid.LIQUID_DREAM_CAP_EXTRACT)).GetInt() > 0)
                && BuybackProgress.HasEffectiveWaterDose(WaterDose(item));
        }
        internal static double WaterDose(GameItem item)
        {
            double potency = 0;
            // Use the installed game's actual liquid effects, with Xiaoman's own
            // story threshold. Other customers retain the native 750 threshold.
            foreach (Liquid liquid in Liquid.Liquids)
                potency += (double)item.GetTagReadonly(liquid.currentPart).GetInt() * liquid.euphoria;
            return potency;
        }
        internal static bool IsConcealedDrugWater(GameItem item)
        {
            if (!IsDrugWater(item)) return false;
            var feature = WaterFeatureHelper.GetWaterFeature(item);
            return feature != null && !feature.isDisabled && !feature.IsExposed()
                && feature.GetCurrentItemCondition() != null
                && feature.GetCurrentItemCondition().customValue >= WaterFeatureHelper.BASE_WATER_PURITY;
        }
        internal void ConfigureStoryBuyer(StoreClient client, XiaomanDemand demand)
        {
            if (!enabled || LineClosed || !visitor.BuybackTradingAvailable) return;
            if (demand == XiaomanDemand.Water || demand == XiaomanDemand.FoodOrWater) visitor.BuybackBuyer(client, demand);
            var progress = ReadPrelude(); int day = StoreStation.Instance.dayCounter;
            if (!progress.ReturnDue(day)) return;
            var opening = ReturnText(progress, day);
            var prepend = visitor.BuybackLines(client, opening, "buyback_monday_return", Mood.Worried);
            prepend.startAction += () => MeetReturn();
            var last = prepend; while (last.nextDialogue != null) last = last.nextDialogue;
            last.nextDialogue = client.mainDialogue; client.mainDialogue = prepend;
        }
        private string[] ReturnText(BuybackProgress progress, int today)
        {
            var text = PreludeText();
            var lines = (progress.Count == 1 ? text.first_return : text.second_return).ToArray();
            if (progress.Count == 1 && today > progress.ReturnDueDay) lines[0] = text.late_return;
            return lines;
        }
        private void MeetReturn()
        {
            var progress = ReadPrelude();
            if (!progress.MeetReturn(StoreStation.Instance.dayCounter)) return;
            SavePrelude(progress);
            log.Log("Guaranteed return actually met: receipt=" + progress.Count + "; day=" + progress.ReturnSeenDay + "; no extra chapter/gift.");
        }
        private void RecoverLowDoseSale()
        {
            if (!enabled || LineClosed || !visitor.BuybackTradingAvailable || EmporiumEntry.Instance == null) return;
            try {
                var progress = ReadPrelude();
                if (progress.Count != 0) return;
                int today = StoreStation.Instance.dayCounter;
                int day = ModHelper.GetModDataInt(Id, "story_trade_day", -1);
                if (day < 1 || today < day || (long)today - day > 1) return;
                var summary = VisitTradeSummary.Restore(ModHelper.GetModData(Id, "story_trade_summary", ""));
                var story = StoryProgress.Restore(ModHelper.GetModData(Id, "story_v34_progress", ""));
                if (summary.Quantity != 1 || !story.Visits.Any(v => v.Day == day)) return;
                var receipts = PlayerStore.Instance.soldItemsTracker.Where(s => s != null
                    && s.clientId == XiaomanMod.ClientId && s.clientSprite == XiaomanMod.SpriteKey).ToArray();
                if (receipts.Length != 1) return;
                var receipt = receipts[0];
                if (receipt.CanReturn || receipt.CanReturnAsBuyer || receipt.soldPrice != summary.Credits) return;
                var item = EmporiumEntry.Instance.soldElement.childItems.FirstOrDefault(i => i != null && i.uniqueId == receipt.itemUniqueId);
                if (item == null || item.identifier != receipt.itemId || GeneralHelper.IsItemOwned(item)
                    || !IsDrugWater(item) || WaterDose(item) >= 750) return;
                if (progress.Record(day, receipt.itemUniqueId) != 1) return;
                SavePrelude(progress);
                log.Log("Recovered verified low-dose water sale 1/2; item=" + receipt.itemUniqueId + "; sale day=" + day
                    + "; potency=" + WaterDose(item) + "; return due=" + progress.ReturnDueDay + ".");
                if (PlayerStore.Instance.storeState == PlayerStore.StoreState.OPEN)
                    HandleVisitQueue(PlayerStore.Instance.storeClientManager);
            } catch (Exception error) { log.Warning("Low-dose receipt recovery kept existing state: " + error.Message); }
        }
        internal bool HandleVisitQueue(StoreClientManager manager)
        {
            if (!enabled || manager == null || manager.clientStack == null || !PlayerStore.HasInstance || StoreStation.Instance == null) return false;
            if (LineClosed) {
                manager.clientStack.RemoveAll(OwnsVisitor); return true;
            }
            if (!visitor.BuybackTradingAvailable) return false;
            try {
                var progress = ReadPrelude(); int day = StoreStation.Instance.dayCounter;
                if (!progress.ReturnDue(day)) return progress.ReturnSeenDay == day;
                // A normal Monday visitor will prepend this return to her own
                // chapter. Additional weekdays never call BeginStorySession.
                if (StoryRules.IsMonday(day)) return false;
                var current = PlayerStore.Instance.currentClientInstance?.GetClientBlueprint();
                if (OwnsVisitor(current)) return true;
                var queued = manager.clientStack.FirstOrDefault(OwnsVisitor);
                if (queued != null) { CustomerQueuePolicy.MoveToTail(manager.clientStack, queued); return true; }
                if (!visitor.EnsurePortraits()) return true;
                var text = PreludeText();
                var client = visitor.BuybackReturn(ReturnText(progress, day), text.repeat, text.accept_clean, text.goodbye, MeetReturn);
                manager.AddClient(client);
                log.Log("Guaranteed water return appended to native queue: day=" + day + "; receipt=" + progress.Count + ".");
                return true;
            }
            catch (Exception error) { log.Warning("Water return remains pending: " + error); return true; }
        }
        internal void ConfirmWaterSale(StoreClient buyer, GameItem item)
        {
            if (!enabled || LineClosed || !visitor.BuybackTradingAvailable || !OwnsVisitor(buyer) || item == null
                || GeneralHelper.IsItemOwned(item)
                || !PlayerStore.Instance.soldItemsTracker.Any(s => s.itemUniqueId == item.uniqueId && s.clientId == buyer.identifier)
                || !IsDrugWater(item)) return;
            try {
                var progress = ReadPrelude(); int number = progress.Record(StoreStation.Instance.dayCounter, item.uniqueId);
                if (number == 0) return;
                SavePrelude(progress);
                var text = PreludeText(); visitor.BuybackAcceptance(buyer, number == 1 ? text.first_accept : text.second_accept, number);
                log.Log("Committed drugged-water sale " + number + "/2; item=" + item.uniqueId + "; return due=" + progress.ReturnDueDay + ".");
            }
            catch (Exception error) { log.Warning("Water prelude receipt failed: " + error); }
        }
        private bool NightEligible(int today)
        {
            try { return !LineClosed && ReadPrelude().NightReady(today); }
            catch (Exception error) { log.Warning("Water prelude unavailable: " + error); return false; }
        }
    }
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SellItem))]
    public static class BuybackWaterSalePatch
    {
        private static void Prefix(PlayerStore __instance, out StoreClient __state)
        { __state = __instance.currentClientInstance?.GetClientBlueprint(); }
        private static void Postfix(GameItem item, StoreClient __state)
        { BuybackStory.Instance?.ConfirmWaterSale(__state, item); }
    }
}
