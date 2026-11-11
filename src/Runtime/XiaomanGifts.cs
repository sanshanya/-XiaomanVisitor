using System;
using System.Collections.Generic;
using UnityEngine;

namespace XiaomanVisitor
{
    public sealed partial class XiaomanMod
    {
        internal const string GiftCassetteId = "sansm.xiaoman.visitor.rain_cassette";
        internal const string GiftNoteBookId = "thank_you_note";
        private readonly HashSet<string> readyGiftSounds = new HashSet<string>(StringComparer.Ordinal);
        private bool deliveringGifts;

        private void EnableGifts()
        {
            ModHook.OnModItemDirectoryInit += RegisterGiftItems;
            ModHook.OnGameLoadedLate += TryDeliverThankYouGift;
            ModHook.OnWakingUpLate += TryDeliverThankYouGift;
            ModHook.OnShutterOpenedEarly += TryDeliverThankYouGift;
            ModHook.OnShutterOpenedLate += TryDeliverThankYouGift;
            ModHook.OnReturningStoreLate += TryDeliverThankYouGift;
        }

        private void DisableGifts()
        {
            RestoreCassetteCaseStorage();
            ModHook.OnModItemDirectoryInit -= RegisterGiftItems;
            ModHook.OnGameLoadedLate -= TryDeliverThankYouGift;
            ModHook.OnWakingUpLate -= TryDeliverThankYouGift;
            ModHook.OnShutterOpenedEarly -= TryDeliverThankYouGift;
            ModHook.OnShutterOpenedLate -= TryDeliverThankYouGift;
            ModHook.OnReturningStoreLate -= TryDeliverThankYouGift;
            readyGiftSounds.Clear();
        }

        private void SetGiftAssetsReady(LoadedMod mod)
        {
            readyGiftSounds.Clear();
            foreach (CassetteTrack track in CassetteCollection.Tracks)
            {
                AudioClip clip;
                if (mod.Sounds.TryGetValue(track.SoundKey, out clip) && clip != null && clip.length > 0) readyGiftSounds.Add(track.Key);
                else Log.Warning("Cassette not ready: " + track.Key + "; delivery remains pending.");
            }
            Log.Log("Story cassette collection ready: " + readyGiftSounds.Count + "/14, fixed visit order.");
            TryDeliverThankYouGift();
        }

        private void RegisterGiftItems(ModItemDirectory directory)
        {
            if (!directory.Has(WineCrateRules.ItemId)) directory.Add(WineCrateRules.ItemId,CreateWineCrate);
            if (!directory.Has(CassetteCaseRules.ItemId)) directory.Add(CassetteCaseRules.ItemId, CreateCassetteCase);
            if (!directory.Has(NotebookRules.ItemId)) directory.Add(NotebookRules.ItemId, CreateNotebook);
            if (!directory.Has(GiftCassetteId)) directory.Add(GiftCassetteId, CreateGiftCassette);
            foreach (CassetteTrack track in CassetteCollection.Tracks)
            {
                CassetteTrack captured = track;
                if (!directory.Has(captured.ItemId)) directory.Add(captured.ItemId, () => CreateCollectionCassette(captured));
            }
        }

        private GameItem CreateGiftCassette()
        {
            GameItem item = ItemDirectory.CreateEmptyItem(GiftCassetteId).SetSpriteAndShape("Items/items_amenities", "cassette1");
            Action<GameItem> describe = cassette => {
                cassette.name = Text("gift_tape_name");
                cassette.shortDescription = Text("gift_tape_desc");
                cassette.flavorText = Text("gift_tape_credit");
            };
            describe(item);
            item.onLoaded += describe;
            InitializeCassette(item, "rain_bgm");
            return item;
        }

        private GameItem CreateCollectionCassette(CassetteTrack track)
        {
            GameItem item = ItemDirectory.CreateEmptyItem(track.ItemId).SetSpriteAndShape("Items/items_amenities", track.SpriteKey);
            Action<GameItem> describe = cassette => {
                string title = track.Key == "rainysound" ? Text("collection_hello_track") : track.Title;
                cassette.name = track.Key == "title" ? Text("collection_title_name") : Text("collection_tape_name", title);
                cassette.shortDescription = Text("collection_tape_desc", title);
                cassette.flavorText = track.Key == "title" ? Text("collection_title_flavor")
                    : Text("collection_tape_flavor");
            };
            describe(item);
            item.onLoaded += describe;
            InitializeCassette(item, track.SoundKey);
            return item;
        }

        private void InitializeCassette(GameItem item, string soundKey)
        {
            item.unitValue = 0;
            item.SetGameItemType("ART");
            item.EnableTag("IMPORTANT_TAG");
            CassetteHelper.InitCassetteItem(item, ModHelper.GetSoundIdentifier(Id, soundKey));
            AudioHelper.InitCassette(item);
            ModHelper.SetSource(item, Id, ModName);
        }

        // Compatibility entry point for old test scripts. Actual story rewards now use visits.
        public static bool CompleteStoryNode(string nodeId)
        {
            Instance?.Log.Log("Story reward " + nodeId + " follows the fourteen-visit schedule; no trade unlock is needed.");
            return false;
        }

        private void TryDeliverThankYouGift()
        {
#if XIAOMAN_BUYBACK_TEST
            if (SideStories.BuybackTestMod.LineClosed) return;
#endif
            if (!enabled || deliveringGifts || !PlayerStore.HasInstance || EmporiumEntry.Instance == null
                || StoreStation.Instance == null || ModHelper.IsOutsideStore()) return;
            deliveringGifts = true;
            try
            {
                int today = StoreStation.Instance.dayCounter;
                TryDeliverWineCrate();
                FreezeThirdWorkLetter();
                FreezeFourthLetter();
                TryDeliverWorkThanksGift();
                TryDeliverImprovGift();
                TryDeliverRokaGift();
                StoryProgress progress = ReadStory();
                EnsureCassetteCaseStorage();
                TryDeliverNotebook(today, progress);
                TryDeliverCassetteCase(today, progress);
                StoryVisitRecord pending = progress.DueGift(today);
                if (pending != null)
                {
                    StoryEpisode episode = StoryRules.Episode(pending.Index);
                    for (int part = 0; part < pending.PartCount; part++)
                    {
                        if (pending.HasPart(part)) continue;
                        bool fixedDrink = pending.HasFixedDrink && part == 2;
                        DrinkRewardProgress drinkReceipt = null;
                        if (fixedDrink)
                        {
                            drinkReceipt = ReadDrinkRewards();
                            // Recover a saved receipt whose letter-bundle mask was
                            // not yet committed, without spawning the drink again.
                            if (drinkReceipt.DeliveredTrades.Contains(pending.Day))
                            {
                                progress.MarkPartDelivered(pending.Index, part, today);
                                SaveStory(progress);
                                continue;
                            }
                            if (!drinkReceipt.CanDeliverStoryDrink(pending.Day, today)) return;
                        }
                        GameItem item;
                        if (part == 0) item = ModHelper.SpawnPaperItem(Id, episode.NoteId);
                        else if (part == 1)
                        {
                            if (!readyGiftSounds.Contains(episode.TrackKey)) return;
                            item = ModHelper.SpawnItem(CassetteCollection.Find(episode.TrackKey).ItemId);
                        }
                        else item = ModHelper.SpawnItem(CocktailCatalog.Find(fixedDrink ? pending.GiftDrinkKey : StoryRules.FinalDrinks[part - 2]).ItemId);
                        if (!TryPlaceStoryGift(item)) return;
                        if (fixedDrink)
                        {
                            drinkReceipt.RecordStoryDrink(pending.Day, today, pending.GiftDrinkKey);
                            SaveDrinkRewards(drinkReceipt);
                        }
                        progress.MarkPartDelivered(pending.Index, part, today);
                        SaveStory(progress);
                        Log.Log("Story gift " + pending.Index + "/14, part " + part + " delivered on day " + today + ".");
                        if (part<2 && StoreUIManager.Instance!=null && ReputationUIManager.Instance!=null) {
                            string owner=item.parentInventory?.GetParentItem()?.identifier;
                            string key=part==0 ? owner==NotebookRules.ItemId ? "gift_note_stored" : "gift_note_arrived"
                                : owner==CassetteCaseRules.ItemId ? "gift_tape_stored" : "gift_tape_arrived";
                            StoreUIManager.Instance.Notify(Text(key));
                        }
                    }
                }
                TryDeliverDrink(today);
            }
            finally { deliveringGifts = false; }
        }

        private bool TryPlaceGift(GameItem item)
        {
            if (item == null) return false;
            ModHelper.SetItemOwnerShip(item, true);
            GraphUtils.TryAcceptAllMid(EmporiumEntry.Instance.frontInvinvElement, item);
            if (item.parentInventory == null && TryStoreWineGift(item)) return true;
            if (item.parentInventory == null) GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement, item);
            if (item.parentInventory == null) GraphUtils.TryAcceptAll(EmporiumEntry.Instance.backInvinvElement, item);
            if (item.parentInventory != null) return true;
            item.Destroy();
            Log.Warning("Xiaoman's gift is waiting for free counter/inventory space.");
            return false;
        }
    }
}
