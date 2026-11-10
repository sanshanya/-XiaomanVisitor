// Generated from story/第十一周实现.json; newspaper display only.
namespace XiaomanVisitor { public static class WeekElevenRules {
public const int NewsDay=71, LastNewsDay=73;
public const string NewsId=XiaomanMod.Id+".jizhou_extension_news";
public static bool HasExtensionNews(int day) { return day>=NewsDay && day<=LastNewsDay; }
} }
