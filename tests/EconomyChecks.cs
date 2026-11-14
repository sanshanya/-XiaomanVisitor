using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using XiaomanVisitor;

internal static class EconomyChecks
{
    private static TavernEconomyProgress Pre(params int[] projectWeeks)
    {
        var p=new TavernEconomyProgress();p.Initialize(1,8,false);
        for (int week=1;week<=10;week++) {
            int start=8+(week-1)*7;
            if (start==64) p.ObserveChapter(10,start);
            if (start==71) p.ObserveChapter(11,start);
            if (projectWeeks.Contains(week) && !p.FinishProject(start)) throw new Exception("Project schedule rejected");
            p.Advance(start+7);p=TavernEconomyProgress.Restore(p.Data);
            if (p.Blocked) throw new Exception("Valid account did not reload");
        }
        p.ObserveChapter(12,78);
        return p;
    }
    private static void Contracts(TavernEconomyProgress p,Action<bool,string> check)
    {
        check(p.Acknowledge(78) && p.Trading && p.Phase=="review_pending","explicit review request keeps trading through the current week");
        check(!p.Acknowledge(78) && p.SignContract("orders",78),"review request is idempotent; first document is separate");
        check(!p.SignContract("sublet",78),"two contracts cannot be signed on one day");
        check(!p.Refinance(78,false,false),"an unsigned contract cannot create refinancing");
        check(p.SignContract("sublet",79) && !p.Refinance(79,false,false),"second signature and its next-day receipt are distinct");
        check(!p.HandlingDay(80) && !p.Leave(80),"handling clock and departure cannot start while the requested week is still trading");
        p.Advance(85);
        check(p.Phase=="review" && p.ReviewBeginDay==85 && p.NoticeDay==78 && p.Weeks.Count==11
            && p.Weeks.Last().EndDay==84 && !p.HandlingDay(85),"pause occurs after the requested week is settled; no handling days backfilled");
    }
    internal static void Run(Action<bool,string> check)
    {
        EconomyUiAccessChecks.Run(check);
        EconomyBalanceChecks.Run(check);
        var low=Pre(2,5,8);
        check(Enumerable.Range(0,6).Sum(n => TavernEconomyProgress.AllocatedUnitCost(235,6,n))==235,
            "batch cost remainders allocate exactly once without duplicating supplier cash and margin");
        check(low.Trading && !low.Reviewing && low.NoticeDay<0 && low.Weeks.Count==10,
            "ten settlements cannot automatically shut the tavern before the twelfth encounter");
        check(low.Weeks.Single(w => w.EndDay==63).Natural==78
            && low.Weeks.Single(w => w.EndDay==70).Natural==55
            && low.Weeks.Single(w => w.EndDay==77).Natural==42,
            "week nine stays a high point; competition begins in actual weeks ten and eleven");

        var delayed=new TavernEconomyProgress();delayed.Initialize(1,8,false);delayed.Advance(92);
        check(delayed.Trading && !delayed.ReviewUnlocked && delayed.Weeks.Last().Natural==78
            && !delayed.SignContract("orders",92) && !delayed.Acknowledge(92),
            "calendar maturity without actual visits cannot reveal crisis, start rescue contracts or close trading");
        delayed.ObserveChapter(10,99);delayed.Advance(106);
        check(delayed.Weeks.Single(w => w.EndDay==98).Natural==78 && delayed.Weeks.Last().Natural==55
            && !delayed.ReviewUnlocked,"late competition changes only weeks at or after the actual tenth encounter");
        delayed.ObserveChapter(11,106);delayed.Advance(113);
        delayed.ObserveChapter(12,113);delayed.Advance(120);
        check(delayed.Trading && delayed.Phase=="maturity_pending" && !delayed.Reviewing && delayed.NoticeDay<0
            && delayed.Weeks.Last().Natural==30,"late twelfth encounter and matured debt still wait for an explicit review request");

        var normalUnseen=Pre();normalUnseen.Advance(92);
        check(normalUnseen.Trading && normalUnseen.Phase=="maturity_pending" && normalUnseen.Weeks.Count==12
            && normalUnseen.NoticeDay<0,"twelve-period maturity changes status only, with no automatic closure");

        string trading=low.Data;
        check(low.Record("last-week-sale","onsite",80,190,60),"real sale is accepted during the requested operating week");
        Contracts(low,check);
        var comparison=TavernEconomyProgress.Restore(trading);Contracts(comparison,check);
        check(low.Weeks.Last().Revenue-comparison.Weeks.Last().Revenue==190
            && low.Weeks.Last().Material-comparison.Weeks.Last().Material==60,
            "review transition neither loses nor duplicates actual sales and material costs");
        string paused=low.Data;low.Advance(500);
        var snapshot=TavernEconomyProgress.Restore(paused);
        check(low.Weeks.Count==11 && low.Principal==snapshot.Principal && low.Fees==snapshot.Fees && low.HandlingDays.Count==0,
            "requested pause freezes receipts, interest and rent without nonexistent handling days");
        low=TavernEconomyProgress.Restore(paused);
        check(!low.Refinance(85,true,false),"property guarantee cannot be signed without native ownership");
        long quote=low.RefinanceQuote;
        check(low.Refinance(85,false,false) && low.Principal==quote && low.Cash>=2000
            && low.ResumeDay==85 && low.NextSettlement==92 && !low.HasStayed,
            "ordinary restructuring needs no property or donation and can reopen for the entire Monday week");
        low.Advance(91);check(!low.HasStayed,"signatures alone never establish the good ending");
        low.Advance(92);
        check(low.HasStayed && low.StableDay==91 && low.Weeks.Last().FixedOrders==48 && low.Weeks.Last().Repaid>=low.Weeks.Last().Minimum
            && low.Weeks.Last().SubletId=="repair-room:91","week fourteen opens stay only after actual deliveries, rent receipt and repayment");
        int periods=1;
        while (low.Debt>0 && periods<53) { low.Advance(low.NextSettlement);periods++; }
        check(periods<=52 && low.Phase=="cleared" && low.Cash>=2000,"established regular trade can repay without player donations");

        var none=Pre();var active=Pre(1,3,5,7);
        foreach (var p in new[]{none,comparison,active}) {
            if (!p.Reviewing) Contracts(p,check);
            string baseline="ECONOMY baseline projects="+p.Level+" principal="+p.Principal+" fees="+p.Fees+" cash="+p.Cash
                +" quote="+p.RefinanceQuote+" extensions="+p.FrozenExtensions;
            check(p.Refinance(85,false,false),"no-project and four-project paths both permit ordinary restructuring");
            var floor=TavernEconomyProgress.Restore(p.Data);
            p.Advance(p.NextSettlement);floor.Advance(floor.NextSettlement,true);
            check(p.HasStayed && floor.HasStayed && floor.Bills==0 && floor.Weeks.Last().Repaid>=floor.Weeks.Last().Minimum,
                "first contracted period is affordable without a player subsidy, walk-ins or regular customers");
            Console.WriteLine(baseline+" first_contract_rent="+floor.Weeks.Last().Rent+" minimum="+floor.Weeks.Last().Minimum);
        }
        var stress=TavernEconomyProgress.Restore(paused);stress.Refinance(85,false,false);periods=0;
        while (stress.Trading && periods<30) { stress.Advance(stress.NextSettlement,true);periods++; }
        check(stress.Phase=="remedy" && !stress.Departing && stress.StableDay>=0 && stress.Weeks.Last().Natural==0
            && stress.Weeks.Last().Regulars==0,"rising rent eventually consumes fixed-contract headroom; it never creates automatic departure");
        check(stress.ContractOperatingCash(85)==559 && stress.ContractOperatingCash(92)==440
            && TavernEconomyProgress.Minimum(14404,277,50)==350
            && TavernEconomyProgress.Minimum(14405,278,50)==351,"refinance affordability uses the actual reopening rent and exact interest rounding");

        var legacyLoan=Pre();Contracts(legacyLoan,check);legacyLoan.Refinance(85,false,false);legacyLoan.Advance(92);
        legacyLoan.Cash=900;legacyLoan.Schema=2;
        legacyLoan=TavernEconomyProgress.Restore(legacyLoan.Data);
        check(!legacyLoan.Blocked && legacyLoan.ContractOrders==20 && legacyLoan.HasStayed && legacyLoan.Cash==900,
            "a saved old loan retains its original signed orders and actual 900 CR cash");
        for (int n=0;n<3;n++) {
            legacyLoan.Advance(legacyLoan.NextSettlement);
            check(legacyLoan.HasStayed && legacyLoan.MissedPeriods==0 && legacyLoan.Weeks.Last().Repaid>=legacyLoan.Weeks.Last().Minimum,
                "available old-save cash pays contractual installments before building the new reserve target");
        }
        var repair=TavernEconomyProgress.Restore(legacyLoan.Data);
        repair.Phase="remedy";repair.Cash=1698;repair.Fees=114;repair.OverduePrincipal=452;repair.MissedPeriods=2;
        long repairDebt=repair.Debt,repairCash=repair.Cash;int priorPayments=repair.Payments.Count;
        var poor=TavernEconomyProgress.Restore(repair.Data);poor.Cash=565;string poorBefore=poor.Data;
        check(!poor.ResumeAfterRemedy(poor.LastDay) && poor.Data==poorBefore,"insufficient store cash cannot waive arrears or invent a repayment");
        check(repair.ResumeAfterRemedy(repair.LastDay) && repair.Cash==repairCash-566 && repair.Debt==repairDebt-566
            && repair.Fees==0 && repair.OverduePrincipal==0 && repair.PaymentHistory.Last().Source=="tavern"
            && repair.Payments.Count==priorPayments+1,"explicit reopening may clear real arrears once from the tavern's own available cash");
        repair=TavernEconomyProgress.Restore(repair.Data);string repaired=repair.Data;
        check(!repair.Blocked && !repair.ResumeAfterRemedy(repair.LastDay) && repair.Data==repaired,
            "store-funded remedy receipt survives reload without repeating a debit or becoming player-funded");

        var extension=TavernEconomyProgress.Restore(paused);
        int rights=extension.FrozenExtensions;long fee=extension.Fees;
        check(rights>0 && extension.Extend(85) && extension.Fees==fee+80 && extension.Phase=="extension_wait",
            "one-week extension requires explicit agreement and charges exactly one 80 CR fee");
        long before=extension.Debt;int count=extension.Weeks.Count;extension.Advance(extension.ResumeDay-1);
        check(extension.Debt==before && extension.Weeks.Count==count,"extension never invents revenue before its full operating week");
        extension.Advance(extension.NextSettlement);
        check(extension.Phase=="review" && extension.FrozenExtensions==rights && extension.ExtensionsUsed==1,
            "explicit one-week extension ends on its agreed boundary without creating new eligibility");

        var paymentTest=Pre();long repaid=paymentTest.OperatingRepaid;
        paymentTest.PreparePayment(78,2000,100);
        var pending=TavernEconomyProgress.Restore(paymentTest.Data);pending.RecoverPayment(78,1900);
        check(pending.PendingPayment==null && pending.Payments.Count==1 && pending.OperatingRepaid==repaid
            && pending.PaymentHistory.Single().Amount==100 && !pending.ConfirmPayment(1900),
            "native payment recovery applies one real receipt; donations cannot buy extension history");
        var uncertain=TavernEconomyProgress.Restore(paymentTest.Data);uncertain.RecoverPayment(80,1820);
        check(uncertain.Blocked && uncertain.PendingPayment.Phase=="uncertain","ambiguous cash changes never trigger guessed repayments or refunds");

        var old=Pre();old.Schema=1;old.Phase="review";
        var json=JObject.Parse(old.Data);
        foreach (string key in new[]{"CompetitionDay","AdjustmentDay","CrisisDay","ReviewRequestedDay","ReviewBeginDay","PeriodStartDay","ExtensionRightsFrozen","PacingRecoveryPending"}) json.Remove(key);
        var recovered=TavernEconomyProgress.Restore(json.ToString());
        check(recovered.Schema==3 && recovered.PacingRecoveryPending && recovered.Trading && recovered.Principal==old.Principal
            && recovered.Fees==old.Fees && recovered.Cash==old.Cash && recovered.Weeks.Count==old.Weeks.Count,
            "old automatic closure reopens without rewriting settled money or history");
        recovered.ObserveChapter(10,64);recovered.ObserveChapter(11,71);recovered.ObserveChapter(12,78);
        recovered.Advance(80);long recoveredPrincipal=recovered.Principal;
        check(recovered.NextSettlement==85 && recovered.PeriodStartDay==80 && recovered.ResumeDay==80
            && !recovered.Record("closed-day","onsite",79,190,60) && recovered.Record("resumed-day","onsite",80,190,60),
            "legacy recovery imports only real post-resumption receipts");
        recovered=TavernEconomyProgress.Restore(recovered.Data);recovered.Advance(81);
        check(recovered.NextSettlement==85 && recovered.PeriodStartDay==80,"reload cannot restart a recovered partial week");
        recovered.Advance(85);var partial=recovered.Weeks.Last();
        check(partial.ActiveDays==5 && partial.Natural==21 && partial.Fixed==247 && partial.Rent==300
            && partial.Interest==TavernEconomyProgress.Up(recoveredPrincipal*100*5,70000)
            && partial.Revenue==21*65+190 && partial.Material==21*30+60,
            "recovered partial week prorates autonomous trading and charges, preserving exact real receipts");

        var future=new TavernEconomyProgress();future.Initialize(44,8,false);
        check(future.Phase=="migration" && future.Principal==0 && future.Cash==0,"legacy save cannot silently inherit made-up historical debt");
        future.AcceptMigration(44);future.Advance(44);
        check(future.StartDay==50 && future.Principal==8800 && future.Weeks.Count==0,"accepted legacy lease begins next full Monday without past interest");
        var mondayMigration=new TavernEconomyProgress();mondayMigration.Initialize(50,8,false);mondayMigration.AcceptMigration(50);
        check(mondayMigration.StartDay==57 && !mondayMigration.Record("old-monday","onsite",50,300,40),"Monday lease acceptance never backfills earlier sales");
        var departed=new TavernEconomyProgress();departed.Initialize(100,8,true);
        check(departed.Phase=="legacy" && departed.Departing && !departed.AcceptMigration(100),"old completed departure never resurrects");
        var chosenLeave=Pre();Contracts(chosenLeave,check);chosenLeave.Leave(85);chosenLeave.Schema=1;
        check(TavernEconomyProgress.Restore(chosenLeave.Data).Departing,"schema migration preserves explicit closure choices");
        var chosenExtension=TavernEconomyProgress.Restore(paused);chosenExtension.Extend(85);
        chosenExtension.Advance(chosenExtension.NextSettlement);chosenExtension.Schema=1;
        var heldExtension=TavernEconomyProgress.Restore(chosenExtension.Data);
        check(heldExtension.Reviewing && !heldExtension.PacingRecoveryPending,"migration preserves an explicitly agreed extension and its end");
        low.Schema=1;var heldStay=TavernEconomyProgress.Restore(low.Data);
        check(heldStay.HasStayed && heldStay.RefinanceDay==low.RefinanceDay && heldStay.Debt==low.Debt,
            "migration never resets an already completed restructuring or stay ending");

        var duplicate=new TavernEconomyProgress();duplicate.Initialize(1,8,false);
        check(duplicate.Record("drink:1","onsite",8,95,30) && !duplicate.Record("drink:1","onsite",8,95,30),"each native receipt has one import owner");
        duplicate.Advance(15);string once=duplicate.Data;duplicate.Advance(15);
        check(duplicate.Data==once && duplicate.Weeks.Single().Revenue==25*65+95,"repeated hooks cannot resettle the same week");
        var leave=Pre();check(!leave.Leave(78),"an unseen notice cannot cause departure");Contracts(leave,check);
        check(leave.Leave(85) && leave.Departing && !leave.Leave(85) && leave.Cash>=0,"explicit closure liquidates only her account once");
        check(EconomyStory.Opening("stay",14).Length==2 && EconomyStory.Opening("review",13).Length==4
            && low.ChapterBranch=="stay" && leave.ChapterBranch=="leave" && delayed.ChapterBranch=="review","late chapters follow actual financial choices and results");
        check(TavernEconomyProgress.Restore("{broken").Blocked
            && TavernEconomyProgress.Restore("{\"Schema\":99}").Blocked,"malformed or future data is preserved and blocked");
        var storm=TavernEconomyProgress.Restore(paused);storm.Refinance(85,true,true);
        storm.Cash=0;storm.Bills=20000;storm.Advance(storm.NextSettlement);
        check(storm.Phase=="remedy" && !storm.Departing && storm.GuaranteeDue==0,"post-contract shortfall creates remedy, not automatic seizure or guarantee charges");
        Console.WriteLine("PASS: fourteen-visit pacing, explicit weekly pause, actual deliveries, payment recovery, migration and zero-subsidy stress.");
    }
}
