using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using XiaomanVisitor;

internal static class WeekTwoChecks
{
    internal static void Run(string package,Action<bool,string> check)
    {
        int basic=TavernPrices.Juice, strong=TavernPrices.Alcohol;
        foreach (int day in new[]{1,2,3,7}) check(!TavernRules.IsOpen(true,true,day,false),"W02-A01/02: no first-week entry, including former preview days");
        check(TavernRules.IsOpen(true,true,8,false) && TavernRules.IsOpen(true,true,52,false),"W02-A03/13: calendar opens independently of the encounter/gift clocks");
        check(!TavernRules.IsOpen(false,true,8,false) && !TavernRules.IsOpen(true,false,8,false)
            && !TavernRules.IsOpen(true,true,99,true),"W02-A16: disabled, unloaded and departed remain closed");
        check(TavernRules.StatusKey(TavernMapRules.LocationId,true,true,7,false)=="ui_tavern_renovating"
            && TavernRules.StatusKey(TavernMapRules.LocationId,true,true,8,false)==null
            && TavernRules.StatusKey("commissary",true,true,8,false)==null,"renovation status ends without changing other native cards");
        var chapter=StoryRules.Episode(2);
        check(chapter.Credits==200 && chapter.Demand==XiaomanDemand.Household && chapter.TrackKey=="intro"
            && chapter.Opening.Select(l => l.Key).SequenceEqual(new[]{"xm_02_01","xm_02_02","xm_02_03","xm_02_04","xm_02_shop"}),
            "W02-A04: second encounter preserves demand, budget, fixed tape and new invitation");
        check(TavernRules.OpeningLineKey(2,"xm_02_02",8)=="xm_02_02"
            && TavernRules.OpeningLineKey(2,"xm_02_02",15)=="xm_02_02_late"
            && TavernRules.OpeningLineKey(1,"xm_01_02",15)=="xm_01_02","W02-A14: delayed encounter does not promise opening tonight");
        var p=new TavernProgress();
        check(p.Enter(8,true).Length==3,"W02-A05: first acquainted arrival has the three-line welcome");
        p.IntroSeen=true;
        check(p.Enter(8,true).Single()=="xm_tavern_w02_reentry","same-night second entry uses the short return line");
        p=TavernProgress.Restore(p.Data,8);
        check(p.Enter(8,true).Single()=="xm_tavern_greeting" && p.EntriesToday==3,"reload preserves same-day entry count without replaying the welcome");
        check(p.Enter(9,true).Single()=="xm_tavern_greeting" && p.EntriesToday==1,"new day resets only the entry counter");
        var unknown=new TavernProgress();
        check(unknown.Enter(8,false).Single()=="xm_tavern_greeting_unknown","unacquainted visitor hears a self-introduction");
        unknown.UnknownIntroSeen=true;
        check(unknown.Enter(8,false).Single()=="xm_tavern_greeting_plain"
            && TavernDialogue.KeyFor("xm_tavern_w02_help_intro",false).EndsWith("_unknown"),"unknown visitors never acquire senpai merely by entering");
        check(TavernRules.MusicKeys(0).SequenceEqual(new[]{"title","intro"}) && TavernRules.MusicKeys(3).Length==3,
            "W02-A06: room playback permission includes two tapes independently of inventory ownership");
        p.RainIntroSeen=true; p.TapeIntroSeen=true;
        p=TavernProgress.Restore(p.Data,9);
        check(p.RainIntroSeen && p.TapeIntroSeen,"W02-A07: discovery introductions persist separately from the gift clock");
        check(p.NextChat(8)=="xm_tavern_w02_chat_01","opening night never mentions yesterday's customer");
        p.ChatSeen.Add(p.NextChat(8));
        check(p.NextChat(8)=="xm_tavern_chat_rest" && p.NextChat(9)=="xm_tavern_w02_chat_02","chat availability is optional and does not replay the first-night monologue");
        p.ChatSeen.Add("xm_tavern_w02_chat_02");p.ChatSeen.Add("xm_tavern_w02_chat_03");
        check(p.NextChat(11)=="xm_tavern_chat_rest" && p.NextChat(12)=="xm_tavern_w02_chat_04","later small talk opens without daily attendance requirements");
        check(new TavernProgress().NextChat(12)=="xm_tavern_w02_chat_04" && !new TavernProgress().NextChat(15).StartsWith("xm_tavern_w02_"),
            "missed opening-week chats do not become a backlog in week three");
        p=new TavernProgress();
        check(!p.MayHelp(7) && p.Help(8) && !p.Help(8),"W02-A08: reception can be confirmed once in the opening week");
        p=TavernProgress.Restore(p.Data,8);
        check(!p.MayHelp(9) && !new TavernProgress().MayHelp(15),"help completion survives reload; unfinished event closes after day fourteen");
        check(p.PrepareDrink(8,"sunset_blue",basic-1,"")==null && p.Drinks.Count==0,"W02-A11: insufficient funds creates no receipt or consumption");
        var receipt=p.PrepareDrink(8,"sunset_blue",basic,"");
        check(receipt!=null && receipt.Paid==basic && p.Recover(8,basic,"")==TavernRecovery.Cancel,
            "W02-A09: helping does not consume the daily glass; an untouched prepared receipt can cancel safely");
        check(p.PrepareDrink(8,"bluebird",999,"")==null,"prepared/double-click attempt cannot issue a second charge");
        check(p.Recover(8,0,"")==TavernRecovery.ApplyBuff && p.Recover(8,0,receipt.AfterBuffs)==TavernRecovery.Finish,
            "W02-A10: exact paid snapshots recover the original effect without another wallet operation");
        check(p.Recover(8,basic+1,"")==TavernRecovery.Hold && p.Recover(9,0,"")==TavernRecovery.Hold,
            "ambiguous cash or a different-day snapshot fails closed");
        var cancelled=new TavernProgress();
        var attempt=cancelled.PrepareDrink(8,"sunset_blue",basic,""); attempt.Status="cancelled";
        cancelled=TavernProgress.Restore(cancelled.Data,8);
        check(cancelled.MayDrink(8) && cancelled.PrepareDrink(8,"sunset_blue",basic,"")!=null && cancelled.Drinks.Count==1,
            "a confirmed untouched cancellation allows a fresh attempt without duplicate daily receipts");
        receipt.Status="complete";
        p=TavernProgress.Restore(p.Data,8);
        p.Enter(8,true); p.Enter(8,true);
        check(p.Drank(8) && !p.MayDrink(8) && p.Recover(8,0,receipt.AfterBuffs)==TavernRecovery.None,
            "same-night extra outings, re-entry and reload never reissue a completed drink");
        check(p.MayDrink(9),"W02-A12: following game day has its own one-glass allowance");
        var buffs=DrinkBuffCollection.Restore(receipt.AfterBuffs);
        check(buffs.ActiveAmount(DrinkEffect.Luck,8)==5 && buffs.ActiveAmount(DrinkEffect.Luck,10)==5
            && buffs.ActiveAmount(DrinkEffect.Luck,11)==0,"onsite drink uses the existing three-day rule, including purchase day");
        foreach (var spec in CocktailCatalog.Drinks) {
            var test=new TavernProgress();
            var r=test.PrepareDrink(8,spec.Key,Math.Max(basic,strong),receipt.AfterBuffs);
            var expected=DrinkBuffCollection.Restore(receipt.AfterBuffs); expected.Replace(spec,8);
            check(r!=null && r.Paid==(spec.Tier==1?basic:strong) && r.AfterBuffs==expected.Data,
                "every menu drink reuses its real category, amount, duration and replacement rule: "+spec.Key);
        }
        string legacy="{\"Schema\":1,\"OpenedOn\":1,\"Entries\":[{\"Day\":1,\"Action\":\"pickup_point\",\"Paid\":0},{\"Day\":2,\"Action\":\"cup\",\"Paid\":35}]}";
        var migrated=TavernProgress.Restore(legacy,8);
        check(migrated.Schema==2 && migrated.LegacyData==legacy && migrated.Entries.Count==2
            && migrated.MayHelp(8) && migrated.MayDrink(8),"W02-A16: exact prototype history remains; old project/cup do not unlock or consume new actions");
        check(TavernProgress.Restore(migrated.Data,8).LegacyData==legacy,"migration is idempotent and retains original history");
        check(TavernProgress.Restore("broken",8).RecoveryBlocked
            && !TavernProgress.Restore("{\"Schema\":99}",8).MayDrink(8),"malformed/future records preserve raw data and cannot silently reset paid consumption");
        var damaged=new TavernProgress(); damaged.PrepareDrink(8,"sunset_blue",basic,"").AfterBuffs="";
        check(TavernProgress.Restore(damaged.Data,8).RecoveryBlocked,"an inconsistent saved buff snapshot cannot be treated as a fresh unpaid transaction");
        Console.WriteLine("PASS: W02 calendar, invitation, greetings, optional chat, help/drink independence, all six effects, payment recovery and prototype migration.");
    }
}
