using System;
using System.Linq;

namespace XiaomanVisitor
{
    // Dialogue evidence only, not a simulator of ordinary NPC turnover. Read
    // existing receipts; never create production, orders, sales or a new ledger.
    public static class WeekTenDialogue
    {
        public const string InventoryEntry="xm_tavern_10_chat_01";
        public const string Supported="xm_tavern_10_chat_supported_01";
        public const string Neutral="xm_tavern_10_chat_neutral_01";

        public static string InventorySequence(int day,SupplyProgress supply,TavernWorkProgress work,TavernProgress tavern)
        {
            int weekStart=day-(day-1)%7, from=Math.Max(1,day-6);
            if (work!=null && !work.RecoveryBlocked && work.Shifts.Any(s => s.Status=="complete" && s.Day>=from && s.Day<=day))
                return Supported;
            if (supply==null || supply.Blocked || supply.Pending!=null) return Neutral;
            // A complete partner purchase is a real sale, not evidence of the
            // partner reselling it. Unsettled bar reservations are not sales.
            if (supply.Trades.Any(t => t.Direction=="out" && t.Status=="complete" && t.Day>=from && t.Day<=day
                && supply.Batches.Any(b => b.Id==t.BatchId))
                || tavern!=null && !tavern.RecoveryBlocked && tavern.Drinks.Any(d => d.Status=="complete" && d.Day>=from && d.Day<=day
                    && supply.BarUses.Any(u => u.Id==d.Id && u.Settled && !u.Cancelled))) return Supported;
            // Conservatively require BOTH actual common ingredients and leftover
            // BAR shares produced last week. Player shares never imply bar surplus.
            return supply.Water>0 && supply.Juice>0 && supply.Batches.Any(b => b.Day>=weekStart-7 && b.Day<weekStart
                && LuxuryDrinkCatalog.IsStandard(b.Key) && b.StoreRemaining>0)
                ? InventoryEntry : Neutral;
        }
    }
}
