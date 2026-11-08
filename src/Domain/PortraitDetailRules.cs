namespace XiaomanVisitor
{
    public static class PortraitDetailRules
    {
        public const int FrameWidth = 96;
        public const int FrameHeight = 172;
        public const int BodyHeight = 162;
        public const int HighScale = 4;
        public const int HighColumns = 6;
        public const int HighRows = 3;
        public const string PreferredFile = "Portraits/xiaoman_tavern_atlas.png";
        public const string HighFile = "Portraits/xiaoman_atlas_hd.png";

        public static bool WantsHighDetail(int viewportHeight, int maxTextureSize)
        {
            return viewportHeight > 0
                && maxTextureSize >= FrameWidth * HighScale * HighColumns
                && maxTextureSize >= FrameHeight * HighScale * HighRows;
        }
    }

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
}
