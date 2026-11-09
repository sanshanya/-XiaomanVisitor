using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private const string TavernMenuId=Id+".tavern.menu";
        private CustomUIWindow tavernPanel,tavernMenu;
        private CustomUIElement tavernCash,tavernNotice;
        private string tavernLocale,tavernMenuPage,tavernSelectedDrink;
        private bool tavernActionBusy,tavernSequence,tavernInputBlocked;
        private float tavernNextRefresh;
        private readonly Queue<string> tavernLines=new Queue<string>();
        private Action tavernDialogueDone;
        private bool TavernKnown { get { return ReadStory().VisitIndex > 0 || ReadWork().ShopkeeperKnown; } }
        private bool TavernDialogueBusy { get { return workRunning || tavernInputBlocked && (tavernSequence || (tavernStage?.Speaking ?? false)); } }
        private bool AtTavern
        { get { return TavernMapEnabled && PlayerStore.HasInstance && ModHelper.GetCurrentAfterhourLocationId()==TavernMapRules.LocationId; } }
        private bool TavernMayAct
        { get { return AtTavern && MapUIManager.Instance!=null && !MapUIManager.Instance.IsInTransit
            && TavernRules.IsOpen(true,true,StoreStation.Instance.dayCounter,ReadStory().Finished); } }

        private bool CanEnterTavern()
        {
            return TavernMapEnabled && PlayerStore.HasInstance
                && TavernRules.IsOpen(true,true,StoreStation.Instance.dayCounter,ReadStory().Finished) && EnsureTavernStage();
        }
        private bool EnsureTavernStage()
        {
            if (!TavernMapEnabled) return false;
            if (tavernStage!=null) return true;
            if (tavernLocation==null || CustomUIManager.Instance==null || atlas==null) return false;
            GameObject stage=null;
            try {
                stage=new GameObject("Xiaoman.Tavern",typeof(RectTransform)); stage.SetActive(false);
                var actor=stage.AddComponent<XiaomanTavernStage>();
                actor.Configure(this,CustomUIManager.Instance.GetLayerRoot("meta"));
                tavernStage=actor; tavernLocation.stageObject=stage; return true;
            }
            catch (Exception error) {
                if (stage!=null) UnityEngine.Object.Destroy(stage);
                Log.Warning("Tavern scene unavailable: "+error.Message); return false;
            }
        }
        private void ArriveAtTavern()
        {
            if (!CanEnterTavern()) return;
            RecoverTavernDrink(); Voice?.Stop(); tavernStage.EndSession();
            RecoverWorkWage(); GenerateNightDemand();
            var progress=ReadTavern();
            bool known=TavernKnown;
            string[] greeting=progress.Enter(StoreStation.Instance.dayCounter,known);
            SaveTavern(progress);
            string[] tracks=TavernRules.MusicKeys(ReadStory().VisitIndex);
            tavernStage.PlayMusic(tracks.Contains(progress.SelectedTrack)?progress.SelectedTrack:"intro");
            QueueTavernSpeech(greeting,() => UpdateTavern(p => { if (known) p.IntroSeen=true; else p.UnknownIntroSeen=true; }));
        }
        private void UpdateTavern(Action<TavernProgress> update)
        { var progress=ReadTavern(); update(progress); SaveTavern(progress); }

        private void BuildTavernPanel(CustomUIBuilder builder)
        {
            tavernPanel=builder.Handle;
            builder.SetSize(280,170).SetDraggable(false).SetCloseOnEscape(false).OnClose(() => {
                if (AtTavern && !MapUIManager.Instance.IsInTransit) ModHelper.ReturnFromAfterhour();
            });
            builder.AddLabel("","xiaoman_tavern_cash"); tavernCash=builder.Last;
            tavernCash.SetPreferredSize(244,30);
            builder.AddLabel("","xiaoman_tavern_notice"); tavernNotice=builder.Last;
            tavernNotice.SetPreferredSize(244,32);
            tavernStage.AttachPanel(tavernPanel.Rect);
            tavernLocale=null;
        }
        private void QueueTavernSpeech(IEnumerable<string> keys,Action done=null,bool blockInput=true)
        {
            if (!AtTavern) return;
            tavernStage?.CancelSpeech();
            tavernLines.Clear();
            foreach (string key in keys) tavernLines.Enqueue(TavernDialogue.KeyFor(key,TavernKnown));
            tavernDialogueDone=done; tavernSequence=true; tavernInputBlocked=blockInput;
            ShowNextTavernLine();
        }
        private void ShowNextTavernLine()
        {
            if (tavernStage==null || tavernStage.Speaking) return;
            if (tavernLines.Count>0) {
                string key=tavernLines.Dequeue(),text=Text(key);
                tavernStage.Say(Text("name"),text,TavernDialogue.MoodFor(key),Voices.Resolve(key,SelectedVoiceLanguage,text));
                return;
            }
            if (!tavernSequence) return;
            tavernSequence=false;
            Action done=tavernDialogueDone; tavernDialogueDone=null; done?.Invoke();
        }
        internal void SetTavernSpeechSpeed(float speed) { tavernStage?.SetSpeechSpeed(speed); }
        internal bool AdvanceTavernSpeech() { return AtTavern && (tavernStage?.AdvanceSpeech() ?? false); }
        private void TickTavernPanel()
        {
            if (!AtTavern || tavernPanel==null || !tavernPanel.IsAlive) return;
            var rect=tavernPanel.Rect;
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,0);
            rect.anchoredPosition=new Vector2(-24,24);
            TickTavernWork();
            if (tavernSequence) ShowNextTavernLine();
            if (Time.unscaledTime<tavernNextRefresh) return;
            tavernNextRefresh=Time.unscaledTime+.2f;
            string locale=LocHelper.GetCurrentLocaleCode();
            if (locale!=tavernLocale) {
                tavernLocale=locale; tavernPanel.SetTitle(Text("ui_tavern_name"));
                tavernNotice.SetText(Text("ui_tavern_scene_hint"));
                if (tavernMenuPage!=null && tavernMenu!=null && tavernMenu.IsAlive) ShowTavernMenu(tavernMenuPage,tavernSelectedDrink);
            }
            tavernCash?.SetText(Text("ui_tavern_cash",PlayerStore.Instance.GetCash()));
        }
        private void TavernNotice(string key) { tavernNotice?.SetText(Text(key)); }

        internal void ClickTavernSpot(string spot)
        {
            if (!TavernMayAct || tavernActionBusy) return;
            if (TavernDialogueBusy) { if (spot=="actor") AdvanceTavernSpeech(); return; }
            if (spot=="actor") {
                CloseTavernMenu();
                QueueTavernSpeech(new[]{"xm_tavern_menu_open"},blockInput:false); ShowTavernMenu("root");
            }
            else if (spot=="rain") {
                CloseTavernMenu();
                if (!ReadTavern().RainIntroSeen)
                    QueueTavernSpeech(new[]{"xm_tavern_w02_rain_01","xm_tavern_w02_rain_02"},
                        () => UpdateTavern(p => p.RainIntroSeen=true));
            }
            else if (spot=="tape") {
                CloseTavernMenu();
                if (!ReadTavern().TapeIntroSeen)
                    QueueTavernSpeech(new[]{"xm_tavern_w02_tape_intro"},() => {
                        UpdateTavern(p => p.TapeIntroSeen=true); ShowTavernMenu("music");
                    });
                else ShowTavernMenu("music");
            }
        }
        private void CloseTavernMenu()
        {
            tavernMenuPage=null;
            if (CustomUIManager.Instance!=null) CustomUIManager.Instance.CloseWindow(TavernMenuId);
            tavernMenu=null;
            tavernStage?.AttachPanel(tavernPanel?.Rect);
        }
        private void ShowTavernMenu(string page,string drink=null)
        {
            if (!TavernMayAct || CustomUIManager.Instance==null) return;
            tavernMenuPage=page; tavernSelectedDrink=drink;
            CustomUIBuilder builder;
            if (tavernMenu!=null && tavernMenu.IsAlive) {
                CustomUIBuilder.ClearContainer(tavernMenu.ContentRoot);
                builder=new CustomUIBuilder(CustomUIManager.Instance,tavernMenu);
            }
            else {
                builder=CustomUIManager.Window(TavernMenuId,Text("ui_tavern_name"),"meta");
                tavernMenu=builder.Handle;
                builder.SetDraggable(false).OnClose(() => { tavernMenuPage=null; tavernStage?.AttachPanel(tavernPanel?.Rect); });
            }
            builder.SetSize(370,page=="drinks"?650:page=="detail"?610:page=="music"?Math.Min(780,160+TavernRules.MusicKeys(ReadStory().VisitIndex).Length*44):360);
            builder.SetPosition(new Vector2(-24,0));
            var rect=tavernMenu.Rect;
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,.5f);
            rect.anchoredPosition=new Vector2(-24,0);
            tavernMenu.SetTitle(Text(page=="drinks" || page=="detail"?"ui_tavern_menu":page=="music"?"ui_tavern_music":"ui_tavern_name"));
            var progress=ReadTavern();
            Action<string,Action> button=(key,click) => { builder.AddButton(Text(key),() => { if (!TavernDialogueBusy && !tavernActionBusy) click(); }); builder.Last.SetPreferredSize(334,48); };
            if (page=="root") {
                button("ui_tavern_menu",() => { CloseTavernMenu(); QueueTavernSpeech(new[]{"xm_tavern_w02_menu"},blockInput:false); ShowTavernMenu("drinks"); });
                button("ui_tavern_interaction",() => ShowTavernMenu("interaction"));
                button("ui_tavern_look",CloseTavernMenu);
            }
            else if (page=="interaction") {
                button("ui_tavern_chat",() => {
                    int day=StoreStation.Instance.dayCounter;
                    string key=ReadTavern().NextChat(day,ReadWork().WorkedOn(day));
                    string receipt=TavernProgress.ChatReceipt(key,day);
                    CloseTavernMenu(); QueueTavernSpeech(new[]{key},() => UpdateTavern(p => { if (!p.ChatSeen.Contains(receipt)) p.ChatSeen.Add(receipt); }));
                });
                if (progress.MayHelp(StoreStation.Instance.dayCounter)) button("ui_tavern_reception",() => {
                    CloseTavernMenu(); ModHelper.PlaySound3(Id,"xm_shutter_knock");
                    QueueTavernSpeech(new[]{"xm_tavern_w02_help_intro"},() => ShowTavernMenu("help"));
                });
                else if (StoreStation.Instance.dayCounter>=TavernWorkConfig.UnlockDay) {
                    builder.AddButton(Text("ui_tavern_paid_work",TavernWorkConfig.Wage),RequestTavernWork);
                    builder.Last.SetPreferredSize(334,48);
                }
                button("ui_tavern_back",() => ShowTavernMenu("root"));
            }
            else if (page=="help") {
                button("ui_tavern_help_confirm",() => {
                    var p=ReadTavern(); if (!p.Help(StoreStation.Instance.dayCounter)) return;
                    SaveTavern(p); CloseTavernMenu(); QueueTavernSpeech(new[]{"xm_tavern_w02_help_done"});
                });
                button("ui_tavern_help_skip",() => { CloseTavernMenu(); QueueTavernSpeech(new[]{"xm_tavern_w02_help_skip"}); });
            }
            else if (page=="work") {
                builder.AddLabel(Text("ui_tavern_work_wage",TavernWorkConfig.Wage));
                button("ui_tavern_work_confirm",StartTavernWork);
                button("ui_tavern_work_decline",() => { CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_w03_work_decline"}); });
            }
            else if (page=="drinks") {
                builder.AddLabel(Text("ui_tavern_cash",PlayerStore.Instance.GetCash()));
                builder.AddLabel(Text(progress.Drank(StoreStation.Instance.dayCounter)?"ui_tavern_drunk_today":"ui_tavern_one_glass"));
                foreach (var spec in CocktailCatalog.Drinks.OrderBy(d => d.Tier)) {
                    string key=spec.Key;
                    builder.AddButton(Text("ui_tavern_drink_list",Text("cocktail_"+key+"_name"),TavernPrices.For(spec),
                        Text("drink_tier_"+spec.Tier),Text("drink_buff_"+spec.Effect.ToString().ToLowerInvariant(),spec.EffectAmount),Text("drink_buff_duration")),
                        () => { if (!TavernDialogueBusy) ShowTavernMenu("detail",key); });
                    builder.Last.SetPreferredSize(334,64);
                }
                button("ui_tavern_back",() => ShowTavernMenu("root"));
            }
            else if (page=="detail") {
                var spec=CocktailCatalog.Find(drink);
                if (spec==null) { CloseTavernMenu(); return; }
                builder.AddImage(Id+":"+spec.SpriteKey,32,64);
                builder.AddLabel(Text("cocktail_"+drink+"_name"));
                builder.AddLabel(Text("cocktail_"+drink+"_desc")); builder.Last.SetPreferredSize(334,64);
                builder.AddLabel(ItemPresentation.DrinkFlavor(Text("cocktail_origin"),Text("drink_tier_"+spec.Tier),
                    Text("drink_buff_"+spec.Effect.ToString().ToLowerInvariant(),spec.EffectAmount),Text("drink_buff_duration")));
                builder.Last.SetPreferredSize(334,96);
                builder.AddLabel(Text("ui_tavern_drink_price",TavernPrices.For(spec),PlayerStore.Instance.GetCash()));
                button("ui_tavern_confirm_drink",() => BuyTavernDrink(drink));
                button("ui_tavern_takeaway",() => { CloseTavernMenu(); QueueTavernSpeech(new[]{"xm_tavern_no_takeaway"}); });
                button("ui_tavern_back",() => ShowTavernMenu("drinks"));
            }
            else if (page=="music") {
                foreach (string key in TavernRules.MusicKeys(ReadStory().VisitIndex)) {
                    string track=key;
                    builder.AddButton(Text("collection_"+track+"_name"),() => {
                        if (!TavernMayAct || TavernDialogueBusy) return;
                        if (tavernStage.PlayMusic(track)) UpdateTavern(p => p.SelectedTrack=track);
                    }); builder.Last.SetPreferredSize(334,40);
                }
                button("ui_tavern_look",CloseTavernMenu);
            }
            builder.Show();
            tavernMenuPage=page; tavernSelectedDrink=drink;
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,.5f); rect.anchoredPosition=new Vector2(-24,0);
            tavernStage.AttachPanel(tavernMenu.Rect);
        }
        private void LeaveTavern()
        {
            CancelTavernWork();
            tavernLines.Clear(); tavernSequence=false; tavernInputBlocked=false; tavernDialogueDone=null;
            CloseTavernMenu(); tavernStage?.EndSession();
            tavernCash=tavernNotice=null; tavernPanel=null; tavernLocale=null; tavernActionBusy=false;
        }
    }
}
