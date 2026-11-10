using System;
using System.IO;
using System.Linq;
using XiaomanVisitor;

internal static class WeekFourChecks
{
    private sealed class ChoiceItem : GameItem
    {
        public ChoiceItem() : base("phone_choice_test") { }
        public override void Destroy() { }
        public override void Validate() { }
    }
    public static void Run(string package,Action<bool,string> check)
    {
        check(XiaomanNewspaper.SupportArticle(21,"title","body")==null
            && XiaomanNewspaper.SupportArticle(25,"title","body")==null
            && XiaomanNewspaper.SupportArticle(31,"title","body")==null,
            "W04 recruitment newspaper is absent before Monday and from Thursday onward, including next week");
        foreach (int day in new[]{22,23,24}) {
            var article=XiaomanNewspaper.SupportArticle(day,"title","body");
            check(article!=null && article.startDay==22 && article.duration==3 && article.eventType==StoreEvent.EventType.COSMETIC,
                "W04 recruitment newspaper shares the Monday-Wednesday application window");
        }
        var application=new SupportProgress();int ended=0;
        // RichTextBuilder.SetText invokes Unity engine colour formatting. This
        // checker has the real assemblies but no engine; supply plain line data.
        Func<Dialogue> call=() => SupportPhoneDialog.Choices((title,key) => new Dialogue { title=title??"support",plainText=key },() => new ChoiceItem(),
            () => application.Apply(22,"run"),() => ended++);
        var phone=call();
        check(phone.choices.Count==2 && phone.choices.All(c => c.Item1!=null && c.Item2.title=="PLAYER")
            && phone.choices[0].Item2.plainText=="ui_w04_phone_agree" && phone.choices[1].Item2.plainText=="ui_w04_phone_later"
            && application.AppliedDay==-1,"W04 native phone has two actual, correctly labelled PLAYER choices; opening never applies");
        phone.choices[1].Item2.ExecuteEndAction();
        check(application.AppliedDay==-1 && ended==1 && call().choices.Count==2,
            "W04 later ends the call without applying, and a new call can still show the choices");
        phone.choices[0].Item3();phone.choices[0].Item3();
        phone.choices[0].Item2.nextDialogue.ExecuteEndAction();
        check(application.AppliedDay==22 && application.AuditorDay==23 && ended==2,
            "W04 only selecting agree immediately applies once and confirmation ends the phone conversation");
        foreach (int day in new[]{22,23,24}) {
            var p=new SupportProgress();check(p.Apply(day,"run") && !p.Apply(day,"run"),"W04 telephone confirmation is unique");
            p=SupportProgress.Restore(p.Data);
            check(!p.AuditorDue(day) && p.AuditorDue(day+1),"W04 each accepted date has its own next-day appointment");
            check(!p.AuditorDue(26) && !p.DecisionGenerated && p.Advance(26) && !p.Advance(27),"W04 expiry never cancels the one Friday decision");
            check(!p.WangDue(26) && p.WangDue(27),"W04 Joe requires the independent refusal and day 27");
            p.WangSeen=true;check(!SupportProgress.Restore(p.Data).WangDue(40),"W04 Joe branch is one-off across reload");
        }
        var none=new SupportProgress();check(!none.Apply(21,"r") && !none.Apply(25,"r") && none.Advance(26) && !none.DecisionGenerated && !none.WangDue(27),"W04 no application means no assessor, refusal or Joe");
        check(!none.LuxuryChannelAvailable(28) && none.LuxuryChannelAvailable(29),"W04 future supplier channel follows independent group contract, not the player application");
        var appointment=new SupportProgress();appointment.Apply(22,"r");
        check(appointment.AuditorDue(24) && !appointment.AuditorDue(25),"W04 missed assessor can defer one day only");
        var supply=new SupplyProgress();int initial=supply.Cash;
        check(!supply.Sign(21) && supply.Sign(22) && !supply.Sign(23) && supply.Cash==initial && supply.Batches.Count==0,"W04 signature never produces cash or stock");
        check(!supply.MayReceive(22,"wrong_ingredient",1) && supply.Produce(22)==0,"W04 unmapped or undelivered ingredients produce nothing");
        var water=supply.PrepareInput("run",22,SupplyConfig.WaterItem,101,1,100);
        check(water!=null && !supply.MayReceive(22,SupplyConfig.JuiceItem,1),"W04 pending input excludes overlapping trades");
        supply=SupplyProgress.Restore(supply.Data);water=supply.Pending;
        check(supply.Complete(water) && !supply.Complete(water) && supply.Water==1 && supply.Produce(22)==0,"W04 raw receipt commits once and still requires juice");
        var juice=supply.PrepareInput("run",22,SupplyConfig.JuiceItem,102,1,108);supply.Complete(juice);
        check(supply.Produce(22)==1 && supply.Produce(22)==0 && supply.Water==0 && supply.Juice==0,"W04 actual ingredients convert once");
        var batch=supply.Batches.Single();
        check(batch.Produced==batch.PlayerInitial+batch.StoreInitial && batch.PlayerInitial==SupplyConfig.Output*SupplyConfig.PlayerNumerator/SupplyConfig.Denominator,
            "W04 configured integer allocation conserves one batch");
        supply=SupplyProgress.Restore(supply.Data);batch=supply.Batches.Single();
        var sale=supply.PrepareOutput("run",22,batch.Id,103,100);
        check(sale!=null && supply.Complete(sale) && !supply.Complete(sale) && batch.PlayerSold==1 && batch.StoreSold==0,"W04 wholesale settles one player share only");
        int cash=supply.Cash;
        check(supply.ReserveBar("drink_22",batch.Key) && supply.ReserveBar("drink_22",batch.Key) && batch.StoreSold==1,"W04 repeated bar callbacks do not consume two glasses");
        supply.SettleBar("drink_22",95);supply.SettleBar("drink_22",95);
        check(supply.Cash==cash+95,"W04 actual onsite income is posted once");
        check(supply.ReserveBar("drink_23",batch.Key),"W04 bar has its independently allocated remainder");
        supply.CancelBar("drink_23");supply.CancelBar("drink_23");
        check(batch.StoreSold==1 && batch.PlayerSold==1,"W04 verified cancellation restores bar stock once");
        check(supply.ReserveBar("drink_23",batch.Key) && batch.StoreSold==2,"W04 cancelled same-day payment can safely retry one reservation");
        while (batch.PlayerRemaining>0) {
            sale=supply.PrepareOutput("run",23,batch.Id,200+batch.PlayerSold,500);supply.Complete(sale);
        }
        check(supply.PrepareOutput("run",23,batch.Id,400,500)==null && batch.StoreRemaining>0,"W04 a sold-out player share cannot borrow from the bar");
        check(SupplyProgress.Restore(supply.Data).Batches.Single().Produced==SupplyConfig.Output,"W04 re-entry/reload cannot redistribute finished stock");
        check(supply.PrepareInput("run",22,SupplyConfig.WaterItem,101,1,100)==null,"W04 same run/order/instance cannot be sold twice");
        var story=new StoryProgress();StoryVisitRecord v;
        foreach (int day in new[]{1,8,15,22}) story.BeginVisit(day,0,0,out v);
        check(!story.FreezeFourthNote(22,supply.SignedBy) && story.FreezeFourthNote(23,supply.SignedBy) && story.Visits[3].NoteVariant=="B","W04 fourth letter freezes from encounter-night signature only");
        check(!StoryProgress.Restore(story.Data).FreezeFourthNote(30,_=>false),"W04 frozen letter survives late delivery");
        story=new StoryProgress();foreach (int day in new[]{1,8,15,22}) story.BeginVisit(day,0,0,out v);
        check(story.FreezeFourthNote(23,d=>23<=d) && story.Visits[3].NoteVariant=="A","W04 Tuesday signature cannot rewrite Monday's letter");
        foreach (string lang in new[]{"zh","ja","en"}) check(File.Exists(Path.Combine(package,"Letters",lang,"story_note_04_b.png")),"W04 translated B ink exists");
        var snapshotStory=StoryProgress.Restore(story.Data);
        snapshotStory.Visits[3].SupplySnapshot=new SupplyNoteSnapshot { Day=22,WaterPrice=8,JuicePrice=10,Wholesale=22 };
        check(StoryProgress.Restore(snapshotStory.Data).Visits[3].SupplySnapshot.WaterPrice==8,"W04 enclosed price/category snapshot survives reload independently of later catalog changes");
        var work=TavernWorkProgress.Restore("","weekly",15);
        // Find a persisted win without relying on Unity RNG.
        string identity=Enumerable.Range(0,1000).Select(i=>"weekly-"+i).First(s=>TavernWorkProgress.StableRoll(s,"paid_shift_15","visit")%100<35);
        work=TavernWorkProgress.Restore("",identity,15);work.EnsureDemand(15,3);work.Complete(work.PrepareShift(15,100));
        var legacy=Newtonsoft.Json.Linq.JObject.Parse(work.Data);legacy["Schema"]=1;
        legacy.Remove("ThanksHistory");legacy.Remove("ThanksVisitWeeks");
        ((Newtonsoft.Json.Linq.JObject)legacy["Thanks"]).Remove("Week");
        ((Newtonsoft.Json.Linq.JObject)legacy["Thanks"]).Remove("ShiftId");
        var migrated=TavernWorkProgress.Restore(legacy.ToString(),identity,22);
        check(!migrated.RecoveryBlocked && migrated.Schema==2 && migrated.Thanks.Roll==work.Thanks.Roll && migrated.ThanksDue(22),"W04 legacy first-shift win migrates as the same pending thanks, without reroll");
        work.BeginThanks(16,false);work.PrepareGift(999);work.GiftPlacementResult(true);work.Thanks.ProcurementClosed=true;
        work.EnsureDemand(16,3);work.Complete(work.PrepareShift(16,100));
        check(work.ThanksHistory.Count==0 && work.ThanksVisitWeeks.Count==1,"W04 one successful thanks per week, no reroll after win");
        work.EnsureDemand(22,3);work.Complete(work.PrepareShift(22,100));
        work=TavernWorkProgress.Restore(work.Data,identity,22);
        check(!work.RecoveryBlocked && work.ThanksHistory.Count==1 && work.Thanks.ShiftId=="paid_shift_22","W04 next calendar week can draw again without losing the old gift receipt");
        Console.WriteLine("PASS: W04 independent calendars, application/defer/refusal/Joe, conserved supply batches, unique receipts, exact-night letter and weekly thanks migration.");
    }
}
