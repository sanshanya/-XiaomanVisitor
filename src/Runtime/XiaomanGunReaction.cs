using System;
using Febucci.UI.Core;
using HarmonyLib;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private readonly GunReaction gunReaction = new GunReaction();
        private readonly CharacterMouthPulse gunMouth = new CharacterMouthPulse();
        private StoreClient gunClient;
        private Dialogue gunRaisedLine, gunRecoveryLine;
        private TypewriterCore gunTypewriter;
        private bool resumingGunConversation;

        private void ConfigureGunReaction(StoreClient client)
        {
            client.forceShootable = true;
            client.nonShootable = false;
            client.holsterResumesDialogue = true;
            client.unholsterDialogue = () => {
                BeginGunDraw();
                gunRaisedLine = Line(client, "xm_gun_plead", Mood.Worried);
                return gunRaisedLine;
            };
        }

        internal bool ProtectGunTarget()
        {
            StoreClient current = PlayerStore.HasInstance
                ? PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() : null;
            return GunReaction.Protects(enabled, current?.identifier);
        }

        internal void BeginGunDraw()
        {
            if (!ProtectGunTarget()) return;
            StoreClient current = PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
            if (gunClient != current) ResetGunReaction();
            // A second raise during the holster reply must not replace the original
            // native paused dialogue with our temporary reply.
            gunRecoveryLine = null;
            gunClient = current;
            gunReaction.Raise(Time.unscaledTime);
            if (EnsurePortraits()) EnsureGunPortraits();
        }

        internal bool PreserveGunPause(DialogUIManager dialog)
        {
            if (!ProtectGunTarget() || !gunReaction.ConsumePreservedPause()) return false;
            dialog.InterruptDialog();
            return true;
        }

        internal bool RecoverBeforeGunResume(DialogUIManager dialog)
        {
            if (resumingGunConversation || !ProtectGunTarget() || !gunReaction.Active
                || gunReaction.Phase == GunReactionPhase.Lingering
                || StoreUIManager.Instance == null || StoreUIManager.Instance.isShootingMode
                || PlayerStore.Instance.currentClientInstance.GetClientBlueprint() != gunClient) return false;
            if (gunReaction.Phase == GunReactionPhase.Recovering && gunRecoveryLine != null) return true;
            gunReaction.Recover();
            Voice?.Stop();
            dialog.InterruptDialog();
            Dialogue reply = Line(gunClient, "xm_gun_holstered", Mood.Worried);
            gunRecoveryLine = reply;
            reply.SetEndAction(() => FinishGunRecovery(dialog, reply));
            if (dialog.StartDialogue(reply, forcedStart: true)) return true;
            gunRecoveryLine = null;
            gunReaction.Reset();
            return false;
        }

        private void FinishGunRecovery(DialogUIManager dialog, Dialogue reply)
        {
            if (gunRecoveryLine != reply || gunReaction.Phase != GunReactionPhase.Recovering
                || !ProtectGunTarget() || PlayerStore.Instance.currentClientInstance.GetClientBlueprint() != gunClient) return;
            gunRecoveryLine = null;
            gunReaction.Resumed(Time.unscaledTime);
            resumingGunConversation = true;
            try { dialog.ResumeConversation(); }
            finally { resumingGunConversation = false; }
        }

        private bool IsGunLine(Dialogue line)
        { return line != null && (line == gunRaisedLine || line == gunRecoveryLine); }

        internal void DisplayGunLine(DialogUIManager dialog, Dialogue line)
        {
            if (!ProtectGunTarget() || !IsGunLine(line) || dialog.clientBubble == null) return;
            TypewriterCore writer = dialog.clientBubble.typewriter;
            if (gunTypewriter != writer) {
                if (gunTypewriter != null) gunTypewriter.onCharacterVisible.RemoveListener(GunCharacter);
                gunTypewriter = writer;
                if (gunTypewriter != null) gunTypewriter.onCharacterVisible.AddListener(GunCharacter);
            }
            gunMouth.Begin(Time.unscaledTime);
        }

        private void GunCharacter(char character)
        {
            if (gunClient != null && IsGunLine(DialogUIManager.Instance?.currentDialog))
                gunMouth.Observe(character, Time.unscaledTime);
        }

        internal Sprite GunPortrait(StoreClient client, Dialogue line, bool visible, bool typing,
            bool hadVoice, bool voiceBusy, int voiceMouth, int normalMouth)
        {
            if (client != gunClient || !gunReaction.Active) return null;
            int row = gunReaction.Row(Time.unscaledTime);
            if (row < 0) { ResetGunReaction(); return null; }
            int mouth = 0;
            if (Animate.Value && !AudioListener.pause && Time.timeScale > 0) {
                if (IsGunLine(line) && visible)
                    mouth = hadVoice ? (voiceBusy ? voiceMouth : 0) : gunMouth.Mouth(typing, Time.unscaledTime);
                else if (gunReaction.Phase == GunReactionPhase.Lingering) mouth = normalMouth;
            }
            return EnsureGunPortraits() ? gunFrames[row, Math.Max(0, Math.Min(2, mouth))] : Frame(Mood.Worried, mouth);
        }

        private void ResetGunReaction()
        {
            if (gunTypewriter != null) gunTypewriter.onCharacterVisible.RemoveListener(GunCharacter);
            gunTypewriter = null;
            gunReaction.Reset(); gunClient = null; gunRaisedLine = null; gunRecoveryLine = null;
            resumingGunConversation = false;
        }

        private void ShutdownGunReaction()
        {
            bool own = ProtectGunTarget();
            ResetGunReaction();
            if (own) {
                StoreClient current = PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
                current.nonShootable = true; current.forceShootable = false;
                if (StoreUIManager.Instance != null && StoreUIManager.Instance.isShootingMode)
                    StoreUIManager.Instance.ExitShootingMode();
            }
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.EnterShootingMode))]
    public static class XiaomanGunDrawPatch
    { private static void Prefix() { XiaomanMod.Instance?.BeginGunDraw(); } }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.PauseConversation))]
    public static class XiaomanGunPausePatch
    { private static bool Prefix(DialogUIManager __instance) { return !(XiaomanMod.Instance?.PreserveGunPause(__instance) ?? false); } }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.ResumeConversation))]
    public static class XiaomanGunResumePatch
    {
        private static bool Prefix(DialogUIManager __instance, ref bool __result)
        {
            if (!(XiaomanMod.Instance?.RecoverBeforeGunResume(__instance) ?? false)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.DisplayClientText))]
    public static class XiaomanGunTextPatch
    { private static void Prefix(DialogUIManager __instance, Dialogue dialogue) { XiaomanMod.Instance?.DisplayGunLine(__instance, dialogue); } }

    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.CanShoot))]
    public static class XiaomanGunCanShootPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!(XiaomanMod.Instance?.ProtectGunTarget() ?? false)) return true;
            __result = false;
            return false;
        }
    }

    // Guard the action itself too, before native recoil, flash, sound or damage.
    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.Shoot))]
    public static class XiaomanGunShootPatch
    { private static bool Prefix() { return !(XiaomanMod.Instance?.ProtectGunTarget() ?? false); } }
}
