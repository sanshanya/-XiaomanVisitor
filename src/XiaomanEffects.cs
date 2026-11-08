using System;
using HarmonyLib;

namespace XiaomanVisitor
{
    public static class DrinkEffectMath
    {
        public static int Luck(int native, int bonus)
        { return (int)Math.Min(int.MaxValue, (long)native + Math.Max(0, bonus)); }

        public static int ScavengingLimit(int native, int bonus)
        {
            // A zero native limit can mean a disabling trait, not a used-up quota.
            return native <= 0 ? native : (int)Math.Min(int.MaxValue, (long)native + Math.Max(0, bonus));
        }

        public static double TradeReputation(double gain, int percent, bool fromTrade)
        {
            // Quest rewards and reputation losses retain their native values.
            return fromTrade && gain > 0 && percent > 0 ? gain * (1.0 + percent / 100.0) : gain;
        }
    }

    public static class TradeRewardScope
    {
        [ThreadStatic] private static int depth;
        public static bool IsActive { get { return depth > 0; } }
        public static int Enter() { int previous = depth; depth++; return previous; }
        public static void Restore(int previous) { depth = Math.Max(0, previous); }
    }

    [HarmonyPatch(typeof(HealthData), nameof(HealthData.GetLuck))]
    public static class XiaomanLuckPatch
    {
        private static void Postfix(ref int __result)
        { __result = DrinkEffectMath.Luck(__result, XiaomanMod.ActiveDrinkBonus(DrinkEffect.Luck)); }
    }

    [HarmonyPatch(typeof(ScavHelper), nameof(ScavHelper.GetMaxScavAttempts))]
    public static class XiaomanScavengingPatch
    {
        private static void Postfix(ref int __result)
        { __result = DrinkEffectMath.ScavengingLimit(__result, XiaomanMod.ActiveDrinkBonus(DrinkEffect.Scavenging)); }
    }

    [HarmonyPatch(typeof(StoreReputation), nameof(StoreReputation.OnItemTraded))]
    public static class XiaomanTradeScopePatch
    {
        private static void Prefix(out int __state) { __state = TradeRewardScope.Enter(); }
        // Always clean up, including a native/other-mod exception. Preserve that exception.
        private static Exception Finalizer(Exception __exception, int __state)
        { TradeRewardScope.Restore(__state); return __exception; }
    }

    [HarmonyPatch(typeof(StoreReputation), nameof(StoreReputation.ModReputation), new Type[] { typeof(double) })]
    public static class XiaomanTradeReputationPatch
    {
        private static void Prefix(ref double modValue)
        {
            if (!TradeRewardScope.IsActive || modValue <= 0) return;
            modValue = DrinkEffectMath.TradeReputation(modValue,
                XiaomanMod.ActiveDrinkBonus(DrinkEffect.TradeReputation), true);
        }
    }
}
