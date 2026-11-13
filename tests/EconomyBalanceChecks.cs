using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using XiaomanVisitor;

internal static class EconomyBalanceChecks
{
    internal sealed class Row
    {
        public int Week, Rent, Natural, Net, Penalty;
        public long Cash, Principal, Fees, Bills, Reserve;
    }
    internal sealed class Profile
    {
        public string Name;
        public int FirstCashShortfall=-1;
        public List<Row> Weeks=new List<Row>();
    }
    internal static Profile Simulate(string name,int shifts,int drinks,int lastHelpWeek)
    {
        var result=new Profile { Name=name };
        var account=new TavernEconomyProgress();account.Initialize(1,8,false);
        var work=new TavernWorkProgress { RunIdentity=name };
        var brewing=new BrewingProgress();int item=1000;
        for (int week=2;week<=30;week++) {
            int monday=1+(week-1)*7;
            account.Advance(monday);account.ObserveChapter(week,monday);
            if (week<=lastHelpWeek && shifts>0) {
                if (week>=4) {
                    if (!brewing.AgreementActive) brewing.Sign(monday);
                    brewing.Arrive(monday+2);
                    var batch=brewing.Prepare(name,monday+2,new List<BrewingMaterial> {
                        new BrewingMaterial { ItemId=item++,Count=1,Key="water",ValueCents=50000 } });
                    if (batch==null || !brewing.RecoverInput(new int[0]) || !brewing.Arrive(monday+4)
                        || !account.ImportBrewingProfit(batch)) throw new Exception("Material settlement failed: "+name);
                    while (brewing.PrepareBottle(item++)!=null) brewing.BottlePlaced(true);
                    brewing.ContinueAfterDelivery(batch.Id,brewing.AgreementRevision,monday+4,true);
                    brewing=BrewingProgress.Restore(brewing.Data);
                    if (brewing.Blocked) throw new Exception("Brewing reload failed: "+name);
                }
                for (int n=0;n<shifts && week>=3;n++) {
                    int day=monday+1+n*2;work.EnsureDemand(day,TavernWorkConfig.DailyOrders);
                    var shift=work.PrepareShift(day,1000);
                    if (shift==null || work.Recover(day,1000+shift.Wage)!=WageRecovery.Finish || !work.Complete(shift)
                        || !account.Record("work:"+shift.Id,"overflow_orders",day,shift.Revenue,shift.Material+shift.Wage))
                        throw new Exception("Actual shift settlement failed: "+name);
                }
                for (int n=0;n<drinks;n++) if (!account.Record("drink:"+week+":"+n,"onsite",monday+n*2,95,30))
                    throw new Exception("Completed drink receipt rejected");
            }
            account.Advance(monday+7);
            var row=account.Weeks.Last();
            result.Weeks.Add(new Row { Week=week,Rent=row.Rent,Natural=row.Natural,Net=row.Revenue-row.Material-row.Fixed-row.Rent,
                Penalty=row.ContractPenalty,Cash=account.Cash,Principal=account.Principal,Fees=account.Fees,Bills=account.Bills,Reserve=account.ReserveTarget });
            if (result.FirstCashShortfall<0 && (account.Cash==0 || account.Bills>0)) result.FirstCashShortfall=week;
            account=TavernEconomyProgress.Restore(account.Data);work=TavernWorkProgress.Restore(work.Data,name,monday+7);
            if (account.Blocked || work.RecoveryBlocked || !account.Trading || account.Departing)
                throw new Exception("Balance run must retain valid accounts and explicit departure choices");
        }
        return result;
    }
    internal static Profile[] Scenarios()
    {
        return new[] { Simulate("none",0,0,0),Simulate("weekly500_one_shift",1,0,30),
            Simulate("weekly500_one_shift_one_drink",1,1,30),Simulate("weekly500_three_shifts_three_drinks",3,3,30),
            Simulate("ordinary_help_through_week9",1,0,9),Simulate("frequent_help_through_week9",3,3,9) };
    }
    private static TavernEconomyProgress BeforeAssessment()
    {
        var p=new TavernEconomyProgress();p.Initialize(1,8,false);
        for (int week=2;week<12;week++) { int day=1+(week-1)*7;p.ObserveChapter(week,day);p.Advance(day+7); }
        p.ObserveChapter(12,78);return p;
    }
    internal static void Run(Action<bool,string> check)
    {
        var results=Scenarios();var none=results[0];var ordinary=results[1];var frequent=results[3];
        check(none.Weeks.Where(w => w.Week>=6 && w.Week<=8).All(w => w.Net>=1500)
            && none.Weeks.Single(w => w.Week==9).Natural==78,"high weeks earn substantial autonomous surplus through week nine");
        check(none.Weeks.Single(w => w.Week==9).Principal<4400 && none.Weeks.Single(w => w.Week==9).Cash==2000,
            "without player help, high-period trading pays down most opening principal while building genuine cash");
        check(none.Weeks.Single(w => w.Week==10).Net<none.Weeks.Single(w => w.Week==9).Net
            && none.Weeks.Where(w => w.Week<12).All(w => w.Penalty==0)
            && none.Weeks.Sum(w => w.Penalty)==1500 && ordinary.Weeks.Sum(w => w.Penalty)==0,
            "competition reduces sales from ten; a single post-twelve penalty is waived for genuine qualifying sales");
        check(ordinary.FirstCashShortfall>none.FirstCashShortfall && frequent.FirstCashShortfall>ordinary.FirstCashShortfall
            && results[4].FirstCashShortfall>none.FirstCashShortfall && results[5].FirstCashShortfall>results[4].FirstCashShortfall,
            "ordinary/frequent participation, including early-only help, gives real additional runway");
        check(results.All(p => p.FirstCashShortfall>=12 && p.FirstCashShortfall<=30),
            "no profile closes early; bounded help cannot defeat exponentially rising rent indefinitely");
        foreach (var p in results) Console.WriteLine("BALANCE "+p.Name+" first_cash_shortfall_week="+p.FirstCashShortfall
            +" week12="+JsonConvert.SerializeObject(p.Weeks.Single(w => w.Week==12)));

        var account=new TavernEconomyProgress();account.Initialize(1,8,false);
        check(account.RentAt(8)==420 && account.RentAt(57)==815 && account.RentAt(64)==896
            && account.RentAt(78)==1083 && account.RentAt(85)==1191,"own calendar rent compounds with integer rounding independently of chapters");
        var batch=new BrewingBatch { Id="proof",Phase="complete",Day=10,ProfitDay=12,Credited=true,TavernProfit=150 };
        var baseAccount=TavernEconomyProgress.Restore(account.Data);
        check(account.ImportBrewingProfit(batch) && !account.ImportBrewingProfit(batch) && account.Cash==1050
            && account.Receipts.Single().Revenue==150 && account.ReserveTarget==2075,
            "500 materials book only 150 frozen net profit once; half is retained without inventing cash");
        account.Advance(15);baseAccount.Advance(15);
        check(account.Cash-account.Debt-(baseAccount.Cash-baseAccount.Debt)==150,
            "cash-booked brewing margin is not added again by weekly settlement");
        account=TavernEconomyProgress.Restore(account.Data);
        check(!account.ImportBrewingProfit(batch) && !account.ImportBrewingProfit(new BrewingBatch { Id="unpaid",TavernProfit=150 }),
            "reload and unfinished material transfers cannot repeat operating profit");
        var boundary=BeforeAssessment();var below=TavernEconomyProgress.Restore(boundary.Data);
        var giftOnly=TavernEconomyProgress.Restore(boundary.Data);
        boundary.Record("qualifying-sale","onsite",78,745,0);below.Record("small-sale","onsite",78,744,0);
        giftOnly.Record("not-a-sale","brewing_profit",78,745,0);
        boundary.Advance(85);below.Advance(85);giftOnly.Advance(85);
        check(boundary.PerformanceRevenue==9000 && boundary.AssessedPenalty==0 && below.PerformanceRevenue==8999
            && below.AssessedPenalty==1500 && giftOnly.AssessedPenalty==1500,
            "performance threshold is inclusive; material profit cannot masquerade as qualifying sales");
        long assessed=below.Weeks.Sum(w => w.ContractPenalty);below=TavernEconomyProgress.Restore(below.Data);below.Advance(99);
        check(below.Weeks.Sum(w => w.ContractPenalty)==assessed,"later settlements and reload cannot repeat the same penalty");
        var paused=BeforeAssessment();paused.Acknowledge(78);paused.Advance(85);long pausedCash=paused.Cash;
        var lateBatch=new BrewingBatch { Id="late-friday",Phase="complete",Day=80,ProfitDay=89,Credited=true,TavernProfit=150 };
        check(paused.ImportBrewingProfit(lateBatch) && paused.Cash==pausedCash+150,"owed pre-pause materials can realize their actual delivery profit during review");
        paused.Advance(99);
        check(paused.Cash==pausedCash+150 && paused.Weeks.Count==11 && paused.RentAt(99)>paused.RentAt(85)
            && !TavernEconomyProgress.Restore(paused.Data).ImportBrewingProfit(lateBatch),
            "paused accounts charge no fictitious trading or rent while lease tiers and actual receipts remain independent");

        var old=TavernEconomyProgress.Restore(baseAccount.Data);old.Advance(78);old.ObserveChapter(12,78);old.Schema=2;
        old.Contracts.Add(new EconomyContract { Kind="orders",SignedDay=78,EffectiveDay=79 });
        long cash=old.Cash,debt=old.Debt;string settled=JsonConvert.SerializeObject(old.Weeks);
        var migrated=TavernEconomyProgress.Restore(old.Data);
        check(!migrated.Blocked && migrated.Schema==3 && migrated.Cash==cash && migrated.Debt==debt
            && JsonConvert.SerializeObject(migrated.Weeks)==settled && migrated.ContractOrders==20
            && migrated.RentAt(78)==420 && migrated.RentAt(85)==462,
            "old accounts retain settled money and signed quantities; new rent rises begin prospectively");
        check(migrated.PenaltyAssessed && migrated.AssessedPenalty==0,"upgrading a disclosed old crisis cannot add a retroactive penalty");
        var source=new BrewingProgress();source.Sign(22);source.Arrive(24);
        var frozen=source.Prepare("frozen",24,new List<BrewingMaterial> { new BrewingMaterial { ItemId=1,Count=1,ValueCents=50000 } });
        check(frozen.TavernProfit==150 && frozen.ReturnMilli==650000,"both sides freeze independent thirty-percent returns");
        source.RecoverInput(new int[0]);source.Schema=3;source=BrewingProgress.Restore(source.Data);
        check(!source.Blocked && source.Schema==4 && source.Batches.Single().TavernProfit==0
            && source.Batches.Single().ReturnMilli==650000,"old accepted batches keep player value without retroactive operating rewards");

        var work=new TavernWorkProgress { Schema=2,RunIdentity="old-work" };work.EnsureDemand(15,3);
        var orders=work.Demand.Single().Orders;
        work.Shifts.Add(new PaidWorkShift { Id="paid_shift_15",Status="prepared",Day=15,BeforeCash=100,Wage=60,
            Revenue=195,Material=90,Net=45,OrderIds=orders.Select(o => o.Id).ToArray() });
        work=TavernWorkProgress.Restore(work.Data,"old-work",15);
        check(!work.RecoveryBlocked && work.Schema==3 && work.Pending.Wage==60 && work.Pending.OrderIds.Length==3
            && work.Recover(15,160)==WageRecovery.Finish && work.Complete(work.Pending) && !work.MayWork(15),
            "saved three-order shifts recover their actual old wage and cannot create another shift today");
        work=TavernWorkProgress.Restore(work.Data,"old-work",15);
        check(!work.RecoveryBlocked && work.IncrementalNet==45 && work.Demand.Single().Orders.Count==6,
            "work migration is idempotent and preserves old claimed order receipts");
    }
}
