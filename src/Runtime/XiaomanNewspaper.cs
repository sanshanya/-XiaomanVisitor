using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        internal StoreEvent LoungeExtensionNews()
        {
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance==null || !WeekElevenRules.HasExtensionNews(StoreStation.Instance.dayCounter)) return null;
            return XiaomanNewspaper.LoungeExtensionArticle(StoreStation.Instance.dayCounter,
                Text("xm_w11_news_title"),Text("xm_w11_news_body"));
        }
        internal StoreEvent LoungeOpeningNews()
        {
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance==null || !WeekNineRules.HasOpeningNews(StoreStation.Instance.dayCounter)) return null;
            return new StoreEvent {
                identifier=WeekNineRules.NewsId,
                newsName=Text("xm_w09_news_title"), newsDescription=Text("xm_w09_news_body"),
                eventType=StoreEvent.EventType.COSMETIC, eventArea=StoreEvent.EventArea.LOWER,
                startDay=WeekNineRules.NewsDay, duration=1, cooldown=0
            };
        }
        internal StoreEvent RenovationNews()
        {
            if (!enabled || !PlayerStore.HasInstance || !WeekOneRules.HasRenovationNews(StoreStation.Instance.dayCounter)) return null;
            return new StoreEvent {
                identifier = WeekOneRules.NewsId,
                newsName = Text("xm_w01_news_title"), newsDescription = Text("xm_w01_news_body"),
                eventType = StoreEvent.EventType.COSMETIC, eventArea = StoreEvent.EventArea.LOWER,
                startDay = 3, duration = 1, cooldown = 0
            };
        }
    }

    public static class XiaomanNewspaper
    {
        internal static bool Rendering;

        public static StoreEvent LoungeExtensionArticle(int day,string title,string body)
        {
            if (!WeekElevenRules.HasExtensionNews(day)) return null;
            return new StoreEvent { identifier=WeekElevenRules.NewsId,newsName=title,newsDescription=body,
                eventType=StoreEvent.EventType.COSMETIC,eventArea=StoreEvent.EventArea.LOWER,
                startDay=WeekElevenRules.NewsDay,duration=WeekElevenRules.LastNewsDay-WeekElevenRules.NewsDay+1,cooldown=0 };
        }

        public static StoreEvent SupportArticle(int day,string title,string body)
        {
            if (!SupportProgress.RegistrationOpen(day)) return null;
            return new StoreEvent { identifier=XiaomanMod.Id+".w04.news",newsName=title,newsDescription=body,
                eventType=StoreEvent.EventType.COSMETIC,eventArea=StoreEvent.EventArea.LOWER,startDay=22,duration=3,cooldown=0 };
        }

        // Only a newspaper-view copy is changed, never the world's active events.
        public static List<StoreEvent> Append(List<StoreEvent> events, StoreEvent article)
        {
            if (events == null || article == null || events.Any(e => e.identifier == article.identifier)
                || events.Count(e => !e.isHiddenFromNewsPaper) >= 4) return events;
            var copy = new List<StoreEvent>(events);
            copy.Add(article);
            return copy;
        }
    }

    [HarmonyPatch(typeof(NewsUIManager), nameof(NewsUIManager.PopulateUI))]
    public static class XiaomanNewspaperScopePatch
    {
        private static void Prefix(out bool __state)
        { __state = XiaomanNewspaper.Rendering; XiaomanNewspaper.Rendering = true; }
        private static Exception Finalizer(Exception __exception, bool __state)
        { XiaomanNewspaper.Rendering = __state; return __exception; }
    }

    [HarmonyPatch(typeof(StoreEventManager), nameof(StoreEventManager.GetActiveEvents))]
    public static class XiaomanNewspaperEventsPatch
    {
        private static void Postfix(ref List<StoreEvent> __result)
        {
            if (!XiaomanNewspaper.Rendering) return;
            __result = XiaomanNewspaper.Append(__result, XiaomanMod.Instance?.RenovationNews());
            // Every article enforces its own calendar window before being appended.
            __result = XiaomanNewspaper.Append(__result,XiaomanMod.Instance?.LoungeExtensionNews());
            __result = XiaomanNewspaper.Append(__result,XiaomanMod.Instance?.LoungeOpeningNews());
            __result = XiaomanNewspaper.Append(__result,XiaomanMod.Instance?.SupportNews());
        }
    }
}
