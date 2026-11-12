using System;
using Newtonsoft.Json;

namespace XiaomanVisitor.SideStories
{
    internal sealed class BuybackDoorProgress
    {
        internal const string SaveKey = "door_refusal_v1";
        public int schema = 2, day = -1, refusals;
        public bool noteDelivered;
        internal bool NeedsRetry(int today) { return refusals > 0 && refusals < 3 && day == today; }
        internal bool HasNews(int today) { return refusals == 3 && today == day + 1; }
        internal bool NeedsNote(int today) { return refusals == 3 && today > day && !noteDelivered; }
        internal void Refuse(int today)
        {
            if (today < 1 || today == int.MaxValue) throw new InvalidOperationException("Invalid refusal day");
            if (refusals == 0) { day = today; refusals = 1; }
            else if (day == today && refusals < 3) refusals++;
            else if (day != today) throw new InvalidOperationException("Refusal belongs to another day");
        }
        internal void Open() { day = -1; refusals = 0; noteDelivered = false; }
        internal string Data { get { return JsonConvert.SerializeObject(new { schema, day, refusals, noteDelivered }); } }
        internal static BuybackDoorProgress Restore(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new BuybackDoorProgress();
            var state = BuybackStage.ParseData<BuybackDoorProgress>(raw);
            if (state != null && state.schema == 1)
            {
                if (state.refusals == 2) state.refusals = 3; // Old completed endings keep their next-day result.
                state.schema = 2;
            }
            if (state == null || state.schema != 2 || state.refusals < 0 || state.refusals > 3
                || (state.refusals == 0 ? state.day != -1 : state.day < 1 || state.day == int.MaxValue))
                throw new InvalidOperationException("Invalid refusal branch state");
            return state;
        }
    }

    // Populated by the runtime JSON parser.
#pragma warning disable CS0649
    internal sealed class BuybackRefusalText
    {
        public int schema;
        public string retry_line, farewell_line, news_title, news_body, note_name, note_photo, note_message;
        public string[] third_lines;
        internal void Validate()
        {
            if (schema != 1 || string.IsNullOrWhiteSpace(retry_line) || retry_line.Length > 100
                || string.IsNullOrWhiteSpace(news_title) || news_title.Length > 40
                || string.IsNullOrWhiteSpace(news_body) || news_body.Length > 300
                || third_lines == null || third_lines.Length != 2 || Array.Exists(third_lines, string.IsNullOrWhiteSpace)
                || string.IsNullOrWhiteSpace(farewell_line) || string.IsNullOrWhiteSpace(note_name)
                || note_photo != "cg_05.png" || string.IsNullOrWhiteSpace(note_message))
                throw new InvalidOperationException("Invalid authored refusal dialogue/news");
        }
    }
}
