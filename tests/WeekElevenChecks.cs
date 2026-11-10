using System;
using System.Collections.Generic;
using System.Linq;
using XiaomanVisitor;

internal static class WeekElevenChecks
{
    public static void Run(Action<bool,string> check)
    {
        var episode=StoryRules.Episode(11);
        check(episode.Credits==350 && episode.Demand==XiaomanDemand.Household && episode.TrackKey=="deepnight"
            && episode.Opening.Select(l => l.Key).SequenceEqual(new[]{"xm_11_01","xm_11_02","xm_11_03","xm_11_04","xm_11_shop"})
            && episode.Opening.Take(4).Select(l => l.Mood).SequenceEqual(new[]{Mood.Soft,Mood.Neutral,Mood.Soft,Mood.Smile})
            && episode.AcceptKey=="xm_11_accept" && episode.GoodbyeKey=="xm_11_bye",
            "W11 final chapter binds the 350-credit household purchase, deepnight and restrained moods");
        var tavern=new TavernProgress();const string chat="xm_tavern_11_chat_01";
        check(tavern.NextChat(71,false,10,64)==WeekTenDialogue.InventoryEntry && tavern.NextChat(71,false,11)==chat,
            "W11 newspaper does not reveal the encounter-gated conversation early");
        check(TavernDialogue.ChatSequence(chat).SequenceEqual(new[]{chat,"xm_tavern_11_chat_02","xm_tavern_11_chat_03"})
            && !TavernDialogue.Lines.Any(l => l.Key=="xm_tavern_11_chat_04"),
            "W11 promise conversation ends after three lines, without the rejected fourth line");
        check(tavern.NextChat(78,false,11)==chat && tavern.ChatSeen.Count==0,
            "W11 postponed or interrupted conversations remain available without consuming a receipt");
        tavern.ChatSeen.Add(chat);tavern=TavernProgress.Restore(tavern.Data,78);
        check(!tavern.RecoveryBlocked && tavern.NextChat(78,false,11)=="xm_tavern_chat_rest",
            "W11 completed conversation uses the existing persistent receipt and does not repeat after reload");

        check(XiaomanNewspaper.LoungeExtensionArticle(70,"title","body")==null
            && XiaomanNewspaper.LoungeExtensionArticle(74,"title","body")==null
            && XiaomanNewspaper.LoungeExtensionArticle(78,"title","body")==null,
            "W11 promotion announcement is absent before Monday and from Thursday onward, even after a delayed meeting");
        foreach (int day in new[]{71,72,73}) {
            var article=XiaomanNewspaper.LoungeExtensionArticle(day,"title","body");
            check(article!=null && article.startDay==71 && article.duration==3 && article.identifier==WeekElevenRules.NewsId
                && article.eventType==StoreEvent.EventType.COSMETIC && article.newsName=="title" && article.newsDescription=="body",
                "W11 displays a bounded, localized cosmetic article, not an active economic event");
        }
        var native=Enumerable.Range(0,4).Select(i => new StoreEvent { identifier="native_"+i }).ToList();
        var articleNow=XiaomanNewspaper.LoungeExtensionArticle(71,"title","body");
        check(ReferenceEquals(native,XiaomanNewspaper.Append(native,articleNow)), "W11 preserves all four native newspaper articles");
        var room=native.Take(3).ToList();var shown=XiaomanNewspaper.Append(room,articleNow);
        check(room.Count==3 && shown.Take(3).SequenceEqual(room) && ReferenceEquals(shown,XiaomanNewspaper.Append(shown,articleNow)),
            "W11 appends only to the newspaper copy and never duplicates its announcement");

        var story=new StoryProgress();StoryVisitRecord visit;
        for (int n=1;n<=10;n++) { story.BeginVisit((n-1)*7+1,99,0,out visit);visit.GiftMask=(1<<visit.PartCount)-1; }
        check(story.BeginVisit(78,99,0,out visit) && visit.Index==11 && story.DueGift(78)==null
            && story.DueGift(79)==visit && visit.PartCount==2,
            "W11 missed Monday postpones the actual chapter and both next-day gifts without requiring a trade or night visit");
        check(TavernRules.IsOpen(true,true,71,false) && TavernRules.IsOpen(true,true,78,false)
            && tavern.Enter(78,true).Length>0 && tavern.Enter(78,true).Length>0 && TavernRules.IsOpen(true,true,78,false),
            "W11 midnight is narrative only: normal entry and same-night return remain available");
        Console.WriteLine("PASS: W11 final dialogue/letter bindings, three-line persistent chat, Monday-Wednesday cosmetic news, independent gifts and unchanged night access.");
    }
}
