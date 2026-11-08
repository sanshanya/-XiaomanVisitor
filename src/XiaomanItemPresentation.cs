using System;
using System.Globalization;
using HarmonyLib;

namespace XiaomanVisitor
{
    public static class ItemPresentation
    {
        public static bool OwnsItemId(string identifier)
        {
            return identifier != null && identifier.StartsWith(XiaomanMod.Id + ".", StringComparison.Ordinal);
        }

        public static string DrinkFlavor(string origin, string tier, string effect, string duration)
        {
            string blue = ((int)RenderHandler.ColorPalette.LightBlue).ToString("X6", CultureInfo.InvariantCulture);
            // The native tooltip adds its own separator before flavorText. This
            // leading newline leaves one blank line after the drink description.
            return "\n" + origin + "\n<color=#" + blue + ">" + tier + " · " + effect + "</color>\n" + duration;
        }
    }

    [HarmonyPatch(typeof(ModSourceHelper), nameof(ModSourceHelper.CreateTooltip))]
    public static class XiaomanItemSourceTooltipPatch
    {
        // Keep provenance tags and the player's global source setting intact.
        // Both native normal/advanced item tooltips append their footer here.
        private static bool Prefix(GameItem item)
        {
            return XiaomanMod.Instance == null || item == null || !ItemPresentation.OwnsItemId(item.identifier);
        }
    }
}
