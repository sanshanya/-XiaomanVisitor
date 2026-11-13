using System;
using System.IO;
using System.Linq;
using XiaomanVisitor;

internal static class WeekThreeChecks
{
    private static TavernWorkProgress Worked(string identity,int day)
    {
        var p=TavernWorkProgress.Restore("",identity,day);
        p.EnsureDemand(day,TavernWorkConfig.DailyOrders);
        p.Complete(p.PrepareShift(day,100));
        return p;
    }
    private static StoryProgress ThirdVisit(int firstDay=1)
    {
        var p=new StoryProgress();StoryVisitRecord ignored;
        foreach (int day in new[]{firstDay,firstDay+7,firstDay+14}) p.BeginVisit(day,0,0,out ignored);
        return p;
    }
    public static void Run(string package,Action<bool,string> check)
    {
        var work=TavernWorkProgress.Restore("","run-A",15);
        check(!work.EnsureDemand(14,TavernWorkConfig.DailyOrders) && !work.MayWork(14),"W03-A01: paid shifts begin on calendar day fifteen");
        check(work.EnsureDemand(15,TavernWorkConfig.DailyOrders) && work.MayWork(15) && !work.HasWorked,
            "W03-A02/A07: a new paid-work ledger needs neither shop encounters nor opening help");
        string initial=work.Data;
        check(!work.EnsureDemand(15,30) && work.Data==initial,"menu/re-entry cannot replenish the day's finite orders");
        work=TavernWorkProgress.Restore(work.Data,"run-A",15);
        check(work.Demand.Single().Orders.Select(o => o.Id).Distinct().Count()==6
            && work.Demand.Single().Orders.All(o => o.Source==TavernWorkConfig.Source),"demand has stable source-tagged unique IDs across reload");
        var shift=work.PrepareShift(15,100,work.WaitingIds(15));
        check(shift!=null && shift.Revenue==390 && shift.Material==180 && shift.Wage==120 && shift.Net==90,
            "W03-A04: six fulfilled orders leave 90 after materials and a real 120 wage");
        check(!work.HasWorked && work.IncrementalNet==0 && !work.Thanks.Drawn,
            "confirming/animating cannot record earnings or draw the surprise");
        check(work.Recover(15,100)==WageRecovery.Cancel && work.Recover(15,220)==WageRecovery.Finish,
            "W03-A11: untouched/paid wallet snapshots recover without replaying AddCredit");
        check(work.Recover(15,221)==WageRecovery.Hold && work.Recover(16,220)==WageRecovery.Hold,
            "unrelated money changes or a different day never justify a second wage");
        work=TavernWorkProgress.Restore(work.Data,"run-A",15);shift=work.Pending;
        check(work.Complete(shift) && !work.Complete(shift) && !work.MayWork(15) && work.PrepareShift(15,160)==null,
            "W03-A05/A20: one stable shift commits only once, including repeated callbacks");
        work=TavernWorkProgress.Restore(work.Data,"run-A",15);
        check(work.WorkedOn(15) && work.IncrementalNet==90 && work.Demand.Single().Orders.All(o => o.RevenueOwner==shift.Id),
            "W03-A11: wage and order ownership survive reload");
        check(!work.TryAssignOrder(shift.OrderIds[0],"L1") && work.IncrementalNet==90,
            "W03-A14: fulfilled overflow cannot also become baseline/L1 revenue");
        var claimed=TavernWorkProgress.Restore("","run-B",15);claimed.EnsureDemand(15,TavernWorkConfig.DailyOrders);
        claimed.TryAssignOrder(claimed.WaitingIds(15)[0],"baseline");
        check(!claimed.MayWork(15),"an order assigned elsewhere reduces available demand before offering a shift");
        var quiet=TavernWorkProgress.Restore("","run-C",16);
        quiet.EnsureDemand(15,0);quiet.EnsureDemand(16,2);
        check(!quiet.MayWork(15) && !quiet.MayWork(16) && !quiet.EnsureDemand(15,TavernWorkConfig.DailyOrders),
            "W03-A10: zero/insufficient orders remain quiet after reload/menu refresh");
        var cancel=TavernWorkProgress.Restore("","run-D",15);cancel.EnsureDemand(15,TavernWorkConfig.DailyOrders);
        var attempt=cancel.PrepareShift(15,400);attempt.Status="cancelled";
        cancel=TavernWorkProgress.Restore(cancel.Data,"run-D",15);
        check(cancel.MayWork(15) && cancel.PrepareShift(15,400)!=null && cancel.Shifts.Count==1 && !cancel.Thanks.Drawn,
            "W03-A06: a verified untouched cancellation can restart without consuming orders");
        var drink=new TavernProgress();
        var glass=drink.PrepareDrink(15,"sunset_blue",500,"");glass.Status="complete";
        check(work.WorkedOn(15) && !drink.MayDrink(15) && work.EnsureDemand(16,TavernWorkConfig.DailyOrders) && work.MayWork(16),
            "W03-A09/A13: drink and paid-shift limits use independent daily receipts");
        check(!work.MayWork(15) && new TavernProgress().MayDrink(15),"working leaves the daily glass available");
        check(TavernWorkProgress.Restore("broken","run",15).RecoveryBlocked
            && TavernWorkProgress.Restore("{\"Schema\":9}","run",15).RecoveryBlocked,
            "future or damaged paid ledgers preserve history and cannot reset earnings");

        var story=ThirdVisit();
        check(!story.FreezeThirdNote(15,work.WorkedOn) && story.Visits[2].NoteVariant=="",
            "W03-A03B: do not choose a letter during the daytime encounter");
        check(story.FreezeThirdNote(16,work.WorkedOn) && story.Visits[2].NoteVariant=="B",
            "the actual encounter night's completed shift selects B on the next day");
        story=StoryProgress.Restore(story.Data);
        check(!story.FreezeThirdNote(30,_ => false) && story.Visits[2].NoteVariant=="B"
            && story.Visits[2].GiftMask==0,"W03-A20B: full inventory/reload never changes frozen B or pretends to deliver");
        var late=ThirdVisit(8);
        check(late.FreezeThirdNote(23,work.WorkedOn) && late.Visits[2].NoteVariant=="A",
            "earlier work does not decide the letter for a delayed third encounter's night");
        var uncommitted=ThirdVisit();
        check(uncommitted.FreezeThirdNote(16,cancel.WorkedOn) && uncommitted.Visits[2].NoteVariant=="A",
            "W03-A03/A20: cancelled or unfinished work selects the unchanged A");
        var old=ThirdVisit();old.Visits[2].GiftMask=1;
        old=StoryProgress.Restore(old.Data);
        check(old.Visits[2].NoteVariant=="A","already-delivered old third letters keep A without regranting items");
        string path="Letters/zh/story_note_03.png";
        check(LetterInkRules.VariantPath(path,"story_note_03","B")=="Letters/zh/story_note_03_b.png"
            && LetterInkRules.VariantPath(path,"story_note_04","B")==path
            && !NotebookRules.IsLetter(XiaomanMod.Id+".story_note_03_b"),"A/B share one valid notebook item; variants only select ink/text");
        foreach (string lang in new[]{"zh","ja","en"}) check(File.Exists(Path.Combine(package,"Letters",lang,"story_note_03_b.png")),"third B handwriting is packaged: "+lang);

        string wonId=Enumerable.Range(0,1000).Select(i => "thanks-"+i)
            .First(id => TavernWorkProgress.StableRoll(id,"paid_shift_15","visit")%100<35);
        string lostId=Enumerable.Range(0,1000).Select(i => "thanks-"+i)
            .First(id => TavernWorkProgress.StableRoll(id,"paid_shift_15","visit")%100>=35);
        var won=Worked(wonId,15);var lost=Worked(lostId,15);
        check(won.Thanks.Won && !lost.Thanks.Won && won.Thanks.DueDay==16
            && !won.ThanksDue(15) && won.ThanksDue(16),"W03-A15: a single stable 35% draw schedules only next-day thanks");
        lost=TavernWorkProgress.Restore(lost.Data,lostId,16);
        lost.EnsureDemand(16,TavernWorkConfig.DailyOrders);lost.Complete(lost.PrepareShift(16,160));
        check(lost.Thanks.ShiftId=="paid_shift_16" && lost.ThanksHistory.Count==1
            && StoryRules.BasicDrinks.Contains(lost.Thanks.DrinkKey),"W04 supersedes first-only: a later paid shift may draw once; the previous miss stays archived");
        var emptyStory=new StoryProgress();
        check(CustomerQueuePolicy.Next(16,emptyStory,won,true)==XiaomanVisitKind.WorkThanks
            && CustomerQueuePolicy.Next(22,emptyStory,won,true)==XiaomanVisitKind.Story
            && CustomerQueuePolicy.Next(16,emptyStory,won,false)==XiaomanVisitKind.None,
            "W03-A16/A17: extra non-Monday tail visit, Monday main priority, experiment-off gate");
        StoryVisitRecord visit;emptyStory.BeginVisit(22,0,0,out visit);
        check(CustomerQueuePolicy.Next(22,emptyStory,won,true)==XiaomanVisitKind.None && won.ThanksDue(23),
            "already-received Mondays never insert a second Xiaoman; pending thanks can wait");
        check(won.BeginThanks(16,false) && !won.BeginThanks(16,false) && !won.ThanksDue(23)
            && won.ShopkeeperKnown && won.PurchaseRemaining==100,"thanks reception is one-off and establishes the shopkeeper's identity");
        check(won.RecordPurchase(123,70) && !won.RecordPurchase(123,70) && !won.RecordPurchase(124,31)
            && won.PurchaseRemaining==30,"private reserve tracks actual unique purchases rather than issuing another 100");
        won=TavernWorkProgress.Restore(won.Data,wonId,16);
        check(won.PurchaseRemaining==30 && won.PrepareGift(555),"W03-A19: gift is independent of procurement and stores an item identity before placement");
        won.GiftPlacementResult(false);
        check(won.Thanks.GiftPhase=="waiting" && won.Thanks.GiftItemId==0 && won.PrepareGift(556),
            "full inventory preserves the chosen drink and permits one later placement attempt");
        won=TavernWorkProgress.Restore(won.Data,wonId,16);won.RecoverGift(true);
        check(won.Thanks.GiftComplete && !won.PrepareGift(557),"a recovered owned gift is complete, with no duplicate item");
        var merged=Worked(wonId,15);merged.BeginThanks(22,true);
        check(merged.PurchaseRemaining==0 && !merged.RecordPurchase(777,1) && merged.PrepareGift(778),
            "Monday merge grants the packed gift but no extra procurement budget");
        merged.RecoverGift(false);
        check(merged.Thanks.GiftPhase=="uncertain" && !merged.PrepareGift(779),"ambiguous gift identity never causes automatic regrant");
        var chat=new TavernProgress();
        check(chat.NextChat(15)=="xm_tavern_w03_chat_01" && chat.NextChat(15,true)=="xm_tavern_w03_chat_after_work",
            "W03 chat responds to actual same-night work without forcing a daily plot");
        chat.ChatSeen.Add(TavernProgress.ChatReceipt(chat.NextChat(15,true),15));
        check(chat.NextChat(15,true)=="xm_tavern_w03_chat_01"
            && TavernDialogue.KeyFor("xm_tavern_w03_work_offer_01",false).EndsWith("_unknown"),"after-work feedback is optional; unknown guests are not called senpai");
        Console.WriteLine("PASS: W03 finite orders, wage recovery, independent daily limits, exact-night A/B, stable thanks/queue, private reserve and gift recovery.");
    }
}
