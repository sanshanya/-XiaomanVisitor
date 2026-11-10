"""Compile the user's final script into local mod assets. No TTS/network calls."""
from pathlib import Path
import argparse
import csv
import hashlib
import json
import re
import sys
import wave
import week_two_source
import week_three_source
import week_four_source
import week_five_six_source

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "package/XiaomanVisitor"
SOURCE = ROOT / "story/小满的十四个星期一_v3.4_专属动作台词_TTS演出整合.md"
MOODS = {"平静": "Neutral", "柔和": "Soft", "害羞": "Shy", "微笑": "Smile", "担心": "Worried", "音效": "Neutral"}
SCHEDULE_PATH = ROOT / "story/来访安排.json"
SCHEDULE = json.loads(SCHEDULE_PATH.read_text(encoding="utf-8"))["visits"]
assert [row["visit"] for row in SCHEDULE] == list(range(1,15))
TRACKS = [row["track_key"] for row in SCHEDULE]
DEMANDS = [row["demand"] for row in SCHEDULE]
BUDGETS = [row["credits"] for row in SCHEDULE]



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
    week_two = week_two_source.read()
    week_three = week_three_source.read()
    week_four = week_four_source.read()
    week_five = week_five_six_source.read(5)
    week_six = week_five_six_source.read(6)
    overlays={2:week_two,3:week_three,4:week_four,5:week_five,6:week_six}
    week_five_six_source.generate()
    week_four_source.generate()
    week_three_source.generate()
    week_two_source.generate(dict(week_two,lines=dict(week_two['lines'],**week_three['lines'],**week_four['lines'],**week_five['lines'],**week_six['lines'])))
    english = json.loads((ROOT / "story/英文台词.json").read_text(encoding="utf-8"))
    english.update({key:row['en'] for key,row in week_two['lines'].items()})
    english.update({key:row['en'] for key,row in week_three['lines'].items()})
    english.update({key:row['en'] for key,row in week_four['lines'].items()})
    for overlay in (week_five,week_six):
        english.update({key:row['en'] for key,row in overlay['lines'].items()})
    notes_path=ROOT/'story/纸条正文.json'
    note_document=json.loads(notes_path.read_text(encoding='utf-8'))
    note_two=next(n for n in note_document['notes'] if n['visit']==2)
    for lang,note in week_two['letters'].items():
        note_two[lang]=note['body']
        note_two['title_'+lang]=note['title']; note_two['signature_'+lang]=note['signature']
    note_two['source']=week_two['source']
    note_three=next(n for n in note_document['notes'] if n['visit']==3)
    for lang,note in week_three['letters']['A'].items():
        note_three[lang]=note['body'];note_three['title_'+lang]=note['title'];note_three['signature_'+lang]=note['signature']
    note_three['source']=week_three['source'];note_three['variants']={'B':week_three['letters']['B']}
    note_four=next(n for n in note_document['notes'] if n['visit']==4)
    for lang,note in week_four['letters']['A'].items():
        note_four[lang]=note['body'];note_four['title_'+lang]=note['title'];note_four['signature_'+lang]=note['signature']
    note_four['source']=week_four['source'];note_four['variants']={'B':week_four['letters']['B']}
    for visit in (5,6):
        overlay=overlays[visit]; note=next(n for n in note_document['notes'] if n['visit']==visit)
        for lang,fields in overlay['letters'].items():
            note[lang]=fields['body'];note['title_'+lang]=fields['title'];note['signature_'+lang]=fields['signature']
        note['source']=overlay['source'];note.pop('variants',None)
    save(notes_path,note_document)
    translated_notes = {n['visit']:n for n in note_document['notes']}
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
        if mood not in MOODS and mood not in MOODS.values():
            raise ValueError(mood)
        clean = re.sub(r"\[(?:chuckles|laughs|gasps|sighs|breath|exhale|inhales)\]", "", ja)
        assert clean == ja, (key, "TTS tag leaked into subtitles")
        loc["zh"][key] = zh
        loc["ja"][key] = ja
        loc["en"][key] = english[key]
        generate = ja if spoken in (None, "＝字幕", "=字幕") else spoken
        lines[key] = dict(key=key, zh=zh, ja=ja, en=english[key], mood=MOODS.get(mood,mood), kind=kind,
                          generate_text="" if kind == "sfx" else generate, **extra)

    sections = list(re.finditer(r"(?m)^## 第 (\d{2}) 次｜([^\n]+)\n", text))
    assert len(sections) == 14
    for i, match in enumerate(sections):
        n = int(match[1])
        section = text[match.end():sections[i+1].start() if i+1 < len(sections) else text.index("## 尾声")]
        opening, special = [], {}
        overlay=overlays.get(n)
        chapter_rows = table_lines(section) if n not in overlays else [
            [f'`{key}` · {row["mood"]}',row['zh'],row['ja'],'暂不配音']
            for key,row in overlay['lines'].items() if key.startswith(f'xm_{n:02d}_')]
        for row in chapter_rows:
            m = re.fullmatch(r"`(xm_\d{2}_[^`]+)`\s*·\s*(.+)", row[0])
            assert m is not None, row
            key, mood = m.groups()
            extra = {"defer_voice": True} if len(row) > 3 and row[3] == "暂不配音" else {}
            add(key, row[1], row[2], mood, **extra)
            if re.search(r"_(?:\d+|shop)$", key) or key=="xm_05_gift":
                opening.append(key)
            elif not key.endswith(('_late','_already_worked','_existing')):
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
        for lang in ("zh", "ja", "en"):
            loc[lang][f"story_note_{n:02d}_body"] = translated_notes[n][lang]
        loc["en"][f"story_note_{n:02d}_signature"] = "— Xiaoman"
        loc["en"][f"story_note_{n:02d}_name"] = "A note from Xiaoman"
        for lang in ("zh", "ja", "en"):
            for field in ("title", "signature"):
                override = translated_notes[n].get(field + "_" + lang)
                if override:
                    loc[lang][f"story_note_{n:02d}_{field}"] = override
        episodes.append(dict(index=n, title=match[2], credits=BUDGETS[i], demand=DEMANDS[i], track=TRACKS[i], opening=opening,
                             accept=special.get("accept", "accept_return"), goodbye=special.get("bye", "goodbye"),
                             cocktail=special.get("cocktail", ""), note_id=f"story_note_{n:02d}"))

    episodes[1]['title']='另一盏灯'
    episodes[2]['title']='忙碌的夜班'
    for lang,note in week_two['letters'].items():
        for field in ('title','body','signature'):
            loc[lang]['story_note_02_'+field]=note[field]
    for key,row in week_two['lines'].items():
        if key.startswith('xm_tavern_'):
            add(key,row['zh'],row['ja'],row['mood'],defer_voice=True)
    for variant,note in week_three['letters'].items():
        for lang,fields in note.items():
            for field,value in fields.items():
                loc[lang]['story_note_03'+('_b' if variant=='B' else '')+'_'+field]=value
    for key,row in week_three['lines'].items():
        if not key.startswith('xm_03_'):
            add(key,row['zh'],row['ja'],row['mood'],defer_voice=True)

    episodes[3]['title']='两份约定'
    for variant,note in week_four['letters'].items():
        for lang,fields in note.items():
            for field,value in fields.items(): loc[lang]['story_note_04'+('_b' if variant=='B' else '')+'_'+field]=value
    for key,row in week_four['lines'].items():
        if not key.startswith('xm_04_'): add(key,row['zh'],row['ja'],row['mood'],defer_voice=True)
    for visit in (5,6):
        overlay=overlays[visit];episodes[visit-1]['title']=overlay['title']
        for key,row in overlay['lines'].items():
            if not key.startswith(f'xm_{visit:02d}_'): add(key,row['zh'],row['ja'],row['mood'],defer_voice=True)
    ui_five=json.loads((ROOT/'assets/week-five-six-localization.json').read_text(encoding='utf-8'))
    for lang,entries in ui_five.items():loc[lang].update(entries)
    ui_four=json.loads((ROOT/'assets/week-four-localization.json').read_text(encoding='utf-8'))
    for lang,entries in ui_four.items(): loc[lang].update(entries)

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
        if any(key in overlay['lines'] for overlay in overlays.values()):
            continue # An older performance draft cannot override a rewritten week's Japanese.
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
    gun_reactions = json.loads((ROOT / "story/持枪反应.json").read_text(encoding="utf-8"))["localization"]
    assert set(gun_reactions) == {"zh", "ja", "en"}
    for lang, entries in gun_reactions.items():
        assert set(entries) == set(gun_reactions["zh"]), "Gun reaction keys must cover all three languages"
        loc[lang].update(entries)
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

    book=json.loads(json.dumps(template));book['id']='w04_support_notice';book['title']='TENQUIN Assets'
    book['item'].update(id='sansm.xiaoman.visitor.w04_support_notice',nameLoc='ui_w04_notice_name')
    book['pages'][0]['elements']=[dict(type='text',id='notice',x=24,y=30,w=270,h=340,fontSize=10,loc='support_notice_body',color='#28466B',wrap=True)]
    save(PACKAGE/'Books/w04_support_notice.json',book)
    book=json.loads(json.dumps(template));book['id']='w04_group_contract';book['title']='TENQUIN Assets';book.pop('item',None)
    book['pages'][0]['elements']=[dict(type='text',id='contract',x=24,y=30,w=270,h=340,fontSize=9,loc='group_contract_body',color='#28466B',wrap=True)]
    save(PACKAGE/'Books/w04_group_contract.json',book)
    book_path=PACKAGE/'Books/story_note_04.json'
    book=json.loads(book_path.read_text(encoding='utf-8'))
    book['pages'].append(dict(id='supply_list',elements=[dict(type='text',id='supply_snapshot',x=28,y=32,w=260,h=340,fontSize=11,loc='ui_w04_supply_list',color='#28466B',wrap=True)]))
    save(book_path,book)

    catalog = json.loads((ROOT / "assets/cassettes.json").read_text(encoding="utf-8"))
    catalog = [next(c for c in catalog if c["key"] == key) for key in TRACKS]
    for n, track in enumerate(catalog, 1):
        track["storyNode"] = f"visit_{n:02d}"
    save(ROOT / "assets/cassettes.json", catalog)
    generated = ["// Generated by prepare_story.py / prepare_cassettes.py from assets/cassettes.json.", "namespace XiaomanVisitor", "{", "    public static partial class CassetteCollection", "    {", "        public static readonly CassetteTrack[] Tracks =", "        {"]
    for c in catalog:
        generated.append("            new CassetteTrack(" + ", ".join(quote(c[k]) for k in ("key", "title", "sprite", "storyNode")) + "),")
    generated += ["        };", "    }", "}", ""]
    (ROOT / "src/Generated/CassetteCatalog.Generated.cs").write_text("\n".join(generated), encoding="utf-8")
    manifest_path = ROOT / "assets/audio/cassette-manifest.json"
    old_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    save(manifest_path, [dict(next(r for r in old_manifest if r["key"] == key), number=n, storyNode=f"visit_{n:02d}") for n, key in enumerate(TRACKS, 1)])

    csharp = ["// Generated from approved weekly story inputs. Subtitles live in Localization.", "namespace XiaomanVisitor", "{", "    public static class StoryCatalog", "    {", "        public static readonly StoryEpisode[] Episodes =", "        {"]
    for e in episodes:
        defs = ", ".join("new StoryLineSpec(" + quote(key) + ", Mood." + lines[key]["mood"] + ")" for key in e["opening"])
        csharp.append(f"            new StoryEpisode({e['index']}, {e['credits']}, XiaomanDemand.{e['demand']}, {quote(e['track'])}, {quote(e['accept'])}, {quote(e['goodbye'])}, {quote(e['cocktail'])}, new StoryLineSpec[] {{ {defs} }}),")
    csharp += ["        };", "        public static readonly StoryActionSpec[] Actions =", "        {"]
    for a in actions:
        originals = ", ".join(quote(v) for v in a["originals"])
        csharp.append(f"            new StoryActionSpec({quote(a['native_key'])}, {quote(a['key'])}, Mood.{a['mood']}, {'true' if a['kind']=='sfx' else 'false'}, new string[] {{ {originals} }}),")
    csharp += ["        };", "    }", "}", ""]
    (ROOT / "src/Generated/StoryCatalog.Generated.cs").write_text("\n".join(csharp), encoding="utf-8")
    for e in episodes:
        for key in e["opening"] + [e["accept"], e["goodbye"]] + ([e["cocktail"]] if e["cocktail"] else []):
            lines[key]["reference"] = False
    for key in ("intro_1", "right_item", "wrong_item", "repeat", "repeat_food", "repeat_household", "goodbye_discount", "xm_repeat_first", "xm_cocktail_accept"):
        lines[key]["reference"] = False
    # The first chapter line uses intro_1's existing recording, once, with its own subtitle template.
    lines["xm_01_01"]["voice_alias"] = "intro_1"
    voice_keys = [k for k, row in lines.items() if not row.get("reference") and row["kind"] == "speech" and "voice_alias" not in row]
    source_path = args.source.resolve()
    source_name = source_path.relative_to(ROOT).as_posix() if source_path.is_relative_to(ROOT) else source_path.name
    snapshot = dict(schema=1, version="4.7-W05-W06", source=source_name, source_sha256=hashlib.sha256(text.encode("utf-8")).hexdigest(),
                    week_two_source=week_two['source'],week_two_sha256=week_two['sha256'],
                    week_three_source=week_three['source'],week_three_sha256=week_three['sha256'],
                    week_four_source=week_four['source'],week_four_sha256=week_four['sha256'],
                    week_five_source=week_five['source'],week_five_sha256=week_five['sha256'],
                    week_six_source=week_six['source'],week_six_sha256=week_six['sha256'],
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
        generic = any("{" in template for template in templates.values())
        assert "{" not in spoken and "}" not in spoken, key
        row = dict(key=key, subtitle_templates=templates, spoken_text=spoken, generate_text=generate, generic=generic,
                   reviewed=True, audio="", note="用户v3.4终版；演出标签仅在generate_text，不进入字幕。",
                   source_sha256=hashlib.sha256(json.dumps([templates, generate], ensure_ascii=False).encode("utf-8")).hexdigest())
        prior = previous.get(key, {})
        if (prior.get("subtitle_templates", {}).get('ja') == templates['ja']
                and prior.get("generate_text", prior.get("spoken_text")) == generate):
            row["audio"] = prior.get("audio", "")
            row["reviewed"] = prior.get("reviewed", False)
        if key == "intro_1":
            row.update(audio=previous[key]["audio"], spoken_text=previous[key]["spoken_text"], generate_text=previous[key]["spoken_text"],
                       speed=prior.get("speed", 1.0), text_normalization=prior.get("text_normalization", False))
        if line.get("defer_voice") and not row['audio']:
            row.update(reviewed=False, audio="", defer_recording=True,
                       note="新增或改写字幕；作者要求暂不生成录音，日语口头稿待审。")
        production.append(row)
    save(old_script_path, dict(schema=1, language="ja", voice_id="ayano", model_id="luna-tts-1.5", speed=1.1, text_normalization=True, lines=production))
    save(ROOT/'story/酒馆配音脚本_ja_待审.json',dict(schema=1,language='ja',voice_id='ayano',speed=1.1,
        purpose='authoring_reference_not_standalone_pack_script',
        lines=[row for row in production if row['key'].startswith(('xm_tavern_','xm_w03_thanks_'))],
        integration_note='完整配音脚本的待录子集，不可单独覆盖现有语音索引；本次不请求TTS。'))
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
