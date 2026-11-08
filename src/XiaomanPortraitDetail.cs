using System;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private Texture2D highDetailAtlas;
        private Texture2D activePortraitAtlas;
        private bool highDetailAttempted;
        internal bool HasDetailedPortraits { get { return activePortraitAtlas != null && activePortraitAtlas != atlas; } }
        internal bool OwnsPortrait(Sprite sprite)
        {
            if (sprite == null) return false;
            for (int mood=0;mood<6;mood++) for (int mouth=0;mouth<3;mouth++)
                if (frames[mood,mouth] == sprite) return true;
            return false;
        }

        private Texture2D SelectPortraitAtlas()
        {
            if (!PortraitDetailRules.WantsHighDetail(LetterboxManager.GameAreaHeight, SystemInfo.maxTextureSize)) return atlas;
            // Share the accepted room pixels through the native SpriteRenderer.
            bool detailed;
            Texture2D accepted = TavernPortraitAtlas(out detailed);
            if (detailed) return accepted;
            if (!highDetailAttempted)
            {
                highDetailAttempted = true;
                Texture2D pending = null;
                try
                {
                    byte[] data = ModHelper.ReadModFileBytes(Id, PortraitDetailRules.HighFile);
                    if (data == null || data.Length == 0) throw new InvalidOperationException("Missing high-detail portraits");
                    pending = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(pending, data, true)
                        || pending.width != FrameWidth * PortraitDetailRules.HighScale * PortraitDetailRules.HighColumns
                        || pending.height != FrameHeight * PortraitDetailRules.HighScale * PortraitDetailRules.HighRows)
                        throw new InvalidOperationException("Invalid high-detail portrait atlas");
                    pending.filterMode = FilterMode.Point;
                    pending.wrapMode = TextureWrapMode.Clamp;
                    pending.anisoLevel = 0;
                    pending.name = "XiaomanAtlas.HighDetail.Point.NoMipmaps";
                    highDetailAtlas = pending;
                    pending = null;
                    Log.Log("High-detail original portraits loaded on demand; no mipmaps or smoothing.");
                }
                catch (Exception error)
                { Log.Warning("Using standard portraits: " + error.Message); }
                finally
                { if (pending != null) UnityEngine.Object.Destroy(pending); }
            }
            return highDetailAtlas == null ? atlas : highDetailAtlas;
        }

        private void ReleaseHighDetailPortraits()
        {
            if (highDetailAtlas != null) UnityEngine.Object.Destroy(highDetailAtlas);
            highDetailAtlas = null;
            highDetailAttempted = false;
            activePortraitAtlas = null;
        }
    }
}
