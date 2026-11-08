using System;
using HarmonyLib;

namespace XiaomanVisitor
{
    // Station Voices installs its hooks under this exact owner ID after startup.
    // Ordering remains valid whether it registers before or after this assembly.
    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.DisplayClientText))]
    public static class XiaomanVoiceDisplayPatch
    {
        [HarmonyAfter("asteroid_voice_lines"), HarmonyPriority(Priority.Last)]
        private static void Postfix(Dialogue dialogue) { XiaomanMod.Instance?.Voice?.Display(dialogue); }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.PlayClientSingleText))]
    public static class XiaomanVoiceSinglePatch
    {
        [HarmonyAfter("asteroid_voice_lines"), HarmonyPriority(Priority.Last)]
        private static void Postfix() { XiaomanMod.Instance?.Voice?.Single(); }
    }

    [HarmonyPatch(typeof(DialogUIManager), "ScheduleFlow")]
    public static class XiaomanVoiceSchedulePatch
    {
        [HarmonyAfter("asteroid_voice_lines"), HarmonyPriority(Priority.Last)]
        private static void Prefix(DialogUIManager __instance, ref Action action)
        { XiaomanMod.Instance?.Voice?.WrapFlow(__instance, ref action); }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.EndConversation))]
    public static class XiaomanVoiceEndPatch
    {
        [HarmonyAfter("asteroid_voice_lines"), HarmonyPriority(Priority.Last)]
        private static bool Prefix(DialogUIManager __instance, bool __runOriginal)
        { return !__runOriginal || !(XiaomanMod.Instance?.Voice?.DeferEnd(__instance) ?? false); }
        private static void Postfix(bool __runOriginal) { if (__runOriginal) XiaomanMod.Instance?.Voice?.Stop(); }
    }

    [HarmonyPatch(typeof(Dialogue), nameof(Dialogue.ExecuteEndAction))]
    public static class XiaomanVoiceActionPatch
    {
        [HarmonyAfter("asteroid_voice_lines"), HarmonyPriority(Priority.Last)]
        private static bool Prefix(Dialogue __instance, bool __runOriginal)
        { return !__runOriginal || !(XiaomanMod.Instance?.Voice?.DeferAction(__instance) ?? false); }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.OnAdvancePressed))]
    public static class XiaomanVoiceManualPatch
    {
        [HarmonyAfter("asteroid_voice_lines"), HarmonyPriority(Priority.Last)]
        private static void Prefix(bool __runOriginal) { if (__runOriginal) XiaomanMod.Instance?.Voice?.Stop(); }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.InterruptDialog))]
    public static class XiaomanVoiceInterruptPatch
    {
        private static void Prefix() { XiaomanMod.Instance?.Voice?.Stop(); }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.HideAllDialogUI))]
    public static class XiaomanVoiceHidePatch
    {
        private static void Prefix() { XiaomanMod.Instance?.Voice?.Stop(); }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.Play3))]
    public static class XiaomanVoiceChannelPatch
    {
        // Return channel volume before an unrelated gun/drink sound takes over.
        private static void Prefix() { XiaomanMod.Instance?.Voice?.Stop(); }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.StopPlay3))]
    public static class XiaomanVoiceChannelStopPatch
    {
        private static void Postfix() { XiaomanMod.Instance?.Voice?.Stop(); }
    }
}
