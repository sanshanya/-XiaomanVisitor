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
        { tavernEnabledForSession = experimentalTavern != null && experimentalTavern.Value; }

        private DelegateAfterhourLocation tavernLocation;
        private XiaomanTavernStage tavernStage;
        internal string TavernStatusLabel
        {
            get {
                string key = TavernRules.StatusKey(TavernMapRules.LocationId, TavernMapEnabled, PlayerStore.HasInstance,
                    PlayerStore.HasInstance ? StoreStation.Instance.dayCounter : 0, PlayerStore.HasInstance && ReadStory().Finished);
                return key == null ? null : Text(key);
            }
        }

        private DelegateAfterhourLocation CreateTavernMapLocation()
        {
            return tavernLocation = new DelegateAfterhourLocation {
                id = TavernMapRules.LocationId,
                source = Id,
                title = () => Text("ui_tavern_name"),
                imageKey = TavernMapRules.ImageKey,
                isDiscovered = () => TavernMapEnabled,
                isAvailable = CanEnterTavern,
                onArrive = ArriveAtTavern,
                onLeave = LeaveTavern,
                onTick = TickTavernPanel,
                buildPanel = BuildTavernPanel,
                usesAfterhourBag = false,
                showsStoreCounters = false,
                allowsHazardousWaste = false,
                countsDumpedWaste = false
            };
        }

        private void EnsureTavernMap()
        {
            if (!TavernMapEnabled) return;
            if (PlayerStore.HasInstance) InitializeTavernProgress();
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
        }
    }
}
