using System;
using System.Collections.Generic;
using System.Linq;
using XiaomanVisitor;

internal static class BrewingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var p=new BrewingProgress();
        check(!p.MayVisit(22,true) && p.MayVisit(24,true) && !p.MayVisit(25,true),"brewing queues only on Wednesday after the existing W04 opening");
        check(p.Arrive(24) && !p.Arrive(24) && !p.MayVisit(24,true),"an actual Wednesday visit is recorded once");
        var goods=new List<BrewingMaterial> { new BrewingMaterial { ItemId=11,Key="water",Count=1,ValueCents=25000 } };
        var batch=p.Prepare("run",24,goods);
        check(batch!=null && batch.ReturnMilli==325000 && batch.DueDay==31,"250 CR materials freeze a 325 CR return for the following Wednesday");
        check(p.Prepare("run",24,goods)==null,"repeated acceptance cannot duplicate a batch");
        check(p.RecoverInput(new int[0]) && batch.Phase=="complete","native ownership transfer commits the prepared receipt");
        p.CreditDue(30);check(p.CreditMilli==0,"bottles cannot be claimed early");
        p.CreditDue(31);p.CreditDue(31);check(p.CreditMilli==325000,"due batch is credited exactly once");
        var values=new List<int>();
        for(int i=0;i<4;i++) {
            var bottle=p.PrepareBottle(100+i);values.Add(bottle.Value);p.BottlePlaced(true);
            p=BrewingProgress.Restore(p.Data);check(!p.Blocked,"each placed bottle survives reload with conserved value");
        }
        check(values.SequenceEqual(new[]{150,150,10,10}) && p.CreditMilli==5000,"whole large bottles precede samples; five CR stays in the ledger");
        check(p.PrepareBottle(999)==null,"a remainder never becomes a free extra sample");
        check(p.MayVisit(38,true) && !p.MayVisit(38,false),"new collection keeps visits alive; small change alone does not summon another visitor");
        p.Arrive(38);p.Prepare("run",38,new List<BrewingMaterial>{new BrewingMaterial{ItemId=42,Count=1,ValueCents=1000}});p.RecoverInput(new int[0]);
        p.CreditDue(45);check(p.CreditMilli==18000,"the previous remainder combines with the next batch without being multiplied again");
        p.PrepareBottle(501);p=BrewingProgress.Restore(p.Data);p.RecoverBottle(true);
        check(p.CreditMilli==8000 && p.Bottles.Last().Phase=="complete","a placed physical identity settles once after reload");
        var retry=new BrewingProgress();retry.Arrive(24);retry.Prepare("r",24,goods);retry.RecoverInput(new[]{11});
        check(retry.Batches[0].Phase=="cancelled" && retry.CreditMilli==0 && retry.Prepare("r",24,goods)!=null,"unmoved materials cancel safely and may be submitted again");
        var partial=new BrewingProgress();partial.Arrive(24);partial.Prepare("r",24,new List<BrewingMaterial>{goods[0],new BrewingMaterial{ItemId=12,Count=1,ValueCents=10}});partial.RecoverInput(new[]{11});
        check(partial.Blocked && partial.Batches[0].Phase=="uncertain","partial native transfer is held instead of guessing or destroying more materials");
        var lost=new BrewingProgress();lost.Arrive(24);lost.Prepare("r",24,goods);lost.RecoverInput(new int[0]);lost.CreditDue(31);lost.PrepareBottle(88);lost.RecoverBottle(false);
        check(lost.Blocked && lost.Bottles[0].Phase=="uncertain","ambiguous delivery never mints another bottle");
        var final=new BrewingProgress();final.Arrive(24);final.Prepare("r",24,goods);final.RecoverInput(new int[0]);final.CreditDue(25,true);
        check(final.CreditMilli==325000,"departure settles an already accepted promise without reviving a visitor");
        var currentStock=new SupplyProgress();currentStock.Sign(22);currentStock.ProduceLuxury(29,true,false);
        check(currentStock.Batches.All(b => b.PlayerInitial==0 && b.StoreInitial==b.Produced),"new supplier batches no longer reserve an inaccessible paid wholesale share; saved old batches remain separate");
        check(BrewingProgress.Restore("broken").Blocked,"damaged data is preserved and held");
        check(CustomerQueuePolicy.IsXiaoman(BrewingProgress.ClientId),"native portrait and gun ownership include the Wednesday visitor");
        check(ItemPresentation.OwnsItemId(BrewingProgress.SampleId) && ItemPresentation.OwnsItemId(BrewingProgress.BottleId),"brewed bottles use Xiaoman's existing item namespace and tooltip style");
        check(GunReaction.Protects(true,BrewingProgress.ClientId),"Wednesday pickup inherits aim-only protection, not an unprotected force-shootable visitor");
    }
}
