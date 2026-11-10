using System;
using System.Linq;
using XiaomanVisitor;

internal static class WeekTenChecks
{
    private static SupplyProgress Stock()
    {
        var p=new SupplyProgress { Water=1,Juice=1 };p.Sign(22);
        p.Batches.Add(new SupplyBatch { Id="prior_week",Key="wonderland",Day=57,Produced=6,PlayerInitial=2,StoreInitial=4 });
        return p;
    }
    public static void Run(Action<bool,string> check)
    {
        var episode=StoryRules.Episode(10);
        check(episode.Credits==400 && episode.Demand==XiaomanDemand.Water && episode.TrackKey=="gallery2"
            && episode.Opening.Select(l => l.Key).SequenceEqual(new[]{"xm_10_01","xm_10_02","xm_10_03","xm_10_04","xm_10_05","xm_10_shop"})
            && episode.AcceptKey=="xm_10_accept" && episode.GoodbyeKey=="xm_10_bye", "W10 fixed water budget, tape and final five-line visit are bound");
        check(TavernRules.OpeningLineKey(10,"xm_10_01",64)=="xm_10_01"
            && TavernRules.OpeningLineKey(10,"xm_10_01",78)=="xm_10_01_late", "W10 delayed opening uses neutral relative dates");
        var tavern=new TavernProgress();string inventory=WeekTenDialogue.InventoryEntry,regular="xm_tavern_10_regular_01";
        check(tavern.NextChat(64,false,9,57)=="xm_tavern_09_chat_01"
            && tavern.NextChat(64,false,10,64)==inventory, "W10 night story waits for the actual tenth meeting");
        tavern.ChatSeen.Add(inventory);tavern=TavernProgress.Restore(tavern.Data,64);
        check(tavern.NextChat(65,false,10,64)=="xm_tavern_chat_rest" && tavern.NextChat(66,false,10,64)==regular,
            "W10 returning regular appears midweek, after the encounter plus two days");
        check(tavern.NextChat(72,false,10,71)=="xm_tavern_chat_rest"
            && tavern.NextChat(73,false,10,71)==regular && tavern.NextChat(80,false,10)=="xm_tavern_chat_rest",
            "W10 delayed or missing encounter timestamp cannot release the return story early");
        check(TavernDialogue.ChatSequence(regular).Length==4 && TavernDialogue.ChatSequence(inventory).Length==3
            && TavernDialogue.ChatSequence(WeekTenDialogue.Supported).Length==3 && TavernDialogue.ChatSequence(WeekTenDialogue.Neutral).Length==3,
            "W10 stock branches and returning regular play as continuous native-bubble sequences");
        tavern.ChatSeen.Add(regular);tavern=TavernProgress.Restore(tavern.Data,66);
        check(tavern.NextChat(67,false,10,64)=="xm_tavern_chat_rest" && tavern.ChatSeen.Count==2,
            "W10 completion shares the original logical receipt across all inventory variants and survives reload");

        var work=new TavernWorkProgress { RunIdentity="run" };var supply=Stock();
        check(WeekTenDialogue.InventorySequence(64,null,work,tavern)==WeekTenDialogue.Neutral,
            "W10 absent sales/inventory records never claim an accumulation");
        string before=supply.Data,workBefore=work.Data,tavernBefore=tavern.Data;
        check(WeekTenDialogue.InventorySequence(64,supply,work,tavern)==inventory
            && supply.Data==before && work.Data==workBefore && tavern.Data==tavernBefore,
            "W10 retained bar stock plus actual common ingredients selects stock text without any economic mutation");
        supply.Water=0;
        check(WeekTenDialogue.InventorySequence(64,supply,work,tavern)==WeekTenDialogue.Neutral,
            "W10 finished drinks alone cannot prove leftover raw ingredients");
        supply=Stock();supply.Batches[0].StoreSold=4;
        check(WeekTenDialogue.InventorySequence(64,supply,work,tavern)==WeekTenDialogue.Neutral,
            "W10 player-owned shares cannot be misreported as bar surplus");
        supply=Stock();supply.Batches[0].Key="whale_dream";
        check(WeekTenDialogue.InventorySequence(64,supply,work,tavern)==WeekTenDialogue.Neutral,
            "W10 leftover old menu stock alone cannot substantiate the new-drink ingredients story");
        supply=Stock();supply.Blocked=true;
        check(WeekTenDialogue.InventorySequence(64,supply,work,tavern)==WeekTenDialogue.Neutral,
            "W10 unreadable or blocked stock falls back conservatively");
        supply=Stock();var trade=supply.PrepareOutput("run",64,"prior_week",12,500);
        check(trade!=null && WeekTenDialogue.InventorySequence(64,supply,work,tavern)==WeekTenDialogue.Neutral,
            "W10 a prepared unpaid wholesale transaction is not a completed order");
        check(supply.Complete(trade) && WeekTenDialogue.InventorySequence(64,supply,work,tavern)==WeekTenDialogue.Supported
            && supply.Batches[0].StoreRemaining==4, "W10 actual partner purchase selects supported text without taking the bar share");
        supply=Stock();work.EnsureDemand(64,TavernWorkConfig.OrdersPerShift);var shift=work.PrepareShift(64,500);
        check(WeekTenDialogue.InventorySequence(64,supply,work,tavern)==inventory,
            "W10 prepared work cannot stand in for fulfilled demand");
        check(work.Complete(shift) && WeekTenDialogue.InventorySequence(64,supply,work,tavern)==WeekTenDialogue.Supported,
            "W10 actual fulfilled work overrides surplus talk even when inventory remains");
        check(WeekTenDialogue.InventorySequence(78,supply,work,tavern)==WeekTenDialogue.Neutral,
            "W10 old work receipts and old batches do not masquerade as this week's business");
        supply=Stock();work=new TavernWorkProgress();tavern=new TavernProgress();
        var drink=tavern.PrepareDrink(64,"wonderland",500,"");supply.ReserveBar(drink.Id,drink.Key);
        check(WeekTenDialogue.InventorySequence(64,supply,work,tavern)==inventory,
            "W10 an unsettled onsite reservation does not count as revenue");
        drink.Status="complete";supply.SettleBar(drink.Id,drink.Paid);
        check(WeekTenDialogue.InventorySequence(64,supply,work,tavern)==WeekTenDialogue.Supported,
            "W10 a matched complete drink and settled bar receipt is actual business evidence");

        // Generic gift engine, including missed Mondays, remains independent of all night branches.
        var story=new StoryProgress();StoryVisitRecord visit;
        for (int n=1;n<=9;n++) { story.BeginVisit((n-1)*7+1,99,0,out visit);visit.GiftMask=(1<<visit.PartCount)-1; }
        check(story.BeginVisit(71,99,0,out visit) && visit.Index==10 && story.DueGift(71)==null
            && story.DueGift(72)==visit && visit.PartCount==2 && !visit.GiftComplete,
            "W10 missed Monday moves the chapter and both next-day gifts; no trade, work, drink or chat is required");
        Console.WriteLine("PASS: W10 final chapter, delayed midweek chat, evidence-based stock variants, shared read receipt and independent next-day gifts.");
    }
}
