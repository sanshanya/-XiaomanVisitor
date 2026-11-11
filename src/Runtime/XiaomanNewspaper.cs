using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
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
            return XiaomanNewspaper.Article(WeekNineRules.NewsId,Text("xm_w09_news_title"),Text("xm_w09_news_body"),WeekNineRules.NewsDay,1);
        }
        internal StoreEvent RenovationNews()
        {
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance==null || !WeekOneRules.HasRenovationNews(StoreStation.Instance.dayCounter)) return null;
            return XiaomanNewspaper.Article(WeekOneRules.NewsId,Text("xm_w01_news_title"),Text("xm_w01_news_body"),3,1);
        }
    }

    public static class XiaomanNewspaper
    {
        internal static StoreEvent Article(string id,string title,string body,int first,int duration)
        {
            return new StoreEvent { identifier=id,newsName=title,newsDescription=body,
                eventType=StoreEvent.EventType.COSMETIC,eventArea=StoreEvent.EventArea.LOWER,
                startDay=first,duration=duration,cooldown=0 };
        }

        public static StoreEvent LoungeExtensionArticle(int day,string title,string body)
        {
            if (!WeekElevenRules.HasExtensionNews(day)) return null;
            return Article(WeekElevenRules.NewsId,title,body,WeekElevenRules.NewsDay,WeekElevenRules.LastNewsDay-WeekElevenRules.NewsDay+1);
        }

        public static StoreEvent SupportArticle(int day,string title,string body)
        {
            if (!SupportProgress.RegistrationOpen(day)) return null;
            return Article(XiaomanMod.Id+".w04.news",title,body,22,3);
        }

        // Only a newspaper-view copy is changed, never the world's active events.
        public static List<StoreEvent> Append(List<StoreEvent> events, StoreEvent article)
        {
            if (events == null || article == null || events.Any(e => e!=null && e.identifier == article.identifier)
                || events.Count(e => e!=null && !e.isHiddenFromNewsPaper) >= 4) return events;
            var copy = new List<StoreEvent>(events);
            copy.Add(article);
            return copy;
        }
        public static List<StoreEvent> DisplayEvents(List<StoreEvent> native,Func<IEnumerable<StoreEvent>> articles,Action<Exception> failed=null)
        {
            try {
                var display=native;
                foreach (var article in articles()) display=Append(display,article);
                return display;
            }
            catch (Exception error) { failed?.Invoke(error);return native; }
        }
        internal static List<StoreEvent> GetDisplayEvents(StoreEventManager manager)
        {
            // Called only at PopulateUI's own read. All other event consumers
            // and the native newspaper click/toggle/fallback retain vanilla data.
            return DisplayEvents(manager.GetActiveEvents(),() => new[] {
                XiaomanMod.Instance?.RenovationNews(),XiaomanMod.Instance?.LoungeExtensionNews(),
                XiaomanMod.Instance?.LoungeOpeningNews(),XiaomanMod.Instance?.SupportNews()
            },error => XiaomanMod.Instance?.Log.Warning("Newspaper uses native articles: "+error));
        }
    }

    [HarmonyPatch(typeof(NewsUIManager), nameof(NewsUIManager.PopulateUI))]
    public static class XiaomanNewspaperPopulatePatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.Select(i => new CodeInstruction(i)).ToList();
            var read=AccessTools.Method(typeof(StoreEventManager),nameof(StoreEventManager.GetActiveEvents));
            var matches=code.Where(i => i.Calls(read)).ToArray();
            if (matches.Length!=1) return code;
            matches[0].opcode=OpCodes.Call;
            matches[0].operand=AccessTools.Method(typeof(XiaomanNewspaper),nameof(XiaomanNewspaper.GetDisplayEvents));
            return code;
        }
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception!=null) XiaomanMod.Instance?.Log.Warning("Native newspaper rendering failed: "+__exception);
            return __exception;
        }
    }

}
