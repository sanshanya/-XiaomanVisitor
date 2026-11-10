using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace XiaomanVisitor
{
    public static class DrinkEffectMath
    {
        public static int SaleCredit(int paid,int percent)
        { return paid<=0 || percent<=0 ? paid : (int)Math.Min(int.MaxValue,(long)paid+(long)paid*percent/100); }
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

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SellItem))]
    public static class XiaomanSaleIncomePatch
    {
        // Multiply only the actual native credit addition, after its customer-budget cap.
        // Prices, customer spending, item callbacks and reputation keep the agreed amount.
        public static int Credit(int paid)
        { return DrinkEffectMath.SaleCredit(paid,XiaomanMod.ActiveDrinkBonus(DrinkEffect.SaleIncome)); }
        public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.Select(c => new CodeInstruction(c)).ToList();
            var cash=AccessTools.Field(typeof(PlayerStore),nameof(PlayerStore.playerCash));
            int at=code.FindIndex(c => c.opcode==OpCodes.Stfld && Equals(c.operand,cash));
            if (at<2 || code[at-1].opcode!=OpCodes.Add || code.Count(c => c.opcode==OpCodes.Stfld && Equals(c.operand,cash))!=1)
                throw new InvalidOperationException("Native sale credit path changed; refusing an unverified income patch");
            var call=new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(XiaomanSaleIncomePatch),nameof(Credit)));
            call.labels.AddRange(code[at-1].labels);code[at-1].labels.Clear();
            code.Insert(at-1,call);return code;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) { return Rewrite(instructions); }
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
