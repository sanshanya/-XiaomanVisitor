using System;
using System.Collections.Generic;
using UnityEngine;

namespace XiaomanVisitor.SideStories
{
    internal sealed partial class BuybackStage
    {
        private Action<bool> onDoorChosen;
        private BuybackRefusalText refusalText;
        private int retryDoor;
        private bool doorScene, waitingForKnock, knockRequested;
        private int knockPromptFrame;
        private float shopShade;
        private Vector2 doorAnchor;

        private void LayoutDoorScene()
        {
            var screen = new Vector2(LetterboxManager.GameAreaX + LetterboxManager.GameAreaWidth * doorAnchor.x,
                LetterboxManager.GameAreaY + LetterboxManager.GameAreaHeight * doorAnchor.y);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out local);
            speechRoot.anchorMin = speechRoot.anchorMax = new Vector2(.5f, .5f);
            speechRoot.anchoredPosition = local;
        }

        private IEnumerable<object> DoorKnocks(int repeats, bool automatic = false)
        {
            AudioClip clip;
            float until = Time.unscaledTime + 3;
            while (!TrySound("knock", out clip) && Time.unscaledTime < until) yield return null;
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            while (clip != null && clip.loadState == AudioDataLoadState.Loading && Time.unscaledTime < until) yield return null;
            bool ready = clip != null && clip.loadState == AudioDataLoadState.Loaded;
            log.Log("Door knocks: ready=" + ready + "; seconds=" + (clip == null ? 0 : clip.length)
                + "; source volume=" + effects.volume + "; listener paused=" + AudioListener.pause
                + "; recorded three-knock sequence.");
            if (!ready) { log.Warning("Recorded door knocks unavailable; no substitute effect will play."); yield break; }
            for (int i = 0; i < repeats; i++)
            {
                if (!automatic)
                {
                    knockRequested = false; waitingForKnock = true; knockPromptFrame = Time.frameCount;
                    hint.text = "点击 / 继续键　·　敲门声 " + (i + 1) + "/" + repeats + "　·　Esc 关闭当前演出";
                    while (!knockRequested) yield return null;
                }
                waitingForKnock = false;
                float before = ambientLevel; ambientLevel = .12f;
                var cue = Array.Find(script.cues, c => c.kind == "knock");
                float gain = automatic ? cue.retry_gains[Math.Min(Math.Max(retryDoor - 1, 0), 1)] : cue.gains[i];
                Sound("knock", gain / soundSpecs["knock"].gain);
                log.Log("Door knocks: " + (automatic ? "bedroll retry" : "player requested") + " sequence " + (i + 1) + "/" + repeats + "; gain=" + gain + ".");
                foreach (var frame in Wait(clip.length)) yield return frame;
                ambientLevel = before;
            }
            hint.text = "点击 / 继续对话键　·　Esc 关闭当前演出";
        }
    }
}
