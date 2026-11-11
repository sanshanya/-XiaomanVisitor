// Generated from approved weekly implementation sources by prepare_story.py.
using System.Linq;
namespace XiaomanVisitor { public static class TavernDialogue {
public static readonly StoryLineSpec[] Lines = new StoryLineSpec[] {
new StoryLineSpec("xm_tavern_w02_entry_01", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_entry_02", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_entry_03", Mood.Shy),
new StoryLineSpec("xm_tavern_w02_reentry", Mood.Smile),
new StoryLineSpec("xm_tavern_greeting", Mood.Soft),
new StoryLineSpec("xm_tavern_greeting_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_menu_open", Mood.Soft),
new StoryLineSpec("xm_tavern_w02_menu", Mood.Smile),
new StoryLineSpec("xm_tavern_order", Mood.Soft),
new StoryLineSpec("xm_tavern_serve", Mood.Smile),
new StoryLineSpec("xm_tavern_no_money", Mood.Neutral),
new StoryLineSpec("xm_tavern_no_takeaway", Mood.Shy),
new StoryLineSpec("xm_tavern_drink_limit", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_chat_01", Mood.Shy),
new StoryLineSpec("xm_tavern_w02_chat_02", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_chat_03", Mood.Soft),
new StoryLineSpec("xm_tavern_w02_chat_04", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_help_intro", Mood.Shy),
new StoryLineSpec("xm_tavern_w02_help_done", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_help_skip", Mood.Soft),
new StoryLineSpec("xm_tavern_w02_rain_01", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_rain_02", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_tape_intro", Mood.Soft),
new StoryLineSpec("xm_tavern_greeting_plain", Mood.Soft),
new StoryLineSpec("xm_tavern_chat_rest", Mood.Soft),
new StoryLineSpec("xm_tavern_drink_limit_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_help_intro_unknown", Mood.Shy),
new StoryLineSpec("xm_tavern_w02_help_done_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_rain_01_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w02_tape_intro_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_chat_rest_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_work_offer_01", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_offer_02", Mood.Shy),
new StoryLineSpec("xm_tavern_w03_work_w02_callback", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_start", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_work_done_01", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_done_02", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_decline", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_work_done_today", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_no_orders", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_chat_01", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_chat_02", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_chat_after_work", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_work_offer_01_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_w02_callback_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_start_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_work_done_01_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_done_02_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_decline_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_work_done_today_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w03_work_no_orders_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_chat_01_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_w03_chat_after_work_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_w04_supply_01", Mood.Smile),
new StoryLineSpec("xm_tavern_w04_supply_02", Mood.Soft),
new StoryLineSpec("xm_tavern_w04_supply_03", Mood.Smile),
new StoryLineSpec("xm_tavern_w04_supply_yes", Mood.Smile),
new StoryLineSpec("xm_tavern_w04_supply_later", Mood.Soft),
new StoryLineSpec("xm_tavern_w04_supply_existing", Mood.Soft),
new StoryLineSpec("xm_tavern_w04_supply_early", Mood.Soft),
new StoryLineSpec("xm_tavern_w04_supply_01_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w04_supply_02_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_w04_supply_03_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w04_supply_yes_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_w04_supply_later_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_w04_supply_existing_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_w04_supply_early_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_05_new_01", Mood.Smile),
new StoryLineSpec("xm_tavern_05_new_02", Mood.Smile),
new StoryLineSpec("xm_tavern_05_new_03", Mood.Shy),
new StoryLineSpec("xm_tavern_05_soldout", Mood.Neutral),
new StoryLineSpec("xm_tavern_05_daily", Mood.Soft),
new StoryLineSpec("xm_tavern_05_new_01_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_05_new_02_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_05_new_03_unknown", Mood.Shy),
new StoryLineSpec("xm_tavern_05_soldout_unknown", Mood.Neutral),
new StoryLineSpec("xm_tavern_05_daily_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_06_chat_01", Mood.Smile),
new StoryLineSpec("xm_tavern_06_chat_02", Mood.Soft),
new StoryLineSpec("xm_tavern_06_chat_01_unknown", Mood.Smile),
new StoryLineSpec("xm_tavern_06_chat_02_unknown", Mood.Soft),
new StoryLineSpec("xm_tavern_07_chat_01", Mood.Soft),
new StoryLineSpec("xm_tavern_07_chat_02", Mood.Smile),
new StoryLineSpec("xm_tavern_08_chat_01", Mood.Smile),
new StoryLineSpec("xm_tavern_08_chat_02", Mood.Soft),
new StoryLineSpec("xm_tavern_09_chat_01", Mood.Smile),
new StoryLineSpec("xm_tavern_09_chat_02", Mood.Smile),
new StoryLineSpec("xm_tavern_09_chat_03", Mood.Smile),
new StoryLineSpec("xm_tavern_10_chat_01", Mood.Soft),
new StoryLineSpec("xm_tavern_10_chat_02", Mood.Neutral),
new StoryLineSpec("xm_tavern_10_chat_03", Mood.Soft),
new StoryLineSpec("xm_tavern_10_chat_supported_01", Mood.Soft),
new StoryLineSpec("xm_tavern_10_chat_supported_02", Mood.Soft),
new StoryLineSpec("xm_tavern_10_chat_supported_03", Mood.Neutral),
new StoryLineSpec("xm_tavern_10_chat_neutral_01", Mood.Soft),
new StoryLineSpec("xm_tavern_10_chat_neutral_02", Mood.Neutral),
new StoryLineSpec("xm_tavern_10_chat_neutral_03", Mood.Soft),
new StoryLineSpec("xm_tavern_10_regular_01", Mood.Smile),
new StoryLineSpec("xm_tavern_10_regular_02", Mood.Soft),
new StoryLineSpec("xm_tavern_10_regular_03", Mood.Smile),
new StoryLineSpec("xm_tavern_10_regular_04", Mood.Smile),
new StoryLineSpec("xm_tavern_11_chat_01", Mood.Soft),
new StoryLineSpec("xm_tavern_11_chat_02", Mood.Neutral),
new StoryLineSpec("xm_tavern_11_chat_03", Mood.Soft),
new StoryLineSpec("xm_tavern_12_roka_01", Mood.Smile),
new StoryLineSpec("xm_tavern_12_roka_02", Mood.Soft),
new StoryLineSpec("xm_tavern_12_roka_03", Mood.Smile),
new StoryLineSpec("xm_tavern_12_roka_04", Mood.Soft),
new StoryLineSpec("xm_tavern_12_business_01", Mood.Neutral),
new StoryLineSpec("xm_tavern_12_business_02", Mood.Soft),
new StoryLineSpec("xm_tavern_12_business_03", Mood.Neutral),
new StoryLineSpec("xm_tavern_12_business_04", Mood.Worried),
new StoryLineSpec("xm_tavern_12_business_05", Mood.Soft),
new StoryLineSpec("xm_tavern_13_entry", Mood.Soft),
new StoryLineSpec("xm_tavern_13_tape", Mood.Smile),
new StoryLineSpec("xm_tavern_13_chat", Mood.Soft),
};
private static readonly string[] Chat5 = new string[] {"xm_tavern_05_daily"};
private static readonly string[] Chat6 = new string[] {"xm_tavern_06_chat_01","xm_tavern_06_chat_02"};
private static readonly string[] Chat7 = new string[] {"xm_tavern_07_chat_01","xm_tavern_07_chat_02"};
private static readonly string[] Chat8 = new string[] {"xm_tavern_08_chat_01","xm_tavern_08_chat_02"};
private static readonly string[] Chat9 = new string[] {"xm_tavern_09_chat_01"};
private static readonly string[] Chat10 = new string[] {"xm_tavern_10_chat_01","xm_tavern_10_regular_01"};
private static readonly string[] Chat11 = new string[] {"xm_tavern_11_chat_01"};
private static readonly string[] Chat12 = new string[] {"xm_tavern_12_business_01"};
private static readonly string[] Chat13 = new string[] {"xm_tavern_13_chat"};
private static readonly string[] Chat14 = new string[] {};
private static readonly string[] Sequence0 = new string[] {"xm_tavern_09_chat_01","xm_tavern_09_chat_02","xm_tavern_09_chat_03"};
private static readonly string[] Sequence1 = new string[] {"xm_tavern_10_chat_01","xm_tavern_10_chat_02","xm_tavern_10_chat_03"};
private static readonly string[] Sequence2 = new string[] {"xm_tavern_10_chat_supported_01","xm_tavern_10_chat_supported_02","xm_tavern_10_chat_supported_03"};
private static readonly string[] Sequence3 = new string[] {"xm_tavern_10_chat_neutral_01","xm_tavern_10_chat_neutral_02","xm_tavern_10_chat_neutral_03"};
private static readonly string[] Sequence4 = new string[] {"xm_tavern_10_regular_01","xm_tavern_10_regular_02","xm_tavern_10_regular_03","xm_tavern_10_regular_04"};
private static readonly string[] Sequence5 = new string[] {"xm_tavern_11_chat_01","xm_tavern_11_chat_02","xm_tavern_11_chat_03"};
private static readonly string[] Sequence6 = new string[] {"xm_tavern_12_business_01","xm_tavern_12_business_02","xm_tavern_12_business_03","xm_tavern_12_business_04","xm_tavern_12_business_05"};
private static readonly string[] Sequence7 = new string[] {"xm_tavern_12_roka_01","xm_tavern_12_roka_02","xm_tavern_12_roka_03","xm_tavern_12_roka_04"};
public static string[] ChatSequence(string key) {
if (key=="xm_tavern_09_chat_01") return Sequence0;
if (key=="xm_tavern_10_chat_01") return Sequence1;
if (key=="xm_tavern_10_chat_supported_01") return Sequence2;
if (key=="xm_tavern_10_chat_neutral_01") return Sequence3;
if (key=="xm_tavern_10_regular_01") return Sequence4;
if (key=="xm_tavern_11_chat_01") return Sequence5;
if (key=="xm_tavern_12_business_01") return Sequence6;
if (key=="xm_tavern_12_roka_01") return Sequence7;
return new[]{key}; }
public static bool ChatReady(string key,int day,int encounterDay) {
if (key=="xm_tavern_10_regular_01") return encounterDay>0 && day>=System.Math.Max(66L,(long)encounterDay+2);
return true; }
public static int WeeklyChatVisit(int day,int visits) {
if (day>=92 && visits>=14) return 14;
if (day>=85 && visits>=13) return 13;
if (day>=78 && visits>=12) return 12;
if (day>=71 && visits>=11) return 11;
if (day>=64 && visits>=10) return 10;
if (day>=57 && visits>=9) return 9;
if (day>=50 && visits>=8) return 8;
if (day>=43 && visits>=7) return 7;
if (day>=36 && visits>=0) return 6;
if (day>=29 && visits>=0) return 5;
return 0; }
public static string[] WeeklyChatPool(int day,int visits) { switch (WeeklyChatVisit(day,visits)) {
case 5: return Chat5;
case 6: return Chat6;
case 7: return Chat7;
case 8: return Chat8;
case 9: return Chat9;
case 10: return Chat10;
case 11: return Chat11;
case 12: return Chat12;
case 13: return Chat13;
case 14: return Chat14;
default: return null; } }
public static Mood MoodFor(string key) { return Lines.FirstOrDefault(l => l.Key == key)?.Mood ?? Mood.Neutral; }
public static string KeyFor(string key, bool known) { return !known && Lines.Any(l => l.Key == key+"_unknown") ? key+"_unknown" : key; }
} }
