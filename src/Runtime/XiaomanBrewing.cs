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
        private StoreClient brewingRenewalClient;
        private string BrewingTerms { get { return Text("ui_brew_terms",BrewingConfig.BonusPercent,BrewingConfig.BottleValue,BrewingConfig.SampleValue,
            ReadBrewing().MaterialLimit(StoreStation.Instance.dayCounter),BrewingConfig.WeeklyMaterialIncrease,BrewingConfig.MaximumMaterialLimit); } }
        private BrewingProgress ReadBrewing() { return BrewingProgress.Restore(ModHelper.GetModData(Id, BrewingProgress.SaveKey, "")); }
        private void SaveBrewing(BrewingProgress p)
        {
            string raw=p.Data; ModHelper.SetModData(Id,BrewingProgress.SaveKey,raw);
            if (ModHelper.GetModData(Id,BrewingProgress.SaveKey,"")!=raw) throw new InvalidOperationException("Brewing receipt was not saved");
        }
        private bool MayCollectBrewing
        { get { return TavernMapEnabled && TavernTrading && ReadBrewing().MayCollect(StoreStation.Instance.dayCounter); } }
        private bool BrewingVisitDue(int day)
        {
            return TavernMapEnabled && !TavernStoryFinished && ReadBrewing().MayVisit(day,TavernTrading);
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
        internal string BrewingQuoteSignature(StoreClient client)
        {
            if (!IsBrewingCollection(client)) return "";
            var goods=SelectedBrewingMaterials();
            return Text("ui_brew_table")+":"+ReadBrewing().MaterialLimit(StoreStation.Instance.dayCounter)+":"+BrewingConfig.BonusPercent+":"
                +(goods==null?"invalid:"+EmporiumEntry.Instance.backInvinvElement.childItems.Count:goods.Sum(m => (decimal)m.ValueCents).ToString("0"));
        }
        private bool IsBrewingCollection(StoreClient client)
        { return enabled && TavernMapEnabled && BrewingTarget() && client?.identifier==BrewingProgress.ClientId && client.clientIntent==StoreClient.ClientIntent.BARTER; }
        internal void ShowBrewingQuote(StoreClient client,NegotiationTabletFrame frame)
        {
            if (!IsBrewingCollection(client) || frame==null) return;
            var goods=SelectedBrewingMaterials();decimal value=goods==null?0:goods.Sum(m => (decimal)m.ValueCents)/100m;
            int limit=ReadBrewing().MaterialLimit(StoreStation.Instance.dayCounter);
            TabletUI.ListRow(frame,frame.content,Text("ui_brew_material_value"),value.ToString("0.##")+" CR",false,null,null);
            TabletUI.ListRow(frame,frame.content,Text("ui_brew_limit"),limit+" CR",false,null,null);
            TabletUI.ListRow(frame,frame.content,Text("ui_brew_return_value"),(value*(100+BrewingConfig.BonusPercent)/100m).ToString("0.##")+" CR",false,null,null);
            bool empty=EmporiumEntry.Instance.backInvinvElement.childItems.Count==0;
            string status=empty?Text("ui_brew_empty"):goods==null?Text("ui_brew_invalid")
                :value>limit?Text("ui_brew_over_limit",(value-limit).ToString("0.##"))
                :value==limit?Text("ui_brew_at_limit")
                :Text("ui_brew_remaining",(limit-value).ToString("0.##"));
            TabletUI.TextRow(frame,frame.content,status,18f,(!empty && goods==null || value>limit)?TabletStyle.RED:value==limit?TabletStyle.GREEN:TabletStyle.INK);
        }
        private StoreClient CreateBrewingVisitor()
        {
            var c=new StoreClient { identifier=BrewingProgress.ClientId,displayName=Text("name"),realName=Text("name"),spriteName=SpriteKey,
                clientFaction=StoreClient.FACTION_LOWER,clientIntent=MayCollectBrewing?StoreClient.ClientIntent.BARTER:StoreClient.ClientIntent.DIALOGUE,cooldown=0,
                arrestable=false,nonShootable=true,noAcceptingContraband=true,
                IsPersonalConsumption=false,CanReturn=false,CanReturnAsBuyer=false };
            ConfigureGunReaction(c);
            c.barterOffer=new BarterOffer { ownerClient=c,barterTextBody=Text("ui_brew_table"),
                onInstance=() => { c.isIntroduced=true; },
                getBarterResult=() => {
                    var goods=SelectedBrewingMaterials();
                    var p=ReadBrewing();
                    bool valid=MayCollectBrewing && goods!=null && p.AcceptsValue(goods.Sum(m => (decimal)m.ValueCents),StoreStation.Instance.dayCounter) && p.Pending==null && !p.Blocked
                        && !p.Batches.Any(b => b.Day==StoreStation.Instance.dayCounter && b.Phase!="cancelled");
                    // Valuation and limits belong to the native tablet, never a
                    // generated character line when materials are moved.
                    return new BarterResult { isAccepted=valid,response=goods==null?Text("xm_brew_wrong"):"" };
                } };
            c.willAcceptItem=item => BrewingMaterialCents(item)>0;
            var ledger=ReadBrewing();
            decimal ready=ledger.CreditMilli+ledger.Batches.Where(b => b.Phase=="complete" && !b.Credited && b.DueDay<=StoreStation.Instance.dayCounter).Sum(b => (decimal)b.ReturnMilli);
            string arrival=MayCollectBrewing?"xm_brew_hello":ready<BrewingConfig.SampleValue*1000L?"xm_brew_small_batch":"xm_brew_last";
            c.mainDialogue=Line(c,arrival,MayCollectBrewing?Mood.Smile:Mood.Soft);
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
                else TryFinishBrewingDelivery();
            });
            c.acceptDealDialogue=Line(c,"xm_brew_accept",Mood.Smile);
            c.repeatDialogue=Line(c,MayCollectBrewing?"xm_brew_request":"xm_brew_last",Mood.Soft);
            c.placedWrongItemWhenSellingToDialogue=Line(c,"xm_brew_wrong",Mood.Soft);
            c.allDoneDialogue=Line(c,"xm_brew_bye",Mood.Soft).SetEndAction(() => PlayerStore.Instance.DismissCurrentClient(false));
            c.OnArrival=() => SetMood(c,Mood.Neutral);
            c.OnDismiss=() => { brewingRenewalClient=null;Voice?.Stop();ResetGunReaction();SetMood(c,Mood.Neutral); };
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
            if (BrewingTarget() && BrewingProgress.Friday(StoreStation.Instance.dayCounter) || TavernStoryFinished) TryDeliverBrewing();
            TryFinishBrewingDelivery();
            if (PlayerStore.Instance.storeState==PlayerStore.StoreState.OPEN && BrewingProgress.VisitDay(StoreStation.Instance.dayCounter))
                Queue(PlayerStore.Instance.storeClientManager);
        }
        private void TryFinishBrewingDelivery()
        {
            if (!BrewingTarget() || !BrewingProgress.Friday(StoreStation.Instance.dayCounter) || brewingBusy
                || DialogUIManager.Instance==null || StoreUIManager.Instance.isShootingMode) return;
            var c=PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
            if (!c.isIntroduced || c==brewingRenewalClient) return;
            var current=DialogUIManager.Instance.currentDialog;
            if (current!=null && !current.isEndActionExecuted) return;
            var p=ReadBrewing();
            if (p.Blocked || p.Placing!=null || p.CreditMilli>=BrewingConfig.SampleValue*1000L
                || p.Batches.Any(b => b.Phase=="complete" && !b.Credited && b.DueDay<=StoreStation.Instance.dayCounter)) return;
            Action dismiss=() => {
                if (BrewingTarget() && PlayerStore.Instance.currentClientInstance.GetClientBlueprint()==c)
                    PlayerStore.Instance.DismissCurrentClient(false);
            };
            var renewal=p.Renewal;
            Dialogue next;
            if (TavernTrading && renewal!=null) {
                string batchId=renewal.Id;int revision=p.AgreementRevision,day=StoreStation.Instance.dayCounter;
                next=Line(c,"xm_brew_renew",Mood.Smile);
                var yes=new Dialogue().SetText("PLAYER",Text("ui_brew_continue"))
                    .SetNextDialogue(Line(c,"xm_brew_renew_yes",Mood.Smile).SetEndAction(dismiss));
                var no=new Dialogue().SetText("PLAYER",Text("ui_brew_pause_choice"))
                    .SetNextDialogue(Line(c,"xm_brew_renew_no",Mood.Soft).SetEndAction(dismiss));
                Action<bool> answer=accept => {
                    if (!BrewingTarget() || StoreStation.Instance.dayCounter!=day || accept && !TavernTrading) return;
                    var state=ReadBrewing();
                    if (state.ContinueAfterDelivery(batchId,revision,day,accept)) SaveBrewing(state);
                };
                next.AddChoice(DirectoryMaster.Item("$"),"","",yes,() => answer(true));
                next.AddChoice(DirectoryMaster.Item("$"),"","",no,() => answer(false));
            } else next=Line(c,"xm_brew_bye",Mood.Soft).SetEndAction(dismiss);
            if (DialogUIManager.Instance.StartDialogue(next,forcedStart:true)) brewingRenewalClient=c;
        }
        private void TryDeliverBrewing()
        {
            if (brewingBusy || !TavernMapEnabled || !PlayerStore.HasInstance || ModHelper.IsOutsideStore() || Time.unscaledTime<nextBrewingDelivery
                || !TavernStoryFinished && !BrewingProgress.Friday(StoreStation.Instance.dayCounter)) return;
            brewingBusy=true;
            try {
                var p=ReadBrewing();if (p.Blocked) return;
                bool final=TavernStoryFinished;
                long before=p.CreditMilli;
                p.CreditDue(StoreStation.Instance.dayCounter,final);
                bool recovering=p.Placing!=null;
                if (recovering) {
                    var recovered=EmporiumEntry.Instance.GetAllOwnedItems().FirstOrDefault(i => i.uniqueId==p.Placing.ItemId);
                    p.RecoverBottle(recovered!=null);
                    if (recovered!=null) RequestWineCrate(recovered);
                }
                if (p.CreditMilli!=before || recovering) SaveBrewing(p);
                // Bound placement work per tick; a full table keeps the remaining value.
                for (int n=0;n<3 && !p.Blocked && p.CreditMilli>=BrewingConfig.SampleValue*1000L;n++) {
                    var item=ModHelper.SpawnItem(p.CreditMilli>=BrewingConfig.BottleValue*1000L?BrewingProgress.BottleId:BrewingProgress.SampleId);
                    if (item==null) return;
                    var bottle=p.PrepareBottle(item.GetUniqueID());if (bottle==null) { item.Destroy();return; }
                    item.ModifyTag("XIAOMAN_BATCH_SOURCE",t => t.Enable().SetString(bottle.Id));
                    SaveBrewing(p);
                    bool placed=TryPlaceGift(item);
                    if (placed) RequestWineCrate(item);
                    p.BottlePlaced(placed);SaveBrewing(p);
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
            Action<GameItem> describe=bottle => {
                bottle.name=Text(small?"ui_brew_sample_name":"ui_brew_bottle_name");
                bottle.shortDescription=Text(small?"ui_brew_sample_desc":"ui_brew_bottle_desc");
                bottle.unitValue=small?BrewingConfig.SampleValue:BrewingConfig.BottleValue;
            };
            describe(item);item.onLoaded+=describe;
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
    [HarmonyPatch(typeof(PlayerStore),nameof(PlayerStore.OnItemsSold))]
    public static class XiaomanBrewingInputSnapshotPatch
    {
        public static List<GameItem> Snapshot(StoreClient client,List<GameItem> items)
        {
            if (client?.identifier!=BrewingProgress.ClientId || client.sourceModId!=XiaomanMod.Id
                || client.clientIntent!=StoreClient.ClientIntent.BARTER) return items;
            // BarterOffer passes the live counter list. Native TrackItem moves each
            // item into the sold bag, so iterate a snapshot, not that shrinking list.
            return items?.ToList();
        }
        private static void Prefix(ref List<GameItem> gameItems)
        {
            if (XiaomanMod.Instance==null || !PlayerStore.HasInstance) return;
            gameItems=Snapshot(PlayerStore.Instance.currentClientInstance?.GetClientBlueprint(),gameItems);
        }
    }
    [HarmonyPatch(typeof(NegociationUIManager),"BuildSignature")]
    public static class XiaomanBrewingQuoteRefreshPatch
    {
        private static void Postfix(StoreClient storeClient,ref string __result)
        { string extra=XiaomanMod.Instance?.BrewingQuoteSignature(storeClient);if (!string.IsNullOrEmpty(extra)) __result+="|xiaoman:"+extra; }
    }
    [HarmonyPatch(typeof(NegociationUIManager),"BuildBarter")]
    public static class XiaomanBrewingQuoteRowsPatch
    {
        private static void Postfix(StoreClient storeClient,NegotiationTabletFrame ___frame)
        { XiaomanMod.Instance?.ShowBrewingQuote(storeClient,___frame); }
    }
}
