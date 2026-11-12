using HarmonyLib;
using TMPro;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private ConfigEntry<bool> experimentalTavern;
        private bool tavernEnabledForSession;
        internal bool TavernMapEnabled { get { return enabled && tavernEnabledForSession; } }

        // Match the native restart-required option; never tear down travel in progress.
        private void CaptureTavernSetting()
        {
            tavernEnabledForSession = experimentalTavern != null && experimentalTavern.Value;
        }

        private DelegateAfterhourLocation tavernLocation;
        private XiaomanTavernStage tavernStage;
        private bool TavernStoryFinished { get { return PlayerStore.HasInstance && (ReadStory().Finished && (!EconomyAvailable || ReadEconomy().Departing || ReadEconomy().Phase=="new" || ReadEconomy().Blocked)
            || SideStories.BuybackStory.LineClosed
            ); } }
        internal string TavernMapDescription { get { return Text(PlayerStore.HasInstance
            && StoreStation.Instance.dayCounter >= TavernRules.OpeningDay ? "ui_tavern_open_desc" : "ui_tavern_map_desc"); } }
        internal string TavernStatusLabel
        {
            get {
                string key = TavernRules.StatusKey(TavernMapRules.LocationId, TavernMapEnabled, PlayerStore.HasInstance,
                    PlayerStore.HasInstance ? StoreStation.Instance.dayCounter : 0, TavernStoryFinished);
                if (key!=null && EconomyAvailable && !ReadEconomy().Departing && !TavernTrading && StoreStation.Instance.dayCounter>=TavernRules.OpeningDay)
                    return Text("ui_eco_phase_"+ReadEconomy().Phase);
                return key == null ? null : Text(key);
            }
        }

        private DelegateAfterhourLocation CreateTavernMapLocation()
        {
            return tavernLocation = new DelegateAfterhourLocation {
                id = TavernMapRules.LocationId,
                source = Id,
                title = () => Text(PlayerStore.HasInstance && StoreStation.Instance.dayCounter >= TavernRules.OpeningDay
                    ? "ui_tavern_name" : "ui_tavern_preview_name"),
                imageKey = TavernMapRules.ImageKey,
                isDiscovered = () => TavernRules.IsDiscovered(TavernMapEnabled, PlayerStore.HasInstance,
                    PlayerStore.HasInstance ? StoreStation.Instance.dayCounter : 0, TavernStoryFinished),
                isAvailable = CanEnterTavern,
                onArrive = ArriveAtTavern,
                onLeave = LeaveTavern,
                onTick = TickTavernPanel,
                usesAfterhourBag = true,
                showsStoreCounters = false,
                allowsHazardousWaste = false,
                countsDumpedWaste = false
            };
        }

        private void EnsureTavernMap()
        {
            if (!TavernMapEnabled) return;
            if (ModHelper.GetAfterhourLocation(TavernMapRules.LocationId) != null) return;
            AfterhourLocation.Register(CreateTavernMapLocation());
        }

        private void DisableTavernMap()
        {
            if (ModHelper.GetCurrentAfterhourLocationId() == TavernMapRules.LocationId)
                ModHelper.ReturnFromAfterhour(false);
            LeaveTavern();
            ModHelper.RemoveAfterhourLocations(Id);
            if (tavernStage != null) UnityEngine.Object.Destroy(tavernStage.gameObject);
            tavernStage = null; tavernLocation = null;
        }
    }

    [HarmonyPatch(typeof(TransitUI), "BuildCard", new[] { typeof(AfterhourLocation), typeof(float) })]
    public static class XiaomanTavernMapCardPatch
    {
        public static void Postfix(AfterhourLocation location, TransitCardView __result)
        {
            XiaomanMod mod = XiaomanMod.Instance;
            if (mod == null || __result == null
                || !TavernMapRules.UsesRenovationLabel(location == null ? null : location.Id, mod.TavernMapEnabled)) return;
            string text = mod.TavernStatusLabel;
            if (text == null) return;
            var status = __result.transform.Find("Status");
            var label = status == null ? null : status.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = text;
            var placeholder = __result.transform.Find("Image/Placeholder");
            var description = placeholder == null ? null : placeholder.GetComponent<TextMeshProUGUI>();
            if (description != null) {
                description.text = mod.TavernMapDescription;
                description.enableWordWrapping = true;
                description.fontSize = 18f;
            }
        }
    }
}
