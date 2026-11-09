using System;
using System.Linq;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        private void EnableCocktails()
        {
            ModHook.OnModItemDirectoryInit += RegisterCocktailItems;
            ModHook.OnGameLoadedEarly += MigrateDrinkBuff;
            ModHook.OnGameLoadedLate += ExpireDrinkBuff;
            ModHook.OnWakingUpLate += ExpireDrinkBuff;
            ModHook.OnShutterOpenedEarly += ExpireDrinkBuff;
        }

        private void DisableCocktails()
        {
            ModHook.OnModItemDirectoryInit -= RegisterCocktailItems;
            ModHook.OnGameLoadedEarly -= MigrateDrinkBuff;
            ModHook.OnGameLoadedLate -= ExpireDrinkBuff;
            ModHook.OnWakingUpLate -= ExpireDrinkBuff;
            ModHook.OnShutterOpenedEarly -= ExpireDrinkBuff;
        }

        private void RegisterCocktailItems(ModItemDirectory directory)
        {
            foreach (CocktailSpec spec in CocktailCatalog.Drinks)
            {
                CocktailSpec captured = spec;
                if (!directory.Has(captured.ItemId))
                    directory.Add(captured.ItemId, () => CreateCocktail(captured));
            }
        }

        private void ValidateCocktailAssets(LoadedMod mod)
        {
            int ready = 0;
            foreach (CocktailSpec spec in CocktailCatalog.Drinks)
            {
                Sprite sprite;
                if (!mod.Sprites.TryGetValue(spec.SpriteKey, out sprite) || sprite == null)
                {
                    Log.Warning("Missing cocktail item sprite: " + spec.SpriteKey);
                    continue;
                }
                if ((int)sprite.rect.width != spec.Width || (int)sprite.rect.height != spec.Height)
                {
                    Log.Warning("Cocktail sprite size does not match its inventory footprint: " + spec.Key);
                    continue;
                }
                ready++;
            }
            Log.Log("Cocktail sprites ready: " + ready + "/" + CocktailCatalog.Drinks.Length
                + "; native palette, binary alpha, all six at 16x32 / 1x2 cells.");
        }

        private GameItem CreateCocktail(CocktailSpec spec)
        {
            LoadedMod mod = ModLoader.Mods.Find(entry => entry.Manifest.ID == Id);
            Sprite sprite;
            if (mod == null || !mod.Sprites.TryGetValue(spec.SpriteKey, out sprite) || sprite == null)
            {
                Log.Warning("Cocktail sprite is not ready: " + spec.SpriteKey + "; offer remains pending.");
                return null;
            }
            GameItem item = ItemDirectory.CreateEmptyItem(spec.ItemId).SetSpriteAndShapeFromMod(Id, spec.SpriteKey);
            // Thin glass stems can miss the loader's cell-centre alpha test. A rectangular
            // footprint keeps every glass draggable and purchasable at the intended size.
            ConfigureCocktail(item, spec);
            // Fix saved test-version FOOD/BREVAGE/TREAT tags as well as their shapes.
            item.onLoaded += loaded => ConfigureCocktail(loaded, spec);
            item.unitValue = CocktailSpec.Price;
            AudioHelper.InitGlass(item);
            ModHelper.SetSource(item, Id, ModName);
            return item;
        }

        private static void ApplyCocktailFootprint(GameItem item, CocktailSpec spec)
        {
            int grid = UISettings.current.gridSize;
            int columns = Math.Max(1, spec.Width / grid);
            int rows = Math.Max(1, spec.Height / grid);
            byte[] cells = new byte[columns * rows];
            for (int i = 0; i < cells.Length; i++) cells[i] = 1;
            item.SetShape(new GridShapeBuilder().SetData(cells, columns));
        }

        private void ConfigureCocktail(GameItem item, CocktailSpec spec)
        {
            ApplyCocktailFootprint(item, spec);
            item.name = Text("cocktail_" + spec.Key + "_name");
            item.shortDescription = Text("cocktail_" + spec.Key + "_desc");
            item.flavorText = ItemPresentation.DrinkFlavor(Text("cocktail_origin"), Text("drink_tier_" + spec.Tier),
                Text("drink_buff_" + spec.Effect.ToString().ToLowerInvariant(), spec.EffectAmount), Text("drink_buff_duration"));
            item.RemoveGameItemType("FOOD");
            item.RemoveGameItemType("BREVAGE");
            item.RemoveGameItemType("TREAT");
            item.SetGameItemType("SUBSTANCE");
            item.SetGameItemType("ALCOHOL");
            ConsumableHelper.InitAlcohol(item, 1);
            // Native alcohol is restricted to an addicted character. These gifts
            // are usable by any owner, while retaining native addiction relief.
            item.canActivateSlotItemFunc = (drink, inventory, slot) => CanDrink(drink);
            item.mayActivateSlotItemFunc = (drink, inventory, slot) => CanDrink(drink);
            item.onActivateSlotItemFunc = (drink, inventory, slot) => DrinkCocktail(drink, spec);
        }

        private bool CanDrink(GameItem item)
        {
            return enabled && item != null && item.unitCount > 0 && PlayerStore.HasInstance
                && GeneralHelper.IsItemOwned(item) && StoreStation.Instance.dayCounter >= 1;
        }

        private void DrinkCocktail(GameItem item, CocktailSpec spec)
        {
            if (!CanDrink(item)) return;
            int today = StoreStation.Instance.dayCounter;
            ExpireDrinkBuff();
            DrinkBuffCollection buffs = ReadDrinkBuffs();
            buffs.Replace(spec, today);
            SaveDrinkBuffs(buffs);
            CocktailConsumed(spec);
            if (item.unitCount > 1) item.SetUnitCount(item.unitCount - 1);
            else item.Destroy();
            Log.Log("Consumed one " + spec.Key + ": " + spec.Effect + " +" + spec.EffectAmount
                + " until day " + (today + spec.DurationDays) + "; other drink categories retained.");
        }

        private void CocktailConsumed(CocktailSpec spec)
        {
            if (spec != null && spec.Tier == 2 && PlayerStore.HasInstance && PlayerStore.Instance.healthData != null && HealthData.IsAlcoholAddicted())
                PlayerStore.Instance.healthData.ConsumeAlcohol(1);
            if (AudioManager.Exists) AudioManager.Instance.Play3("drink");
        }

        private DrinkBuffCollection ReadDrinkBuffs()
        {
            MigrateDrinkBuff();
            return DrinkBuffCollection.Restore(ModHelper.GetModData(Id, "drink_buffs_active", ""));
        }

        private void SaveDrinkBuffs(DrinkBuffCollection buffs)
        {
            ModHelper.SetModData(Id, "drink_buffs_active", buffs.Data);
        }

        private void ExpireDrinkBuff()
        {
            if (!PlayerStore.HasInstance || PlayerStore.Instance.healthData == null) return;
            DrinkBuffCollection buffs = ReadDrinkBuffs();
            if (!buffs.Expire(StoreStation.Instance.dayCounter)) return;
            SaveDrinkBuffs(buffs);
        }

        private void MigrateDrinkBuff()
        {
            if (!PlayerStore.HasInstance || PlayerStore.Instance.healthData == null) return;
            int schema = ModHelper.GetModDataInt(Id, "drink_effect_schema", 0);
            if (schema >= 2) return;
            int until = ModHelper.GetModDataInt(Id, "drink_buff_until", -1);
            var buffs = new DrinkBuffCollection();
            var old = new DrinkBuffProgress { Key = ModHelper.GetModData(Id, "drink_buff_key", ""), UntilDay = until };
            if (schema < 1)
            {
                // 0.5.x embedded its temporary luck in the native saved field.
                // Undo that contribution once; retain its original expiry.
                int bonus = Math.Max(0, ModHelper.GetModDataInt(Id, "drink_buff_bonus", 0));
                PlayerStore.Instance.healthData.luck -= bonus;
                old.Effect = DrinkEffect.Luck;
                old.Amount = bonus;
            }
            else
            {
                // 0.6.0 stored exactly one active category without touching native stats.
                old.Effect = DrinkBuffProgress.ValidEffect(ModHelper.GetModDataInt(Id, "drink_buff_effect", 0));
                old.Amount = ModHelper.GetModDataInt(Id, "drink_buff_amount", 0);
            }
            if (old.IsActive(StoreStation.Instance.dayCounter)) buffs.Import(old);
            SaveDrinkBuffs(buffs);
            ModHelper.SetModDataInt(Id, "drink_buff_bonus", 0);
            ModHelper.SetModDataInt(Id, "drink_effect_schema", 2);
        }

        internal static int ActiveDrinkBonus(DrinkEffect effect)
        {
            XiaomanMod mod = Instance;
            if (mod == null || !mod.enabled || !PlayerStore.HasInstance || PlayerStore.Instance.healthData == null) return 0;
            return mod.ReadDrinkBuffs().ActiveAmount(effect, StoreStation.Instance.dayCounter);
        }
    }
}
