using System.Linq;

namespace XiaomanVisitor
{
    public enum DrinkEffect { None, Luck, TradeReputation, Scavenging, SaleIncome }

    public sealed class CocktailSpec
    {
        public readonly string Key;
        public readonly int Width;
        public readonly int Height;
        public readonly DrinkEffect Effect;
        public readonly int Tier;
        public readonly int EffectAmount;
        public readonly int RewardThreshold;
        public readonly int DurationDays, MarketValue;
        public const int Price = 30;

        public CocktailSpec(string key, DrinkEffect effect, int tier, int amount, int threshold, int durationDays=3, int marketValue=Price)
        {
            Key = key;
            Width = 16;
            Height = 32;
            Effect = effect;
            Tier = tier;
            EffectAmount = amount;
            RewardThreshold = threshold;
            DurationDays = durationDays;
            MarketValue = marketValue;
        }

        public string ItemId { get { return XiaomanMod.Id + ".cocktail." + Key; } }
        // The first-test unique drink reuses an approved glass; no fabricated new original artwork.
        public string SpriteKey { get { return "xiaoman_cocktail_" + (Key=="roka" ? "spring_sway" : Key); } }
    }

    public static class CocktailCatalog
    {
        public static readonly CocktailSpec[] Drinks =
        {
            new CocktailSpec("whale_dream", DrinkEffect.Scavenging, 1, 2, 3),
            new CocktailSpec("sunset_blue", DrinkEffect.Luck, 1, 5, 100),
            new CocktailSpec("bluebird", DrinkEffect.Luck, 2, 8, 200),
            new CocktailSpec("cosmo_zone", DrinkEffect.Scavenging, 2, 4, 6),
            new CocktailSpec("sky_after_rain", DrinkEffect.TradeReputation, 2, 20, 25),
            new CocktailSpec("rainy_blue", DrinkEffect.TradeReputation, 1, 10, 10)
        };
        public static readonly CocktailSpec[] All = Drinks.Concat(LuxuryDrinkCatalog.Standard).Concat(new[]{LuxuryDrinkCatalog.Improv,LuxuryDrinkCatalog.Roka}).ToArray();
        public static CocktailSpec Find(string key) { return All.FirstOrDefault(d => d.Key == key); }
    }

    public static class LuxuryDrinkCatalog
    {
        public static readonly CocktailSpec[] Standard = {
            new CocktailSpec("wonderland",DrinkEffect.Luck,2,8,0,3,300),
            new CocktailSpec("spring_sway",DrinkEffect.TradeReputation,2,20,0,3,300)
        };
        public static readonly CocktailSpec Improv=new CocktailSpec("wonderland_improv",DrinkEffect.Luck,2,37,0,1,0);
        public static readonly CocktailSpec Roka=new CocktailSpec("roka",DrinkEffect.SaleIncome,2,15,0,1,0);
        public static bool IsStandard(string key) { return Standard.Any(s => s.Key==key); }
        public static bool IsLuxury(string key) { return IsStandard(key) || key==Improv.Key || key==Roka.Key; }
        public static bool OnMenu(string key,int day)
        { return CocktailCatalog.Drinks.Any(d => d.Key==key) || day>=LuxuryConfig.UnlockDay && IsStandard(key); }
    }
}
