using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor
{
    public static class ShopPortraitRules
    {
        public static bool MayPresent(bool ownClient, bool inStore, bool arrived, bool moving,
            bool nativeVisible, bool normalMode, bool sourceReady)
        { return ownClient && inStore && arrived && !moving && nativeVisible && normalMode && sourceReady; }

        public static bool SupportsBackdrop(string wallName, float wallWidth, float wallHeight,
            string deskName, float deskWidth, float deskHeight)
        {
            // LoadFaucetUpdate selects wall2 before either water tap is unlocked.
            // Both native variants have the same transparent serving window.
            return (wallName == "wall" || wallName == "wall2") && wallWidth == 960 && wallHeight == 540
                && deskName == "desk" && deskWidth == 960 && deskHeight == 540;
        }
        // Native 960x540 wall's transparent serving window and desk alpha edge.
        public const float WindowLeft = 324f / 960f;
        public const float WindowRight = 639f / 960f;
        public const float DeskTop = 271f / 540f;
    }

    // Present just Xiaoman after the world's 960x540 pixel buffer. Native motion,
    // interactions and every other client keep their original renderer.
    [DefaultExecutionOrder(1000)]
    public sealed class XiaomanShopPortrait : MonoBehaviour
    {
        private RectTransform parent, clip;
        private RawImage image;
        private SpriteRenderer source, wall, desk;
        private Texture2D displayAtlas;
        private bool originalForceOff, ownsSource;
        private string reportedState, prepareFailure;
        private MaterialPropertyBlock properties;
        private static readonly int SolidBlendId = Shader.PropertyToID("_SolidBlend");
        private const string Diagnostic = "SHOP-HD 0.0.3.2: ";

        private void LateUpdate()
        {
            if (!PlayerStore.HasInstance || XiaomanMod.Instance == null || StoreClientMono.Instance == null
                || !XiaomanMod.CurrentClientIsXiaoman()) { Hide(); reportedState = null; return; }
            var client = StoreClientMono.Instance;
            var renderer = client.image;
            bool inside = !ModHelper.IsOutsideStore(), arrived = PlayerStore.Instance.isClientArrived,
                moving = client.IsMoving(), visible = renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy,
                normal = StoreUIManager.Instance != null && !StoreUIManager.Instance.isShootingMode && !StoreUIManager.Instance.isMagnifyingMode,
                ready = renderer != null && renderer.sprite != null && PlayerStore.Instance.storeState == PlayerStore.StoreState.OPEN;
            if (!ShopPortraitRules.MayPresent(true,inside,arrived,moving,visible,normal,ready)) {
                Fallback("native state: inside=" + inside + ", arrived=" + arrived + ", moving=" + moving
                    + ", visible=" + visible + ", normalCamera=" + normal + ", ready=" + ready);
                return;
            }
            int frameIndex = FindFrame(XiaomanMod.Instance, renderer.sprite);
            if (frameIndex < 0) { Fallback("sprite is not an owned expression frame"); return; }
            if (renderer.color.a < .999f || renderer.maskInteraction != SpriteMaskInteraction.None) {
                Fallback("native alpha/mask transition"); return;
            }
            if (properties == null) properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            if (properties.GetFloat(SolidBlendId) > .001f) { Fallback("native solid-color transition"); return; }
            if (!Prepare()) { Fallback(prepareFailure); return; }
            if (StoreUIManager.Instance.mainCamera == null) { Fallback("normal camera is missing"); return; }
            Camera camera = StoreUIManager.Instance.mainCamera.GetComponent<Camera>();
            if (camera == null || !camera.isActiveAndEnabled) { Fallback("normal camera is inactive"); return; }
            Rect body, opening, counter;
            if (!Project(camera, renderer, renderer.sprite.bounds.min, renderer.sprite.bounds.max, out body)
                || !Project(camera, wall, WallPoint(ShopPortraitRules.WindowLeft,0), WallPoint(ShopPortraitRules.WindowRight,1), out opening)
                || !Project(camera, desk, DeskPoint(0,ShopPortraitRules.DeskTop), DeskPoint(1,1), out counter)) {
                Fallback("world-to-UI projection is not ready"); return;
            }
            // Clip below the opaque desktop; leave the original serving-window
            // sides in place. All coordinates follow the native transforms.
            float bottom = Mathf.Max(opening.yMin, counter.yMin);
            if (bottom >= opening.yMax || !body.Overlaps(opening)) { Fallback("portrait is outside the serving window"); return; }
            opening.yMin = bottom;
            if (clip.GetSiblingIndex() != 0) clip.SetAsFirstSibling();
            clip.anchorMin = clip.anchorMax = parent.pivot;
            clip.anchoredPosition = opening.center;
            clip.sizeDelta = opening.size;
            image.rectTransform.anchoredPosition = body.center - opening.center;
            image.rectTransform.sizeDelta = body.size;
            image.texture = displayAtlas;
            float u = (frameIndex % 6) / 6f, v = 1f - (frameIndex / 6 + 1) / 3f;
            float w = 1f/6, h = 1f/3;
            image.uvRect = new Rect(renderer.flipX ? u+w : u, renderer.flipY ? v+h : v,
                renderer.flipX ? -w : w, renderer.flipY ? -h : h);
            image.color = renderer.color;
            if (source != renderer) { ReleaseSource(); source = renderer; }
            if (!ownsSource) {
                originalForceOff = renderer.forceRenderingOff;
                if (originalForceOff) { Fallback("native renderer is already suppressed by another owner"); return; }
                ownsSource = true;
                Report("active; image=384x688, nativeFrame="
                    + renderer.sprite.rect.width + "x" + renderer.sprite.rect.height + ", viewport=" + LetterboxManager.GameAreaHeight
                    + ", nativeCamera=" + camera.pixelWidth + "x" + camera.pixelHeight
                    + ", wall=" + wall.sprite.name + ", layer=front (behind native dialogue).");
            }
            renderer.forceRenderingOff = true;
            clip.gameObject.SetActive(true);
        }

        private bool Prepare()
        {
            prepareFailure = "native front UI layer is unavailable";
            if (CustomUIManager.Instance == null || !CustomUIManager.HasLayer("front")) return false;
            if (displayAtlas == null) {
                bool detailed;
                displayAtlas = XiaomanMod.Instance.TavernPortraitAtlas(out detailed);
                if (!detailed) { displayAtlas = null; prepareFailure = "full-detail atlas could not be loaded"; return false; }
            }
            // The native front canvas is below dialogue, papers, transitions and
            // menus. Meta is above them and would let the actor cover the UI.
            RectTransform root = CustomUIManager.Instance.GetLayerRoot("front");
            prepareFailure = "native front canvas is inactive";
            if (root == null || !root.gameObject.activeInHierarchy) return false;
            Canvas canvas = root.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.isActiveAndEnabled) return false;
            if (parent != root || clip == null) {
                Hide();
                if (clip != null) UnityEngine.Object.Destroy(clip.gameObject);
                parent = root;
                clip = new GameObject("Xiaoman.ShopPortrait.Window", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
                clip.SetParent(parent, false); clip.pivot = new Vector2(.5f,.5f); clip.SetAsFirstSibling();
                image = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
                image.transform.SetParent(clip,false); image.raycastTarget = false;
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = new Vector2(.5f,.5f);
                clip.gameObject.SetActive(false);
            }
            wall = StoreUIManager.Instance.wallRenderer;
            if (desk == null) { var go = GameObject.Find("Desk"); desk = go == null ? null : go.GetComponent<SpriteRenderer>(); }
            prepareFailure = "native wall or desk renderer is missing";
            if (wall == null || desk == null || wall.sprite == null || desk.sprite == null) return false;
            if (ShopPortraitRules.SupportsBackdrop(wall.sprite.name,wall.sprite.rect.width,wall.sprite.rect.height,
                desk.sprite.name,desk.sprite.rect.width,desk.sprite.rect.height)) return true;
            prepareFailure = "unsupported backdrop: " + wall.sprite.name + " " + wall.sprite.rect.size
                + " / " + desk.sprite.name + " " + desk.sprite.rect.size;
            return false;
        }

        private static int FindFrame(XiaomanMod mod, Sprite sprite)
        {
            for (int mood=0;mood<6;mood++) for (int mouth=0;mouth<3;mouth++)
                if (mod.Frame((Mood)mood,mouth) == sprite) return mood*3+mouth;
            return -1;
        }

        private Vector3 WallPoint(float x, float y)
        { return Point(wall.sprite.bounds,x,y); }
        private Vector3 DeskPoint(float x, float y)
        { return Point(desk.sprite.bounds,x,y); }
        private static Vector3 Point(Bounds bounds, float x, float y)
        { return new Vector3(bounds.min.x + bounds.size.x*x, bounds.min.y + bounds.size.y*y, 0); }

        private bool Project(Camera camera, SpriteRenderer renderer, Vector3 min, Vector3 max, out Rect rect)
        {
            rect = default(Rect);
            Vector3 a = camera.WorldToViewportPoint(renderer.transform.TransformPoint(min));
            Vector3 b = camera.WorldToViewportPoint(renderer.transform.TransformPoint(max));
            if (a.z <= 0 || b.z <= 0) return false;
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;
            Camera uiCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null
                : canvas.worldCamera != null ? canvas.worldCamera : LetterboxManager.UICamera;
            Vector2 localA, localB;
            Vector2 screenA = new Vector2(LetterboxManager.GameAreaX + a.x * LetterboxManager.GameAreaWidth,
                LetterboxManager.GameAreaY + a.y * LetterboxManager.GameAreaHeight);
            Vector2 screenB = new Vector2(LetterboxManager.GameAreaX + b.x * LetterboxManager.GameAreaWidth,
                LetterboxManager.GameAreaY + b.y * LetterboxManager.GameAreaHeight);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,screenA,uiCamera,out localA)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,screenB,uiCamera,out localB)) return false;
            rect = Rect.MinMaxRect(Mathf.Min(localA.x,localB.x),Mathf.Min(localA.y,localB.y),Mathf.Max(localA.x,localB.x),Mathf.Max(localA.y,localB.y));
            return rect.width > 0 && rect.height > 0;
        }

        private void ReleaseSource()
        {
            if (ownsSource && source != null) source.forceRenderingOff = originalForceOff;
            ownsSource = false; source = null;
        }
        private void Hide()
        { if (clip != null) clip.gameObject.SetActive(false); ReleaseSource(); }
        private void Fallback(string reason)
        { Hide(); Report("native fallback; " + reason); }
        private void Report(string state)
        {
            if (reportedState == state) return;
            reportedState = state;
            if (XiaomanMod.Instance != null) XiaomanMod.Instance.Log.Log(Diagnostic + state);
        }
        internal void Shutdown()
        {
            Hide();
            if (clip != null) UnityEngine.Object.Destroy(clip.gameObject);
            clip = null; image = null; parent = null; wall = null; desk = null; displayAtlas = null;
        }
        private void OnDisable() { Hide(); }
        private void OnDestroy() { Shutdown(); }
    }
}
