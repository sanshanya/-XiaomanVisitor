using HarmonyLib;
using TMPro;

namespace XiaomanVisitor
{
    public static class TavernMapRules
    {
        public const string LocationId = XiaomanMod.Id + ".tavern";
        public const string PreviewKey = "xiaoman_tavern_map";
        public const string ImageKey = XiaomanMod.Id + ":" + PreviewKey;

        public static bool UsesRenovationLabel(string locationId, bool enabled)
        { return enabled && locationId == LocationId; }
    }

    public sealed partial class XiaomanMod
    {
        internal bool TavernMapEnabled { get { return enabled; } }
        internal string TavernRenovationLabel { get { return Text("ui_tavern_renovating"); } }

        private DelegateAfterhourLocation CreateTavernMapLocation()
        {
            // Phase one is a visible closed location only. No arrival callback,
            // expenses, business clock or story transition can run from this card.
            return new DelegateAfterhourLocation {
                id = TavernMapRules.LocationId,
                source = Id,
                title = () => Text("ui_tavern_name"),
                imageKey = TavernMapRules.ImageKey,
                backgroundKey = TavernMapRules.ImageKey,
                isDiscovered = () => true,
                isAvailable = () => false,
                usesAfterhourBag = false,
                showsStoreCounters = false,
                allowsHazardousWaste = false,
                countsDumpedWaste = false
            };
        }

        private void EnsureTavernMap()
        {
            if (!enabled || ModHelper.GetAfterhourLocation(TavernMapRules.LocationId) != null) return;
            AfterhourLocation.Register(CreateTavernMapLocation());
        }

        private void DisableTavernMap()
        { ModHelper.RemoveAfterhourLocations(Id); }
    }

    [HarmonyPatch(typeof(TransitUI), "BuildCard", new[] { typeof(AfterhourLocation), typeof(float) })]
    public static class XiaomanTavernMapCardPatch
    {
        public static void Postfix(AfterhourLocation location, TransitCardView __result)
        {
            XiaomanMod mod = XiaomanMod.Instance;
            if (mod == null || __result == null
                || !TavernMapRules.UsesRenovationLabel(location == null ? null : location.Id, mod.TavernMapEnabled)) return;
            // Keep the native unavailable card, font, color and travel guard.
            // Only this location's status wording changes; no fake timed event.
            var status = __result.transform.Find("Status");
            var label = status == null ? null : status.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = mod.TavernRenovationLabel;
        }
    }
}
