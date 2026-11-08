using System.Collections.Generic;

namespace XiaomanVisitor
{
    public static class CustomerQueuePolicy
    {
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
