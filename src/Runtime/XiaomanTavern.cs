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
        private CustomUIElement tavernBackpackButton,tavernReturnButton;
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
            if (tavernLocation==null || CustomUIManager.Instance==null || atlas==null
                || RenderHandler.current==null || RenderHandler.current.windowUICanvas==null) return false;
            GameObject stage=null;
            try {
                stage=new GameObject("Xiaoman.Tavern",typeof(RectTransform)); stage.SetActive(false);
                var actor=stage.AddComponent<XiaomanTavernStage>();
                // The native ground/backpack windows share this canvas. Keep our
                // scene first, below them, rather than covering them on the meta layer.
                actor.Configure(this,(RectTransform)RenderHandler.current.windowUICanvas.transform);
                tavernStage=actor;tavernLocation.stageObject=stage;
                tavernLocation.panelObject=actor.ControlRoot.gameObject;
                BuildTavernControls(actor.ControlRoot);return true;
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
            RecoverWorkWage(); GenerateNightDemand();PrepareLuxuryStock();
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

        private void BuildTavernControls(RectTransform root)
        {
            var builder=CustomUIManager.Instance.BuildInto(root,true,Id+".tavern.controls");
            tavernPanel=builder.Handle;
            builder.BeginRow(16);
            builder.AddButton(Text("ui_tavern_backpack"),ToggleTavernBackpack);tavernBackpackButton=builder.Last;
            tavernBackpackButton.SetPreferredSize(112,36);
            builder.AddButton(Text("ui_tavern_back"),() => { if (TavernMayAct && !workRunning) ModHelper.ReturnFromAfterhour(); });
            tavernReturnButton=builder.Last;tavernReturnButton.SetPreferredSize(112,36);builder.End();
            foreach (var text in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) text.fontSize=18;
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
            TickTavernWork();
            if (tavernSequence) ShowNextTavernLine();
            if (Time.unscaledTime<tavernNextRefresh) return;
            tavernNextRefresh=Time.unscaledTime+.2f;
            string locale=LocHelper.GetCurrentLocaleCode();
            if (locale!=tavernLocale) {
                tavernLocale=locale;
                tavernBackpackButton.SetText(Text("ui_tavern_backpack"));tavernReturnButton.SetText(Text("ui_tavern_back"));
                if (tavernMenuPage!=null) ShowTavernMenu(tavernMenuPage,tavernSelectedDrink);
            }
        }
        private void TavernNotice(string key) { if (ReputationUIManager.Instance!=null) StoreUIManager.Instance.Notify(Text(key)); }
        private void ToggleTavernBackpack()
        {
            if (!TavernMayAct || EmporiumEntry.Instance?.afterhourWindow==null) return;
            CloseTavernMenu();
            var bag=EmporiumEntry.Instance.afterhourWindow;
            if (bag.handler.gameObject.activeSelf) EmporiumEntry.Instance.HideAfterhourInv();
            else {
                var position=bag.rectTransform.anchoredPosition;
                EmporiumEntry.Instance.ShowAfterhourInv();
                bag.rectTransform.anchoredPosition=position;
            }
        }

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
            tavernStage?.HideChoices();
            if (CustomUIManager.Instance!=null) CustomUIManager.Instance.CloseWindow(TavernMenuId);
            tavernMenu=null;
            tavernStage?.AttachPanel(tavernPanel?.Rect);
        }
        private void ShowTavernMenu(string page,string drink=null)
        {
            if (page=="music") { OpenTavernCassettePlayer();return; }
            if (!TavernMayAct || CustomUIManager.Instance==null) return;
            if (page=="root" || page=="interaction" || page=="help" || page=="work") {
                ShowTavernChoices(page);return;
            }
            tavernStage?.HideChoices();
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
            tavernMenu.SetTitle(Text(page=="drinks" || page=="luxury" || page=="detail"?"ui_tavern_menu":page=="music"?"ui_tavern_music":"ui_tavern_name"));
            var progress=ReadTavern();
            if (page.StartsWith("supply") || page=="support_news") {
                builder.SetSize(420,620);BuildSupplyMenu(builder,page);tavernMenu.Show();return;
            }
            Action<string,Action> button=(key,click) => { builder.AddButton(Text(key),() => { if (!TavernDialogueBusy && !tavernActionBusy) click(); }); builder.Last.SetPreferredSize(334,48); };
            if (page=="drinks" || page=="luxury") {
                builder.AddLabel(Text("ui_tavern_cash",PlayerStore.Instance.GetCash()));
                builder.AddLabel(Text(progress.Drank(StoreStation.Instance.dayCounter)?"ui_tavern_drunk_today":"ui_tavern_one_glass"));
                float available=((RectTransform)rect.parent).rect.width;
                bool luxury=page=="luxury";
                int columns=luxury?2:available<900?3:6;
                float menuWidth=Mathf.Min(luxury?560:1140,available*.94f),cardWidth=(menuWidth-60-(columns-1)*10)/columns;
                bool hasLuxury=StoreStation.Instance.dayCounter>=LuxuryConfig.UnlockDay;
                builder.SetSize(menuWidth,luxury?510:columns==6?(hasLuxury?510:440):(hasLuxury?800:740));
                builder.BeginGrid(columns,cardWidth,luxury?320:292,10);
                var supply=ReadSupply();
                foreach (var spec in (luxury?LuxuryDrinkCatalog.Standard:CocktailCatalog.Drinks).OrderBy(d => d.Tier)) {
                    string key=spec.Key;
                    builder.BeginPanel(cardWidth,luxury?320:292,padding:8,spacing:5);
                    builder.AddLabel(Text("cocktail_"+key+"_name"));builder.Last.SetPreferredSize(cardWidth-16,32);
                    builder.AddImage(Id+":"+spec.SpriteKey,36,72).WithTooltip(Text("cocktail_"+key+"_desc"));
                    builder.AddLabel(TavernPrices.For(spec)+" CR");
                    if (luxury) builder.AddLabel(Text("ui_w05_stock",supply.Batches.Where(b => b.Key==key).Sum(b => b.StoreRemaining)));
                    builder.AddLabel(Text("drink_tier_"+spec.Tier));
                    builder.AddLabel(Text("drink_buff_"+spec.Effect.ToString().ToLowerInvariant(),spec.EffectAmount)+"\n"+Text("drink_buff_duration"));
                    builder.Last.SetPreferredSize(cardWidth-16,46);
                    builder.AddButton(Text("ui_tavern_confirm_drink"),() => BuyTavernDrink(key));
                    builder.Last.SetPreferredSize(cardWidth-16,40).SetInteractable(!progress.Drank(StoreStation.Instance.dayCounter) && PlayerStore.Instance.GetCash()>=TavernPrices.For(spec) && (!luxury || supply.Batches.Any(b => b.Key==key && b.StoreRemaining>0)));
                    builder.End();
                }
                builder.End();
                if (StoreStation.Instance.dayCounter>=LuxuryConfig.UnlockDay) {
                    if (!luxury) button("ui_w05_luxury",OpenLuxuryMenu);
                    else button("ui_w05_classic",() => ShowTavernMenu("drinks"));
                }
                button("ui_tavern_back",() => ShowTavernMenu("root"));
            }
            builder.Show();
            tavernMenuPage=page; tavernSelectedDrink=drink;
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,.5f); rect.anchoredPosition=new Vector2(-24,0);
            if (page=="drinks" || page=="luxury") { rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero; }
            tavernStage.AttachPanel(tavernMenu.Rect);
        }
        private void ShowTavernChoices(string page)
        {
            CloseTavernMenu();
            var options=new List<TavernChoice>();
            Action<string,Action> choice=(label,click) => options.Add(new TavernChoice(label,() => {
                if (TavernMayAct && !TavernDialogueBusy && !tavernActionBusy) click();
            }));
            if (page=="root") {
                choice(Text("ui_tavern_menu"),() => { CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_w02_menu"},blockInput:false);ShowTavernMenu("drinks"); });
                choice(Text("ui_tavern_interaction"),() => ShowTavernMenu("interaction"));
                choice(Text("ui_tavern_look"),CloseTavernMenu);
            }
            else if (page=="interaction") {
                choice(Text("ui_tavern_chat"),() => {
                    int day=StoreStation.Instance.dayCounter;
                    string key=ReadTavern().NextChat(day,ReadWork().WorkedOn(day),ReadStory().VisitIndex);
                    string receipt=TavernProgress.ChatReceipt(key,day);
                    CloseTavernMenu();QueueTavernSpeech(TavernDialogue.ChatSequence(key),() => UpdateTavern(p => { if (!p.ChatSeen.Contains(receipt)) p.ChatSeen.Add(receipt); }));
                });
                if (ReadTavern().MayHelp(StoreStation.Instance.dayCounter)) choice(Text("ui_tavern_reception"),() => {
                    CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_w02_help_intro"},() => ShowTavernMenu("help"));
                });
                else if (StoreStation.Instance.dayCounter>=TavernWorkConfig.UnlockDay)
                    choice(Text("ui_tavern_paid_work",TavernWorkConfig.Wage),RequestTavernWork);
                if (StoreStation.Instance.dayCounter>=22) choice(Text("ui_w04_supply"),RequestSupply);
                choice(Text("ui_tavern_back"),() => ShowTavernMenu("root"));
            }
            else if (page=="help") {
                choice(Text("ui_tavern_help_confirm"),() => {
                    var p=ReadTavern();if (!p.Help(StoreStation.Instance.dayCounter)) return;
                    SaveTavern(p);CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_w02_help_done"});
                });
                choice(Text("ui_tavern_help_skip"),() => { CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_w02_help_skip"}); });
            }
            else if (page=="work") {
                choice(Text("ui_tavern_paid_work",TavernWorkConfig.Wage),StartTavernWork);
                choice(Text("ui_tavern_work_decline"),() => { CloseTavernMenu();QueueTavernSpeech(new[]{"xm_tavern_w03_work_decline"}); });
            }
            tavernMenuPage=page;
            tavernStage.ShowChoices(options.ToArray());
        }
        private void LeaveTavern()
        {
            CancelTavernWork();CloseTavernCassettePlayer();
            tavernLines.Clear(); tavernSequence=false; tavernInputBlocked=false; tavernDialogueDone=null;
            CloseTavernMenu(); tavernStage?.EndSession();
            tavernLocale=null;tavernActionBusy=false;
        }
    }
}
