// Generated from story/第九周实现.json; newspaper display only.
namespace XiaomanVisitor { public static class WeekNineRules {
public const int NewsDay=57, LastNewsDay=57;
public const string NewsId=XiaomanMod.Id+".jizhou_opening_news";
public static bool HasOpeningNews(int day) { return day>=NewsDay && day<=LastNewsDay; }
} }
