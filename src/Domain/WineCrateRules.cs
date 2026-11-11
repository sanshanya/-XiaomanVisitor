using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XiaomanVisitor
{
    public static class WineCrateRules
    {
        public const string ItemId=XiaomanMod.Id+".wine_crate", SaveKey="wine_crate_v1";
        public const string SpriteKey="xiaoman_wine_crate";
        public const int Footprint=3, Columns=8, Rows=8;
        public static bool IsBottledWine(string id)
        { return id==BrewingProgress.SampleId || id==BrewingProgress.BottleId; }
        public static bool IsWine(string id)
        { return id!=null && (IsBottledWine(id) || CocktailCatalog.All.Any(d => d.ItemId==id)); }
        public static bool CanStore(string id,bool owned,bool crateOwned,bool container)
        { return owned && crateOwned && !container && IsWine(id); }
    }
    public sealed class WineCrateProgress
    {
        public int Schema=2;
        public bool Requested;
        public int ItemId;
        public string Phase="waiting",PreservedData="";
        public bool RequestForDelivery(string bottleId,bool delivered)
        {
            if (!delivered || !WineCrateRules.IsBottledWine(bottleId) || Requested || Phase!="waiting") return false;
            Requested=true;return true;
        }
        public bool Prepare(int id)
        { if (!Requested || Phase!="waiting" || id<=0) return false;ItemId=id;Phase="placing";return true; }
        public void Placed(bool success) { if (Phase=="placing") Phase=success?"complete":"waiting"; }
        public void Recover(bool owned) { if (Phase=="placing") Phase=owned?"complete":"uncertain"; }
        public static WineCrateProgress Restore(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new WineCrateProgress();
            try {
                var root=JObject.Parse(raw);
                int schema=root.Value<int?>("Schema") ?? 1;
                var p=root.ToObject<WineCrateProgress>();
                if (p!=null && new[]{"waiting","placing","complete","uncertain"}.Contains(p.Phase)
                    && schema>=1 && schema<=2 && (p.Phase=="waiting" || p.ItemId>0 && p.Requested)) {
                    // Old waiting requests could come from owning/buying a drink,
                    // or from a gift that never fitted. Only a new real gift may
                    // request that crate. Existing physical identities survive.
                    if (schema==1 && p.Phase=="waiting") { p.Requested=false;p.ItemId=0; }
                    p.Schema=2;return p;
                }
            } catch (JsonException) { }
            return new WineCrateProgress { Requested=true,Phase="uncertain",PreservedData=raw };
        }
        [JsonIgnore] public string Data { get { return JsonConvert.SerializeObject(this); } }
    }
}
