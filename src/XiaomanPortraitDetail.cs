using System;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private Texture2D highDetailAtlas;
        private Texture2D activePortraitAtlas;
        private bool highDetailAttempted;

        private Texture2D SelectPortraitAtlas()
        {
            if (!PortraitDetailRules.WantsHighDetail(LetterboxManager.GameAreaHeight, SystemInfo.maxTextureSize)) return atlas;
            return LoadHighDetailAtlas() ?? atlas;
        }

        internal Texture2D TavernPortraitAtlas(out bool highDetail)
        {
            Texture2D selected = SystemInfo.maxTextureSize >= 2304 ? LoadHighDetailAtlas() : null;
            highDetail = selected != null;
            return selected ?? atlas;
        }

        private Texture2D LoadHighDetailAtlas()
        {
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
            return highDetailAtlas;
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
