using System;
using System.Linq;
using HarmonyLib;

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
    public sealed partial class BuybackTestMod
    {
        private const string PreludeKey = "buyback_water_receipts_v1";
        private XiaomanMod visitor;
        private ConfigEntry<bool> skipPrerequisite;
        private bool previewForSession;
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
        {
            string data = progress.Data; ModHelper.SetModData(Id, PreludeKey, data);
            if (ModHelper.GetModData(Id, PreludeKey, "") != data) throw new InvalidOperationException("Water receipt was not saved");
        }
        private BuybackPreludeText PreludeText()
        {
            if (preludeText == null) {
                var loaded = BuybackStage.ParseData<BuybackPreludeText>(ModHelper.ReadModFileText(Id, "Scenes/prelude.json"));
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
                && ChemicalFeedbackHelper.IsNarcotic(item);
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
            if (!enabled || previewForSession || LineClosed || !visitor.BuybackTradingAvailable) return;
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
        internal bool HandleVisitQueue(StoreClientManager manager)
        {
            if (!enabled || manager == null || manager.clientStack == null || !PlayerStore.HasInstance || StoreStation.Instance == null) return false;
            if (LineClosed) {
                manager.clientStack.RemoveAll(OwnsVisitor); return true;
            }
            if (previewForSession || !visitor.BuybackTradingAvailable) return false;
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
            if (!enabled || previewForSession || LineClosed || !visitor.BuybackTradingAvailable || !OwnsVisitor(buyer) || item == null
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
            if (previewForSession) return today == 1;
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
        { BuybackTestMod.Instance?.ConfirmWaterSale(__state, item); }
    }
}
