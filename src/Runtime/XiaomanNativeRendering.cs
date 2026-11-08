using UnityEngine;
using UnityEngine.U2D;
using HarmonyLib;
using EPOOutline;

namespace XiaomanVisitor
{
    // Follow the native game's non-pixel-perfect camera mode while Xiaoman is
    // visible. Keep its SpriteRenderer, material/path/shadow and sorting intact.
    [DefaultExecutionOrder(-1000)]
    internal sealed class XiaomanNativeRendering : MonoBehaviour
    {
        private readonly NativeSamplingLease lease = new NativeSamplingLease();
        private GameObject cameraRoot;
        private Camera nativeCamera;
        private PixelPerfectCamera pixelCamera;
        private Outliner outline;
        private bool changedMode;
        internal static XiaomanNativeRendering Running;
        internal bool Active { get { return lease.Active && nativeCamera != null && pixelCamera != null; } }
        private void OnEnable() { Running = this; }

        private void LateUpdate()
        {
            XiaomanMod mod = XiaomanMod.Instance;
            if (mod == null || !PlayerStore.HasInstance) {
                Restore(); return;
            }
            StoreUIManager ui = StoreUIManager.Instance;
            SpriteRenderer image = StoreClientMono.Instance == null ? null : StoreClientMono.Instance.image;
            bool ownClient = XiaomanMod.CurrentClientIsXiaoman();
            bool ownSprite = image != null && mod.OwnsPortrait(image.sprite);
            if (!ownClient && !ownSprite) { Restore(); return; }
            bool visible = image != null && image.enabled && image.gameObject.activeInHierarchy;
            bool normal = ui != null && !ui.isShootingMode && !ui.isMagnifyingMode;
            // DismissCurrentClient clears the client before walking out. Keep
            // that existing sprite alive; do not rebuild it in mid-departure.
            bool detailed = mod.HasDetailedPortraits;
            if (!NativePortraitRules.MayUseDetail(ownClient,ownSprite,!ModHelper.IsOutsideStore(),visible,normal,detailed)) {
                Restore(); return;
            }
            if (!BindNativeCamera(ui.mainCamera)) { Restore(); return; }
            if (!lease.Active) {
                lease.Begin(pixelCamera.enabled,nativeCamera.forceIntoRenderTexture);
                changedMode = false;
            }
            UseNativeDetailedMode();
        }

        private bool BindNativeCamera(GameObject root)
        {
            if (cameraRoot != root || nativeCamera == null || pixelCamera == null) {
                Restore();
                cameraRoot = root;
                if (root != null) {
                    nativeCamera = root.GetComponent<Camera>();
                    pixelCamera = root.GetComponent<PixelPerfectCamera>();
                    outline = root.GetComponent<Outliner>();
                }
            }
            return nativeCamera != null && nativeCamera.isActiveAndEnabled && pixelCamera != null;
        }

        private void UseNativeDetailedMode()
        {
            if (!Active || !pixelCamera.enabled) return;
            float intended = LetterboxManager.GetIntendedOrthographicSize(nativeCamera);
            pixelCamera.enabled = false;
            changedMode = true;
            LetterboxManager.SetIntendedOrthographicSize(nativeCamera,intended);
            RefreshNativeOutline();
        }

        // EmporiumOption does this same refresh after changing camera mode.
        private void RefreshNativeOutline()
        {
            if (outline != null && outline.enabled) { outline.enabled = false; outline.enabled = true; }
        }

        // Keep display-setting changes made during her visit when restoring.
        internal void NativePreferenceApplied(bool enabled)
        {
            if (!Active) return;
            lease.UpdatePreference(enabled);
            UseNativeDetailedMode();
        }
        internal void ClientShown()
        {
            XiaomanMod mod = XiaomanMod.Instance;
            if (mod != null && XiaomanMod.CurrentClientIsXiaoman()) mod.EnsurePortraits();
            LateUpdate();
        }

        private void Restore()
        {
            bool enabled, forceTexture;
            if (lease.End(out enabled,out forceTexture)) {
                if (pixelCamera != null && pixelCamera.enabled != enabled) {
                    pixelCamera.enabled = enabled;
                    changedMode = true;
                }
                if (changedMode && nativeCamera != null) {
                    nativeCamera.forceIntoRenderTexture = forceTexture;
                    RefreshNativeOutline();
                }
            }
            cameraRoot = null; nativeCamera = null; pixelCamera = null; outline = null; changedMode = false;
        }

        internal void Shutdown()
        {
            Restore();
            if (Running == this) Running = null;
        }
        private void OnDisable() { Restore(); if (Running == this) Running = null; }
        private void OnDestroy() { Shutdown(); }
    }

    // Prepare before the first visible walk frame; do not wait for arrival.
    [HarmonyPatch(typeof(StoreUIManager), "QueueClientArriveAnimation")]
    public static class XiaomanNativeArrivalPatch
    {
        public static void Postfix() { if (XiaomanNativeRendering.Running != null) XiaomanNativeRendering.Running.ClientShown(); }
    }

    [HarmonyPatch(typeof(EmporiumOption), "ApplyPixelPerfectCamera", new[] { typeof(bool) })]
    public static class XiaomanNativeDisplayPatch
    {
        public static void Postfix(bool pixelPerfect)
        {
            if (XiaomanNativeRendering.Running != null) XiaomanNativeRendering.Running.NativePreferenceApplied(pixelPerfect);
        }
    }
}
