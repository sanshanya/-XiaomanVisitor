using System.Linq;

namespace XiaomanVisitor
{
    public enum DrinkEffect { None, Luck, TradeReputation, Scavenging }

    public sealed class CocktailSpec
    {
        public readonly string Key;
        public readonly int Width;
        public readonly int Height;
        public readonly DrinkEffect Effect;
        public readonly int Tier;
        public readonly int EffectAmount;
        public readonly int RewardThreshold;
        public int DurationDays { get { return 3; } }
        public const int Price = 30;

        public CocktailSpec(string key, DrinkEffect effect, int tier, int amount, int threshold)
        {
            Key = key;
            Width = 16;
            Height = 32;
            Effect = effect;
            Tier = tier;
            EffectAmount = amount;
            RewardThreshold = threshold;
        }

        public string ItemId { get { return XiaomanMod.Id + ".cocktail." + Key; } }
        public string SpriteKey { get { return "xiaoman_cocktail_" + Key; } }
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
        public static CocktailSpec Find(string key) { return Drinks.FirstOrDefault(d => d.Key == key); }
    }
}
