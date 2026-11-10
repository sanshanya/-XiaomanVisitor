using System;
using System.Linq;
using HarmonyLib;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        internal const string SupportPhoneId=Id+".tenquin.phone",AuditorId=Id+".tenquin.auditor",WangId=Id+".tenquin.wild";
        private bool supportBusy;
        private SupportProgress ReadSupport() { return SupportProgress.Restore(ModHelper.GetModData(Id,SupportProgress.SaveKey,"")); }
        private void SaveSupport(SupportProgress p) { ModHelper.SetModData(Id,SupportProgress.SaveKey,p.Data); }
        private void EnableWeekFour()
        {
            PhoneClientList.DialogFuncs[SupportPhoneId]=_ => SupportCall();
            ModHook.OnGameLoadedLate+=TickWeekFour;
            ModHook.OnWakingUpLate+=TickWeekFour;
            ModHook.OnShutterOpenedLate+=TickWeekFour;
            ModHook.OnReturningStoreLate+=TickWeekFour;
            ModHook.OnGenerateCustomerVeryLate+=QueueSupportVisitors;
        }
        private void DisableWeekFour()
        {
            PhoneClientList.DialogFuncs.Remove(SupportPhoneId);
            ModHook.OnGameLoadedLate-=TickWeekFour;ModHook.OnWakingUpLate-=TickWeekFour;
            ModHook.OnShutterOpenedLate-=TickWeekFour;ModHook.OnReturningStoreLate-=TickWeekFour;
            ModHook.OnGenerateCustomerVeryLate-=QueueSupportVisitors;
            if (PlayerStore.HasInstance) {
                foreach (long n in PlayerStore.Instance.PhoneClientDict.Where(k => k.Value.dialogFuncId==SupportPhoneId).Select(k => k.Key).ToArray())
                    PlayerStore.Instance.PhoneClientDict.Remove(n);
                PlayerStore.Instance.storeClientManager?.clientStack.RemoveAll(c => c!=null && (c.identifier==AuditorId || c.identifier==WangId));
            }
            CloseTavernCassettePlayer();
        }
        private void TickWeekFour()
        {
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance==null || supportBusy) return;
            int day=StoreStation.Instance.dayCounter;
            FreezeFourthLetter();RecoverSupplyTrade();RecoverSupplyBarStock();
            if (day<22) return;
            supportBusy=true;
            try {
                var p=ReadSupport();if (p.Blocked) return;
                var dict=PlayerStore.Instance.PhoneClientDict;
                if (dict!=null) {
                    var existing=dict.FirstOrDefault(k => k.Value.dialogFuncId==SupportPhoneId);
                    if (existing.Value!=null) p.PhoneNumber=existing.Key;
                    else {
                        long n=p.PhoneNumber>0?p.PhoneNumber:SupplyConfig.PhoneNumber;
                        while (dict.ContainsKey(n)) n++;
                        p.PhoneNumber=n;
                        dict.Add(n,new StorePhoneClient { dialogFuncId=SupportPhoneId,locID=SupportPhoneId,
                            displayName=Text("ui_w04_phone_name"),phoneState=StorePhoneClient.PhoneState.Regular,dialedBefore=true });
                    }
                }
                p.Advance(day);SaveSupport(p);
                if (p.DecisionGenerated && !p.DecisionSent && !ModHelper.IsOutsideStore()) {
                    if (p.NoticePhase=="placing") {
                        if (EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i.uniqueId==p.NoticeItemId)) { p.DecisionSent=true;p.NoticePhase="complete";SaveSupport(p); }
                        else { p.NoticePhase="uncertain";SaveSupport(p); }
                    }
                    if (p.NoticePhase=="waiting") {
                        var item=ModHelper.SpawnPaperItem(Id,"w04_support_notice");
                        if (item!=null) {
                            p.NoticeItemId=item.GetUniqueID();p.NoticePhase="placing";SaveSupport(p);
                            bool placed=TryPlaceGift(item);p.DecisionSent=placed;p.NoticePhase=placed?"complete":"waiting";SaveSupport(p);
                            if (placed && ReputationUIManager.Instance!=null) StoreUIManager.Instance.Notify(Text("ui_w04_notification"));
                        }
                    }
                }
                if (PlayerStore.Instance.storeState==PlayerStore.StoreState.OPEN) QueueSupportVisitors(PlayerStore.Instance.storeClientManager);
            } finally { supportBusy=false; }
        }
        internal StoreEvent SupportNews()
        {
            if (!enabled || !PlayerStore.HasInstance || StoreStation.Instance.dayCounter<22) return null;
            var p=ReadSupport();
            return new StoreEvent { identifier=Id+".w04.news",newsName=Text("xm_w04_news_title"),
                newsDescription=Text("xm_w04_news_body")+"\n\n"+Text("ui_w04_phone",p.PhoneNumber>0?p.PhoneNumber:SupplyConfig.PhoneNumber),
                eventType=StoreEvent.EventType.COSMETIC,eventArea=StoreEvent.EventArea.LOWER,startDay=22,duration=1,cooldown=0 };
        }
        private Dialogue SupportCall()
        {
            var p=ReadSupport();int day=StoreStation.Instance.dayCounter;
            string speaker=Text("ui_w04_phone_name");
            Func<string,Dialogue> line=key => new Dialogue().SetText(speaker,Text(key));
            Action end=() => PhoneUIManager.Instance.OnPhoneDialogEnd();
            if (!enabled || !SupportProgress.RegistrationOpen(day)) return line("xm_w04_phone_closed").SetEndAction(end);
            if (p.AppliedDay>=22) return line("ui_w04_registered").SetEndAction(end);
            var first=line("xm_w04_phone_01");var second=line("xm_w04_phone_02");first.nextDialogue=second;
            var yes=line("xm_w04_phone_confirm").SetStartAction(() => {
                var state=ReadSupport();if (state.Apply(StoreStation.Instance.dayCounter,WorkRunIdentity)) SaveSupport(state);
            }).SetEndAction(end);
            second.AddChoice(null,Text("ui_w04_apply"),"",yes);
            second.AddChoice(null,Text("ui_w04_news"),"",new Dialogue().SetText(speaker,Text("xm_w04_news_body")).SetNextDialogue(SupportCallChoices()));
            second.AddChoice(null,Text("ui_w04_cancel"),"",new Dialogue().SetText("","").SetEndAction(end));
            return first;
        }
        private Dialogue SupportCallChoices()
        {
            var choice=new Dialogue().SetText(Text("ui_w04_phone_name"),Text("xm_w04_phone_02"));
            choice.AddChoice(null,Text("ui_w04_apply"),"",new Dialogue().SetText(Text("ui_w04_phone_name"),Text("xm_w04_phone_confirm")).SetStartAction(() => {
                var p=ReadSupport();if (p.Apply(StoreStation.Instance.dayCounter,WorkRunIdentity)) SaveSupport(p);
            }).SetEndAction(() => PhoneUIManager.Instance.OnPhoneDialogEnd()));
            choice.AddChoice(null,Text("ui_w04_cancel"),"",new Dialogue().SetText("","").SetEndAction(() => PhoneUIManager.Instance.OnPhoneDialogEnd()));
            return choice;
        }
        private void QueueSupportVisitors(StoreClientManager manager)
        {
            if (!enabled || !PlayerStore.HasInstance || manager?.clientStack==null || StoreStation.Instance==null) return;
            int day=StoreStation.Instance.dayCounter;var p=ReadSupport();
            var current=PlayerStore.Instance.currentClientInstance?.GetClientBlueprint();
            if (p.AuditorDue(day) && current?.identifier!=AuditorId && !manager.clientStack.Any(c => c?.identifier==AuditorId)) {
                var c=SupportVisitor(false);ModHelper.SetClientSource(c,Id);manager.AddClient(c);
            }
            bool joeBusy=current?.spriteName=="joe" || manager.clientStack.Any(c => c?.spriteName=="joe")
                || PlayerStore.Instance.futurStoreClientIdQueue.Where((id,i) => PlayerStore.Instance.futurStoreClientDayQueue[i]<=0).Any(id => id!=null && (id.ToLowerInvariant().Contains("wild") || id.ToLowerInvariant().Contains("marketplaceintro")));
            if (joeBusy && manager.clientStack.Any(c => c?.identifier!=WangId && c?.spriteName=="joe"))
                manager.clientStack.RemoveAll(c => c?.identifier==WangId);
            if (p.WangDue(day) && !joeBusy && current?.identifier!=WangId && !manager.clientStack.Any(c => c?.identifier==WangId)) {
                var c=SupportVisitor(true);ModHelper.SetClientSource(c,Id);manager.AddClient(c);
            }
        }
        private StoreClient SupportVisitor(bool wang)
        {
            var c=new StoreClient { identifier=wang?WangId:AuditorId,displayName=wang?LocHelper.GetLocalizedClientName("name_joe_wild"):Text("ui_w04_assessor"),
                spriteName=wang?"joe":SpriteDict.GetRandomUpperMaleSpriteName(),clientFaction=wang?StoreClient.FACTION_LOWER:"FACTION_UPPER_LEVEL",
                nonShootable=true,arrestable=false,clientIntent=wang?StoreClient.ClientIntent.DIALOGUE:StoreClient.ClientIntent.BUY };
            c.realName=c.displayName;if (wang) c.bubbleColor="pink";
            string[] keys=wang?new[]{"xm_w04_wang_01","xm_w04_wang_02","xm_w04_wang_03","xm_w04_wang_04","xm_w04_wang_bye"}
                :new[]{"xm_w04_audit_01","xm_w04_audit_02","xm_w04_audit_shop"};
            Dialogue tail=null;
            foreach (string key in keys) {
                string lineKey=key=="xm_w04_audit_01" && StoreStation.Instance.dayCounter>ReadSupport().AuditorDay ? "xm_w04_audit_01_late" : key;
                var d=new Dialogue().SetText(c.displayName,Text(lineKey,SupplyConfig.AuditorBudget));
                if (tail==null) c.mainDialogue=d;else tail.nextDialogue=d;tail=d;
            }
            c.mainDialogue.startAction+=() => { var p=ReadSupport();if (wang) p.WangSeen=true;else p.AuditorSeen=true;SaveSupport(p); };
            if (wang) tail.SetEndAction(() => PlayerStore.Instance.DismissCurrentClient(false));
            else {
                c.SetBudget(SupplyConfig.AuditorBudget);c.clientBuyingTagList.Add("SUBSTANCE");
                c.willAcceptItem=item => item!=null && item.IsTag("SUBSTANCE") && !item.IsTag("CONTRABAND");
                tail.SetEndAction(c.OnIntroduced);
                c.acceptDealDialogue=new Dialogue().SetText(c.displayName,Text("xm_w04_audit_accept"));
                c.repeatDialogue=new Dialogue().SetText(c.displayName,Text("xm_w04_audit_shop",SupplyConfig.AuditorBudget));
                c.allDoneDialogue=new Dialogue().SetText(c.displayName,Text("xm_w04_audit_bye")).SetEndAction(() => PlayerStore.Instance.DismissCurrentClient(false));
                bool sold=false,saidBye=false;c.onItemBought+=(i,cost) => sold=true;
                c.tryInterceptDismiss=() => {
                    if (saidBye) return false;saidBye=true;
                    var d=new Dialogue().SetText(c.displayName,Text(sold?"xm_w04_audit_bye":"xm_w04_audit_no_trade"));
                    d.SetEndAction(() => PlayerStore.Instance.DismissCurrentClient(false));return DialogUIManager.Instance.StartDialogue(d);
                };
                c.OnDismiss=() => { var p=ReadSupport();p.AuditorComplete=true;SaveSupport(p); };
            }
            c.CompleteClientCreation();return c;
        }
        internal string FourthNoteVariant { get { return ReadStory().Visits.FirstOrDefault(v => v.Index==4)?.NoteVariant??"A"; } }
        private void FreezeFourthLetter()
        {
            if (!PlayerStore.HasInstance) return;
            var p=ReadStory();int day=StoreStation.Instance.dayCounter;
            bool changed=p.FreezeFourthNote(day,ReadSupply().SignedBy);
            var visit=p.Visits.FirstOrDefault(v => v.Index==4);
            if (visit!=null && day>visit.Day && visit.SupplySnapshot==null) {
                visit.SupplySnapshot=new SupplyNoteSnapshot { Day=visit.Day,WaterPrice=SupplyConfig.WaterPrice,JuicePrice=SupplyConfig.JuicePrice,Wholesale=SupplyConfig.Wholesale };changed=true;
            }
            if (changed) SaveStory(p);
        }
    }
    [HarmonyPatch(typeof(StorePhoneClient),nameof(StorePhoneClient.GetLocalizedDisplayName))]
    public static class XiaomanSupportPhoneName
    {
        private static bool Prefix(StorePhoneClient __instance,ref string __result)
        {
            if (__instance.dialogFuncId!=XiaomanMod.SupportPhoneId || XiaomanMod.Instance==null) return true;
            __result=ModHelper.GetLocalized(XiaomanMod.Id,"ui_w04_phone_name");return false;
        }
    }
    [HarmonyPatch(typeof(PaperUI),nameof(PaperUI.ResolveText),new[]{typeof(PaperElementData),typeof(PaperPageData)})]
    public static class XiaomanFourthLetterText
    {
        private static void Postfix(PaperUI __instance,PaperElementData element,ref string __result)
        {
            var mod=XiaomanMod.Instance;
            if (mod==null || __instance.EditorMode || __instance.ModId!=XiaomanMod.Id || __instance.Data?.id!="story_note_04" || element==null) return;
            if (element.id=="supply_snapshot") __result=mod.FourthSupplySnapshot();
            else if (mod.FourthNoteVariant=="B" && new[]{"title","body","signature"}.Contains(element.id))
                __result=ModHelper.GetLocalized(XiaomanMod.Id,"story_note_04_b_"+element.id);
        }
    }
}
