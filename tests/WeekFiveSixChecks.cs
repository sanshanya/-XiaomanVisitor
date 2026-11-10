using System;
using System.Linq;
using XiaomanVisitor;

internal static class WeekFiveSixChecks
{
    public static void Run(Action<bool,string> check)
    {
        check(CocktailCatalog.Drinks.Length==6 && CocktailCatalog.All.Length==10
            && StoryRules.BasicDrinks.All(k => !LuxuryDrinkCatalog.IsLuxury(k)),"W05 original reward pool stays at six; luxury has separate identities");
        var e5=StoryRules.Episode(5);var e6=StoryRules.Episode(6);
        check(e5.Credits==350 && e5.Demand==XiaomanDemand.Household && e5.TrackKey=="midnight"
            && e6.Credits==400 && e6.Demand==XiaomanDemand.Water && e6.TrackKey=="latenight","W05/W06 fixed budget, demand and tape schedule");
        check(e5.Opening.Count(s => s.Key=="xm_05_gift")==1 && TavernRules.OpeningLineKey(5,"xm_05_01",36)=="xm_05_01_late"
            && TavernRules.OpeningLineKey(5,"xm_05_03",29)=="xm_05_03","W05 fifth encounter retains one gift cue and uses delayed chronology");
        check(!LuxuryDrinkCatalog.OnMenu("wonderland",28) && LuxuryDrinkCatalog.OnMenu("wonderland",29)
            && !LuxuryDrinkCatalog.OnMenu("wonderland_improv",100),"W05 calendar unlock never sells improvised gift");
        foreach (var spec in LuxuryDrinkCatalog.Standard) {
            check(spec.Tier==2 && spec.DurationDays==3 && TavernPrices.For(spec)==300,"W05 strong luxury drinks are 300 CR for three days");
            var tavern=new TavernProgress();
            check(tavern.PrepareDrink(28,spec.Key,999,"")==null && tavern.PrepareDrink(29,spec.Key,299,"")==null,"W05 no premature/underfunded luxury receipt");
            var drink=tavern.PrepareDrink(29,spec.Key,1000,"");
            check(drink!=null && drink.Paid==300 && !tavern.MayDrink(29),"W05 luxury consumes the same daily purchase slot");
            var restored=TavernProgress.Restore(tavern.Data,29);
            check(!restored.RecoveryBlocked && restored.Recover(29,700,"")==TavernRecovery.ApplyBuff
                && restored.PrepareDrink(29,"sunset_blue",1000,"")==null,"W05 reload preserves payment recovery and old/new shared quota");
        }
        var buffs=new DrinkBuffCollection();buffs.Replace(CocktailCatalog.Find("cosmo_zone"),29);
        buffs.Replace(CocktailCatalog.Find("spring_sway"),29);buffs.Replace(CocktailCatalog.Find("bluebird"),29);
        buffs.Replace(LuxuryDrinkCatalog.Improv,30);buffs=DrinkBuffCollection.Restore(buffs.Data);
        check(buffs.ActiveAmount(DrinkEffect.Luck,30)==37 && buffs.ActiveAmount(DrinkEffect.Scavenging,30)==4
            && buffs.ActiveAmount(DrinkEffect.TradeReputation,30)==20,"W05 improvised luck replaces +8 while other categories stack");
        buffs.Expire(31);check(buffs.ActiveAmount(DrinkEffect.Luck,31)==0 && buffs.ActiveAmount(DrinkEffect.Scavenging,31)==4,"W05 one-day effect expires next morning independently");
        var gift=new ImprovGiftProgress { VisitDay=29 };
        check(gift.Prepare(17) && !gift.Prepare(18),"W05 gift persists identity before inventory placement");
        gift.Placed(false);check(gift.Phase=="waiting" && gift.Prepare(18),"W05 full counter keeps gift pending");
        gift=ImprovGiftProgress.Restore(gift.Data);gift.Recover(true);
        check(gift.Phase=="complete" && !gift.Prepare(19),"W05 reload cannot duplicate owned gift");
        var uncertain=new ImprovGiftProgress { VisitDay=29 };uncertain.Prepare(20);uncertain.Recover(false);
        check(uncertain.Phase=="uncertain" && !uncertain.Prepare(21),"W05 ambiguous gift placement never guesses a duplicate");

        var unsigned=new SupplyProgress();int start=unsigned.Cash;
        check(unsigned.RecordPreview(28) && unsigned.Cash==start && unsigned.Batches.Count==0
            && unsigned.TrialSoldOut && unsigned.UpperPreviewOrders,"W05 narrative trial and upper preview create no fabricated cash/stock");
        check(unsigned.ProduceLuxury(28,true)==0 && unsigned.ProduceLuxury(29,false)==0 && unsigned.ProduceLuxury(29,true)==2,"W05 supplier channel follows date, independent of agreement/application");
        check(unsigned.Cash==start-420 && unsigned.CompanyLots.Sum(l => l.Paid)==396 && unsigned.Batches.All(b => b.PlayerInitial==0 && b.StoreInitial==6),"W05 unsigned company materials and preparation are actually paid; no player quota");
        check(unsigned.ProduceLuxury(29,true)==0 && unsigned.ProduceLuxury(30,true)==0 && unsigned.Sign(30)
            && unsigned.Batches.All(b => b.PlayerInitial==0),"W05 reopening/signing never clones or retroactively reallocates existing batch");
        unsigned=SupplyProgress.Restore(unsigned.Data);
        check(!unsigned.Blocked && unsigned.CompanyLots.Count==2,"W05 new supply fields survive round trip");
        var mixed=new SupplyProgress { Water=1,Juice=1 };mixed.Sign(22);
        check(mixed.ProduceLuxury(29,true)==2 && mixed.Water==0 && mixed.Juice==0 && mixed.Cash==198,"W05 paid player inputs are consumed once, not repurchased");
        check(mixed.CompanyLots[0].WaterFromPlayer==1 && mixed.CompanyLots[0].JuiceFromPlayer==1
            && mixed.CompanyLots[0].CommonPaid==0 && mixed.CompanyLots[1].CommonPaid==18,"W05 company/player provenance and missing quantities are separate");
        check(mixed.Batches.All(b => b.Produced==6 && b.PlayerInitial==2 && b.StoreInitial==4 && b.WholesalePrice==220),"W05 single batch split conserves each recipe output");
        var batch=mixed.Batches[0];
        var wholesale=mixed.PrepareOutput("r",29,batch.Id,101,220);
        check(wholesale!=null && wholesale.Price==220 && mixed.Complete(wholesale) && !mixed.Complete(wholesale)
            && batch.PlayerRemaining==1 && batch.StoreRemaining==4,"W05 wholesale consumes only player share and credits once");
        for (int i=0;i<4;i++) check(mixed.ReserveBar("drink"+i,batch.Key),"W05 bar consumes finite store share");
        int cash=mixed.Cash;check(!mixed.ReserveBar("overflow",batch.Key) && batch.StoreRemaining==0,"W05 sold-out stock refuses another drink");
        mixed.SettleBar("drink0",300);mixed.SettleBar("drink0",300);
        check(mixed.Cash==cash+300 && batch.PlayerRemaining==1,"W05 retail revenue posts once, never counts a wholesale resale");
        mixed.CancelBar("drink1");mixed.CancelBar("drink1");
        check(batch.StoreRemaining==1 && mixed.ReserveBar("retry",batch.Key),"W05 cancelled unpaid reservation restores exactly once");
        check(!SupplyProgress.Restore(mixed.Data).Blocked,"W05 settled stock and mixed provenance remain valid");
        var supplied=new SupplyProgress();supplied.Sign(22);
        var input=supplied.PrepareInput("r",28,SupplyConfig.WaterItem,71,1,100);
        check(supplied.Complete(input) && supplied.Produce(28)==0 && supplied.ProduceLuxury(29,true)==2
            && supplied.CompanyLots[0].WaterFromPlayer==1 && supplied.CompanyLots[0].CommonPaid==10 && supplied.Cash==180,
            "W05 real unpaired W04 delivery reaches the mixed-source recipe with a reconciled 420 CR total cost");
        check(!SupplyProgress.Restore("{\"Schema\":1,\"SignedDay\":22,\"Cash\":600,\"Water\":0,\"Juice\":0,\"Batches\":[],\"Trades\":[],\"BarUses\":[]}").Blocked,
            "W05 old supply save initializes new lists without resetting its operating account");

        var p=new ReferralProgress { Run="test-run" };
        check(!p.Draw(35) && !p.Draw(36),"W06 referral mechanism never rolls Monday or before unlock");
        for (int day=37;day<=42;day++) {
            check(p.Draw(day) && !p.Draw(day),"W06 each date draws once, even before the shop opens");
            p=ReferralProgress.Restore(p.Data,"test-run",day);
            check(!p.Blocked && p.Days.Count(d => d.Day==day)==1,"W06 draws and visitor kind survive reload");
        }
        check(p.Days.Any(d => d.Won) && p.Days.All(d => d.ArrivedDay==-1),"W06 whole week without reception still retains at least one event");
        var backlog=new ReferralProgress { Run="backlog",Days=new System.Collections.Generic.List<ReferralDay> {
            new ReferralDay { Day=37,Won=true,Roll=0 },new ReferralDay { Day=38,Won=true,Roll=0 } } };
        check(backlog.Due(43)==null && !backlog.Arrive(backlog.Days[0].Id,43),"W06 pending customers never catch up on Monday");
        var first=backlog.Due(44);check(first.Day==37 && backlog.Arrive(first.Id,44) && !backlog.Arrive(first.Id,44)
            && backlog.Due(44)==null,"W06 oldest pending event, maximum one actual arrival per day");
        backlog=ReferralProgress.Restore(backlog.Data,"backlog",45);
        check(backlog.Due(45).Day==38 && !backlog.Blocked,"W06 remaining backlog survives reception and reload");
        backlog.Days[1].ProfileReady=true;backlog.Days[1].Sprite="scav";backlog.Days[1].Budget=0;
        backlog=ReferralProgress.Restore(backlog.Data,"backlog",45);
        check(backlog.Due(45).ProfileReady && backlog.Due(45).Budget==0,"W06 even a zero budget from native/other-mod rules is frozen, not a reroll sentinel");
        var guarantee=new ReferralProgress { Run="guarantee" };
        for (int d=37;d<=41;d++) guarantee.Days.Add(new ReferralDay { Day=d,Roll=99 });
        guarantee.Draw(42);check(guarantee.Days.Last().Won && guarantee.Days.Last().Guaranteed,"W06 Sunday guarantees only if source week has no win");
        bool multi=false;
        for (int run=0;run<40;run++) {
            var week=new ReferralProgress { Run="run"+run };for (int d=37;d<=42;d++) week.Draw(d);
            check(week.Days.Any(d => d.Won),"W06 Sunday always leaves a weekly event");
            multi|=week.Days.Count(d => d.Won)>1;
        }
        check(multi,"W06 no accidental weekly cap inherited from work thanks");
        check(ReferralProgress.Restore("{broken","r",42).Blocked && SupplyProgress.Restore("{broken").Blocked,"W05/W06 damaged state is retained and fails closed");
        Console.WriteLine("PASS: W05 luxury supply, split/stock/payment, one-day gift, delayed encounter; W06 stable draws, Sunday guarantee, retained non-Monday backlog and independent arrival quota.");
    }
}
