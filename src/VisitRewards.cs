using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace XiaomanVisitor
{
    public enum XiaomanDemand { Water, Food, Household, FoodOrWater }

    public static class ChapterSchedule
    {
        public static XiaomanDemand Demand(int today, int firstWaterDay)
        {
            if (firstWaterDay < 1) return XiaomanDemand.Water;
            int week = Math.Max(0, (today - 1) / 7 - (firstWaterDay - 1) / 7);
            return week % 3 == 1 ? XiaomanDemand.Food
                : week % 3 == 2 ? XiaomanDemand.Household : XiaomanDemand.Water;
        }

        public static bool MayVisit(int today, int lastVisit, int interval, bool invited)
        {
            return today >= 1 && today != lastVisit
                && (invited || lastVisit > today || today - lastVisit >= Math.Max(1, interval));
        }
    }

    // Figures are recorded only after a real player -> customer item transfer.
    // A budget cap or an intrinsically cheap item is not a voluntary discount.
    public sealed class VisitTradeSummary
    {
        public int Quantity;
        public long Credits;
        public long FairCredits;
        public long DiscountCredits;
        public decimal DiscountWeight;
        public bool HasDiscount { get { return DiscountCredits > 0; } }
        public decimal DiscountPercentExact
        {
            get { return FairCredits > 0 && HasDiscount ? Math.Min(100m, DiscountWeight / FairCredits) : 0m; }
        }
        public int DiscountPercent
        {
            get { return (int)DiscountPercentExact; }
        }

        public void Record(int quantity, long paid, long quote, long withoutVoluntaryDiscount, bool voluntary,
            int offeredDiscountPercent = 0)
        {
            if (quantity < 1 || paid < 0) return;
            Quantity = (int)Math.Min(int.MaxValue, (long)Quantity + quantity);
            Credits = AddSafe(Credits, paid);
            long fair = Math.Max(0, withoutVoluntaryDiscount);
            FairCredits = AddSafe(FairCredits, fair);
            // Compare the actual quoted prices, never the budget-capped payment.
            if (voluntary && quote >= 0 && fair > quote)
            {
                DiscountCredits = AddSafe(DiscountCredits, fair - quote);
                // Use the native percentage button, weighted by the full quote.
                // Integer price rounding must not turn a 25% offer into 24%.
                DiscountWeight += offeredDiscountPercent > 0
                    ? (decimal)fair * Math.Min(100, offeredDiscountPercent) : (decimal)(fair - quote) * 100m;
            }
        }

        private static long AddSafe(long a, long b) { return a > long.MaxValue - b ? long.MaxValue : a + b; }
        public string Data { get { return string.Join(",", Quantity, Credits, FairCredits, DiscountCredits)
            + "," + DiscountWeight.ToString(CultureInfo.InvariantCulture); } }
        public static VisitTradeSummary Restore(string data)
        {
            string[] p = (data ?? "").Split(',');
            int count;
            long paid, fair, discount;
            decimal weight;
            if ((p.Length != 4 && p.Length != 5) || !int.TryParse(p[0], out count) || !long.TryParse(p[1], out paid)
                || !long.TryParse(p[2], out fair) || !long.TryParse(p[3], out discount)
                || count < 0 || paid < 0 || fair < 0 || discount < 0) return new VisitTradeSummary();
            weight = (decimal)Math.Min(discount, fair) * 100m;
            if (p.Length == 5 && (!decimal.TryParse(p[4], NumberStyles.Number, CultureInfo.InvariantCulture, out weight)
                || weight < 0)) return new VisitTradeSummary();
            return new VisitTradeSummary { Quantity = count, Credits = paid, FairCredits = fair,
                DiscountCredits = Math.Min(discount, fair), DiscountWeight = Math.Min(weight, (decimal)fair * 100m) };
        }
    }

    public static class DrinkRewardRules
    {
        // Tier first, then relative completion of that tier's own threshold.
        // Exact ties rotate between groups, never reroll on a delivery retry.
        public static string Select(VisitTradeSummary visit, DrinkEffect lastDelivered = DrinkEffect.None)
        {
            if (visit == null || visit.Quantity < 1) return null;
            CocktailSpec winner = null;
            decimal bestScore = 0m;
            foreach (CocktailSpec spec in CocktailCatalog.Drinks)
            {
                decimal value = spec.Effect == DrinkEffect.TradeReputation ? visit.DiscountPercentExact
                    : spec.Effect == DrinkEffect.Scavenging ? visit.Quantity : visit.Credits;
                if (value < spec.RewardThreshold) continue;
                decimal score = value / spec.RewardThreshold;
                if (winner == null || spec.Tier > winner.Tier || (spec.Tier == winner.Tier
                    && (score > bestScore || (score == bestScore
                        && RotationRank(spec.Effect, lastDelivered) < RotationRank(winner.Effect, lastDelivered)))))
                { winner = spec; bestScore = score; }
            }
            return winner == null ? null : winner.Key;
        }

        private static int RotationRank(DrinkEffect effect, DrinkEffect last)
        {
            // Initial tie order: discount, quantity, credits. Continue after last delivery.
            int index = effect == DrinkEffect.TradeReputation ? 0 : effect == DrinkEffect.Scavenging ? 1 : 2;
            int previous = last == DrinkEffect.TradeReputation ? 0 : last == DrinkEffect.Scavenging ? 1
                : last == DrinkEffect.Luck ? 2 : -1;
            return (index - previous - 1 + 3) % 3;
        }
    }

    public sealed class PendingDrinkReward
    {
        public string Key;
        public readonly int TradeDay;
        public readonly int DueDay;
        public PendingDrinkReward(string key, int tradeDay, int dueDay)
        { Key = key; TradeDay = tradeDay; DueDay = dueDay; }
    }

    public sealed class DrinkRewardProgress
    {
        public readonly List<PendingDrinkReward> Pending = new List<PendingDrinkReward>();
        public readonly HashSet<int> DeliveredTrades = new HashSet<int>();
        public int LastDeliveryDay = -1;
        public DrinkEffect LastDeliveredEffect;

        public bool Update(int tradeDay, VisitTradeSummary visit)
        {
            if (tradeDay < 1 || DeliveredTrades.Contains(tradeDay)) return false;
            string key = DrinkRewardRules.Select(visit, LastDeliveredEffect);
            PendingDrinkReward old = Pending.Find(p => p.TradeDay == tradeDay);
            if (key == null)
            {
                if (old != null) Pending.Remove(old);
                return false;
            }
            if (old == null) Pending.Add(new PendingDrinkReward(key, tradeDay, tradeDay + 1));
            else old.Key = key; // Later items in the SAME visit update its single gift.
            return true;
        }

        public PendingDrinkReward Due(int today)
        {
            if (today <= LastDeliveryDay) return null;
            return Pending.Where(p => p.DueDay <= today).OrderBy(p => p.TradeDay).FirstOrDefault();
        }

        public bool Deliver(int tradeDay, int today)
        {
            PendingDrinkReward pending = Pending.Find(p => p.TradeDay == tradeDay);
            if (pending == null || today < pending.DueDay || today <= LastDeliveryDay) return false;
            Pending.Remove(pending);
            LastDeliveredEffect = CocktailCatalog.Find(pending.Key).Effect;
            DeliveredTrades.Add(tradeDay);
            LastDeliveryDay = today;
            return true;
        }

        public bool CanDeliverStoryDrink(int visitDay, int today)
        { return visitDay >= 1 && today > visitDay && today > LastDeliveryDay && !DeliveredTrades.Contains(visitDay); }

        public bool RecordStoryDrink(int visitDay, int today, string key)
        {
            CocktailSpec spec = CocktailCatalog.Find(key);
            if (spec == null || spec.Tier != 2 || !CanDeliverStoryDrink(visitDay, today)) return false;
            Pending.RemoveAll(p => p.TradeDay == visitDay);
            DeliveredTrades.Add(visitDay);
            LastDeliveryDay = today;
            LastDeliveredEffect = spec.Effect;
            return true;
        }

        public string PendingData { get { return string.Join(";", Pending.Select(p => p.Key + "," + p.TradeDay + "," + p.DueDay)); } }
        public string DeliveredData { get { return string.Join(",", DeliveredTrades.OrderBy(d => d)); } }

        public static DrinkRewardProgress Restore(string pending, string delivered, int lastDelivery,
            int lastEffect = 0)
        {
            var result = new DrinkRewardProgress { LastDeliveryDay = lastDelivery,
                LastDeliveredEffect = DrinkBuffProgress.ValidEffect(lastEffect) };
            int day, due;
            foreach (string s in (delivered ?? "").Split(','))
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out day) && day >= 1)
                    result.DeliveredTrades.Add(day);
            foreach (string s in (pending ?? "").Split(';'))
            {
                string[] p = s.Split(',');
                if (p.Length == 3 && CocktailCatalog.Find(p[0]) != null && int.TryParse(p[1], out day)
                    && int.TryParse(p[2], out due) && day >= 1 && due == day + 1
                    && !result.DeliveredTrades.Contains(day) && !result.Pending.Any(r => r.TradeDay == day))
                    result.Pending.Add(new PendingDrinkReward(p[0], day, due));
            }
            return result;
        }
    }

    // Only this state is serialized. Native attributes are calculated on read,
    // so reload, expiry and disabling the mod never leave a permanent stat delta.
    public sealed class DrinkBuffProgress
    {
        public string Key = "";
        public DrinkEffect Effect;
        public int Amount;
        public int UntilDay = -1;
        public bool IsActive(int day) { return Effect != DrinkEffect.None && Amount > 0 && day >= 1 && day < UntilDay; }
        public int ActiveAmount(DrinkEffect effect, int day) { return Effect == effect && IsActive(day) ? Amount : 0; }
        public void Replace(CocktailSpec spec, int today)
        {
            Key = spec.Key; Effect = spec.Effect; Amount = spec.EffectAmount; UntilDay = today + spec.DurationDays;
        }
        public bool Expire(int today)
        {
            if (Effect == DrinkEffect.None || today < UntilDay) return false;
            Key = ""; Effect = DrinkEffect.None; Amount = 0; UntilDay = -1;
            return true;
        }
        public static DrinkEffect ValidEffect(int value)
        {
            return value >= (int)DrinkEffect.Luck && value <= (int)DrinkEffect.Scavenging ? (DrinkEffect)value : DrinkEffect.None;
        }
    }

    public sealed class DrinkBuffCollection
    {
        private readonly Dictionary<DrinkEffect, DrinkBuffProgress> effects = new Dictionary<DrinkEffect, DrinkBuffProgress>();
        public int Count { get { return effects.Count; } }

        public void Replace(CocktailSpec spec, int today)
        {
            var buff = new DrinkBuffProgress();
            buff.Replace(spec, today);
            effects[spec.Effect] = buff; // Replace only this category, retaining other timers.
        }

        public void Import(DrinkBuffProgress buff)
        {
            if (buff == null || DrinkBuffProgress.ValidEffect((int)buff.Effect) == DrinkEffect.None
                || buff.Amount <= 0 || buff.UntilDay < 1) return;
            effects[buff.Effect] = new DrinkBuffProgress { Key = buff.Key ?? "", Effect = buff.Effect,
                Amount = buff.Amount, UntilDay = buff.UntilDay };
        }

        public int ActiveAmount(DrinkEffect effect, int today)
        {
            DrinkBuffProgress buff;
            return effects.TryGetValue(effect, out buff) ? buff.ActiveAmount(effect, today) : 0;
        }

        public bool Expire(int today)
        {
            bool changed = false;
            foreach (DrinkEffect effect in effects.Keys.ToArray())
                if (effects[effect].Expire(today)) { effects.Remove(effect); changed = true; }
            return changed;
        }

        public string Data
        {
            get
            {
                return string.Join(";", effects.OrderBy(pair => (int)pair.Key).Select(pair =>
                    ((int)pair.Key).ToString(CultureInfo.InvariantCulture) + "," + pair.Value.Key + ","
                    + pair.Value.Amount.ToString(CultureInfo.InvariantCulture) + ","
                    + pair.Value.UntilDay.ToString(CultureInfo.InvariantCulture)));
            }
        }

        public static DrinkBuffCollection Restore(string data)
        {
            var result = new DrinkBuffCollection();
            foreach (string row in (data ?? "").Split(';'))
            {
                string[] p = row.Split(',');
                int effect, amount, until;
                if (p.Length != 4 || !int.TryParse(p[0], out effect) || !int.TryParse(p[2], out amount)
                    || !int.TryParse(p[3], out until)) continue;
                result.Import(new DrinkBuffProgress { Key = p[1], Effect = DrinkBuffProgress.ValidEffect(effect),
                    Amount = amount, UntilDay = until });
            }
            return result;
        }
    }
}
