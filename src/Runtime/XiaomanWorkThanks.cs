using System;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private bool deliveringWorkGift;
        private Dialogue AppendWorkThanks(Dialogue tail,StoreClient client,bool merged)
        {
            var first=Append(tail,client,"xm_w03_thanks_01",Mood.Smile);
            first.startAction+=() => {
                var p=ReadWork();
                if (p.BeginThanks(StoreStation.Instance.dayCounter,merged)) SaveWork(p);
                TryDeliverWorkThanksGift();
            };
            return Append(first,client,"xm_w03_thanks_02",Mood.Shy);
        }

        private StoreClient CreateWorkThanksVisitor()
        {
            var client=new StoreClient {
                identifier=CustomerQueuePolicy.WorkThanksId,displayName=Text("name"),realName=Text("name"),spriteName=SpriteKey,
                clientFaction=StoreClient.FACTION_LOWER,clientIntent=StoreClient.ClientIntent.BUY,cooldown=0,
                arrestable=false,nonShootable=true,noAcceptingContraband=true };
            ConfigureGunReaction(client);
            client.SetBudget(ReadWork().PurchaseRemaining);
            client.willAcceptItem=item => IsChapterDemand(item,XiaomanDemand.FoodOrWater);
            // A dummy head is not played: AppendWorkThanks attaches the two authored lines.
            var head=new Dialogue();
            var tail=AppendWorkThanks(head,client,false);
            client.mainDialogue=head.nextDialogue;
            tail=Append(tail,client,"xm_w03_thanks_shop",Mood.Neutral);
            tail.SetEndAction(VisitorAction(client,() => { client.OnIntroduced();SetMood(client,Mood.Neutral); }));
            client.acceptDealDialogue=Line(client,"accept_return",Mood.Smile);
            client.placedWrongItemWhenSellingToDialogue=Line(client,"wrong_item",Mood.Worried);
            client.placeRightItemWhenSellingToDialogue=Line(client,"right_item",Mood.Soft);
            client.repeatDialogue=Line(client,"xm_repeat_first",Mood.Soft);
            bool farewell=false;
            Func<Dialogue> goodbye=() => {
                var line=Line(client,"xm_w03_thanks_bye",Mood.Soft);
                line.startAction+=() => farewell=true;
                line.SetEndAction(() => {
                    if (CurrentClientIsXiaoman() && PlayerStore.Instance.currentClientInstance.GetClientBlueprint()==client)
                        PlayerStore.Instance.DismissCurrentClient(false);
                });
                return line;
            };
            client.allDoneDialogue=goodbye();
            client.tryInterceptDismiss=() => !farewell && DialogUIManager.Instance.StartDialogue(goodbye());
            client.onItemBought+=(item,cost) => {
                if (!enabled || item==null) return;
                var p=ReadWork();
                if (p.RecordPurchase(item.GetUniqueID(),Math.Max(0,cost))) SaveWork(p);
                else Log.Warning("Work-thanks purchase receipt did not match its private reserve.");
            };
            client.OnArrival=() => SetMood(client,Mood.Neutral);
            client.OnDismiss=() => {
                Voice?.Stop();ResetGunReaction();SetMood(client,Mood.Neutral);
                var p=ReadWork();
                if (p.Thanks.Seen) { p.Thanks.ProcurementClosed=true;SaveWork(p); }
                TryDeliverWorkThanksGift();
            };
            client.CompleteClientCreation();client.noAcceptingContraband=true;
            return client;
        }

        private void TryDeliverWorkThanksGift()
        {
            if (!enabled || !TavernMapEnabled || deliveringWorkGift || !PlayerStore.HasInstance
                || EmporiumEntry.Instance==null || ModHelper.IsOutsideStore()) return;
            var p=ReadWork();
            if (!p.Thanks.Won || !p.Thanks.Seen || p.Thanks.GiftComplete) return;
            deliveringWorkGift=true;
            try {
                if (p.Thanks.GiftPhase=="placing") {
                    bool found=EmporiumEntry.Instance.GetAllOwnedItems().Any(item => item.uniqueId==p.Thanks.GiftItemId);
                    p.RecoverGift(found);SaveWork(p);
                }
                if (p.Thanks.GiftPhase!="waiting") return;
                var spec=CocktailCatalog.Find(p.Thanks.DrinkKey);
                if (spec==null) return;
                GameItem gift=ModHelper.SpawnItem(spec.ItemId);
                if (gift==null) return;
                if (!p.PrepareGift(gift.GetUniqueID())) { gift.Destroy();return; }
                SaveWork(p); // Persist identity before placement; ambiguous recovery never creates a second glass.
                bool placed=TryPlaceGift(gift);
                p.GiftPlacementResult(placed);SaveWork(p);
            }
            catch (Exception e) { Log.Warning("Work-thanks gift remains pending: "+e.Message); }
            finally { deliveringWorkGift=false; }
        }
    }
}
