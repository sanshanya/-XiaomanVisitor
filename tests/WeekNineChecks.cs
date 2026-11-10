using System;
using System.Collections.Generic;
using System.Linq;
using XiaomanVisitor;

internal static class WeekNineChecks
{
    public static void Run(Action<bool,string> check)
    {
        var chapter=StoryRules.Episode(9);
        check(chapter.Credits==450 && chapter.Demand==XiaomanDemand.Food && chapter.TrackKey=="lost",
            "W09 private food purchase keeps the fixed 450-credit budget and lost cassette");
        check(chapter.Opening.Select(l => l.Key).SequenceEqual(new[]{"xm_09_01","xm_09_02","xm_09_03","xm_09_shop"})
            && chapter.Opening.Take(3).All(l => l.Mood==Mood.Smile) && chapter.AcceptKey=="xm_09_accept"
            && chapter.GoodbyeKey=="xm_09_bye","W09 prosperous revision replaces the competitor-visit chapter");
        check(TavernRules.OpeningLineKey(9,"xm_09_02",57)=="xm_09_02"
            && TavernRules.OpeningLineKey(9,"xm_09_02",64)=="xm_09_02_late","W09 delayed chapter does not call its old extra batch last week");
        check(!WeekNineRules.HasOpeningNews(56) && WeekNineRules.HasOpeningNews(57) && !WeekNineRules.HasOpeningNews(58),
            "W09 today-opening newspaper has an exact world-calendar publication day");
        var native=new List<StoreEvent> { new StoreEvent { identifier="native_story" },new StoreEvent { identifier="native_economy" },new StoreEvent { identifier="native_special" } };
        var opening=new StoreEvent { identifier=WeekNineRules.NewsId,eventType=StoreEvent.EventType.COSMETIC };
        var shown=XiaomanNewspaper.Append(native,opening);
        var withOldSupport=XiaomanNewspaper.Append(shown,new StoreEvent { identifier=XiaomanMod.Id+".w04.news" });
        check(native.Count==3 && shown.Count==4 && ReferenceEquals(shown,withOldSupport)
            && shown.Take(3).SequenceEqual(native) && shown[3].identifier==WeekNineRules.NewsId,
            "W09 takes this mod's spare news slot before ongoing support news and never changes native articles");
        check(ReferenceEquals(shown,XiaomanNewspaper.Append(shown,opening)),"W09 repeated paper refresh never duplicates the opening article");

        var tavern=new TavernProgress();string first="xm_tavern_09_chat_01";
        check(tavern.NextChat(57,false,8)=="xm_tavern_08_chat_01" && tavern.NextChat(57,false,9)==first,
            "W09 grouped chat follows actual ninth encounter; newspaper remains calendar-only");
        var sequence=TavernDialogue.ChatSequence(first);
        check(sequence.SequenceEqual(new[]{first,"xm_tavern_09_chat_02","xm_tavern_09_chat_03"})
            && sequence.All(k => TavernDialogue.MoodFor(k)==Mood.Smile),"W09 preparation story is three consecutive lighthearted native-bubble lines");
        check(tavern.NextChat(57,false,9)==first && tavern.ChatSeen.Count==0,"W09 starting or interrupting the sequence does not mark it read");
        tavern.ChatSeen.Add(TavernProgress.ChatReceipt(first,57));tavern=TavernProgress.Restore(tavern.Data,57);
        check(!tavern.RecoveryBlocked && tavern.NextChat(57,false,9)=="xm_tavern_chat_rest"
            && TavernDialogue.ChatSequence("xm_tavern_08_chat_01").SequenceEqual(new[]{"xm_tavern_08_chat_01"}),
            "W09 completion consumes the grouped entry once; older one-line chats retain their behavior");
        var work=new TavernWorkProgress { RunIdentity="run" };
        check(work.EnsureDemand(57,TavernWorkConfig.OrdersPerShift) && work.MayWork(57),
            "W09 real outstanding orders still permit paid work");
        var noOrders=new TavernWorkProgress { RunIdentity="run" };noOrders.EnsureDemand(57,0);
        check(!noOrders.MayWork(57),"W09 no shift is invented when actual outstanding demand is insufficient");
        var supply=new SupplyProgress();supply.Sign(22);supply.ProduceLuxury(57,true);
        check(supply.Batches.Count==2 && supply.Batches.All(b => b.PlayerRemaining==2 && b.StoreRemaining==4)
            && TavernPrices.For(CocktailCatalog.Find("wonderland"))==300,"W09 does not confiscate partner inventory or discount Xiaoman's own menu");
        var referrals=new ReferralProgress { Run="run" };
        check(!referrals.Draw(57) && referrals.Draw(58) && ReferralConfig.DailyPercent==25,
            "W09 newspaper does not disable or weaken the existing non-Monday referrals");
        Console.WriteLine("PASS: W09 prosperous chapter, exact-day cosmetic news, original article priority, grouped chat completion and preserved work/supply/referral rules.");
    }
}
