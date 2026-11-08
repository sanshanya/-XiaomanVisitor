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
}
