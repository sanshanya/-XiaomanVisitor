using System;
using System.Linq;
using Newtonsoft.Json;

namespace XiaomanVisitor
{
    public static class WineCrateRules
    {
        public const string ItemId=XiaomanMod.Id+".wine_crate", SaveKey="wine_crate_v1";
        public const int Footprint=3, Columns=8, Rows=8;
        public static bool IsWine(string id)
        { return id!=null && (id==BrewingProgress.SampleId || id==BrewingProgress.BottleId || CocktailCatalog.All.Any(d => d.ItemId==id)); }
        public static bool CanStore(string id,bool owned,bool crateOwned,bool container)
        { return owned && crateOwned && !container && IsWine(id); }
    }
    public sealed class WineCrateProgress
    {
        public bool Requested;
        public int ItemId;
        public string Phase="waiting",PreservedData="";
        public bool Prepare(int id)
        { if (!Requested || Phase!="waiting" || id<=0) return false;ItemId=id;Phase="placing";return true; }
        public void Placed(bool success) { if (Phase=="placing") Phase=success?"complete":"waiting"; }
        public void Recover(bool owned) { if (Phase=="placing") Phase=owned?"complete":"uncertain"; }
        public static WineCrateProgress Restore(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new WineCrateProgress();
            try {
                var p=JsonConvert.DeserializeObject<WineCrateProgress>(raw);
                if (p!=null && new[]{"waiting","placing","complete","uncertain"}.Contains(p.Phase)
                    && (p.Phase=="waiting" || p.ItemId>0 && p.Requested)) return p;
            } catch (JsonException) { }
            return new WineCrateProgress { Requested=true,Phase="uncertain",PreservedData=raw };
        }
        [JsonIgnore] public string Data { get { return JsonConvert.SerializeObject(this); } }
    }
}
