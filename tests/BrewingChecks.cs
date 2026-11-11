using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using XiaomanVisitor;

internal static class BrewingChecks
{
    private static BrewingProgress Signed()
    { var p=new BrewingProgress();p.Sign(22);p.Arrive(24);return p; }
    internal static void Run(Action<bool,string> check)
    {
        CheckNativeExchange(check);
        var p=new BrewingProgress();
        check(!p.MayVisit(24,true) && !p.Sign(21),"a chapter or Wednesday alone never authorizes material collection");
        check(p.Sign(22) && p.NextCollectionDay==24 && !p.Sign(22),"explicit tavern consent schedules the next Wednesday exactly once");
        check(!p.MayVisit(22,true) && p.MayVisit(24,true) && !p.MayVisit(25,true),"signed collection preserves its Wednesday");
        check(p.Arrive(24) && !p.Arrive(24) && !p.MayVisit(24,true),"an actual collection visit is recorded once");
        var goods=new List<BrewingMaterial>{new BrewingMaterial{ItemId=11,Key="water",Count=1,ValueCents=25000}};
        var batch=p.Prepare("run",24,goods);
        check(batch!=null && batch.ReturnMilli==325000 && batch.DueDay==26,"250 CR materials freeze 325 CR for the same week's Friday");
        check(p.Prepare("run",24,goods)==null,"repeated acceptance cannot duplicate a batch");
        var limit=Signed();
        check(limit.Prepare("limit",24,new List<BrewingMaterial>{new BrewingMaterial{ItemId=21,Count=1,ValueCents=50000}})?.ReturnMilli==650000,
            "the first 500 CR ceiling is inclusive and returns 650 CR");
        var over=Signed();
        check(over.Prepare("over",24,new List<BrewingMaterial>{new BrewingMaterial{ItemId=22,Count=1,ValueCents=50001}})==null
            && over.Batches.Count==0 && over.MayCollect(24),"over-limit material is rejected intact without consuming the appointment");
        var empty=Signed();check(!empty.MayVisit(26,true),"signature and Wednesday arrival without an accepted batch never create Friday delivery");
        check(over.MaterialLimit(24)==500 && over.MaterialLimit(31)==600 && over.MaterialLimit(199)==3000 && over.MaterialLimit(1000)==3000,
            "capacity grows by calendar week from the first booked collection week and stops at 3000");
        over.Pause();over.Sign(30);check(over.MaterialLimit(31)==600,"pause and renewal never reset earned capacity");
        var tiny=Signed();tiny.Prepare("tiny",24,new List<BrewingMaterial>{new BrewingMaterial{ItemId=25,Count=1,ValueCents=1}});tiny.RecoverInput(new int[0]);tiny.CreditDue(26);
        check(tiny.CreditMilli==13 && tiny.PrepareBottle(98)==null && tiny.Renewal!=null,"no minimum material value; tiny valid batches retain value instead of fabricating a bottle");
        check(p.RecoverInput(new int[0]) && batch.Phase=="complete" && p.NextCollectionDay==-1,"native ownership commits one batch and consumes this collection appointment");
        p.CreditDue(25);check(p.CreditMilli==0 && !p.MayVisit(25,true),"delivery cannot happen on Thursday");
        check(p.MayVisit(26,true) && p.MayVisit(26,false),"an accepted batch is owed on Friday even after the business closes");
        p.CreditDue(26);p.CreditDue(26);
        check(p.CreditMilli==325000 && p.Renewal==null,"credit is applied once and renewal waits for physical delivery");
        int revision=p.AgreementRevision;var values=new List<int>();
        for(int i=0;i<2;i++) {
            var bottle=p.PrepareBottle(100+i);values.Add(bottle.Value);p.BottlePlaced(true);
            p=BrewingProgress.Restore(p.Data);check(!p.Blocked,"placed bottles retain conserved value after reload");
        }
        check(values.SequenceEqual(new[]{150,150}) && p.CreditMilli==25000,"30 CR small bottles leave 25 CR without manufacturing an extra bottle");
        check(p.PrepareBottle(999)==null && p.Renewal?.Id==batch.Id && !p.MayVisit(31,true),"unanswered Friday renewal never schedules an automatic Wednesday visit");
        check(p.ContinueAfterDelivery(batch.Id,revision,26,true) && p.NextCollectionDay==31 && !p.ContinueAfterDelivery(batch.Id,revision,26,true),"Friday yes schedules next Wednesday; duplicate choice callbacks do nothing");
        check(p.Pause() && !p.MayVisit(31,true) && p.Sign(30) && p.MayVisit(31,true),"tavern pause and renewal cancel and restore the next collection appointment");
        p.Arrive(31);p.Prepare("run",31,new List<BrewingMaterial>{new BrewingMaterial{ItemId=42,Count=1,ValueCents=1000}});p.RecoverInput(new int[0]);
        check(p.Pause() && p.MayVisit(33,true) && !p.MayVisit(38,true),"pausing during production stops future collection but preserves Friday delivery");
        p.CreditDue(33);check(p.CreditMilli==38000,"the old remainder joins the new return without being multiplied again");
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
        check(!legacy.Blocked && legacy.Schema==3 && legacy.Batches[0].DueDay==26 && legacy.Batches[0].ReturnMilli==325000
            && !legacy.AgreementActive && !legacy.MayVisit(31,true),"old seven-day receipts migrate to Friday without silently creating agreement consent");
        legacy.CreditDue(26);legacy.PrepareBottle(77);legacy.BottlePlaced(true);legacy.PrepareBottle(78);legacy.BottlePlaced(true);
        legacy.Bottles.Add(new BrewingBottle{Id="old_sample",ItemId=79,Value=10,Phase="complete"});legacy.CreditMilli-=10000;legacy.Schema=2;
        legacy=BrewingProgress.Restore(legacy.Data);
        check(!legacy.Blocked && legacy.CreditMilli==15000 && legacy.Bottles.Last().Value==10,"old 10 CR receipts preserve their exact value when new small bottles become 30 CR");
        var oldLarge=Signed();var oldBatch=oldLarge.Prepare("old",24,new List<BrewingMaterial>{new BrewingMaterial{ItemId=41,Count=1,ValueCents=25000}});
        oldBatch.Materials[0].ValueCents=100000;oldBatch.InputCents=100000;oldBatch.ReturnMilli=1300000;oldLarge.RecoverInput(new int[0]);oldLarge.Schema=2;
        oldLarge=BrewingProgress.Restore(oldLarge.Data);oldLarge.CreditDue(26);
        check(!oldLarge.Blocked && oldLarge.CreditMilli==1300000,"already accepted old batches above the new cap still honor their frozen return");
        CheckCrate(check);
        var currentStock=new SupplyProgress();currentStock.Sign(22);currentStock.ProduceLuxury(29,true,false);
        check(currentStock.Batches.All(b => b.PlayerInitial==0 && b.StoreInitial==b.Produced),"new supplier batches have no inaccessible paid wholesale share");
        check(BrewingProgress.Restore("broken").Blocked,"damaged data is preserved and held");
        var uiType=typeof(NegociationUIManager);const BindingFlags native=BindingFlags.Instance|BindingFlags.NonPublic;
        check(uiType.GetMethod("BuildSignature",native)?.ReturnType==typeof(string)
            && uiType.GetMethod("BuildBarter",native)?.ReturnType==typeof(float)
            && uiType.GetField("frame",native)?.FieldType==typeof(NegotiationTabletFrame),"native quote refresh and row injection match the installed tablet contract");
        check(CustomerQueuePolicy.IsXiaoman(BrewingProgress.ClientId) && GunReaction.Protects(true,BrewingProgress.ClientId),"both cooperation visits retain native portrait and aim-only protection");
        check(CustomerQueuePolicy.Next(26,new StoryProgress(),new TavernWorkProgress(),true,true)==XiaomanVisitKind.Brewing
            && CustomerQueuePolicy.Next(25,new StoryProgress(),new TavernWorkProgress(),true,true)!=XiaomanVisitKind.Brewing,"native queue selection admits Friday delivery but never a Thursday spillover");
        check(ItemPresentation.OwnsItemId(BrewingProgress.SampleId) && ItemPresentation.OwnsItemId(BrewingProgress.BottleId),"bottled goods retain the existing scoped tooltip style");
    }
    private sealed class MaterialItem : GameItem
    {
        public MaterialItem(int id) : base("water_ration") { uniqueId=id;unitCount=1; }
        public override void Destroy() { }
        public override void Validate() { }
    }
    private static void CheckNativeExchange(Action<bool,string> check)
    {
        var client=new StoreClient { identifier=BrewingProgress.ClientId,sourceModId=XiaomanMod.Id,clientIntent=StoreClient.ClientIntent.BARTER };
        var counter=new List<GameItem>{new MaterialItem(11),new MaterialItem(12),new MaterialItem(13)};
        var stable=XiaomanBrewingInputSnapshotPatch.Snapshot(client,counter);
        var p=Signed();
        p.Prepare("native",24,counter.Select(i => new BrewingMaterial{ItemId=i.uniqueId,Key=i.identifier,Count=i.unitCount,ValueCents=10000}).ToList());
        int tracked=0;
        foreach (var item in stable) { counter.Remove(item);tracked++; }
        check(tracked==3 && counter.Count==0 && p.RecoverInput(counter.Select(i => i.uniqueId)) && p.Batches[0].Phase=="complete",
            "native sale tracking can remove every material without invalidating the barter enumeration or losing Friday's batch");
        p.CreditDue(26);p.CreditDue(26);
        check(p.CreditMilli==390000 && p.Prepare("native",24,new List<BrewingMaterial>{new BrewingMaterial{ItemId=99,Count=1,ValueCents=100}})==null,
            "one material transfer produces one 30 percent Friday credit, never a second acceptance");
        check(ReferenceEquals(counter,XiaomanBrewingInputSnapshotPatch.Snapshot(null,counter)),"no customer retains the native material list");
        client.sourceModId="another.mod";
        check(ReferenceEquals(counter,XiaomanBrewingInputSnapshotPatch.Snapshot(client,counter)),"same-named customers from other sources are not changed");
        client.sourceModId=XiaomanMod.Id;client.identifier="native_barter";
        check(ReferenceEquals(counter,XiaomanBrewingInputSnapshotPatch.Snapshot(client,counter)),"other barter customers keep their native input");
        client.identifier=BrewingProgress.ClientId;client.clientIntent=StoreClient.ClientIntent.DIALOGUE;
        check(ReferenceEquals(counter,XiaomanBrewingInputSnapshotPatch.Snapshot(client,counter)),"delivery-only visits never enter material exchange");
        var native=PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PlayerStore),nameof(PlayerStore.AcceptBarter))).ToArray();
        int complete=Array.FindIndex(native,i => i.Calls(AccessTools.Method(typeof(StoreClient),nameof(StoreClient.CompleteBusiness))));
        int accepted=Array.FindIndex(native,i => i.Calls(AccessTools.Method(typeof(BarterOffer),nameof(BarterOffer.OnBarterAccepted))));
        int swap=Array.FindIndex(native,i => i.Calls(AccessTools.Method(typeof(EmporiumEntry),nameof(EmporiumEntry.SwapInv))));
        check(complete>=0 && accepted>complete && swap>accepted,"installed native barter completes business and transfers goods in its original order");
        check(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PlayerStore),nameof(PlayerStore.OnItemsSold)))
            .Any(i => i.Calls(AccessTools.Method(typeof(SoldItem),nameof(SoldItem.TrackItem))))
            && PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(SoldItem),nameof(SoldItem.TrackItem)))
                .Any(i => i.Calls(AccessTools.Method(typeof(SoldItem),"MoveToSoldBag"))),
            "installed native sale tracking moves items out of the live counter, requiring the scoped snapshot");
    }
    private static void CheckCrate(Action<bool,string> check)
    {
        check(WineCrateRules.Footprint==3 && WineCrateRules.Columns==8 && WineCrateRules.Rows==8,"wine crate occupies 3x3 with a native 8x8 interior");
        check(CocktailCatalog.All.All(d => WineCrateRules.CanStore(d.ItemId,true,true,false))
            && WineCrateRules.CanStore(BrewingProgress.SampleId,true,true,false) && WineCrateRules.CanStore(BrewingProgress.BottleId,true,true,false),"crate accepts every Xiaoman drink and both bottled sizes");
        check(!WineCrateRules.CanStore("wine_bottle",true,true,false) && !WineCrateRules.CanStore(CassetteCaseRules.ItemId,true,true,true)
            && !WineCrateRules.CanStore(BrewingProgress.SampleId,false,true,false) && !WineCrateRules.CanStore(BrewingProgress.SampleId,true,true,true),"native wine, other items, unowned goods and nested containers are refused");
        var p=new WineCrateProgress();check(!p.Prepare(7),"no crate before a wine gift has been requested");
        check(!p.RequestForDelivery(BrewingProgress.SampleId,false) && !p.RequestForDelivery(CassetteCaseRules.ItemId,true)
            && !p.RequestForDelivery(CocktailCatalog.Drinks[0].ItemId,true) && !p.Requested,"failed deliveries, normal cocktail gifts and cassette-case gifts cannot trigger the wine crate");
        check(p.RequestForDelivery(BrewingProgress.SampleId,true) && !p.RequestForDelivery(BrewingProgress.BottleId,true),"the first real cooperation delivery requests exactly one crate");
        p.Prepare(7);p=WineCrateProgress.Restore(p.Data);p.Recover(true);
        check(p.Phase=="complete" && !p.Prepare(8),"the saved crate identity recovers once without creating another crate");
        var oldWaiting=WineCrateProgress.Restore("{\"Requested\":true,\"ItemId\":0,\"Phase\":\"waiting\"}");
        check(oldWaiting.Schema==2 && !oldWaiting.Requested && !oldWaiting.Prepare(8)
            && oldWaiting.RequestForDelivery(BrewingProgress.SampleId,true),"legacy possession-only or failed-gift requests wait for a real cooperation delivery");
        var oldDelivered=WineCrateProgress.Restore("{\"Requested\":true,\"ItemId\":7,\"Phase\":\"complete\"}");
        check(oldDelivered.Phase=="complete" && oldDelivered.ItemId==7 && !oldDelivered.RequestForDelivery(BrewingProgress.BottleId,true),"already delivered legacy crates and their identity survive without a second gift");
        var full=new WineCrateProgress{Requested=true};full.Prepare(8);full.Placed(false);
        check(full.Prepare(9),"full counter retains a pending crate for later placement");
        full.Recover(false);check(full.Phase=="uncertain" && !full.Prepare(10),"ambiguous crate placement is held instead of duplicated");
    }
}
