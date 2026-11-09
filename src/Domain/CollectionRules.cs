using System;
using System.Globalization;
using System.Linq;

namespace XiaomanVisitor
{
    public static class NotebookRules
    {
        public const string ItemId = XiaomanMod.Id + ".letter_notebook";

        public static bool IsLetter(string identifier)
        {
            string prefix = XiaomanMod.Id + ".";
            if (identifier == null || !identifier.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string ignored;
            return LetterInkRules.TryResolve(XiaomanMod.Id, identifier.Substring(prefix.Length), "en", out ignored);
        }

        public static bool CanStore(string identifier, bool letterOwned, bool notebookOwned, bool isContainer)
        { return letterOwned && notebookOwned && !isContainer && IsLetter(identifier); }

        public static bool ShouldGrant(bool alreadyDelivered, bool ownsNotebook, bool ownsLetter, int today, int firstVisitDay)
        {
            return !alreadyDelivered && !ownsNotebook && today >= 1
                && (ownsLetter || firstVisitDay >= 1 && today > firstVisitDay);
        }
    }

    public static class CassetteCaseRules
    {
        public const string ItemId = XiaomanMod.Id + ".cassette_case";
        public const int Columns = 8;
        public const int Rows = 8;

        public static bool IsCassette(string identifier)
        {
            return identifier == XiaomanMod.Id + ".rain_cassette"
                || identifier != null && CassetteCollection.Tracks.Any(track => track.ItemId == identifier);
        }

        public static bool CanStore(string identifier, bool owned, bool caseOwned, bool cassetteTag, bool containerTag)
        { return owned && caseOwned && cassetteTag && !containerTag && IsCassette(identifier); }

        public static bool MachineStorageAccepts(string identifier, bool owned, bool enabled, bool nativeAccepts)
        { return enabled && identifier == ItemId ? owned : nativeAccepts; }
    }

    // Fixed letters only. Never touches another mod's papers or global fonts.
    public static class LetterInkRules
    {
        public static string VariantPath(string path,string bookId,string variant)
        { return bookId=="story_note_03" && variant=="B" && path!=null ? path.Replace("story_note_03.png","story_note_03_b.png") : path; }
        public static string Language(string locale)
        {
            string language = VoiceCatalog.Language(locale);
            return language == "ja" || language == "zh" ? language : "en";
        }

        public static bool TryResolve(string modId, string bookId, string locale, out string path)
        {
            path = null;
            if (modId != XiaomanMod.Id || bookId == null) return false;
            if (bookId == "thank_you_note") bookId = "story_note_01";
            const string prefix = "story_note_";
            int number;
            if (!bookId.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(bookId.Substring(prefix.Length), NumberStyles.None,
                               CultureInfo.InvariantCulture, out number)
                || number < 1 || number > 14
                || bookId != prefix + number.ToString("00", CultureInfo.InvariantCulture)) return false;
            path = "Letters/" + Language(locale) + "/" + bookId + ".png";
            return true;
        }
    }
}
