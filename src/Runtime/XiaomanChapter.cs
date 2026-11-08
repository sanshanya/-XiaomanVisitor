using System;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private static bool IsSafeDrinkingWater(GameItem item)
        {
            return item != null && item.IsTag("WATER") && item.IsTag("LIQUID_CONTAINER_TAG")
                && !item.IsTag("ALCOHOL") && !item.IsTag("CHEMICAL_PRODUCT_TAG")
                && !WaterHelper.IsEmpty(item) && WaterHelper.GetWaterPurity(item) >= 9800;
        }

        private static bool IsChapterDemand(GameItem item, XiaomanDemand demand)
        {
            if (item == null || item.IsTag("ALCOHOL") || item.IsTag("CONTRABAND")) return false;
            if (demand == XiaomanDemand.Water) return IsSafeDrinkingWater(item);
            if (demand == XiaomanDemand.FoodOrWater) return IsSafeDrinkingWater(item)
                || (item.IsTag("FOOD") && !item.IsTag("WATER") && !item.IsTag("BREVAGE"));
            if (demand == XiaomanDemand.Food)
                return item.IsTag("FOOD") && !item.IsTag("WATER") && !item.IsTag("BREVAGE");
            return item.IsTag("HOUSEHOLD_GOOD") && !item.IsTag("FOOD");
        }

        private VisitTradeSummary ReadVisitSummary(int today)
        {
            return ModHelper.GetModDataInt(Id, "story_trade_day", -1) == today
                ? VisitTradeSummary.Restore(ModHelper.GetModData(Id, "story_trade_summary", ""))
                : new VisitTradeSummary();
        }

        private DrinkRewardProgress ReadDrinkRewards()
        {
            return DrinkRewardProgress.Restore(ModHelper.GetModData(Id, "drink_rewards_pending", ""),
                ModHelper.GetModData(Id, "drink_rewards_delivered", ""),
                ModHelper.GetModDataInt(Id, "drink_reward_last_delivery_day", -1),
                ModHelper.GetModDataInt(Id, "drink_reward_last_effect", 0));
        }

        private void SaveDrinkRewards(DrinkRewardProgress progress)
        {
            ModHelper.SetModData(Id, "drink_rewards_pending", progress.PendingData);
            ModHelper.SetModData(Id, "drink_rewards_delivered", progress.DeliveredData);
            ModHelper.SetModDataInt(Id, "drink_reward_last_delivery_day", progress.LastDeliveryDay);
            ModHelper.SetModDataInt(Id, "drink_reward_last_effect", (int)progress.LastDeliveredEffect);
        }

        private void TryDeliverDrink(int today)
        {
            if (ReadStory().Finished) return;
            DrinkRewardProgress drinks = ReadDrinkRewards();
            PendingDrinkReward pending = drinks.Due(today);
            if (pending == null) return;
            CocktailSpec spec = CocktailCatalog.Find(pending.Key);
            if (!TryPlaceGift(ModHelper.SpawnItem(spec.ItemId))) return;
            drinks.Deliver(pending.TradeDay, today);
            SaveDrinkRewards(drinks);
            Log.Log("Xiaoman left one trade thank-you drink: " + spec.Key + ".");
        }
    }
}
