using System;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using XiaomanVisitor;

internal static class EndingChecks
{
    public static void Run(Action<bool,string> check)
    {
        var story=new StoryProgress();StoryVisitRecord visit;
        for (int n=1;n<=12;n++) story.BeginVisit(1+(n-1)*7,0,0,out visit);
        check(StoryRules.BusinessOpen(story.VisitIndex) && !story.Finished,"W12 remains trading, independent of calendar age");
        story.BeginVisit(99,0,0,out visit); // A missed Monday postpones the thirteenth encounter.
        check(visit.Index==13 && !StoryRules.BusinessOpen(story.VisitIndex) && !story.Finished,
            "W13 stops new business only when the thirteenth encounter actually happens");
        check(StoryRules.SaleOffer(13,0,0).Length==0 && !StoryRules.AllowsTradeDrink(13)
            && StoryRules.GoodbyeKey(13,true)=="xm_13_bye","packing does not offer stock or a cheerful discount farewell");
        story.BeginVisit(106,0,0,out visit);
        check(story.Finished && visit.PartCount==5 && visit.RequiredMask==31 && StoryRules.FinalDrinks.Distinct().Count()==3,
            "final letter, tape and three different strong drinks retain individual delivery parts");
        foreach (var v in story.Visits.Take(13)) v.GiftMask=v.RequiredMask;
        check(story.DueGift(106)==null && story.DueGift(107).Index==14,"final bundle is due next day without a trade");
        for (int part=0;part<5;part++) {
            check(story.MarkPartDelivered(14,part,107),"each pending final part commits once");
            story=StoryProgress.Restore(story.Data);
            check(!story.MarkPartDelivered(14,part,107),"reloading cannot duplicate the delivered final part");
        }
        check(story.DueGift(108)==null,"finished bundle stays complete");

        var roka=LuxuryDrinkCatalog.Roka;
        check(roka.Effect==DrinkEffect.SaleIncome && roka.EffectAmount==15 && roka.DurationDays==1
            && !LuxuryDrinkCatalog.OnMenu(roka.Key,999),"Roka is a unique one-day sale-income gift, never normal stock");
        var buffs=new DrinkBuffCollection();
        foreach (var key in new[]{"bluebird","sky_after_rain","cosmo_zone","roka"}) buffs.Replace(CocktailCatalog.Find(key),78);
        buffs=DrinkBuffCollection.Restore(buffs.Data);
        check(buffs.Count==4 && buffs.ActiveAmount(DrinkEffect.SaleIncome,78)==15,"new income category survives reload and stacks with three original categories");
        buffs.Expire(79);
        check(buffs.ActiveAmount(DrinkEffect.SaleIncome,79)==0 && buffs.ActiveAmount(DrinkEffect.Luck,79)==8
            && buffs.ActiveAmount(DrinkEffect.TradeReputation,79)==20,"Roka expires next morning without expiring other wines");
        check(DrinkEffectMath.SaleCredit(100,15)==115 && DrinkEffectMath.SaleCredit(47,15)==54
            && DrinkEffectMath.SaleCredit(0,15)==0 && DrinkEffectMath.SaleCredit(-1,15)==-1
            && DrinkEffectMath.SaleCredit(int.MaxValue,15)==int.MaxValue,"income uses integer floor, no loss amplification or integer overflow");

        const string source="private_roka_visit_12";
        var gift=ImprovGiftProgress.Restore("",source);gift.VisitDay=78;
        check(gift.Prepare(401),"Roka persists one physical item identity before either delivery channel");
        gift.Placed(false);check(gift.Prepare(402),"full inventory retains a pending unique gift");
        gift=ImprovGiftProgress.Restore(gift.Data,source);gift.Recover(true);
        check(gift.Phase=="complete" && !gift.Prepare(403),"night collection excludes another next-day gift");
        check(ImprovGiftProgress.Restore(gift.Data).Blocked,"Roka and week-five receipts cannot be mistaken for each other");
        var interrupted=ImprovGiftProgress.Restore("",source);interrupted.VisitDay=78;interrupted.Prepare(500);interrupted.Recover(false);
        check(interrupted.Phase=="uncertain" && !interrupted.Prepare(501),"ambiguous recovery fails closed instead of creating a second unique drink");

        var native=PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(PlayerStore),nameof(PlayerStore.SellItem)));
        var patched=XiaomanSaleIncomePatch.Rewrite(native).ToList();
        var credit=AccessTools.Method(typeof(XiaomanSaleIncomePatch),nameof(XiaomanSaleIncomePatch.Credit));
        int at=patched.FindIndex(i => i.Calls(credit));
        check(at>0 && patched.Count(i => i.Calls(credit))==1 && patched[at+1].opcode==OpCodes.Add
            && patched[at+2].opcode==OpCodes.Stfld,"installed native sale credits receive exactly one scoped income multiplier");
    }
}
