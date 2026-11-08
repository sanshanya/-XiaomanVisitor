"""Compile the user's final script into local mod assets. No TTS/network calls."""
from pathlib import Path
import argparse
import csv
import hashlib
import json
import re
import sys
import wave

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "package/XiaomanVisitor"
SOURCE = ROOT / "story/小满的十四个星期一_v3.4_专属动作台词_TTS演出整合.md"
MOODS = {"平静": "Neutral", "柔和": "Soft", "害羞": "Shy", "微笑": "Smile", "担心": "Worried", "音效": "Neutral"}
TRACKS = ["title", "intro", "nightfall", "intro2", "midnight", "latenight", "diary", "gallery1", "lost", "gallery2", "deepnight", "outro2", "outro", "rainysound"]
DEMANDS = ["FoodOrWater", "Household", "Food", "Water", "Household", "Water", "Food", "Household", "Food", "Water", "Household", "Food", "Household", "Water"]
BUDGETS = [150, 200, 250, 300, 350, 400, 450, 500, 450, 400, 350, 300, 250, 200]


def save(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def quote(value):
    return json.dumps(value, ensure_ascii=False)


def table_lines(section):
    return [[c.strip() for c in line.strip().strip("|").split("|")] for line in section.splitlines() if re.match(r"^\|\s*`", line)]


def quoted(section):
    return "\n".join(line[1:].lstrip() for line in section.splitlines() if line.startswith(">" )).strip()


def apply_paper_style(book, style):
    height = style["design_height"]
    book.update(background=style["sprite"], width=style["display_width"], height=style["display_height"],
                designWidth=style["design_width"], designHeight=height)
    for page in book["pages"]:
        for element in page["elements"]:
            if element.get("type") != "text":
                continue
            element["color"] = style["ink_color"]
            if element["id"] == "title":
                element.update(x=30, y=30, w=250, h=22, fontSize=12)
            elif element["id"] == "body":
                element.update(x=30, y=70, w=250, h=height-130, fontSize=style.get("body_font_size", 9))
            elif element["id"] == "signature":
                element.update(x=30, y=height-36, w=250, h=24, fontSize=10)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=SOURCE)
    args = parser.parse_args()
    text = args.source.read_text(encoding="utf-8-sig")
    english = json.loads((ROOT / "story/英文剧情与动作_0.01.json").read_text(encoding="utf-8"))
    translated_notes = {n["visit"]: n for n in json.loads((ROOT / "story/纸条译文_0.01.json").read_text(encoding="utf-8"))["notes"]}
    paper_style_path = ROOT / "assets/paper-style.json"
    paper_style = json.loads(paper_style_path.read_text(encoding="utf-8")) if paper_style_path.exists() else None
    loc = {}
    for lang in ("zh", "ja", "en"):
        with (PACKAGE / f"Localization/{lang}.csv").open(encoding="utf-8-sig", newline="") as f:
            loc[lang] = {k: v.replace("\\n", "\n") for k, v in csv.reader(f)}
    lines, episodes, actions = {}, [], []

    def add(key, zh, ja, mood, spoken=None, kind="speech", **extra):
        if key in lines:
            raise ValueError("Duplicate dialogue key: " + key)
        if mood not in MOODS:
            raise ValueError(mood)
        clean = re.sub(r"\[(?:chuckles|laughs|gasps|sighs|breath|exhale|inhales)\]", "", ja)
        assert clean == ja, (key, "TTS tag leaked into subtitles")
        loc["zh"][key] = zh
        loc["ja"][key] = ja
        loc["en"][key] = english[key]
        generate = ja if spoken in (None, "＝字幕", "=字幕") else spoken
        lines[key] = dict(key=key, zh=zh, ja=ja, en=english[key], mood=MOODS[mood], kind=kind,
                          generate_text="" if kind == "sfx" else generate, **extra)

    sections = list(re.finditer(r"(?m)^## 第 (\d{2}) 次｜([^\n]+)\n", text))
    assert len(sections) == 14
    for i, match in enumerate(sections):
        n = int(match[1])
        section = text[match.end():sections[i+1].start() if i+1 < len(sections) else text.index("## 尾声")]
        opening, special = [], {}
        for row in table_lines(section):
            m = re.fullmatch(r"`(xm_\d{2}_[^`]+)`\s*·\s*(.+)", row[0])
            assert m is not None, row
            key, mood = m.groups()
            add(key, row[1], row[2], mood)
            if re.search(r"_(?:\d+|shop)$", key):
                opening.append(key)
            else:
                special[key.rsplit("_", 1)[1]] = key
        note_start = section.index("**周二｜纸条")
        jp_start = section.index("**日本語｜手紙**", note_start)
        end = section.index("**暗线", jp_start)
        cn_note, jp_note = quoted(section[note_start:jp_start]), quoted(section[jp_start:end])
        assert cn_note and jp_note
        for lang, value in (("zh", cn_note), ("ja", jp_note), ("en", cn_note)):
            pieces = value.split("\n\n")
            loc[lang][f"story_note_{n:02d}_title"] = pieces[0]
            loc[lang][f"story_note_{n:02d}_body"] = "\n\n".join(pieces[1:-1])
            loc[lang][f"story_note_{n:02d}_signature"] = pieces[-1]
            loc[lang][f"story_note_{n:02d}_name"] = "小满留下的纸条" if lang != "ja" else "みちるの手紙"
        loc["en"][f"story_note_{n:02d}_title"] = "Dear shopkeeper,"
        loc["en"][f"story_note_{n:02d}_body"] = translated_notes[n]["en"]
        loc["en"][f"story_note_{n:02d}_signature"] = "— Xiaoman"
        loc["en"][f"story_note_{n:02d}_name"] = "A note from Xiaoman"
        episodes.append(dict(index=n, title=match[2], credits=BUDGETS[i], demand=DEMANDS[i], track=TRACKS[i], opening=opening,
                             accept=special.get("accept", "accept_return"), goodbye=special.get("bye", "goodbye"),
                             cocktail=special.get("cocktail", ""), note_id=f"story_note_{n:02d}"))

    generic_section = text[text.index("## 通用交易台词与衔接"):text.index("## 0.7.0 专属动作")]
    for row in table_lines(generic_section):
        m = re.fullmatch(r"`(xm_[^`]+)`\s*·\s*(.+)", row[0])
        if m:
            add(m[1], row[1], row[2], m[2], reference=True)
    legacy_section = text[text.index("### B. 当前"):text.index("### C. 新增")]
    for row in table_lines(legacy_section):
        key = row[0].strip("`")
        mood = row[1].rsplit("／", 1)[1]
        add(key, row[2], row[3], mood, row[4], reference=True)
    source_localization = {r["key"]: r for r in json.loads((ROOT / "reference/action-localization.snapshot.json").read_text(encoding="utf-8")) if r["table"] == "Dialogue"}
    action_section = text[text.index("### C. 新增"):text.index("### D. 十四周")]
    for row in table_lines(action_section):
        native, key = row[0].strip("`"), row[1].strip("`")
        kind = "sfx" if row[2] == "音效" else "speech"
        add(key, row[3], row[4], row[2], row[5], kind=kind, native_key=native)
        original = source_localization[native]
        actions.append(dict(native_key=native, key=key, mood=MOODS[row[2]], kind=kind,
                            originals=[original[lang] for lang in ("zh", "ja", "en") if original.get(lang)]))
    assert len(actions) == 49
    override_section = text[text.index("### D. 十四周"):text.index("### E. 实现验收")]
    for row in table_lines(override_section):
        key = row[0].strip("`")
        lines[key]["generate_text"] = row[2]
        lines[key]["performance_optional"] = True
    # Direct user instructions override the draft's older dynamic-budget discussion.
    for lang in ("zh", "ja", "en"):
        loc[lang]["xm_01_shop"] = loc[lang]["xm_01_shop"].replace("{0}", "150")
        for field in ("title", "body", "signature"):
            loc[lang]["gift_note_" + field] = loc[lang]["story_note_01_" + field]
    lines["xm_01_shop"]["zh"] = loc["zh"]["xm_01_shop"]
    lines["xm_01_shop"]["ja"] = loc["ja"]["xm_01_shop"]
    lines["xm_01_shop"]["generate_text"] = loc["ja"]["xm_01_shop"].replace("150", "百五十")
    loc["ja"]["name"] = "みちる"
    for lang, title in (("zh", "Hello!（雨声混音）"), ("ja", "Hello!（雨音ミックス）"), ("en", "Hello! (Rain Mix)")):
        loc[lang]["collection_hello_track"] = title
    loc["zh"]["cfg_shortcut_tip"] = "营业周一将尚待出现的小满补排到队尾；保留原生顾客数量与顺序，不会越过日期、重复推进或在终章后重新出现。"
    loc["ja"]["cfg_shortcut_tip"] = "月曜の営業中、未登場のみちるを客の列の最後に追加します。既存の客の人数と順序は変えず、日付や物語の進行も飛ばしません。"
    loc["en"]["cfg_shortcut_tip"] = "On an open Monday, queue the pending visit at the end. Keeps every existing customer in order; cannot skip dates or replay a completed visit."
    import apply_item_presentation
    apply_item_presentation.apply_to(loc)
    for lang, rows in loc.items():
        for obsolete in ("cfg_interval", "cfg_interval_tip", "cfg_budget"):
            rows.pop(obsolete, None)
        with (PACKAGE / f"Localization/{lang}.csv").open("w", encoding="utf-8", newline="") as f:
            csv.writer(f, lineterminator="\n").writerows((k, v.replace("\n", "\\n")) for k, v in rows.items())

    template = json.loads((PACKAGE / "Books/thank_you_note.json").read_text(encoding="utf-8"))
    if paper_style:
        apply_paper_style(template, paper_style)
        save(PACKAGE / "Books/thank_you_note.json", template)
    for episode in episodes:
        n, id = episode["index"], episode["note_id"]
        book = json.loads(json.dumps(template))
        book.update(id=id, title="A letter from Xiaoman", height=1000, designHeight=500)
        elements = book["pages"][0]["elements"]
        elements[0].update(loc=f"story_note_{n:02d}_title", y=30)
        elements[1].update(loc=f"story_note_{n:02d}_body", y=70, h=360, fontSize=9)
        elements[2].update(loc=f"story_note_{n:02d}_signature", y=450)
        if paper_style:
            apply_paper_style(book, paper_style)
        book["item"].update(id=f"sansm.xiaoman.visitor.{id}", nameLoc=f"story_note_{n:02d}_name")
        save(PACKAGE / f"Books/{id}.json", book)

    catalog = json.loads((ROOT / "assets/cassettes.json").read_text(encoding="utf-8"))
    catalog = [next(c for c in catalog if c["key"] == key) for key in TRACKS]
    for n, track in enumerate(catalog, 1):
        track["storyNode"] = f"visit_{n:02d}"
    save(ROOT / "assets/cassettes.json", catalog)
    generated = ["// Generated by prepare_story.py / prepare_cassettes.py from assets/cassettes.json.", "namespace XiaomanVisitor", "{", "    public static partial class CassetteCollection", "    {", "        public static readonly CassetteTrack[] Tracks =", "        {"]
    for c in catalog:
        generated.append("            new CassetteTrack(" + ", ".join(quote(c[k]) for k in ("key", "title", "sprite", "storyNode")) + "),")
    generated += ["        };", "    }", "}", ""]
    (ROOT / "src/CassetteCatalog.Generated.cs").write_text("\n".join(generated), encoding="utf-8")
    manifest_path = ROOT / "assets/audio/cassette-manifest.json"
    old_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    save(manifest_path, [dict(next(r for r in old_manifest if r["key"] == key), number=n, storyNode=f"visit_{n:02d}") for n, key in enumerate(TRACKS, 1)])

    csharp = ["// Generated from the user's v3.4 final script. Subtitles live in Localization.", "namespace XiaomanVisitor", "{", "    public static class StoryCatalog", "    {", "        public static readonly StoryEpisode[] Episodes =", "        {"]
    for e in episodes:
        defs = ", ".join("new StoryLineSpec(" + quote(key) + ", Mood." + lines[key]["mood"] + ")" for key in e["opening"])
        csharp.append(f"            new StoryEpisode({e['index']}, {e['credits']}, XiaomanDemand.{e['demand']}, {quote(e['track'])}, {quote(e['accept'])}, {quote(e['goodbye'])}, {quote(e['cocktail'])}, new StoryLineSpec[] {{ {defs} }}),")
    csharp += ["        };", "        public static readonly StoryActionSpec[] Actions =", "        {"]
    for a in actions:
        originals = ", ".join(quote(v) for v in a["originals"])
        csharp.append(f"            new StoryActionSpec({quote(a['native_key'])}, {quote(a['key'])}, Mood.{a['mood']}, {'true' if a['kind']=='sfx' else 'false'}, new string[] {{ {originals} }}),")
    csharp += ["        };", "    }", "}", ""]
    (ROOT / "src/StoryCatalog.Generated.cs").write_text("\n".join(csharp), encoding="utf-8")
    for e in episodes:
        for key in e["opening"] + [e["accept"], e["goodbye"]] + ([e["cocktail"]] if e["cocktail"] else []):
            lines[key]["reference"] = False
    for key in ("intro_1", "right_item", "wrong_item", "repeat", "repeat_food", "repeat_household", "goodbye_discount", "xm_repeat_first", "xm_cocktail_accept"):
        lines[key]["reference"] = False
    # The first chapter line uses intro_1's existing recording, once, with its own subtitle template.
    lines["xm_01_01"]["voice_alias"] = "intro_1"
    voice_keys = [k for k, row in lines.items() if not row.get("reference") and row["kind"] == "speech" and "voice_alias" not in row]
    snapshot = dict(schema=1, version="3.4", source=str(args.source), source_sha256=hashlib.sha256(text.encode("utf-8")).hexdigest(),
                    episodes=episodes, actions=actions, lines=list(lines.values()), voice_keys=voice_keys,
                    sale_rule=dict(first=4, last=13, later_chance_percent=30, count=2, basic_only=True, excluded_visits=[6]),
                    fixed_drink_gift=dict(visit=6, count=1, tier=2, random_once=True, next_day=True, replaces_trade_reward=True),
                    tts=dict(language="ja", voice_id="ayano", model_id="luna-tts-1.5", speed=1.1, audio_format="wav", text_normalization=True))
    save(ROOT / "assets/story.json", snapshot)
    # Ready-to-edit offline script: no external generation here. Preserve the approved first WAV.
    old_script_path = ROOT / "story/配音脚本_ja.json"
    previous = {r["key"]: r for r in json.loads(old_script_path.read_text(encoding="utf-8-sig"))["lines"]}
    production = []
    for key in voice_keys:
        line = lines[key]
        generate = line["generate_text"]
        spoken = re.sub(r"\[[^\]]+\]", "", generate).strip()
        templates = {lang: loc[lang][key] for lang in ("zh", "ja", "en")}
        generic = "{" in loc["ja"][key]
        assert "{" not in spoken and "}" not in spoken, key
        row = dict(key=key, subtitle_templates=templates, spoken_text=spoken, generate_text=generate, generic=generic,
                   reviewed=True, audio="", note="用户v3.4终版；演出标签仅在generate_text，不进入字幕。",
                   source_sha256=hashlib.sha256(json.dumps([templates, generate], ensure_ascii=False).encode("utf-8")).hexdigest())
        prior = previous.get(key, {})
        if (all(prior.get("subtitle_templates", {}).get(lang) == templates[lang] for lang in ("zh", "ja"))
                and prior.get("generate_text", prior.get("spoken_text")) == generate):
            row["audio"] = prior.get("audio", "")
        if key == "intro_1":
            row.update(audio=previous[key]["audio"], spoken_text=previous[key]["spoken_text"], generate_text=previous[key]["spoken_text"],
                       speed=prior.get("speed", 1.0), text_normalization=prior.get("text_normalization", False))
        production.append(row)
    save(old_script_path, dict(schema=1, language="ja", voice_id="ayano", model_id="luna-tts-1.5", speed=1.1, text_normalization=True, lines=production))
    # A quiet double tap for the non-verbal shutter slot. This is not a voice line.
    import numpy as np
    rate = 48000
    samples = np.zeros(int(rate * .7), dtype=np.float64)
    rng = np.random.default_rng(2077)
    for start in (0.03, .28):
        t = np.arange(int(rate * .18)) / rate
        knock = (np.sin(2*np.pi*190*t)*np.exp(-t*45) + .35*np.sin(2*np.pi*470*t)*np.exp(-t*70)
                 + .16*rng.normal(size=len(t))*np.exp(-t*100)) * (1-np.exp(-t*1500))
        offset = int(start*rate)
        samples[offset:offset+len(t)] += knock*.22
    with wave.open(str(PACKAGE / "Sounds/xm_shutter_knock.wav"), "wb") as audio:
        audio.setparams((1, 2, rate, 0, "NONE", "not compressed"))
        audio.writeframes((np.clip(samples,-1,1)*32767).astype('<i2').tobytes())
    if (PACKAGE / "Letters/index.json").exists():
        import prepare_handwriting
        prepare_handwriting.main()
    print(json.dumps(dict(episodes=len(episodes), action_slots=len(actions), sfx=sum(a["kind"]=="sfx" for a in actions),
                          source_rows=len(lines), active_voice_lines=len(voice_keys), new_tts_requests=0, tape_order=TRACKS), ensure_ascii=False))


if __name__ == "__main__":
    main()
