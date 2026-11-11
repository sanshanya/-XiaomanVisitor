using System;
using System.Collections.Generic;
using System.Linq;
using XiaomanVisitor;

internal static class BrewingChecks
{
    private static BrewingProgress Signed()
    { var p=new BrewingProgress();p.Sign(22);p.Arrive(24);return p; }
    internal static void Run(Action<bool,string> check)
    {
        var p=new BrewingProgress();
        check(!p.MayVisit(24,true) && !p.Sign(21),"a chapter or Wednesday alone never authorizes material collection");
        check(p.Sign(22) && p.NextCollectionDay==24 && !p.Sign(22),"explicit tavern consent schedules the next Wednesday exactly once");
        check(!p.MayVisit(22,true) && p.MayVisit(24,true) && !p.MayVisit(25,true),"signed collection preserves its Wednesday");
        check(p.Arrive(24) && !p.Arrive(24) && !p.MayVisit(24,true),"an actual collection visit is recorded once");
        var goods=new List<BrewingMaterial>{new BrewingMaterial{ItemId=11,Key="water",Count=1,ValueCents=25000}};
        var batch=p.Prepare("run",24,goods);
        check(batch!=null && batch.ReturnMilli==325000 && batch.DueDay==26,"250 CR materials freeze 325 CR for the same week's Friday");
        check(p.Prepare("run",24,goods)==null,"repeated acceptance cannot duplicate a batch");
        check(p.RecoverInput(new int[0]) && batch.Phase=="complete" && p.NextCollectionDay==-1,"native ownership commits one batch and consumes this collection appointment");
        p.CreditDue(25);check(p.CreditMilli==0 && !p.MayVisit(25,true),"delivery cannot happen on Thursday");
        check(p.MayVisit(26,true) && p.MayVisit(26,false),"an accepted batch is owed on Friday even after the business closes");
        p.CreditDue(26);p.CreditDue(26);
        check(p.CreditMilli==325000 && p.Renewal==null,"credit is applied once and renewal waits for physical delivery");
        int revision=p.AgreementRevision;var values=new List<int>();
        for(int i=0;i<4;i++) {
            var bottle=p.PrepareBottle(100+i);values.Add(bottle.Value);p.BottlePlaced(true);
            p=BrewingProgress.Restore(p.Data);check(!p.Blocked,"placed bottles retain conserved value after reload");
        }
        check(values.SequenceEqual(new[]{150,150,10,10}) && p.CreditMilli==5000,"whole bottles leave five CR without manufacturing an extra sample");
        check(p.PrepareBottle(999)==null && p.Renewal?.Id==batch.Id && !p.MayVisit(31,true),"unanswered Friday renewal never schedules an automatic Wednesday visit");
        check(p.ContinueAfterDelivery(batch.Id,revision,26,true) && p.NextCollectionDay==31 && !p.ContinueAfterDelivery(batch.Id,revision,26,true),"Friday yes schedules next Wednesday; duplicate choice callbacks do nothing");
        check(p.Pause() && !p.MayVisit(31,true) && p.Sign(30) && p.MayVisit(31,true),"tavern pause and renewal cancel and restore the next collection appointment");
        p.Arrive(31);p.Prepare("run",31,new List<BrewingMaterial>{new BrewingMaterial{ItemId=42,Count=1,ValueCents=1000}});p.RecoverInput(new int[0]);
        check(p.Pause() && p.MayVisit(33,true) && !p.MayVisit(38,true),"pausing during production stops future collection but preserves Friday delivery");
        p.CreditDue(33);check(p.CreditMilli==18000,"the old remainder joins the new return without being multiplied again");
        p.PrepareBottle(501);p=BrewingProgress.Restore(p.Data);p.RecoverBottle(true);
        check(p.CreditMilli==8000 && p.Bottles.Last().Phase=="complete" && p.Renewal==null,"identity recovery settles once; a later explicit pause supersedes the old renewal prompt");
        var no=Signed();no.Prepare("r",24,goods);no.RecoverInput(new int[0]);no.CreditDue(26);
        while(no.PrepareBottle(600+no.Bottles.Count)!=null) no.BottlePlaced(true);
        check(no.ContinueAfterDelivery(no.Renewal.Id,no.AgreementRevision,26,false) && !no.AgreementActive && !no.MayVisit(31,true),"Friday no suspends cooperation until tavern renewal");
        check(no.Sign(31) && no.NextCollectionDay==38,"signing on Wednesday night schedules the following Wednesday, not an elapsed daytime visit");
        var missed=new BrewingProgress();missed.Sign(22);
        check(missed.MayVisit(31,true) && !missed.MayVisit(25,true),"a missed signed collection waits for the next Wednesday");
        missed.Arrive(31);missed.Prepare("r",31,goods);missed.RecoverInput(new int[0]);
        check(missed.MayVisit(40,true) && !missed.MayVisit(38,true),"a missed Friday delivery waits for Friday and never grants next-week collection");
        var retry=Signed();retry.Prepare("r",24,goods);retry.RecoverInput(new[]{11});
        check(retry.Batches[0].Phase=="cancelled" && retry.Prepare("r",24,goods)!=null,"unmoved materials cancel safely and may be submitted again");
        var partial=Signed();partial.Prepare("r",24,new List<BrewingMaterial>{goods[0],new BrewingMaterial{ItemId=12,Count=1,ValueCents=10}});partial.RecoverInput(new[]{11});
        check(partial.Blocked && partial.Batches[0].Phase=="uncertain","partial native transfer is held rather than guessed");
        var lost=Signed();lost.Prepare("r",24,goods);lost.RecoverInput(new int[0]);lost.CreditDue(26);lost.PrepareBottle(88);lost.RecoverBottle(false);
        check(lost.Blocked && lost.Bottles[0].Phase=="uncertain","ambiguous placement never mints another bottle");
        var final=Signed();final.Prepare("r",24,goods);final.RecoverInput(new int[0]);final.CreditDue(25,true);
        check(final.CreditMilli==325000,"departure honors an already accepted promise without reviving a visitor");
        var legacy=Signed();legacy.Prepare("r",24,goods);legacy.RecoverInput(new int[0]);legacy.Schema=1;legacy.Batches[0].DueDay=31;
        legacy=BrewingProgress.Restore(legacy.Data);
        check(!legacy.Blocked && legacy.Schema==2 && legacy.Batches[0].DueDay==26 && legacy.Batches[0].ReturnMilli==325000
            && !legacy.AgreementActive && !legacy.MayVisit(31,true),"old seven-day receipts migrate to Friday without silently creating agreement consent");
        var currentStock=new SupplyProgress();currentStock.Sign(22);currentStock.ProduceLuxury(29,true,false);
        check(currentStock.Batches.All(b => b.PlayerInitial==0 && b.StoreInitial==b.Produced),"new supplier batches have no inaccessible paid wholesale share");
        check(BrewingProgress.Restore("broken").Blocked,"damaged data is preserved and held");
        check(CustomerQueuePolicy.IsXiaoman(BrewingProgress.ClientId) && GunReaction.Protects(true,BrewingProgress.ClientId),"both cooperation visits retain native portrait and aim-only protection");
        check(CustomerQueuePolicy.Next(26,new StoryProgress(),new TavernWorkProgress(),true,true)==XiaomanVisitKind.Brewing
            && CustomerQueuePolicy.Next(25,new StoryProgress(),new TavernWorkProgress(),true,true)!=XiaomanVisitKind.Brewing,"native queue selection admits Friday delivery but never a Thursday spillover");
        check(ItemPresentation.OwnsItemId(BrewingProgress.SampleId) && ItemPresentation.OwnsItemId(BrewingProgress.BottleId),"bottled goods retain the existing scoped tooltip style");
    }
}
