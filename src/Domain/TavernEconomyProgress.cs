using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    public sealed class EconomyReceipt
    {
        public string Id, Kind;
        public int Day, Revenue, Cost;
        public bool CashBooked;
    }
    public sealed class EconomyProject
    {
        public int Level, Day, Week;
    }
    public sealed class EconomyContract
    {
        public string Kind;
        public int SignedDay, EffectiveDay, WeeklyOrders, WeeklyRent;
    }
    public sealed class EconomyWeek
    {
        public int Number, EndDay, Natural, Regulars, FixedOrders, Revenue, Material, Fixed, Rent, RentAdvance;
        public int ActiveDays=7, ContractPenalty;
        public long Interest, OneOffFee, Repaid, Cash, Principal, Fees, Bills, Minimum;
        public string OrdersId, SubletId;
    }
    public sealed class EconomyPayment
    {
        public string Id, Phase="prepared";
        public int Day, BeforeCash, Amount;
    }

    // This account owns only the tavern. Native property, credits and customers
    // are read/applied by the adapter, never by this deterministic rules object.
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TavernEconomyProgress
    {
        public const string SaveKey="tavern_economy_v1";
        [JsonProperty] public int Schema=3;
        [JsonProperty] public string Phase="new", PreservedData;
        [JsonProperty] public bool Blocked, LeaseSeen, GoodEndingSeen, DebtFreeSeen;
        [JsonProperty] public bool ReviewAnnounced;
        [JsonProperty] public int CompetitionDay=-1, AdjustmentDay=-1, CrisisDay=-1;
        [JsonProperty] public int ReviewRequestedDay=-1, ReviewBeginDay=-1, PeriodStartDay=-1;
        [JsonProperty] public bool ExtensionRightsFrozen, PacingRecoveryPending;
        [JsonProperty] public int StartDay=-1, NextSettlement=-1, LastDay=-1, OperatingWeeks, Regulars;
        [JsonProperty] public int FullRentFromPeriod=-1;
        [JsonProperty] public int RentStartDay=-1;
        [JsonProperty] public bool PenaltyAssessed;
        [JsonProperty] public int AssessedPenalty, PerformanceRevenue;
        [JsonProperty] public long Cash, Principal, Fees, Bills, OperatingRepaid;
        [JsonProperty] public int FrozenExtensions, ExtensionsUsed, NoticeDay=-1, PaperDay=-1, ResumeDay=-1;
        [JsonProperty] public long FrozenOperatingRepaid;
        [JsonProperty] public List<int> HandlingDays=new List<int>();
        [JsonProperty] public int RefinanceDay=-1, RefinancePeriods, RateBp, PrincipalInstallment;
        [JsonProperty] public long RefinancePrincipal, OverduePrincipal;
        [JsonProperty] public int MissedPeriods, RemedyNoticeDay=-1;
        [JsonProperty] public bool Guaranteed;
        [JsonProperty] public int PlayerReserve=500, GuaranteeDue, GuaranteePaid;
        [JsonProperty] public int StableDay=-1, DebtFreeDay=-1, DepartureDay=-1;
        [JsonProperty] public int FarewellItemId;
        [JsonProperty] public string FarewellPhase="waiting";
        [JsonProperty] public int StayLetterItemId;
        [JsonProperty] public string StayLetterPhase="waiting";
        [JsonProperty] public List<EconomyReceipt> Receipts=new List<EconomyReceipt>();
        [JsonProperty] public List<EconomyProject> Projects=new List<EconomyProject>();
        [JsonProperty] public List<EconomyContract> Contracts=new List<EconomyContract>();
        [JsonProperty] public List<EconomyWeek> Weeks=new List<EconomyWeek>();
        [JsonProperty] public List<string> Payments=new List<string>();
        [JsonProperty] public List<EconomyPayment> PaymentHistory=new List<EconomyPayment>();
        [JsonProperty] public EconomyPayment PendingPayment;

        public bool Active { get { return !Blocked && Phase!="new" && Phase!="migration" && Phase!="legacy"; } }
        public bool Departing { get { return Phase=="leaving" || Phase=="legacy"; } }
        public bool Trading { get { return !Blocked && new[]{"operating","maturity_pending","review_pending","extended","restructured","stable","cleared"}.Contains(Phase); } }
        public bool Reviewing { get { return new[]{"review","decision","remedy"}.Contains(Phase); } }
        public bool ReviewUnlocked { get { return CrisisDay>=0; } }
        public bool HasStayed { get { return StableDay>=0 && (Phase=="stable" || Phase=="cleared"); } }
        public long Debt { get { return Principal+Fees+Bills; } }
        public int Level { get { return Projects.Count; } }
        public long ReserveTarget {
            get {
                long contribution=Receipts.Where(r => r.Day>=RentStartDay && (r.Kind=="onsite" || r.Kind=="overflow_orders" || r.Kind=="brewing_profit"))
                    .Sum(r => Math.Max(0,(long)r.Revenue-r.Cost));
                return TavernEconomyConfig.Reserve+contribution*TavernEconomyConfig.SupportCashRetentionPercent/100;
            }
        }
        public static int MondayOnOrAfter(int day) { return day+(7-(day-1)%7)%7; }
        public int WeekAt(int day) { return StartDay<0 || day<StartDay ? 0 : (day-StartDay)/7+1; }
        public int RentAt(int day)
        {
            // The lease clock is independent of story encounters, operation
            // pauses and the player's property. Round each rise as native rent does.
            long rent=TavernEconomyConfig.Rent;
            int weeks=RentStartDay<0?0:Math.Max(0,(day-RentStartDay)/7);
            for (int i=0;i<weeks;i++) {
                rent=rent*(100+TavernEconomyConfig.RentIncreasePercent)/100;
                if (rent>=1000000000) return 1000000000;
            }
            return (int)rent;
        }

        // These dates come from displayed story visits, never from the calendar
        // week or the count of financial settlements. Missed meetings delay them.
        public bool ObserveChapter(int index,int day)
        {
            if (Blocked || !Active || !StoryRules.IsMonday(day)) return false;
            if (index==10 && CompetitionDay<0) { CompetitionDay=day;return true; }
            if (index==11 && AdjustmentDay<0) { AdjustmentDay=day;return true; }
            if (index==12 && CrisisDay<0) {
                CrisisDay=day;
                if (Phase=="operating" && OperatingWeeks>=TavernEconomyConfig.OldTerm) Phase="maturity_pending";
                return true;
            }
            return false;
        }
        private int NaturalOrders(int end)
        {
            if (CompetitionDay>=0 && end>=CompetitionDay) {
                int stage=CrisisDay>=0 && end>=CrisisDay?2:AdjustmentDay>=0 && end>=AdjustmentDay?1:0;
                return TavernEconomyConfig.CompetitionOrders[stage];
            }
            return TavernEconomyConfig.NaturalOrders[Math.Min(WeekAt(end)-1,TavernEconomyConfig.NaturalOrders.Length-1)];
        }

        public void Initialize(int today,int openingDay,bool legacyFinished,bool legacyInProgress=false)
        {
            if (Phase!="new") return;
            LastDay=today;
            if (legacyFinished) { Phase="legacy";return; }
            if (today>openingDay || today==openingDay && legacyInProgress) { Phase="migration";return; }
            Open(MondayOnOrAfter(openingDay));
        }
        public bool AcceptMigration(int today)
        {
            if (Blocked || Phase!="migration") return false;
            Open(MondayOnOrAfter(today+1));LeaseSeen=true;return true;
        }
        private void Open(int day)
        {
            StartDay=RentStartDay=day;NextSettlement=day+7;Phase="operating";
            Cash=TavernEconomyConfig.InitialCash;Principal=TavernEconomyConfig.InitialPrincipal;
        }
        public bool Record(string id,string kind,int day,int revenue,int cost)
        {
            if (!Active || !Trading || day<Math.Max(StartDay,ResumeDay) || day<PeriodStartDay || day<NextSettlement-7 || day>=NextSettlement
                || string.IsNullOrEmpty(id) || Receipts.Any(r => r.Id==id) || revenue<0 || cost<0
                || revenue>1000000 || cost>1000000) return false;
            Receipts.Add(new EconomyReceipt { Id=id,Kind=kind,Day=day,Revenue=revenue,Cost=cost });return true;
        }
        public bool ImportBrewingProfit(BrewingBatch batch)
        {
            if (!Active || Departing || batch==null || batch.Phase!="complete" || !batch.Credited
                || batch.ProfitDay<StartDay || batch.TavernProfit<=0) return false;
            string id="brewing-profit:"+batch.Id;
            if (Receipts.Any(r => r.Id==id)) return false;
            // Only the frozen net margin is cash. Materials and the player's
            // wine return are never counted as the tavern's sales or cash.
            Cash+=batch.TavernProfit;
            Receipts.Add(new EconomyReceipt { Id=id,Kind="brewing_profit",Day=batch.ProfitDay,
                Revenue=batch.TavernProfit,CashBooked=true });return true;
        }
        public bool MayProject(int day)
        {
            return Trading && Level<4 && day>=Math.Max(StartDay,ResumeDay) && WeekAt(day)>=TavernEconomyConfig.ProjectEarliest[Level]
                && !Projects.Any(p => p.Week==WeekAt(day));
        }
        public bool FinishProject(int day)
        {
            if (!MayProject(day)) return false;
            Projects.Add(new EconomyProject { Level=Level+1,Day=day,Week=WeekAt(day) });return true;
        }
        public bool ProjectTonight(int day) { return Projects.Any(p => p.Day==day); }
        public bool ContractReady(string kind,int day)
        { return Contracts.Any(c => c.Kind==kind && c.EffectiveDay<=day); }
        public bool SignContract(string kind,int day)
        {
            if (Blocked || !Active || !ReviewUnlocked || day<CrisisDay || Departing || RefinanceDay>=0 || day<StartDay || day==PaperDay
                || kind!="orders" && kind!="sublet" || Contracts.Any(c => c.Kind==kind)) return false;
            // Separate signatures, counterparties and next-day receipts. They do
            // not create any income while the shop is under financial review.
            Contracts.Add(new EconomyContract { Kind=kind,SignedDay=day,EffectiveDay=day+1,
                WeeklyOrders=kind=="orders"?TavernEconomyConfig.ContractOrders:0,
                WeeklyRent=kind=="sublet"?TavernEconomyConfig.SubletRent:0 });PaperDay=day;return true;
        }
        public bool Acknowledge(int day)
        {
            if (Blocked || PendingPayment!=null || NoticeDay>=0) return false;
            if (Trading && RefinanceDay<0 && ReviewUnlocked && day>=CrisisDay && day<NextSettlement) {
                // An explicit request finishes this week's real transactions
                // before pausing. Opening the ledger cannot close the shop.
                ReviewRequestedDay=NoticeDay=day;Phase="review_pending";HandlingDays.Clear();return true;
            }
            if (!Reviewing) return false;
            NoticeDay=ReviewBeginDay=day;HandlingDays.Clear();return true;
        }
        public bool HandlingDay(int day)
        {
            if (!Reviewing || NoticeDay<0 || day<=Math.Max(NoticeDay,ReviewBeginDay) || HandlingDays.Contains(day)) return false;
            HandlingDays.Add(day);
            if (HandlingDays.Count>=7 && Phase=="review") Phase="decision";
            return true;
        }
        public bool Extend(int day)
        {
            if (Blocked || PendingPayment!=null || !Reviewing || NoticeDay<0 || RefinanceDay>=0
                || ExtensionsUsed>=FrozenExtensions) return false;
            Fees+=TavernEconomyConfig.ExtensionFees[ExtensionsUsed++];
            ResumeDay=MondayOnOrAfter(day+1);NextSettlement=ResumeDay+7;Phase="extension_wait";
            NoticeDay=ReviewRequestedDay=ReviewBeginDay=-1;HandlingDays.Clear();return true;
        }
        public long RefinanceQuote
        { get { return Debt+Math.Max(0,TavernEconomyConfig.Reserve-Cash)+TavernEconomyConfig.PartitionCost; } }
        public static long Minimum(long principal,int installment,int rateBp)
        { return Math.Min(principal,installment)+Up(principal*rateBp,10000); }
        public static long Up(long amount,long divisor) { return (amount+divisor-1)/divisor; }
        public static int AllocatedUnitCost(int total,int produced,int ordinal)
        {
            if (total<0 || produced<=0 || ordinal<0 || ordinal>=produced) throw new ArgumentOutOfRangeException();
            return (int)((long)total*(ordinal+1)/produced-(long)total*ordinal/produced);
        }
        public int ContractOrders { get { return Contracts.Where(c => c.Kind=="orders").Sum(c => c.WeeklyOrders); } }
        public int SubletRent { get { return Contracts.Where(c => c.Kind=="sublet").Sum(c => c.WeeklyRent); } }
        public long ContractOperatingCash(int day)
        {
            int orders=Contracts.Any(c => c.Kind=="orders")?ContractOrders:TavernEconomyConfig.ContractOrders;
            int sublet=Contracts.Any(c => c.Kind=="sublet")?SubletRent:TavernEconomyConfig.SubletRent;
            return (long)(TavernEconomyConfig.OrderPrice-TavernEconomyConfig.OrderMaterial)*orders
                    -TavernEconomyConfig.StableFixedCost-RentAt(day)+sublet;
        }
        public bool Refinance(int day,bool guarantee,bool ownsNativeProperty)
        {
            if (Blocked || PendingPayment!=null || !Reviewing || NoticeDay<0 || RefinanceDay>=0
                || !ContractReady("orders",day) || !ContractReady("sublet",day)
                || guarantee && !ownsNativeProperty) return false;
            long quote=RefinanceQuote;
            int rate=guarantee?TavernEconomyConfig.GuaranteedRateBp:TavernEconomyConfig.NewRateBp;
            int installment=(int)Up(quote,TavernEconomyConfig.NewTerm);
            if (Minimum(quote,installment,rate)>ContractOperatingCash(MondayOnOrAfter(day))) return false;
            Principal=RefinancePrincipal=quote;Fees=Bills=OverduePrincipal=0;Cash=Math.Max(Cash,TavernEconomyConfig.Reserve);
            RefinanceDay=day;RateBp=rate;PrincipalInstallment=installment;Guaranteed=guarantee;
            DebtFreeDay=-1;DebtFreeSeen=false;
            // Signing while closed on Monday can reopen for that entire week;
            // otherwise begin next Monday. No partial week earns full orders.
            NextSettlement=MondayOnOrAfter(day)+7;ResumeDay=NextSettlement-7;
            PeriodStartDay=ReviewRequestedDay=ReviewBeginDay=-1;
            Phase="restructured";NoticeDay=-1;HandlingDays.Clear();return true;
        }
        public bool UpgradeGuarantee(int day,bool ownsNativeProperty)
        {
            if (!Trading || RefinanceDay<0 || Guaranteed || !ownsNativeProperty || Principal<=0
                || !StoryRules.IsMonday(day) || PendingPayment!=null) return false;
            Guaranteed=true;RateBp=TavernEconomyConfig.GuaranteedRateBp;
            // Never reset the installment, remaining term, or past interest.
            return true;
        }
        public bool StopRentAdvances()
        {
            if (!Trading || RefinanceDay>=0 || FullRentFromPeriod>=0 || OperatingWeeks>=7) return false;
            FullRentFromPeriod=OperatingWeeks+2;return true;
        }
        public bool ResumeAfterRemedy(int day)
        {
            if (Blocked || Phase!="remedy" || Bills>0 || Fees>0 || OverduePrincipal>0 || PendingPayment!=null) return false;
            MissedPeriods=0;NoticeDay=-1;HandlingDays.Clear();ResumeDay=MondayOnOrAfter(day+1);
            NextSettlement=ResumeDay+7;Phase=StableDay>=0?"stable":"restructured";return true;
        }
        public bool Advance(int today,bool stress=false)
        {
            if (Blocked || today<LastDay || PendingPayment!=null) return false;
            bool changed=today!=LastDay;LastDay=today;
            if (PacingRecoveryPending) {
                // Old v1 stopped automatically after ten periods. Preserve all
                // settled amounts and resume today, without inventing closed-day
                // sales or retroactively refunding historical debt.
                ResumeDay=PeriodStartDay=today;NextSettlement=MondayOnOrAfter(today+1);
                PacingRecoveryPending=false;changed=true;
            }
            if (Phase=="extension_wait" && today>=ResumeDay) { Phase="extended";changed=true; }
            while (Trading && today>=NextSettlement)
            {
                int end=NextSettlement-1;
                Settle(end,stress);NextSettlement+=7;PeriodStartDay=-1;changed=true;
                if (RefinanceDay<0 && (Phase=="extended" || ReviewRequestedDay>=0 && end>=ReviewRequestedDay))
                {
                    bool extended=Phase=="extended";
                    Phase="review";ReviewBeginDay=end+1;HandlingDays.Clear();
                    if (extended) { NoticeDay=-1;ReviewAnnounced=false; }
                    if (!ExtensionRightsFrozen) {
                        FrozenOperatingRepaid=OperatingRepaid;
                        FrozenExtensions=TavernEconomyConfig.ExtensionThresholds.Count(n => OperatingRepaid>=n);
                        ExtensionRightsFrozen=true;
                    }
                }
                else if (RefinanceDay<0 && OperatingWeeks>=TavernEconomyConfig.OldTerm && ReviewUnlocked)
                    Phase="maturity_pending";
            }
            return changed;
        }
        private void Settle(int end,bool stress)
        {
            int period=OperatingWeeks+1;
            int days=PeriodStartDay<0?7:end-PeriodStartDay+1;
            int level=Projects.Count(p => p.Day<=end);
            Regulars=Math.Min(TavernEconomyConfig.ProjectCapacity[level],Regulars+TavernEconomyConfig.ProjectGrowth[level]*days/7);
            bool reorganized=RefinanceDay>=0;
            int natural=stress?0:(reorganized?2:NaturalOrders(end))*days/7;
            int regular=stress?0:Regulars*days/7;
            int fixedOrders=reorganized?ContractOrders:0;
            int count=natural+regular+fixedOrders;
            var receipts=Receipts.Where(r => r.Day>=NextSettlement-7 && r.Day<=end).ToArray();
            int revenue=count*TavernEconomyConfig.OrderPrice+receipts.Sum(r => r.Revenue)+(reorganized?SubletRent:0);
            int material=count*TavernEconomyConfig.OrderMaterial+receipts.Sum(r => r.Cost);
            int fixedCost=(int)Up((reorganized?TavernEconomyConfig.StableFixedCost:TavernEconomyConfig.FixedCost)*days,7);
            int faceRent=RentAt(end);
            int rent=reorganized || period>7 || FullRentFromPeriod>0 && period>=FullRentFromPeriod?faceRent:period<=5?90:260;
            rent=(int)Up((long)rent*days,7);
            int rentAdvance=(int)Up((long)faceRent*days,7)-rent;
            long interest=Up(Principal*(reorganized?RateBp:TavernEconomyConfig.OldRateBp)*days,70000);
            long oneOff=!reorganized && period==6?TavernEconomyConfig.TransferFee:0;
            long minimum=reorganized?Math.Min(Principal,PrincipalInstallment)+interest:0;
            long principalDue=reorganized?Math.Min(Principal,PrincipalInstallment):0;
            long beforePrincipal=Principal;
            Principal+=rentAdvance;
            Fees+=interest+oneOff;
            long operating=Cash+revenue-receipts.Where(r => r.CashBooked).Sum(r => r.Revenue)-material-fixedCost-rent-Bills;
            Cash=Math.Max(0,operating);Bills=Math.Max(0,-operating);
            int penalty=0;
            if (!PenaltyAssessed && !reorganized && CrisisDay>=0 && end>=CrisisDay+6) {
                PenaltyAssessed=true;
                // The last three operating weeks are assessed only after the
                // twelfth displayed visit. Genuine sales can satisfy the target;
                // gifts of materials and rent income cannot.
                PerformanceRevenue=Weeks.Where(w => w.EndDay>end-21).Sum(w => (w.Natural+w.Regulars)*TavernEconomyConfig.OrderPrice)
                    +(natural+regular)*TavernEconomyConfig.OrderPrice
                    +Receipts.Where(r => r.Day>end-21 && r.Day<=end && (r.Kind=="onsite" || r.Kind=="overflow_orders")).Sum(r => r.Revenue);
                if (PerformanceRevenue<TavernEconomyConfig.PerformanceTarget) penalty=TavernEconomyConfig.ContractPenalty;
                AssessedPenalty=penalty;
            }
            long penaltyPaid=Math.Min(Cash,penalty);Cash-=penaltyPaid;Fees+=penalty-penaltyPaid;
            long sweep=Math.Min(Math.Max(0,Cash-ReserveTarget),Principal+Fees);
            Cash-=sweep;PayDebt(sweep);OperatingRepaid+=sweep+penaltyPaid;
            var row=new EconomyWeek { Number=period,EndDay=end,ActiveDays=days,Natural=natural,Regulars=regular,FixedOrders=fixedOrders,
                Revenue=revenue,Material=material,Fixed=fixedCost,Rent=rent,RentAdvance=rentAdvance,
                Interest=interest,OneOffFee=oneOff+penalty,ContractPenalty=penalty,Repaid=sweep+penaltyPaid,Cash=Cash,Principal=Principal,Fees=Fees,Bills=Bills,Minimum=minimum,
                OrdersId=reorganized?"dock-teams:"+end:"natural-and-regular:"+end,
                SubletId=reorganized?"repair-room:"+end:"" };
            Weeks.Add(row);OperatingWeeks++;
            if (reorganized) {
                RefinancePeriods++;
                long paidPrincipal=beforePrincipal-Principal;
                OverduePrincipal=Math.Max(0,OverduePrincipal+principalDue-paidPrincipal);
                MissedPeriods=sweep<minimum?MissedPeriods+1:0;
                if (MissedPeriods>=2 || OverduePrincipal>PrincipalInstallment || Bills>0) {
                    Phase="remedy";NoticeDay=-1;ReviewAnnounced=false;HandlingDays.Clear();
                }
                else if (fixedOrders>0 && Cash>=TavernEconomyConfig.Reserve && sweep>=minimum) {
                    if (StableDay<0) StableDay=end;Phase=Debt==0?"cleared":"stable";
                }
            }
            if (Debt==0 && RefinanceDay>=0) {
                if (DebtFreeDay<0) DebtFreeDay=end;
                if (StableDay>=0) Phase="cleared";
            }
        }
        private void PayDebt(long amount)
        {
            long fee=Math.Min(Fees,amount);Fees-=fee;amount-=fee;
            long principal=Math.Min(Principal,amount);Principal-=principal;
        }
        public EconomyPayment PreparePayment(int day,int nativeCash,int amount)
        {
            if (Blocked || !Active || Departing && GuaranteeDue<=0 || PendingPayment!=null || amount<=0 || nativeCash<amount || Debt<=0) return null;
            int actual=(int)Math.Min(amount,Departing?Math.Min(Debt,GuaranteeDue):Debt);
            var payment=new EconomyPayment { Id="repay:"+day+":"+Payments.Count,Day=day,BeforeCash=nativeCash,Amount=actual };
            PendingPayment=payment;return payment;
        }
        public bool ConfirmPayment(int nativeCash)
        {
            var p=PendingPayment;
            if (p==null || p.Phase!="prepared" || nativeCash!=(long)p.BeforeCash-p.Amount || Payments.Contains(p.Id)) return false;
            int guaranteePaid=Math.Min(GuaranteeDue,p.Amount);GuaranteeDue-=guaranteePaid;GuaranteePaid+=guaranteePaid;
            long bills=Math.Min(Bills,p.Amount);Bills-=bills;PayDebt(p.Amount-bills);
            OverduePrincipal=Math.Min(OverduePrincipal,Principal);
            Payments.Add(p.Id);p.Phase="complete";PaymentHistory.Add(p);PendingPayment=null;
            if (Debt==0 && DebtFreeDay<0) DebtFreeDay=p.Day;
            if (Debt==0 && HasStayed) Phase="cleared";
            // A voluntary player payment never creates operating extension rights.
            return true;
        }
        public void RecoverPayment(int day,int nativeCash)
        {
            if (PendingPayment==null) return;
            if (ConfirmPayment(nativeCash)) return;
            if (PendingPayment.Day==day && nativeCash==PendingPayment.BeforeCash) PendingPayment=null;
            else { PendingPayment.Phase="uncertain";Blocked=true; }
        }
        public bool Leave(int day)
        {
            if (Blocked || PendingPayment!=null || !Reviewing || NoticeDay<0 || Phase=="remedy" && HandlingDays.Count<14) return false;
            // Equipment recovery 2160 + pledged deposit 800 - removal 500. No
            // cash goes to the player. Any residual liability remains visible.
            long funds=Cash+2460, bills=Math.Min(Bills,funds);Bills-=bills;funds-=bills;
            long repayment=Math.Min(funds,Principal+Fees);PayDebt(repayment);funds-=repayment;
            Cash=funds;DepartureDay=day;
            if (Phase=="remedy" && Guaranteed && HandlingDays.Count>=14) GuaranteeDue=(int)Math.Min(Debt,TavernEconomyConfig.GuaranteeLimit-GuaranteePaid);
            Phase="leaving";return true;
        }
        public string ChapterBranch { get { return Departing?"leave":HasStayed?"stay":"review"; } }
        public string Data { get { return JsonConvert.SerializeObject(this); } }
        public static TavernEconomyProgress Restore(string data)
        {
            if (string.IsNullOrEmpty(data)) return new TavernEconomyProgress();
            try {
                var p=JsonConvert.DeserializeObject<TavernEconomyProgress>(data);
                if (p==null || p.Schema<1 || p.Schema>3 || p.Receipts==null || p.Projects==null || p.Contracts==null || p.Weeks==null
                    || p.Payments==null || p.PaymentHistory==null || p.HandlingDays==null || p.OperatingWeeks!=p.Weeks.Count
                    || !new[]{"new","migration","legacy","operating","maturity_pending","review_pending","review","decision","extension_wait","extended","restructured","stable","cleared","remedy","leaving"}.Contains(p.Phase)
                    || p.Cash<0 || p.Principal<0 || p.Fees<0 || p.Bills<0 || p.OverduePrincipal<0
                    || p.Debt>1000000000 || p.Cash>1000000000 || p.Projects.Count>4
                    || p.PlayerReserve<0 || p.GuaranteeDue<0 || p.GuaranteePaid<0 || p.GuaranteeDue+p.GuaranteePaid>TavernEconomyConfig.GuaranteeLimit
                    || p.Receipts.Any(r => r==null || string.IsNullOrEmpty(r.Id) || r.Revenue<0 || r.Cost<0)
                    || p.Receipts.GroupBy(r => r.Id).Any(g => g.Count()>1)
                    || p.Contracts.Any(c => c==null || c.Kind!="orders" && c.Kind!="sublet" || c.EffectiveDay!=c.SignedDay+1 || c.WeeklyOrders<0 || c.WeeklyRent<0)
                    || p.Contracts.GroupBy(c => c.Kind).Any(g => g.Count()>1)
                    || p.Projects.Where((r,i) => r==null || r.Level!=i+1).Any()
                    || p.Weeks.Any(w => w==null || w.Cash<0 || w.Principal<0 || w.Fees<0 || w.Bills<0 || w.ActiveDays<1 || w.ActiveDays>7)
                    || p.Payments.Distinct().Count()!=p.Payments.Count
                    || p.PaymentHistory.Count!=p.Payments.Count || p.PaymentHistory.Any(r => r==null || r.Amount<=0 || r.Phase!="complete" || !p.Payments.Contains(r.Id))
                    || p.PaymentHistory.Select(r => r.Id).Distinct().Count()!=p.Payments.Count
                    || p.FrozenExtensions<0 || p.FrozenExtensions>2 || p.ExtensionsUsed<0 || p.ExtensionsUsed>p.FrozenExtensions
                    || p.RefinanceDay>=0 && (p.RateBp!=TavernEconomyConfig.NewRateBp && p.RateBp!=TavernEconomyConfig.GuaranteedRateBp
                        || p.PrincipalInstallment<=0 || p.RefinancePrincipal<=0)
                    || p.Active && (p.StartDay<1 || !StoryRules.IsMonday(p.StartDay) || p.NextSettlement<=p.StartDay)
                    || p.PeriodStartDay>=0 && (p.PeriodStartDay<p.NextSettlement-7 || p.PeriodStartDay>=p.NextSettlement)
                    || new[]{p.CompetitionDay,p.AdjustmentDay,p.CrisisDay}.Any(d => d!=-1 && (d<1 || !StoryRules.IsMonday(d)))
                    || p.PendingPayment!=null && (p.PendingPayment.Amount<=0 || p.PendingPayment.BeforeCash<p.PendingPayment.Amount))
                    return Invalid(data);
                if (p.Schema==1) {
                    if (!p.Blocked && p.RefinanceDay<0 && p.ExtensionsUsed==0 && (p.Phase=="review" || p.Phase=="decision")) {
                        p.Phase="operating";p.NoticeDay=p.ReviewRequestedDay=p.ReviewBeginDay=-1;
                        p.ReviewAnnounced=false;p.PacingRecoveryPending=true;
                    }
                    p.ExtensionRightsFrozen=p.OperatingWeeks>=10;
                    p.Schema=2;
                }
                if (p.Schema==2) {
                    // Start the new escalator prospectively; settled rows, cash,
                    // old contracts and completed departure choices remain frozen.
                    p.RentStartDay=p.StartDay<0?-1:MondayOnOrAfter(Math.Max(p.StartDay,p.LastDay));
                    p.PenaltyAssessed=p.CrisisDay>=0 || p.OperatingWeeks>=10 || p.RefinanceDay>=0 || p.Departing;
                    foreach (var c in p.Contracts) {
                        c.WeeklyOrders=c.Kind=="orders"?20:0;c.WeeklyRent=c.Kind=="sublet"?240:0;
                    }
                    p.Schema=3;
                }
                if (p.Active && (p.RentStartDay<1 || !StoryRules.IsMonday(p.RentStartDay))
                    || p.AssessedPenalty<0 || p.PerformanceRevenue<0
                    || p.Contracts.Any(c => c.Kind=="orders" ? c.WeeklyOrders<1 || c.WeeklyOrders>10000 || c.WeeklyRent!=0
                        : c.WeeklyRent<1 || c.WeeklyRent>1000000 || c.WeeklyOrders!=0)) return Invalid(data);
                return p;
            } catch (JsonException) { return Invalid(data); }
        }
        private static TavernEconomyProgress Invalid(string data)
        { return new TavernEconomyProgress { Blocked=true,Phase="review",PreservedData=data }; }
    }
}
