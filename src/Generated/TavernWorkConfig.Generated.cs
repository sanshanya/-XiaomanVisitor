// Generated from assets/tavern-work.json.
namespace XiaomanVisitor { public static class TavernWorkConfig {
public const string Source="dock_shift_overflow_v1";
public const int UnlockDay = 15;
public const int DailyOrders = 3;
public const int OrdersPerShift = 3;
public const int OrderRevenue = 65;
public const int OrderMaterial = 30;
public const int Wage = 60;
public const int DurationSeconds = 3;
public const int ThanksPercent = 35;
public const int PrivateReserve = 100;
public static int DemandCount(int day) { switch(day) {
default: return DailyOrders; } }
} }
