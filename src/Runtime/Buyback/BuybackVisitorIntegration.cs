using System;
using UnityEngine;

namespace XiaomanVisitor
{
    // Only the main build compiles this partial. All rendering, Monday
    // visits, gifts and letterbooks remain the existing implementation.
    public sealed partial class XiaomanMod
    {
        internal bool BuybackTradingAvailable { get { return enabled && PlayerStore.HasInstance && StoreStation.Instance != null
            && BuybackProgress.CanRecordSale(StoreStation.Instance.dayCounter, ReadStory().Finished); } }

        internal Dialogue BuybackLines(StoreClient client, string[] text, string key, Mood expression)
        {
            Dialogue first = null, previous = null;
            for (int i = 0; i < text.Length; i++) {
                var voiceKey = SideStories.BuybackStory.Instance?.PreludeVoiceKey(text[i]) ?? key + "_" + i;
                var line = BindVoice(new Dialogue().SetText(client.displayName, text[i])
                    .SetStartAction(() => { Voice?.Stop(); SetMood(client, expression); }), voiceKey, text[i], expression);
                if (previous == null) first = line; else previous.nextDialogue = line;
                previous = line;
            }
            return first;
        }
        internal void BuybackBuyer(StoreClient client, XiaomanDemand demand)
        {
            bool water = demand == XiaomanDemand.Water || demand == XiaomanDemand.FoodOrWater;
            // The native getter closes over its mutable preference field: do
            // not capture and call that getter after assigning a new predicate.
            client.willAcceptItem = item => item != null &&
                ((!ContrabandHelper.IsContraband(item) && IsChapterDemand(item, demand))
                || (water && SideStories.BuybackStory.IsConcealedDrugWater(item)));
            client.noAcceptingContraband = false;
            client.refusesHazardousWaterLabel = true;
            // Our persisted next-day receipt owns this character's return.
            // Native security/evidence processing of the sale stays intact.
            client.CanReturn = client.CanReturnAsBuyer = false;
        }
        internal void BuybackAcceptance(StoreClient client, string[] text, int receipt)
        {
            var first = BuybackLines(client, text, "buyback_accept_" + receipt, Mood.Worried);
            first.startAction += () => {
                if (storySession != null && storySession.Client == client) storySession.Flow.DemandAcceptanceDisplayed();
            };
            var last = first; while (last.nextDialogue != null) last = last.nextDialogue;
            var final = last;
            final.SetEndAction(() => client.OnDealAccepted(final));
            client.acceptDealDialogue = first; client.acceptDealDialoguePlayed = false;
        }
        internal StoreClient BuybackReturn(string[] text, string repeat, string accept, string goodbye, Action seen)
        {
            var client = new StoreClient { identifier = ClientId, displayName = Text("name"), realName = Text("name"),
                spriteName = SpriteKey, clientFaction = StoreClient.FACTION_LOWER, clientIntent = StoreClient.ClientIntent.BUY,
                cooldown = 0, arrestable = false, nonShootable = true };
            ConfigureGunReaction(client); client.SetBudget(150);
            client.mainDialogue = BuybackLines(client, text, "buyback_return", Mood.Worried);
            client.mainDialogue.startAction += seen;
            var last = client.mainDialogue; while (last.nextDialogue != null) last = last.nextDialogue;
            last.SetEndAction(() => client.OnIntroduced());
            client.repeatDialogue = BuybackLines(client, new[] { repeat }, "buyback_repeat", Mood.Worried);
            client.acceptDealDialogue = BuybackLines(client, new[] { accept }, "buyback_clean", Mood.Soft);
            client.allDoneDialogue = BuybackLines(client, new[] { goodbye }, "buyback_goodbye", Mood.Worried)
                .SetEndAction(() => { if (PlayerStore.HasInstance && PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() == client)
                    PlayerStore.Instance.DismissCurrentClient(false); });
            client.OnArrival = () => SetMood(client, Mood.Worried);
            client.OnDismiss = () => { Voice?.Stop(); ResetGunReaction(); SetMood(client, Mood.Neutral); };
            client.CompleteClientCreation(); BuybackBuyer(client, XiaomanDemand.Water);
            ModHelper.SetClientSource(client, Id);
            return client;
        }
    }
}
