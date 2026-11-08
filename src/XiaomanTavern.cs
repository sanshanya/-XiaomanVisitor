using System;
using System.Linq;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private CustomUIElement tavernCash, tavernDrink, tavernHelp, tavernStatus, tavernLedger;
        private CustomUIWindow tavernPanel;
        private string tavernLocale, tavernSpeechKey;
        private Mood tavernSpeechMood;
        private bool tavernActionBusy;
        private float tavernNextRefresh;
        private const string TavernLedgerId = Id + ".tavern.ledger";

        private TavernProgress ReadTavern()
        { return TavernProgress.Restore(ModHelper.GetModData(Id, TavernRules.SaveKey, ""), StoreStation.Instance.dayCounter); }
        private void InitializeTavernProgress()
        {
            if (!ModHelper.HasModData(Id, TavernRules.SaveKey))
                ModHelper.SetModData(Id, TavernRules.SaveKey, ReadTavern().Data);
        }
        private bool AtTavern
        { get { return enabled && PlayerStore.HasInstance && ModHelper.GetCurrentAfterhourLocationId() == TavernMapRules.LocationId; } }
        private bool TavernMayAct
        { get { return AtTavern && MapUIManager.Instance != null && !MapUIManager.Instance.IsInTransit; } }

        private bool CanEnterTavern()
        {
            if (!enabled || !PlayerStore.HasInstance) return false;
            if (!TavernRules.IsOpen(true, true, StoreStation.Instance.dayCounter, ReadStory().Finished)) return false;
            return EnsureTavernStage();
        }

        private bool EnsureTavernStage()
        {
            if (tavernStage != null) return true;
            if (tavernLocation == null || CustomUIManager.Instance == null || atlas == null) return false;
            GameObject stage = null;
            try {
                stage = new GameObject("Xiaoman.Tavern", typeof(RectTransform)); stage.SetActive(false);
                var actor = stage.AddComponent<XiaomanTavernStage>();
                actor.Configure(this, CustomUIManager.Instance.GetLayerRoot("meta"));
                tavernStage = actor; tavernLocation.stageObject = stage;
                return true;
            }
            catch (Exception error) {
                if (stage != null) UnityEngine.Object.Destroy(stage);
                Log.Warning("Tavern scene unavailable: " + error.Message); return false;
            }
        }

        private void ArriveAtTavern()
        {
            InitializeTavernProgress();
            Voice?.Stop();
            tavernStage?.EndSession();
            tavernSpeechKey = "ui_tavern_welcome"; tavernSpeechMood = Mood.Soft;
        }

        private void BuildTavernPanel(CustomUIBuilder builder)
        {
            tavernPanel = builder.Handle;
            builder.SetSize(360, 660).SetDraggable(false).SetCloseOnEscape(false).OnClose(() => {
                tavernStage?.EndSession();
                if (TavernMayAct) ModHelper.ReturnFromAfterhour();
            });
            builder.AddLabel("", "xiaoman_tavern_cash"); tavernCash = builder.Last;
            tavernCash.SetPreferredSize(324, 32);
            builder.AddLabel("", "xiaoman_tavern_speech");
            CustomUIElement speech = builder.Last;
            speech.SetPreferredSize(324, 100);
            if (speech.text != null) { speech.text.fontSize = 19; speech.text.enableWordWrapping = true; }
            tavernStage?.AttachCaption(speech.text);
            builder.AddButton("", BuyTavernCup, "xiaoman_tavern_drink"); tavernDrink = builder.Last;
            tavernDrink.SetPreferredSize(324, 50);
            builder.AddButton("", HelpTavern, "xiaoman_tavern_help"); tavernHelp = builder.Last;
            tavernHelp.SetPreferredSize(324, 62);
            builder.AddButton("", OpenTavernLedger, "xiaoman_tavern_ledger"); tavernLedger = builder.Last;
            tavernLedger.SetPreferredSize(324, 50);
            builder.AddLabel("", "xiaoman_tavern_status"); tavernStatus = builder.Last;
            tavernStatus.SetPreferredSize(324, 70);
            tavernLocale = null;
            RefreshTavernPanel();
        }

        private void SayInTavern(string key, Mood expression)
        {
            tavernSpeechKey = key; tavernSpeechMood = expression;
            string text = Text(key);
            VoiceRecording recording = Voices.Resolve(key, SelectedVoiceLanguage, text);
            tavernStage?.Say(text, expression, recording);
        }

        private void TickTavernPanel()
        {
            if (!AtTavern || tavernPanel == null || !tavernPanel.IsAlive) return;
            // The native panel builder applies its default position after BuildPanel.
            // Anchor just our panel to the right; leave every other location alone.
            var rect = tavernPanel.Rect;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, .5f);
            rect.anchoredPosition = new Vector2(-24f, 0);
            if (Time.unscaledTime >= tavernNextRefresh) {
                tavernNextRefresh = Time.unscaledTime + .2f; RefreshTavernPanel();
            }
        }

        private void RefreshTavernPanel()
        {
            if (!AtTavern || tavernCash == null) return;
            var progress = ReadTavern();
            int day = StoreStation.Instance.dayCounter, cash = PlayerStore.Instance.GetCash();
            string locale = LocHelper.GetCurrentLocaleCode();
            bool changed = locale != tavernLocale;
            tavernLocale = locale;
            tavernCash.SetText(Text("ui_tavern_cash", cash));
            tavernDrink.SetText(Text("ui_tavern_buy", TavernRules.CupPrice)).SetInteractable(!tavernActionBusy && progress.MayBuy(day, cash));
            tavernHelp.SetText(Text("ui_tavern_help")).SetInteractable(!tavernActionBusy && progress.MayHelp(day));
            tavernLedger.SetText(Text("ui_tavern_ledger"));
            tavernStage?.SetPickupPoint(progress.PickupPointReady, Text("ui_tavern_pickup_sign"));
            tavernStatus.SetText(Text(!progress.MayAct(day) ? "ui_tavern_rest"
                : progress.PickupPointReady ? "ui_tavern_pickup_ready" : "ui_tavern_pickup_waiting"));
            if (changed) {
                tavernPanel.SetTitle(Text("ui_tavern_name"));
                SayInTavern(tavernSpeechKey ?? "ui_tavern_welcome", tavernSpeechMood);
            }
        }

        private void BuyTavernCup()
        {
            if (!TavernMayAct || tavernActionBusy) return;
            tavernActionBusy = true;
            try {
                TavernProgress progress = ReadTavern();
                if (!progress.Buy(StoreStation.Instance.dayCounter, PlayerStore.Instance.GetCash())) return;
                string data = progress.Data;
                PlayerStore.Instance.PayMoney(TavernRules.CupPrice);
                ModHelper.SetModData(Id, TavernRules.SaveKey, data);
                SayInTavern("xm_cocktail_accept", Mood.Smile);
            }
            finally { tavernActionBusy = false; RefreshTavernPanel(); }
        }

        private void HelpTavern()
        {
            if (!TavernMayAct || tavernActionBusy) return;
            tavernActionBusy = true;
            try {
                TavernProgress progress = ReadTavern();
                if (!progress.Help(StoreStation.Instance.dayCounter)) return;
                ModHelper.SetModData(Id, TavernRules.SaveKey, progress.Data);
                SayInTavern("accept_return", Mood.Soft);
            }
            finally { tavernActionBusy = false; RefreshTavernPanel(); }
        }

        private void OpenTavernLedger()
        {
            if (!TavernMayAct || CustomUIManager.Instance == null) return;
            TavernProgress progress = ReadTavern();
            var builder = CustomUIManager.Window(TavernLedgerId, Text("ui_tavern_ledger"), "meta");
            builder.SetSize(430, 520).Center().SetDraggable(false);
            builder.AddLabel(Text("ui_tavern_ledger_total", progress.Cups, progress.Receipts));
            builder.Last.SetPreferredSize(394, 66);
            if (progress.Entries.Count == 0) builder.AddLabel(Text("ui_tavern_ledger_empty"));
            foreach (TavernEntry entry in progress.Entries.OrderByDescending(e => e.Day).Take(6)) {
                builder.AddLabel(Text(entry.Action == "cup" ? "ui_tavern_ledger_cup" : "ui_tavern_ledger_help", entry.Day, entry.Paid));
                builder.Last.SetPreferredSize(394, 32);
            }
            builder.AddLabel(Text(progress.PickupPointReady ? "ui_tavern_pickup_ready" : "ui_tavern_pickup_waiting"));
            builder.Last.SetPreferredSize(394, 64);
            builder.AddButton(Text("ui_tavern_close_ledger"), () => CustomUIManager.Instance.CloseWindow(TavernLedgerId));
            builder.Show();
        }

        private void LeaveTavern()
        {
            tavernStage?.EndSession();
            if (CustomUIManager.Instance != null) CustomUIManager.Instance.CloseWindow(TavernLedgerId);
            tavernCash = tavernDrink = tavernHelp = tavernLedger = tavernStatus = null;
            tavernPanel = null; tavernLocale = null; tavernSpeechKey = null; tavernActionBusy = false;
        }
    }
}
