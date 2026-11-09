// Generated from assets/tavern-prices.json; independent of inventory resale value.
namespace XiaomanVisitor { public static class TavernPrices {
public const int Juice = 95, Alcohol = 190;
public static int For(CocktailSpec spec) { return spec.Tier == 1 ? Juice : Alcohol; }
} }
