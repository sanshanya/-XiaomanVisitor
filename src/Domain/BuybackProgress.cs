using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    // Committed sale receipts, separate from Monday chapters and gift receipts.
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class BuybackProgress
    {
        [JsonProperty] public int Schema = 1;
        [JsonProperty] public List<BuybackWaterReceipt> Sales = new List<BuybackWaterReceipt>();
        [JsonProperty] public int ReturnSeenDay = -1;
        public const double MinimumWaterDose = 20;
        public static bool HasEffectiveWaterDose(double potency)
        { return !double.IsNaN(potency) && !double.IsInfinity(potency) && potency >= MinimumWaterDose; }
        public static bool CanRecordSale(int day, bool storyFinished) { return day >= 1 && !storyFinished; }
        public int Count { get { return Sales.Count; } }
        public int ReturnDueDay { get { return Count == 0 ? -1 : Sales[Count - 1].Day + 1; } }
        public bool ReturnDue(int today) { return Count > 0 && today >= ReturnDueDay && ReturnSeenDay < ReturnDueDay; }
        public bool NightReady(int today) { return Count == 2 && today >= ReturnDueDay && ReturnSeenDay == today; }
        public int Record(int day, int itemId)
        {
            if (day < 1 || day == int.MaxValue || itemId <= 0) throw new InvalidOperationException("Invalid water receipt");
            if (Count == 2 || Sales.Any(s => s.ItemId == itemId)) return 0;
            if (Count > 0 && day < Sales[Count - 1].Day) throw new InvalidOperationException("Water receipt day moved backwards");
            Sales.Add(new BuybackWaterReceipt { Day = day, ItemId = itemId });
            return Count;
        }
        public bool MeetReturn(int today)
        {
            if (!ReturnDue(today)) return false;
            ReturnSeenDay = today; return true;
        }
        public string Data { get { return JsonConvert.SerializeObject(this); } }
        public static BuybackProgress Restore(string data)
        {
            if (string.IsNullOrEmpty(data)) return new BuybackProgress();
            var result = JsonConvert.DeserializeObject<BuybackProgress>(data);
            if (result == null || result.Schema != 1 || result.Sales == null || result.Count > 2
                || result.Sales.Any(s => s == null || s.Day < 1 || s.Day == int.MaxValue || s.ItemId <= 0)
                || result.Sales.Select(s => s.ItemId).Distinct().Count() != result.Count
                || (result.Count == 2 && result.Sales[1].Day < result.Sales[0].Day)
                || result.ReturnSeenDay < -1 || result.ReturnSeenDay == 0
                || (result.Count == 0 && result.ReturnSeenDay != -1)
                || (result.Count > 0 && result.ReturnSeenDay != -1 && result.ReturnSeenDay <= result.Sales[0].Day))
                throw new InvalidOperationException("Invalid saved water prelude; refusing to reset receipts");
            return result;
        }
    }
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class BuybackWaterReceipt
    {
        [JsonProperty] public int Day, ItemId;
    }
}
