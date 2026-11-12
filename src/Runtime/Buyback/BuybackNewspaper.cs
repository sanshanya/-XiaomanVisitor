using System.Collections.Generic;
using System.Linq;

namespace XiaomanVisitor.SideStories
{
    internal static class BuybackNewspaper
    {
        internal const string ArticleId = BuybackStory.Id + ".refusal_news";
        internal static StoreEvent Article(BuybackRefusalText text, int day)
        {
            return new StoreEvent { identifier = ArticleId, newsName = "<b>" + text.news_title + "</b>", newsDescription = text.news_body,
                eventType = StoreEvent.EventType.COSMETIC, eventArea = StoreEvent.EventArea.LOWER,
                startDay = day, duration = 1, cooldown = 0 };
        }
        internal static List<StoreEvent> Append(List<StoreEvent> native, StoreEvent article)
        {
            if (native == null || article == null || native.Any(e => e != null && e.identifier == ArticleId)) return native;
            var display = new List<StoreEvent>(native);
            var visible = native.Select((e, index) => new { e, index }).Where(p => p.e != null && !p.e.isHiddenFromNewsPaper).ToArray();
            if (visible.Length < 4) display.Add(article);
            else
            {
                // Native paper has four slots. Preserve the fourth article's text and negotiation data.
                var slot = visible[3]; var original = slot.e;
                display[slot.index] = new StoreEvent { identifier = ArticleId, newsName = original.newsName,
                    newsDescription = original.newsDescription + "\n\n" + article.newsName + "\n" + article.newsDescription,
                    eventType = original.eventType, eventArea = original.eventArea, startDay = original.startDay,
                    duration = original.duration, cooldown = original.cooldown, isPermanent = original.isPermanent,
                    negociationDatas = original.negociationDatas };
            }
            return display;
        }
    }
}
