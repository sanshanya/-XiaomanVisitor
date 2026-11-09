namespace XiaomanVisitor
{
    public static class WeekOneRules
    {
        public const string NewsId = XiaomanMod.Id + ".renovation_news";
        public static bool HasRenovationNews(int day) { return day >= 3 && day <= 7; }
    }
}
