using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace XiaomanVisitor
{
    // One decoder, owned by the night stage. No frame sequence or persistent
    // render target; the background and counter sample the same decoded frame.
    internal sealed class XiaomanTavernAmbient : MonoBehaviour
    {
        private XiaomanMod mod;
        private RawImage background, foreground;
        private Texture still, counter;
        private VideoPlayer player;
        private float deadline;
        private bool visible;
        internal long DisplayedFrame { get { return visible && player != null ? player.frame : -1; } }

        internal void Configure(XiaomanMod owner, RawImage back, RawImage front)
        {
            mod = owner; background = back; foreground = front;
            still = back.texture; counter = front.texture;
        }

        private void OnEnable()
        {
            if (mod == null || background == null) return;
            RestoreStill();
            if (!ModHelper.ModFileExists(XiaomanMod.Id, TavernRules.SceneFolder + "ambient.mp4")) return;
            try {
                player = gameObject.AddComponent<VideoPlayer>();
                player.playOnAwake = false; player.isLooping = true;
                player.audioOutputMode = VideoAudioOutputMode.None;
                player.renderMode = VideoRenderMode.APIOnly;
                player.waitForFirstFrame = true; player.skipOnDrop = true;
                player.timeReference = VideoTimeReference.Freerun;
                player.source = VideoSource.Url;
                player.url = ModHelper.GetModFolder(XiaomanMod.Id) + "/" + TavernRules.SceneFolder + "ambient.mp4";
                player.prepareCompleted += Prepared;
                player.errorReceived += Failed;
                player.frameReady += FirstFrame;
                player.sendFrameReadyEvents = true;
                deadline = Time.unscaledTime + 10f;
                player.Prepare();
            }
            catch (Exception error) { Fallback(error.Message); }
        }

        private void Prepared(VideoPlayer source)
        { if (source == player && isActiveAndEnabled) source.Play(); }

        private void FirstFrame(VideoPlayer source, long frame)
        {
            if (source != player || !isActiveAndEnabled || source.texture == null) return;
            if (source.texture.width != TavernSceneLayout.EncodedWidth || source.texture.height != TavernSceneLayout.EncodedHeight)
            { Fallback("Unexpected video dimensions"); return; }
            Texture texture = source.texture;
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            background.texture = foreground.texture = texture;
            // The encoded file has one replicated row/column for H.264. Crop
            // those pixels in UV space instead of stretching the authored room.
            float u = (float)TavernSceneLayout.ContentWidth / TavernSceneLayout.EncodedWidth;
            float v = (float)TavernSceneLayout.ContentHeight / TavernSceneLayout.EncodedHeight;
            background.uvRect = new Rect(0, 1f - v, u, v);
            foreground.uvRect = new Rect(
                u * TavernSceneLayout.BarX / TavernSceneLayout.CanvasWidth,
                1f - v * (TavernSceneLayout.BarY + TavernSceneLayout.BarHeight) / TavernSceneLayout.CanvasHeight,
                u * TavernSceneLayout.BarWidth / TavernSceneLayout.CanvasWidth,
                v * TavernSceneLayout.BarHeight / TavernSceneLayout.CanvasHeight);
            visible = true;
            source.sendFrameReadyEvents = false;
            source.frameReady -= FirstFrame;
        }

        private void Update()
        { if (player != null && !visible && Time.unscaledTime >= deadline) Fallback("Video preparation timed out"); }

        private void Failed(VideoPlayer source, string error)
        { if (source == player) Fallback(error); }

        private void Fallback(string error)
        { mod.Log.Warning("Tavern ambient uses still image: " + error); Release(); }

        private void RestoreStill()
        {
            visible = false;
            if (background != null) { background.texture = still; background.uvRect = new Rect(0, 0, 1, 1); }
            if (foreground != null) { foreground.texture = counter; foreground.uvRect = new Rect(0, 0, 1, 1); }
        }

        private void Release()
        {
            RestoreStill();
            if (player == null) return;
            VideoPlayer old = player; player = null;
            old.prepareCompleted -= Prepared; old.errorReceived -= Failed; old.frameReady -= FirstFrame;
            old.Stop(); UnityEngine.Object.Destroy(old);
        }
        private void OnDisable() { Release(); }
        private void OnDestroy() { Release(); }
    }
}
