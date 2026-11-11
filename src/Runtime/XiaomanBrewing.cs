using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private float nextBrewingTick;
        private float nextBrewingDelivery;
        private bool brewingBusy;
        private string BrewingTerms { get { return Text("ui_brew_terms",BrewingConfig.BonusPercent,BrewingConfig.BottleValue,BrewingConfig.SampleValue); } }
        private BrewingProgress ReadBrewing() { return BrewingProgress.Restore(ModHelper.GetModData(Id, BrewingProgress.SaveKey, "")); }
        private void SaveBrewing(BrewingProgress p)
        {
            string raw=p.Data; ModHelper.SetModData(Id,BrewingProgress.SaveKey,raw);
            if (ModHelper.GetModData(Id,BrewingProgress.SaveKey,"")!=raw) throw new InvalidOperationException("Brewing receipt was not saved");
        }
        private bool MayCollectBrewing
        { get { return TavernMapEnabled && TavernTrading && StoreStation.Instance.dayCounter>=BrewingConfig.UnlockDay
            && (ReadStory().VisitIndex>=4 || ReadSupply().Signed); } }
        private bool BrewingVisitDue(int day)
        {
            return TavernMapEnabled && !ReadStory().Finished && (ReadStory().VisitIndex>=4 || ReadSupply().Signed)
                && ReadBrewing().MayVisit(day,MayCollectBrewing);
        }
        private static bool BrewingTarget()
        { return PlayerStore.HasInstance && PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()?.identifier==BrewingProgress.ClientId; }

        internal static long BrewingMaterialCents(GameItem item)
        {
            if (item==null || item.unitCount<=0 || !GeneralHelper.IsItemOwned(item) || ContrabandHelper.IsContraband(item)
                || item.IsTag("ALCOHOL") || item.IsTag("CHEMICAL_PRODUCT_TAG") || item.FindAllChildItems().Any()) return 0;
            decimal value;
            if (WineHelper.IsWaterRation(item)) {
                value=(item.identifier=="water_ration_small"?0.375m:0.750m)*WaterFeatureHelper.BASE_WATER_PRICE_PER_LITER*item.unitCount;
            } else if (IsSafeDrinkingWater(item)) {
                int tier=WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(item));
                value=(decimal)WaterHelper.GetTotalVolume(item)*WaterFeatureHelper.WATER_PRICES[tier]/1000000m*item.unitCount;
            } else {
                if ((!item.IsTag(WineHelper.WINE_INGREDIENT_TAG) && !item.IsTag(WineHelper.WINE_YEAST_TAG))
                    || item.IsTag(WineHelper.WINE_YEAST_REUSABLE_TAG)) return 0;
                long quality=item.itemFeatures.Where(f => !f.isDisabled && f.GetValueStage()==ItemFeature.ValueStage.Innate)
                    .Sum(f => (long)f.GetActualValueModifier());
                value=(decimal)item.unitValue*Math.Max(0L,100L+quality)/100m*item.unitCount;
            }
            return value<=0 || value>long.MaxValue/100m ? 0 : (long)decimal.Floor(value*100m);
        }
        private List<BrewingMaterial> SelectedBrewingMaterials()
        {
            var items=EmporiumEntry.Instance.backInvinvElement.childItems;
            if (items.Count==0) return null;
            var result=new List<BrewingMaterial>();
            foreach (var item in items) {
                long cents=BrewingMaterialCents(item);
                if (cents<=0) return null;
                result.Add(new BrewingMaterial { ItemId=item.GetUniqueID(),Key=item.identifier,Count=item.unitCount,ValueCents=cents });
            }
            return result;
        }
        private string BrewingQuote(List<BrewingMaterial> materials)
        {
            if (materials==null) return Text("xm_brew_wrong");
            decimal value=materials.Sum(m => (decimal)m.ValueCents)/100m;
            return Text("ui_brew_quote",value.ToString("0.##"),(value*(100+BrewingConfig.BonusPercent)/100m).ToString("0.##"));
        }
        private StoreClient CreateBrewingVisitor()
        {
            var c=new StoreClient { identifier=BrewingProgress.ClientId,displayName=Text("name"),realName=Text("name"),spriteName=SpriteKey,
                clientFaction=StoreClient.FACTION_LOWER,clientIntent=MayCollectBrewing?StoreClient.ClientIntent.BARTER:StoreClient.ClientIntent.DIALOGUE,cooldown=0,
                arrestable=false,nonShootable=true,noAcceptingContraband=true };
            ConfigureGunReaction(c);
            c.barterOffer=new BarterOffer { ownerClient=c,barterTextBody=Text("ui_brew_table"),
                onInstance=() => { c.isIntroduced=true; },
                getBarterResult=() => {
                    var goods=SelectedBrewingMaterials();
                    var p=ReadBrewing();
                    bool valid=MayCollectBrewing && goods!=null && p.Pending==null && !p.Blocked
                        && !p.Batches.Any(b => b.Day==StoreStation.Instance.dayCounter && b.Phase!="cancelled");
                    return new BarterResult { isAccepted=valid,response=valid?BrewingQuote(goods):Text("xm_brew_wrong") };
                } };
            c.willAcceptItem=item => BrewingMaterialCents(item)>0;
            c.mainDialogue=Line(c,MayCollectBrewing?"xm_brew_hello":"xm_brew_last",MayCollectBrewing?Mood.Smile:Mood.Soft);
            var request=MayCollectBrewing?Append(c.mainDialogue,c,"xm_brew_request",Mood.Soft):c.mainDialogue;
            c.mainDialogue.startAction+=() => {
                var p=ReadBrewing(); if (p.Arrive(StoreStation.Instance.dayCounter)) SaveBrewing(p);
                TryDeliverBrewing();
            };
            request.SetEndAction(() => {
                c.OnIntroduced();
                if (c.clientIntent==StoreClient.ClientIntent.BARTER) {
                    c.barterOffer.InstanciateOffer();
                    NegociationUIManager.Instance.OpenUI();
                }
            });
            c.acceptDealDialogue=Line(c,"xm_brew_accept",Mood.Smile);
            c.repeatDialogue=Line(c,MayCollectBrewing?"xm_brew_request":"xm_brew_last",Mood.Soft);
            c.placedWrongItemWhenSellingToDialogue=Line(c,"xm_brew_wrong",Mood.Soft);
            c.allDoneDialogue=Line(c,"xm_brew_bye",Mood.Soft).SetEndAction(() => PlayerStore.Instance.DismissCurrentClient(false));
            c.OnArrival=() => SetMood(c,Mood.Neutral);
            c.OnDismiss=() => { Voice?.Stop();ResetGunReaction();SetMood(c,Mood.Neutral); };
            c.CompleteClientCreation();return c;
        }
        internal bool BeginBrewingExchange()
        {
            if (!BrewingTarget()) return true;
            if (!MayCollectBrewing || brewingBusy) return false;
            try {
                var materials=SelectedBrewingMaterials();var p=ReadBrewing();
                var batch=p.Prepare(WorkRunIdentity,StoreStation.Instance.dayCounter,materials);
                if (batch==null) return false;
                SaveBrewing(p);return true;
            } catch (Exception e) { Log.Warning("Brewing exchange held: "+e.Message);return false; }
        }
        internal void CompleteBrewingExchange()
        {
            if (!BrewingTarget()) return;
            RecoverBrewingInput();
        }
        private void RecoverBrewingInput()
        {
            var p=ReadBrewing();
            if (p.Pending!=null && p.RecoverInput(EmporiumEntry.Instance.GetAllOwnedItems().Select(i => i.uniqueId))) SaveBrewing(p);
        }
        internal void TickBrewing()
        {
            if (!enabled || !TavernMapEnabled || !PlayerStore.HasInstance || EmporiumEntry.Instance==null || StoreStation.Instance==null
                || ModHelper.IsOutsideStore() || brewingBusy || Time.unscaledTime<nextBrewingTick) return;
            nextBrewingTick=Time.unscaledTime+1f;
            RecoverBrewingInput();
            if (BrewingTarget() || ReadStory().Finished) TryDeliverBrewing();
            if (PlayerStore.Instance.storeState==PlayerStore.StoreState.OPEN && BrewingProgress.Wednesday(StoreStation.Instance.dayCounter))
                Queue(PlayerStore.Instance.storeClientManager);
        }
        private void TryDeliverBrewing()
        {
            if (brewingBusy || !TavernMapEnabled || !PlayerStore.HasInstance || ModHelper.IsOutsideStore() || Time.unscaledTime<nextBrewingDelivery) return;
            brewingBusy=true;
            try {
                var p=ReadBrewing();if (p.Blocked) return;
                bool final=ReadStory().Finished;
                long before=p.CreditMilli;
                p.CreditDue(StoreStation.Instance.dayCounter,final);
                bool recovering=p.Placing!=null;
                if (recovering) p.RecoverBottle(EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i.uniqueId==p.Placing.ItemId));
                if (p.CreditMilli!=before || recovering) SaveBrewing(p);
                // Bound placement work per tick; a full table keeps the remaining value.
                for (int n=0;n<3 && !p.Blocked && p.CreditMilli>=BrewingConfig.SampleValue*1000L;n++) {
                    var item=ModHelper.SpawnItem(p.CreditMilli>=BrewingConfig.BottleValue*1000L?BrewingProgress.BottleId:BrewingProgress.SampleId);
                    if (item==null) return;
                    var bottle=p.PrepareBottle(item.GetUniqueID());if (bottle==null) { item.Destroy();return; }
                    item.ModifyTag("XIAOMAN_BATCH_SOURCE",t => t.Enable().SetString(bottle.Id));
                    SaveBrewing(p);
                    bool placed=TryPlaceGift(item);p.BottlePlaced(placed);SaveBrewing(p);
                    if (!placed) { nextBrewingDelivery=Time.unscaledTime+5f;return; }
                }
            } catch (Exception e) { Log.Warning("Brewing gift remains pending: "+e.Message); }
            finally { brewingBusy=false; }
        }
        private void RegisterBrewingItems(ModItemDirectory directory)
        {
            foreach (string id in new[]{BrewingProgress.SampleId,BrewingProgress.BottleId}) {
                string key=id;if (!directory.Has(key)) directory.Add(key,() => CreateBrewingBottle(key));
            }
        }
        private GameItem CreateBrewingBottle(string id)
        {
            bool small=id==BrewingProgress.SampleId;
            var model=DirectoryMaster.Item(small?"mini_bottle":"wine_bottle");
            var item=ItemDirectory.CreateEmptyItem(id).SetSpriteAndShape(model.spriteAtlasPath,model.spritePath);
            model.Destroy();
            item.name=Text(small?"ui_brew_sample_name":"ui_brew_bottle_name");
            item.shortDescription=Text(small?"ui_brew_sample_desc":"ui_brew_bottle_desc");
            item.unitValue=small?BrewingConfig.SampleValue:BrewingConfig.BottleValue;
            item.SetGameItemType("SUBSTANCE");item.SetGameItemType("ALCOHOL");
            AudioHelper.InitGlass(item);ConsumableHelper.InitAlcohol(item,1);
            ModHelper.SetSource(item,Id,ModName);return item;
        }
    }
    [HarmonyPatch(typeof(PlayerStore),"AcceptBarter")]
    public static class XiaomanBrewingExchangePatch
    {
        public static bool Prefix() { return XiaomanMod.Instance==null || XiaomanMod.Instance.BeginBrewingExchange(); }
        public static void Postfix() { XiaomanMod.Instance?.CompleteBrewingExchange(); }
    }
}
