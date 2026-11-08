using UnityEngine;
using UnityEngine.U2D;
using HarmonyLib;
using EPOOutline;

namespace XiaomanVisitor
{
    public static class NativePortraitRules
    {
        // Native movement and fade remain eligible, including the first frame.
        public static bool MayUseDetail(bool ownClient, bool ownSprite, bool inStore, bool visible, bool normalCamera, bool detailedFrames)
        { return (ownClient || ownSprite) && inStore && visible && normalCamera && detailedFrames; }
    }

    public sealed class NativeSamplingLease
    {
        public bool Active { get; private set; }
        private bool pixelPerfect, forceTexture;
        public bool Begin(bool originalPixelPerfect, bool originalForceTexture)
        {
            if (Active) return false;
            pixelPerfect = originalPixelPerfect; forceTexture = originalForceTexture; Active = true;
            return true;
        }
        public void UpdatePreference(bool enabled) { if (Active) pixelPerfect = enabled; }
        public bool End(out bool originalPixelPerfect, out bool originalForceTexture)
        {
            originalPixelPerfect = pixelPerfect; originalForceTexture = forceTexture;
            if (!Active) return false;
            Active = false; return true;
        }
    }

    // Follow the native game's non-pixel-perfect camera mode while Xiaoman is
    // visible. Keep its SpriteRenderer, material/path/shadow and sorting intact.
    [DefaultExecutionOrder(-1000)]
    internal sealed class XiaomanNativeRendering : MonoBehaviour
    {
        private readonly NativeSamplingLease lease = new NativeSamplingLease();
        private Camera nativeCamera;
        private PixelPerfectCamera pixelCamera;
        private XiaomanNativeCameraObserver observer;
        private string reported;
        private const string Diagnostic = "SHOP-NATIVE 0.0.3.3: ";
        internal static XiaomanNativeRendering Running;
        internal bool Active { get { return lease.Active && nativeCamera != null && pixelCamera != null; } }
        private void OnEnable() { Running = this; }

        private void LateUpdate()
        {
            XiaomanMod mod = XiaomanMod.Instance;
            if (mod == null || !PlayerStore.HasInstance) {
                Restore(); reported = null; return;
            }
            StoreUIManager ui = StoreUIManager.Instance;
            SpriteRenderer image = StoreClientMono.Instance == null ? null : StoreClientMono.Instance.image;
            bool ownClient = XiaomanMod.CurrentClientIsXiaoman();
            bool ownSprite = image != null && mod.OwnsPortrait(image.sprite);
            if (!ownClient && !ownSprite) { Restore(); reported = null; return; }
            bool visible = image != null && image.enabled && image.gameObject.activeInHierarchy;
            bool normal = ui != null && !ui.isShootingMode && !ui.isMagnifyingMode;
            // DismissCurrentClient clears the client before walking out. Keep
            // that existing sprite alive; do not rebuild it in mid-departure.
            bool detailed = (ownClient ? mod.EnsurePortraits() : ownSprite) && mod.HasDetailedPortraits;
            if (!NativePortraitRules.MayUseDetail(ownClient,ownSprite,!ModHelper.IsOutsideStore(),visible,normal,detailed)) {
                Restore(); Report("original sampling; visible=" + visible + ", normal=" + normal + ", detail=" + detailed); return;
            }
            Camera camera = ui.mainCamera == null ? null : ui.mainCamera.GetComponent<Camera>();
            PixelPerfectCamera pixels = camera == null ? null : camera.GetComponent<PixelPerfectCamera>();
            if (camera == null || !camera.isActiveAndEnabled || pixels == null) {
                Restore(); Report("original sampling; native camera is unavailable"); return;
            }
            if (camera != nativeCamera || pixels != pixelCamera) Restore();
            if (!lease.Active) {
                nativeCamera = camera; pixelCamera = pixels;
                lease.Begin(pixels.enabled,camera.forceIntoRenderTexture);
                if (observer != null && observer.gameObject != camera.gameObject) {
                    observer.Owner = null; UnityEngine.Object.Destroy(observer); observer = null;
                }
                if (observer == null) observer = camera.gameObject.AddComponent<XiaomanNativeCameraObserver>();
                observer.Owner = this; observer.ResetReport();
                Report("native SpriteRenderer throughout movement and dialogue; low-resolution upscale disabled; "
                    + "native material, foreground sorting, tint and solid-colour fade retained.");
            }
            UseNativeDetailedMode();
        }

        private void UseNativeDetailedMode()
        {
            if (!Active || !pixelCamera.enabled) return;
            float intended = LetterboxManager.GetIntendedOrthographicSize(nativeCamera);
            pixelCamera.enabled = false;
            LetterboxManager.SetIntendedOrthographicSize(nativeCamera,intended);
            RefreshNativeOutline(nativeCamera);
        }

        // EmporiumOption does this same refresh after changing camera mode.
        private static void RefreshNativeOutline(Camera camera)
        {
            Outliner outline = camera.GetComponent<Outliner>();
            if (outline != null && outline.enabled) { outline.enabled = false; outline.enabled = true; }
        }

        // Keep display-setting changes made during her visit when restoring.
        internal void NativePreferenceApplied(bool enabled)
        {
            if (!Active) return;
            lease.UpdatePreference(enabled);
            UseNativeDetailedMode();
        }
        internal void ClientShown() { LateUpdate(); }

        internal void ObserveNativeTarget()
        {
            if (!Active) return;
            RenderTexture target = nativeCamera.activeTexture;
            int width = target == null ? nativeCamera.pixelWidth : target.width;
            int height = target == null ? nativeCamera.pixelHeight : target.height;
            observer.ReportTarget(width,height,target != null,pixelCamera.enabled);
        }

        internal void LogTarget(int width, int height, bool texture, bool pixels)
        {
            Report("native output=" + width + "x" + height + ", viewport=" + LetterboxManager.GameAreaWidth + "x"
                + LetterboxManager.GameAreaHeight + ", textureTarget=" + texture + ", pixelCameraEnabled=" + pixels
                + ", upscaleRT=" + pixelCamera.upscaleRT + "; no UI portrait or renderer suppression.");
        }

        private void Restore()
        {
            bool enabled, forceTexture;
            if (lease.End(out enabled,out forceTexture)) {
                if (pixelCamera != null) pixelCamera.enabled = enabled;
                if (nativeCamera != null) {
                    nativeCamera.forceIntoRenderTexture = forceTexture;
                    RefreshNativeOutline(nativeCamera);
                }
                if (XiaomanMod.Instance != null) XiaomanMod.Instance.Log.Log(Diagnostic + "original sampling restored.");
            }
            if (observer != null) observer.Owner = null;
            nativeCamera = null; pixelCamera = null;
        }

        private void Report(string state)
        {
            if (reported == state) return;
            reported = state;
            if (XiaomanMod.Instance != null) XiaomanMod.Instance.Log.Log(Diagnostic + state);
        }
        internal void Shutdown()
        {
            Restore();
            if (observer != null) UnityEngine.Object.Destroy(observer);
            observer = null;
            if (Running == this) Running = null;
        }
        private void OnDisable() { Restore(); if (Running == this) Running = null; }
        private void OnDestroy() { Shutdown(); }
    }

    [DefaultExecutionOrder(1000)]
    internal sealed class XiaomanNativeCameraObserver : MonoBehaviour
    {
        internal XiaomanNativeRendering Owner;
        private int width = -1, height = -1;
        internal void ResetReport() { width = height = -1; }
        private void OnPostRender() { if (Owner != null) Owner.ObserveNativeTarget(); }
        internal void ReportTarget(int w, int h, bool texture, bool pixels)
        {
            if (width == w && height == h) return;
            width = w; height = h;
            if (Owner != null) Owner.LogTarget(w,h,texture,pixels);
        }
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
