using System.Linq;

namespace XiaomanVisitor
{
    public sealed class CassetteTrack
    {
        public readonly string Key, Title, SpriteKey, StoryNode;
        public CassetteTrack(string key, string title, string spriteKey, string storyNode)
        { Key=key; Title=title; SpriteKey=spriteKey; StoryNode=storyNode; }
        public string ItemId { get { return XiaomanMod.Id + ".tape." + Key; } }
        public string SoundKey { get { return "rain_" + Key; } }
    }

    public static partial class CassetteCollection
    {
        public static CassetteTrack Find(string key)
        { return Tracks.FirstOrDefault(track => track.Key == key); }
    }
}
