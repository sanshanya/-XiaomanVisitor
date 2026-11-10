using System.Collections.Generic;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed class CompanyMaterialLot
    {
        public string Id,Key;
        public int Day,WaterFromPlayer,JuiceFromPlayer,WaterPurchased,JuicePurchased,RarePaid,CommonPaid;
        public int WaterUsed,JuiceUsed,WaterUnitCost,JuiceUnitCost;
        public int Paid { get { return RarePaid+CommonPaid; } }
    }
    public sealed partial class SupplyProgress
    {
        public List<CompanyMaterialLot> CompanyLots=new List<CompanyMaterialLot>();
        public bool TrialSoldOut, UpperPreviewOrders;
        public int TrialDay=-1;
        public bool LuxuryIntroSeen;

        public bool RecordPreview(int day)
        {
            if (Blocked || day<LuxuryConfig.PreviewDay || TrialSoldOut) return false;
            // Narrative facts only. No fabricated NPC sale, revenue or ingredient lot.
            TrialSoldOut=true;UpperPreviewOrders=true;TrialDay=LuxuryConfig.PreviewDay;return true;
        }
        public int ProduceLuxury(int day,bool channel)
        {
            if (Blocked || Pending!=null || day<LuxuryConfig.UnlockDay || !channel) return 0;
            int made=0;
            foreach (var spec in LuxuryDrinkCatalog.Standard) {
                string id="company_luxury:"+Week(day)+":"+spec.Key;
                if (CompanyLots.Any(l => l.Id==id)) continue;
                int playerWater=System.Math.Min(Water,LuxuryConfig.Water), playerJuice=System.Math.Min(Juice,LuxuryConfig.Juice);
                int missingWater=LuxuryConfig.Water-playerWater, missingJuice=LuxuryConfig.Juice-playerJuice;
                int common=missingWater*SupplyConfig.WaterPrice+missingJuice*SupplyConfig.JuicePrice;
                int paid=LuxuryConfig.RareCost+common+LuxuryConfig.MakingCost;
                if (Cash<paid) continue;
                Cash-=paid;Water-=playerWater;Juice-=playerJuice;
                CompanyLots.Add(new CompanyMaterialLot { Id=id,Key=spec.Key,Day=day,WaterFromPlayer=playerWater,JuiceFromPlayer=playerJuice,
                    WaterPurchased=missingWater,JuicePurchased=missingJuice,RarePaid=LuxuryConfig.RareCost,CommonPaid=common,
                    WaterUsed=LuxuryConfig.Water,JuiceUsed=LuxuryConfig.Juice,WaterUnitCost=SupplyConfig.WaterPrice,JuiceUnitCost=SupplyConfig.JuicePrice });
                int partner=SignedBy(day)?LuxuryConfig.Output*SupplyConfig.PlayerNumerator/SupplyConfig.Denominator:0;
                Batches.Add(new SupplyBatch { Id=id,CompanyLot=id,Key=spec.Key,Day=day,Produced=LuxuryConfig.Output,
                    PlayerInitial=partner,StoreInitial=LuxuryConfig.Output-partner,WholesalePrice=LuxuryConfig.Wholesale,
                    IngredientCost=LuxuryConfig.RareCost+LuxuryConfig.Water*SupplyConfig.WaterPrice+LuxuryConfig.Juice*SupplyConfig.JuicePrice,
                    MakingCost=LuxuryConfig.MakingCost,MaterialSource=playerWater+playerJuice>0?"company+player":"company" });
                made++;
            }
            return made;
        }
        private bool ValidLuxury()
        {
            return CompanyLots!=null && CompanyLots.All(l => l!=null && l.Id=="company_luxury:"+Week(l.Day)+":"+l.Key
                && LuxuryDrinkCatalog.IsStandard(l.Key) && l.Day>=LuxuryConfig.UnlockDay && l.WaterFromPlayer>=0 && l.JuiceFromPlayer>=0
                && l.WaterPurchased>=0 && l.JuicePurchased>=0 && l.WaterUsed>0 && l.JuiceUsed>0 && l.WaterUnitCost>0 && l.JuiceUnitCost>0
                && (long)l.WaterFromPlayer+l.WaterPurchased==l.WaterUsed && (long)l.JuiceFromPlayer+l.JuicePurchased==l.JuiceUsed
                && l.CommonPaid==(long)l.WaterPurchased*l.WaterUnitCost+(long)l.JuicePurchased*l.JuiceUnitCost && l.RarePaid>0)
                && CompanyLots.GroupBy(l => l.Id).All(g => g.Count()==1)
                && Batches.All(b => b!=null && b.WholesalePrice>0 && CocktailCatalog.Find(b.Key)!=null && b.Key!=LuxuryDrinkCatalog.Improv.Key
                    && (!LuxuryDrinkCatalog.IsStandard(b.Key) || CompanyLots.Count(l => l.Id==b.CompanyLot && l.Id==b.Id && l.Key==b.Key)==1))
                && CompanyLots.All(l => Batches.Count(b => b.Id==l.Id && b.CompanyLot==l.Id)==1);
        }
    }
}
