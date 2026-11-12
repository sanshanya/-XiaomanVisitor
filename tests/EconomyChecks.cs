using System;
using System.Linq;
using XiaomanVisitor;

internal static class EconomyChecks
{
    private static TavernEconomyProgress Pre(params int[] projectWeeks)
    {
        var p=new TavernEconomyProgress();p.Initialize(1,8,false);
        for (int week=1;week<=10;week++) {
            int start=8+(week-1)*7;
            if (projectWeeks.Contains(week) && !p.FinishProject(start)) throw new Exception("Project schedule rejected");
            p.Advance(start+7);p=TavernEconomyProgress.Restore(p.Data);
            if (p.Blocked) throw new Exception("Valid account did not reload");
        }
        return p;
    }
    private static void Contracts(TavernEconomyProgress p,Action<bool,string> check)
    {
        check(p.Acknowledge(78),"full notice acknowledged before window starts");
        check(p.SignContract("orders",78),"first document signed independently");
        check(!p.SignContract("sublet",78),"two documents cannot be signed on one day");
        check(!p.Refinance(78,false,false),"unsigned/undelivered contract cannot create refinancing");
        check(p.SignContract("sublet",79),"second signature takes a separate available day");
        check(!p.Refinance(79,false,false),"counterparty receipt arrives next day");
    }
    internal static void Run(Action<bool,string> check)
    {
        var low=Pre(2,5,8);
        check(Enumerable.Range(0,6).Sum(n => TavernEconomyProgress.AllocatedUnitCost(235,6,n))==235,
            "batch cost remainders allocate exactly once and cannot duplicate supplier cash and operating margin");
        check(low.Principal==9875 && low.Fees==541 && low.Cash==775 && low.OperatingRepaid==1550
            && low.RefinanceQuote==10781 && low.FrozenExtensions==1,"low-frequency three-night path reproduces v0.3 independently");
        var none=Pre();var active=Pre(1,3,5,7);
        check(none.RefinanceQuote==12374 && none.FrozenExtensions==0 && active.RefinanceQuote==9385
            && active.FrozenExtensions==2,"no-help and four-project fixed debt paths match candidate model");
        string frozen=low.Data;low.Advance(500);
        check(low.Weeks.Count==10 && low.Principal==9875 && low.Fees==541 && low.NoticeDay<0,"missed meetings stop revenue/rent/interest at frozen maturity, not at chapter 13");
        low=TavernEconomyProgress.Restore(frozen);Contracts(low,check);
        check(!low.Refinance(80,true,false),"ordinary mortgage holder cannot sign a property-owner guarantee");
        check(low.Refinance(80,false,false) && low.Principal==10781 && low.Cash==900
            && !low.HasStayed && low.Phase=="restructured","normal refinancing requires no native property or player cash, but is not yet a stay ending");
        int first=low.NextSettlement;
        low.Advance(first);
        check(low.HasStayed && low.StableDay==first-1 && low.Weeks.Last().FixedOrders==20
            && low.Weeks.Last().Repaid==735 && low.Weeks.Last().SubletId=="repair-room:"+(first-1),"first actual contract deliveries and feasible repayment establish staying");
        int periods=1;
        while (low.Debt>0 && periods<53) { low.Advance(low.NextSettlement);periods++; }
        check(periods==16 && low.Phase=="cleared" && low.Cash>=900,"three-night path self-repays in sixteen weeks with no donations");
        var stress=Pre(2,5,8);Contracts(stress,check);stress.Refinance(80,false,false);periods=0;
        while (stress.Debt>0 && periods<53) { stress.Advance(stress.NextSettlement,true);periods++; }
        check(periods==34 && stress.HasStayed && stress.Bills==0,"zero walk-ins, zero old regulars, zero player visits still self-repay in thirty-four periods");
        check(TavernEconomyProgress.GuaranteedOperatingCash==350
            && TavernEconomyProgress.Minimum(14404,277,50)==350
            && TavernEconomyProgress.Minimum(14405,278,50)==351,"guaranteed 350 CR floor and exact debt affordability boundary");
        var extension=TavernEconomyProgress.Restore(frozen);Contracts(extension,check);
        check(extension.Extend(80) && extension.Fees==621 && extension.Phase=="extension_wait","one extension is explicit and charges its single 80 CR fee");
        long before=extension.Debt;extension.Advance(extension.ResumeDay-1);
        check(extension.Debt==before && extension.Weeks.Count==10,"no backfilled trading between acceptance and next full week");
        extension.Advance(extension.NextSettlement);
        check(extension.RefinanceQuote==11060 && extension.FrozenExtensions==1 && !extension.Extend(extension.LastDay),"one extended operating week has the exact candidate cost and never creates more extension rights");
        var repay=Pre(2,5,8);Contracts(repay,check);repay.Refinance(80,false,false);
        int left=0;periods=0;
        while (repay.Debt>0 && periods<20) {
            repay.Advance(repay.NextSettlement);var payment=repay.PreparePayment(repay.LastDay,2000,1500);
            if (payment!=null) { left=1500-payment.Amount;check(repay.ConfirmPayment(2000-payment.Amount),"actual native cash delta confirms exactly one authorized repayment"); }
            periods++;
        }
        check(periods==5 && left==232,"optional 1500 budget accelerates payoff to five periods and leaves unused 232 with player");
        var paymentTest=Pre();long repaid=paymentTest.OperatingRepaid;
        var request=paymentTest.PreparePayment(78,2000,100);
        var pending=TavernEconomyProgress.Restore(paymentTest.Data);pending.RecoverPayment(78,1900);
        check(pending.PendingPayment==null && pending.Payments.Count==1 && pending.OperatingRepaid==repaid
            && pending.PaymentHistory.Single().Amount==100 && !pending.ConfirmPayment(1900),"reload recovers a paid receipt once with its actual amount; donations cannot buy extension history");
        request=paymentTest.PendingPayment;
        var uncertain=TavernEconomyProgress.Restore(paymentTest.Data);uncertain.RecoverPayment(80,1820);
        check(uncertain.Blocked && uncertain.PendingPayment.Phase=="uncertain","unrelated money changes never trigger guessed repayment or refund");
        var future=new TavernEconomyProgress();future.Initialize(44,8,false);
        check(future.Phase=="migration" && future.Principal==0 && future.Cash==0,"unentered legacy save does not silently inherit historical debt");
        future.AcceptMigration(44);future.Advance(44);
        check(future.StartDay==50 && future.Principal==8800 && future.Weeks.Count==0,"accepted legacy lease begins next complete Monday without past interest");
        var mondayMigration=new TavernEconomyProgress();mondayMigration.Initialize(50,8,false);mondayMigration.AcceptMigration(50);
        check(mondayMigration.StartDay==57 && !mondayMigration.Record("old-monday","onsite",50,300,40),"accepting on Monday never backfills that day's earlier sales into a new lease");
        var departed=new TavernEconomyProgress();departed.Initialize(100,8,true);
        check(departed.Phase=="legacy" && departed.Departing && !departed.AcceptMigration(100),"already departed legacy story cannot be resurrected");
        var duplicate=new TavernEconomyProgress();duplicate.Initialize(1,8,false);
        check(duplicate.Record("drink:1","onsite",8,95,30) && !duplicate.Record("drink:1","onsite",8,95,30),"native receipts have a unique single import owner");
        duplicate.Advance(15);string once=duplicate.Data;duplicate.Advance(15);
        check(duplicate.Data==once && duplicate.Weeks.Single().Revenue==15*65+95,"same-day load and repeated hooks cannot resettle a week");
        var leave=Pre();check(!leave.Leave(78),"no unseen notice can cause departure");leave.Acknowledge(78);
        check(leave.Leave(78) && leave.Departing && !leave.Leave(78) && leave.Cash>=0,"explicit closure liquidates only her account once, without paying the player");
        check(EconomyStory.Opening("stay",14).Length==2 && EconomyStory.Opening("review",13).Length==4
            && low.ChapterBranch=="stay" && leave.ChapterBranch=="leave" && none.ChapterBranch=="review","late chapters choose coherent stay, pending, or departure text");
        check(TavernEconomyProgress.Restore("{broken").Blocked
            && TavernEconomyProgress.Restore("{\"Schema\":99}").Blocked,"malformed/newer financial data is preserved and blocked");
        var storm=Pre();Contracts(storm,check);storm.Refinance(80,true,true);
        storm.Cash=0;storm.Bills=20000;storm.Advance(storm.NextSettlement);
        check(storm.Phase=="remedy" && !storm.Departing && storm.GuaranteeDue==0,"a shortfall creates review, not immediate seizure or automatic guarantee charges");
        Console.WriteLine("PASS: actual economic paths, dated contracts, operating receipts, migration, frozen decisions, rescue, repayment and zero-subsidy stress case.");
    }
}
