using UnityEngine;
using UnityEngine.UI;

namespace XiaomanVisitor
{
    public static class ShopPortraitRules
    {
        public static bool MayPresent(bool ownClient, bool inStore, bool arrived, bool moving,
            bool nativeVisible, bool normalMode, bool sourceReady)
        { return ownClient && inStore && arrived && !moving && nativeVisible && normalMode && sourceReady; }
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
        private MaterialPropertyBlock properties;
        private static readonly int SolidBlendId = Shader.PropertyToID("_SolidBlend");

        private void LateUpdate()
        {
            if (!PlayerStore.HasInstance || XiaomanMod.Instance == null || StoreClientMono.Instance == null) { Hide(); return; }
            var client = StoreClientMono.Instance;
            var renderer = client.image;
            if (!ShopPortraitRules.MayPresent(XiaomanMod.CurrentClientIsXiaoman(), !ModHelper.IsOutsideStore(),
                PlayerStore.Instance.isClientArrived, client.IsMoving(), renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy,
                StoreUIManager.Instance != null && !StoreUIManager.Instance.isShootingMode && !StoreUIManager.Instance.isMagnifyingMode,
                renderer != null && renderer.sprite != null
                    && PlayerStore.Instance.storeState == PlayerStore.StoreState.OPEN)) { Hide(); return; }
            int frameIndex = FindFrame(XiaomanMod.Instance, renderer.sprite);
            if (frameIndex < 0) { Hide(); return; }
            if (renderer.color.a < .999f || renderer.maskInteraction != SpriteMaskInteraction.None) { Hide(); return; }
            if (properties == null) properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            if (properties.GetFloat(SolidBlendId) > .001f) { Hide(); return; }
            if (!Prepare() || StoreUIManager.Instance.mainCamera == null) { Hide(); return; }
            Camera camera = StoreUIManager.Instance.mainCamera.GetComponent<Camera>();
            if (camera == null || !camera.isActiveAndEnabled) { Hide(); return; }
            Rect body, opening, counter;
            if (!Project(camera, renderer, renderer.sprite.bounds.min, renderer.sprite.bounds.max, out body)
                || !Project(camera, wall, WallPoint(ShopPortraitRules.WindowLeft,0), WallPoint(ShopPortraitRules.WindowRight,1), out opening)
                || !Project(camera, desk, DeskPoint(0,ShopPortraitRules.DeskTop), DeskPoint(1,1), out counter)) { Hide(); return; }
            // Clip below the opaque desktop; leave the original serving-window
            // sides in place. All coordinates follow the native transforms.
            float bottom = Mathf.Max(opening.yMin, counter.yMin);
            if (bottom >= opening.yMax || !body.Overlaps(opening)) { Hide(); return; }
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
                if (originalForceOff) { Hide(); return; }
                ownsSource = true;
                XiaomanMod.Instance.Log.Log("SHOP-HD 0.0.3.1: full-resolution presenter active; image=384x688, nativeFrame="
                    + renderer.sprite.rect.width + "x" + renderer.sprite.rect.height + ", viewport=" + LetterboxManager.GameAreaHeight
                    + ", nativeCamera=" + camera.pixelWidth + "x" + camera.pixelHeight
                    + ", layer=front (behind native dialogue); input, entrance/exit, counter clipping and native magnifier retained.");
            }
            renderer.forceRenderingOff = true;
            clip.gameObject.SetActive(true);
        }

        private bool Prepare()
        {
            if (CustomUIManager.Instance == null || !CustomUIManager.HasLayer("front")) return false;
            if (displayAtlas == null) {
                bool detailed;
                displayAtlas = XiaomanMod.Instance.TavernPortraitAtlas(out detailed);
                if (!detailed) { displayAtlas = null; return false; }
            }
            // The native front canvas is below dialogue, papers, transitions and
            // menus. Meta is above them and would let the actor cover the UI.
            RectTransform root = CustomUIManager.Instance.GetLayerRoot("front");
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
            if (wall == null) { var go = GameObject.Find("EmporiumWall"); wall = go == null ? null : go.GetComponent<SpriteRenderer>(); }
            if (desk == null) { var go = GameObject.Find("Desk"); desk = go == null ? null : go.GetComponent<SpriteRenderer>(); }
            return wall != null && desk != null && wall.sprite != null && desk.sprite != null
                && wall.sprite.name == "wall" && desk.sprite.name == "desk"
                && wall.sprite.rect.width == 960 && wall.sprite.rect.height == 540
                && desk.sprite.rect.width == 960 && desk.sprite.rect.height == 540;
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
