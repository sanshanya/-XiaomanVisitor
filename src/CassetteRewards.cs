using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace XiaomanVisitor
{
    public sealed class CassetteTrack
    {
        public readonly string Key;
        public readonly string Title;
        public readonly string SpriteKey;
        public readonly string StoryNode;

        public CassetteTrack(string key, string title, string spriteKey, string storyNode)
        {
            Key = key;
            Title = title;
            SpriteKey = spriteKey;
            StoryNode = storyNode;
        }

        public string ItemId { get { return XiaomanMod.Id + ".tape." + Key; } }
        public string SoundKey { get { return "rain_" + Key; } }
        public bool IsStoryReward { get { return !string.IsNullOrEmpty(StoryNode); } }
    }

    public static partial class CassetteCollection
    {
        public const string FirstTradeNode = "first_trade";

        public static CassetteTrack Find(string key)
        {
            return Tracks.FirstOrDefault(track => track.Key == key);
        }

        public static bool HasStoryNode(string node)
        {
            return Tracks.Any(track => track.IsStoryReward && track.StoryNode == node);
        }
    }

    public sealed class PendingCassetteReward
    {
        public readonly string TrackKey;
        public readonly int DueDay;
        public readonly int TradeDay;

        public PendingCassetteReward(string key, int dueDay, int tradeDay)
        {
            TrackKey = key;
            DueDay = dueDay;
            TradeDay = tradeDay;
        }
    }

    // Persist only strings, ints and bools through ModHelper, never these CLR objects.
    // Selection happens when the real trade completes, so delivery retries cannot reroll it.
    public sealed class CassetteRewardProgress
    {
        public int FirstTradeDay = -1;
        public int LastRewardTradeDay = -1;
        public bool NoteDelivered;
        public readonly HashSet<string> Received = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> StoryNodes = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<PendingCassetteReward> Pending = new List<PendingCassetteReward>();

        public bool IsAssigned(string key)
        {
            return Received.Contains(key) || Pending.Any(reward => reward.TrackKey == key);
        }

        public bool UnlockStoryNode(string node)
        {
            return CassetteCollection.HasStoryNode(node) && StoryNodes.Add(node);
        }

        public bool RecordTrade(int today, int randomRoll, bool safeDrinkingWater, out string selectedKey)
        {
            selectedKey = null;
            if (today < 1 || today <= LastRewardTradeDay) return false;
            // The opening cannot be advanced by a sale in the other direction,
            // food, an empty bottle, or merely meeting the customer.
            bool firstWater = FirstTradeDay < 1;
            if (firstWater && !safeDrinkingWater) return false;
            LastRewardTradeDay = today;
            if (firstWater) FirstTradeDay = today;
            UnlockStoryNode(CassetteCollection.FirstTradeNode);

            if (firstWater)
            {
                CassetteTrack opening = CassetteCollection.Tracks.First(t => t.StoryNode == CassetteCollection.FirstTradeNode);
                // An unclaimed Title from the old experiment now belongs to this
                // water trade. Never duplicate a Title already in the collection.
                Pending.RemoveAll(p => p.TrackKey == opening.Key);
                if (!Received.Contains(opening.Key))
                {
                    selectedKey = opening.Key;
                    Pending.Insert(0, new PendingCassetteReward(opening.Key, today + 1, today));
                }
                return true;
            }

            CassetteTrack selected = CassetteCollection.Tracks.FirstOrDefault(track =>
                track.IsStoryReward && StoryNodes.Contains(track.StoryNode) && !IsAssigned(track.Key));
            if (selected == null)
            {
                CassetteTrack[] pool = CassetteCollection.Tracks.Where(track =>
                    !track.IsStoryReward && !IsAssigned(track.Key)).ToArray();
                if (pool.Length > 0) selected = pool[(int)((uint)randomRoll % (uint)pool.Length)];
            }
            if (selected != null)
            {
                selectedKey = selected.Key;
                Pending.Add(new PendingCassetteReward(selected.Key, today + 1, today));
            }
            return true;
        }

        public bool Deliver(string key)
        {
            int removed = Pending.RemoveAll(reward => reward.TrackKey == key);
            if (removed == 0) return false;
            Received.Add(key);
            return true;
        }

        public PendingCassetteReward[] DueRewards(int today)
        {
            if (FirstTradeDay < 1 || today <= FirstTradeDay) return new PendingCassetteReward[0];
            return Pending.Where(reward => today >= reward.DueDay)
                .OrderBy(reward => CassetteCollection.Find(reward.TrackKey).StoryNode == CassetteCollection.FirstTradeNode ? 0 : 1)
                .ThenBy(reward => reward.TradeDay).ToArray();
        }

        public bool IsNoteDue(int today)
        {
            return FirstTradeDay >= 1 && today > FirstTradeDay && !NoteDelivered;
        }

        public string ReceivedData { get { return string.Join(",", Received.OrderBy(key => key, StringComparer.Ordinal)); } }
        public string StoryData { get { return string.Join(",", StoryNodes.OrderBy(key => key, StringComparer.Ordinal)); } }
        public string PendingData
        {
            get
            {
                return string.Join(";", Pending.Select(reward => reward.TrackKey + ","
                    + reward.DueDay.ToString(CultureInfo.InvariantCulture) + ","
                    + reward.TradeDay.ToString(CultureInfo.InvariantCulture)));
            }
        }

        public static CassetteRewardProgress Restore(int firstDay, int lastDay, bool noteDelivered,
            string received, string pending, string story)
        {
            var progress = new CassetteRewardProgress
            {
                FirstTradeDay = firstDay,
                LastRewardTradeDay = lastDay,
                NoteDelivered = noteDelivered
            };
            foreach (string key in (received ?? "").Split(','))
                if (CassetteCollection.Find(key) != null) progress.Received.Add(key);
            foreach (string node in (story ?? "").Split(',')) progress.UnlockStoryNode(node);
            foreach (string row in (pending ?? "").Split(';'))
            {
                string[] parts = row.Split(',');
                int due, trade;
                if (parts.Length != 3 || CassetteCollection.Find(parts[0]) == null
                    || progress.IsAssigned(parts[0])
                    || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out due)
                    || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out trade)
                    || trade < 1 || due <= trade) continue;
                progress.Pending.Add(new PendingCassetteReward(parts[0], due, trade));
            }
            return progress;
        }

        public static CassetteRewardProgress MigrateLegacy(int firstDay, bool cassetteDelivered, bool noteDelivered)
        {
            var progress = new CassetteRewardProgress
            {
                NoteDelivered = noteDelivered
            };
            // A test-version trade does not prove she bought safe drinking water.
            // Keep collected items; start the new opening at its real water gate.
            if (cassetteDelivered) progress.Received.Add("nightfall");
            return progress;
        }

        public void MigrateTestChapter()
        {
            FirstTradeDay = -1;
            LastRewardTradeDay = -1;
            StoryNodes.Remove(CassetteCollection.FirstTradeNode);
            // Keep earned pending cassettes, held until the new water opening.
        }
    }
}
