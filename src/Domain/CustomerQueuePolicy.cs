using System.Collections.Generic;

namespace XiaomanVisitor
{
    public enum XiaomanVisitKind { None, Story, WorkThanks, Brewing }
    public static class CustomerQueuePolicy
    {
        public const string WorkThanksId = XiaomanMod.ClientId + ".work_thanks";
        public static bool IsXiaoman(string id)
        { return id == XiaomanMod.ClientId || id == WorkThanksId || id == BrewingProgress.ClientId; }
        public static XiaomanVisitKind Next(int day,StoryProgress story,TavernWorkProgress work,bool tavernEnabled,bool brewingDue=false,bool tavernContinues=false)
        {
            if (story.MayVisit(day)) return XiaomanVisitKind.Story;
            // Finishing fourteen chapters does not cancel a continuing tavern's
            // signed appointments. Financial departure remains adapter-owned.
            bool sideVisits=tavernEnabled && (!story.Finished || tavernContinues);
            if (sideVisits && BrewingProgress.VisitDay(day) && brewingDue) return XiaomanVisitKind.Brewing;
            // A Monday with a completed/queued chapter never gets a second Xiaoman.
            return sideVisits && !StoryRules.IsMonday(day) && work.ThanksDue(day)
                ? XiaomanVisitKind.WorkThanks : XiaomanVisitKind.None;
        }
        // Reposition only the mod's own pending visitor. Never insert over an
        // existing slot, remove another customer, or reorder the native clients.
        public static bool MoveToTail<T>(List<T> queue, T visitor) where T : class
        {
            if (queue == null || visitor == null) return false;
            int index = queue.IndexOf(visitor);
            if (index < 0 || index == queue.Count - 1) return false;
            queue.RemoveAt(index);
            queue.Add(visitor);
            return true;
        }
    }
}
