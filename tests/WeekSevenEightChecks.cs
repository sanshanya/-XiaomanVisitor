using System;
using System.Linq;
using XiaomanVisitor;

internal static class WeekSevenEightChecks
{
    public static void Run(Action<bool,string> check)
    {
        var seven=StoryRules.Episode(7);var eight=StoryRules.Episode(8);
        check(seven.Credits==450 && seven.Demand==XiaomanDemand.Food && seven.TrackKey=="diary"
            && eight.Credits==500 && eight.Demand==XiaomanDemand.Household && eight.TrackKey=="gallery1",
            "W07/W08 retain fixed budgets, private shopping categories and tape order");
        foreach (var episode in new[]{seven,eight}) {
            string prefix="xm_"+episode.Index.ToString("00")+"_";
            check(episode.Opening.Select(l => l.Key).SequenceEqual(new[]{prefix+"01",prefix+"02",prefix+"03",prefix+"04",prefix+"shop"})
                && episode.AcceptKey==prefix+"accept" && episode.GoodbyeKey==prefix+"bye" && episode.CocktailKey=="",
                "W07/W08 chapter only adds authored dialogue, not a new sale/gift cue");
        }
        check(TavernRules.OpeningLineKey(7,"xm_07_03",43)=="xm_07_03"
            && TavernRules.OpeningLineKey(7,"xm_07_03",57)=="xm_07_03_late"
            && TavernRules.OpeningLineKey(8,"xm_08_02",50)=="xm_08_02"
            && TavernRules.OpeningLineKey(8,"xm_08_02",64)=="xm_08_02_late",
            "W07/W08 delayed arrivals never describe the old story event as yesterday");
        var story=new StoryProgress();StoryVisitRecord visit;
        for (int n=1;n<=6;n++) story.BeginVisit((n-1)*7+1,99,0,out visit);
        var tavern=new TavernProgress();
        check(story.VisitIndex==6 && tavern.NextChat(50,false,story.VisitIndex)=="xm_tavern_06_chat_01",
            "W07/W08 calendar alone cannot reveal unencountered story in night chats");
        check(story.BeginVisit(57,99,0,out visit) && visit.Index==7 && visit.PartCount==2 && !visit.GiftComplete
            && !story.BeginVisit(57,99,0,out visit),"W07 delayed chapter remains seventh and advances once without a trade or night visit");
        check(tavern.NextChat(57,false,story.VisitIndex)=="xm_tavern_07_chat_01"
            && tavern.NextChat(57,false,story.VisitIndex)=="xm_tavern_07_chat_01",
            "W07 choosing an unfinished chat never prematurely consumes it");
        tavern.ChatSeen.Add("xm_tavern_07_chat_01");tavern=TavernProgress.Restore(tavern.Data,57);
        check(!tavern.RecoveryBlocked && tavern.NextChat(57,false,story.VisitIndex)=="xm_tavern_07_chat_02",
            "W07 completed chat receipt survives reload and advances within its two-line pool");
        tavern.ChatSeen.Add("xm_tavern_07_chat_02");
        check(tavern.NextChat(57,false,story.VisitIndex)=="xm_tavern_chat_rest", "W07 exhausted pool does not repeat or start W08 early");
        check(story.BeginVisit(64,99,0,out visit) && visit.Index==8 && visit.PartCount==2
            && tavern.NextChat(64,false,story.VisitIndex)=="xm_tavern_08_chat_01", "W08 follows actual seventh encounter and unlocks after its own meeting");
        tavern.ChatSeen.Add("xm_tavern_08_chat_01");tavern=TavernProgress.Restore(tavern.Data,64);
        check(tavern.NextChat(64,false,8)=="xm_tavern_08_chat_02"
            && tavern.NextChat(49,false,8)=="xm_tavern_chat_rest", "W08 receipt and calendar floor both remain effective");
        check(TavernDialogue.WeeklyChatPool(29,0).SequenceEqual(new[]{"xm_tavern_05_daily"})
            && TavernDialogue.WeeklyChatPool(36,0).SequenceEqual(new[]{"xm_tavern_06_chat_01","xm_tavern_06_chat_02"}),
            "W07/W08 shared generator preserves W05/W06 calendar-only chat rules");
        check(!TavernDialogue.Lines.Any(l => l.Key.StartsWith("xm_tavern_07_") && l.Key.EndsWith("_unknown")
            || l.Key.StartsWith("xm_tavern_08_") && l.Key.EndsWith("_unknown")),
            "W07/W08 meeting-gated chat adds no unreachable stranger voice variants");
        Console.WriteLine("PASS: W07/W08 authored chapter bindings, fixed budgets/tapes, delayed chronology and encounter-gated persistent night chats; W05/W06 rules preserved.");
    }
}
