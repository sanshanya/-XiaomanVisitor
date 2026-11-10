"""Validate current story, localization and asset contracts; no game writes."""
import csv
import hashlib
import json
import re
import sys
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "package/XiaomanVisitor"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    texts = {}
    for language in ("zh", "en", "ja"):
        with (PACKAGE / f"Localization/{language}.csv").open(encoding="utf-8-sig", newline="") as f:
            rows = list(csv.reader(f))
        assert all(len(row) == 2 and row[0] and row[1] for row in rows)
        texts[language] = {key: value.replace("\\n", "\n") for key, value in rows}
        assert len(texts[language]) == len(rows), "Duplicate localization key"
    assert set(texts["zh"]) == set(texts["en"]) == set(texts["ja"])
    presentation = json.loads((ROOT / "assets/item-presentation.json").read_text(encoding="utf-8"))
    for language, entries in presentation.items():
        assert all(texts[language][key] == value for key, value in entries.items()), "Stale item descriptions"
    map_texts = json.loads((ROOT / "assets/tavern-map-localization.json").read_text(encoding="utf-8"))
    for language, entries in map_texts.items():
        assert all(texts[language][key] == value for key, value in entries.items()), "Stale tavern map text"
    for key in texts["zh"]:
        placeholders = re.findall(r"\{\d+\}", texts["zh"][key])
        for lang in ('en','ja'):
            # Approved shopping subtitles deliberately omit the Japanese spoken budget.
            expected=[] if key in ('xm_02_shop','xm_03_shop','xm_04_shop','xm_05_shop','xm_06_shop','xm_07_shop','xm_08_shop','xm_09_shop','xm_10_shop','xm_11_shop','xm_w04_audit_shop') and lang=='ja' else sorted(placeholders)
            assert sorted(re.findall(r"\{\d+\}",texts[lang][key]))==expected,key
    assert texts["zh"]["gift_note_body"] == texts["zh"]["story_note_01_body"]
    assert texts["zh"]["gift_note_title"] == "致店长先生："
    for language, rows in texts.items():
        item_texts = [value for key,value in rows.items()
                      if key.startswith(("collection_","gift_note_","gift_tape_"))
                      or re.fullmatch(r"story_note_\d+_name",key)]
        assert not any(re.search(r"\d+\s*/\s*14|\{0\}/\{1\}",value) for value in item_texts), "Progress numbering leaks into item UI"
    assert "Title" in texts["zh"]["collection_title_name"]
    story = json.loads((ROOT / "assets/story.json").read_text(encoding="utf-8"))
    for language, rows in texts.items():
        opening = "\n".join(rows[k] for k in story["episodes"][0]["opening"])
        assert not re.search(r"酒吧|\bbar\b|バー", opening, re.I), "Premature bar reveal"
        assert "cocktail_offer" not in rows
        assert all(key in rows for key in ("drink_tier_1", "drink_tier_2", "drink_buff_luck",
                   "drink_buff_tradereputation", "drink_buff_scavenging", "drink_buff_duration"))
        for book_file in (PACKAGE / "Books").glob("*.json"):
            book = json.loads(book_file.read_text(encoding="utf-8"))
            for page in book["pages"]:
                for element in page["elements"]:
                    if "loc" in element:
                        assert element["loc"] in rows, f"Untranslated {book_file.name}:{element['loc']}"
                    assert element["x"] >= 0 and element["y"] >= 0
                    assert element["x"] + element["w"] <= book["designWidth"]
                    assert element["y"] + element["h"] <= book["designHeight"]
    catalog = json.loads((ROOT / "assets/cassettes.json").read_text(encoding="utf-8"))
    assert catalog[0]["key"] == "title" and catalog[0]["storyNode"] == "visit_01"
    assert [t["key"] for t in catalog] == [e["track"] for e in story["episodes"]]
    assert len(story["episodes"]) == 14 and len(story["actions"]) == 49
    assert len({row["key"] for row in story["lines"]}) == len(story['lines']), 'Duplicate authored dialogue key'
    for line in story["lines"]:
        assert not re.search(r"\[(chuckles|laughs|gasps|sighs|breath|exhale|inhales)\]", line["zh"] + line["ja"])
    assert "150" in texts["zh"]["xm_01_shop"] and "{0}" not in texts["zh"]["xm_01_shop"]
    # The baseline was verified against the approved 0.4.2 archive before old
    # releases were removed. Keeping nine hashes avoids retaining a 149 MB ZIP.
    baseline = json.loads((ROOT / "assets/approved-textures.sha256.json").read_text(encoding="utf-8"))
    current = {path.relative_to(PACKAGE).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
               for path in (PACKAGE / "Textures").rglob("*.png")}
    style = json.loads((ROOT / "assets/paper-style.json").read_text(encoding="utf-8"))
    expected = dict(baseline["files"], **{style["packaged"]: style["sha256"]})
    tavern_map = json.loads((ROOT / "assets/tavern-map.json").read_text(encoding="utf-8"))
    expected[tavern_map["packaged"]] = tavern_map["sha256"]
    notebook_style = json.loads((ROOT / "assets/notebook-style.json").read_text(encoding="utf-8"))
    for icon in notebook_style["files"]:
        expected[icon["packaged"]] = icon["sha256"]
        original = Image.open(ROOT / icon["source"]).convert("RGBA")
        pink = Image.open(PACKAGE / icon["packaged"]).convert("RGBA")
        assert original.size == pink.size == (32,48)
        assert original.getchannel("A").tobytes() == pink.getchannel("A").tobytes(), "Notebook slot shape changed"
        assert hashlib.sha256((ROOT / icon["source"]).read_bytes()).hexdigest() == icon["source_sha256"]
    assert list(Image.open(PACKAGE / tavern_map["packaged"]).size) == tavern_map["size"] == [592,336]
    assert tavern_map["phase"] == "week_two_opening" and tavern_map["enterable"] and tavern_map["discovery_game_day"] == 3 and tavern_map['opening_game_day']==8
    assert tavern_map["image_key"] == "sansm.xiaoman.visitor:" + Path(tavern_map["packaged"]).stem
    luxury_art=json.loads((ROOT/'assets/luxury-art.json').read_text(encoding='utf-8'))
    for art in luxury_art['files']:
        expected[art['packaged']]=art['sha256']
    assert current == expected, "Approved texture changed, missing or unexpected"
    detail = json.loads((ROOT / "assets/portrait-detail.json").read_text(encoding="utf-8"))
    assert hashlib.sha256((PACKAGE / detail["packaged"]).read_bytes()).hexdigest() == detail["sha256"]
    assert list(Image.open(PACKAGE / detail["packaged"]).size) == detail["size"] == [2304,2064]
    print(f"PASS: original {len(baseline['files'])} PNGs and paper unchanged; tavern preview and two pink notebook states verified.")
    english = json.loads((ROOT / "story/英文台词.json").read_text(encoding="utf-8"))
    import week_two_source,week_three_source
    w02=week_two_source.read()
    w03=week_three_source.read()
    import week_four_source
    w04=week_four_source.read()
    prices=json.loads((ROOT/'assets/tavern-prices.json').read_text(encoding='utf-8'))
    generated=(ROOT/'src/Generated/TavernPrices.Generated.cs').read_text(encoding='utf-8')
    match=re.search(r'Juice = (\d+), Alcohol = (\d+)',generated)
    assert match and tuple(map(int,match.groups()))==(prices['juice'],prices['alcohol']),'Stale tavern prices; regenerate the authored story'
    assert story['week_two_sha256']==w02['sha256'] and set(w02['lines']).issubset({row['key'] for row in story['lines']})
    assert story['week_three_sha256']==w03['sha256'] and set(w03['lines']).issubset({row['key'] for row in story['lines']})
    english.update({key:row['en'] for key,row in w02['lines'].items()})
    english.update({key:row['en'] for key,row in w03['lines'].items()})
    english.update({key:row['en'] for key,row in w04['lines'].items()})
    assert story['week_four_sha256']==w04['sha256']
    notes = json.loads((ROOT / "story/纸条正文.json").read_text(encoding="utf-8"))["notes"]
    import weekly_story_source
    # Rewritten chapters replace their old voice/script slots, without keeping abandoned rows active.
    english={k:v for k,v in english.items() if not k.startswith(tuple(f'xm_{n:02d}_' for n in weekly_story_source.SOURCES))}
    for visit,name in weekly_story_source.WEEK_NAMES.items():
        overlay=weekly_story_source.read(visit)
        english.update({key:row['en'] for key,row in overlay['lines'].items()})
        assert story['week_'+name+'_sha256']==overlay['sha256']
        if 'news' in overlay:
            for lang in ('zh','ja','en'):
                for field in ('title','body'):assert texts[lang][f'xm_w{visit:02d}_news_'+field]==overlay['news'][field][lang]
        for lang,fields in overlay['letters'].items():
            for field,value in fields.items():assert texts[lang][f'story_note_{visit:02d}_{field}']==value
    assert set(english) == {line["key"] for line in story["lines"]}
    assert all(texts["en"][key].replace("\\n", "\n") == value for key, value in english.items())
    for note in notes:
        for lang in ("zh", "ja", "en"):
            assert texts[lang][f"story_note_{note['visit']:02d}_body"].replace("\\n", "\n") == note[lang]
    for variant,letters in w03['letters'].items():
        for lang,fields in letters.items():
            for field,value in fields.items():
                assert texts[lang]['story_note_03'+('_b' if variant=='B' else '')+'_'+field]==value
    assert len(list((PACKAGE/'Books').glob('story_note_*.json')))==14,'A/B must share one paper item ID'
    work=json.loads((ROOT/'assets/tavern-work.json').read_text(encoding='utf-8'))
    generated=(ROOT/'src/Generated/TavernWorkConfig.Generated.cs').read_text(encoding='utf-8')
    for field,key in {'UnlockDay':'unlock_day','DailyOrders':'daily_overflow_orders','OrdersPerShift':'orders_per_shift',
        'OrderRevenue':'order_revenue','OrderMaterial':'order_material','Wage':'wage','DurationSeconds':'duration_seconds',
        'ThanksPercent':'thanks_percent','PrivateReserve':'private_purchase_reserve'}.items():
        assert f'public const int {field} = {work[key]};' in generated,'Stale work config'
    for book_file in (PACKAGE / "Books").glob("*.json"):
        book = json.loads(book_file.read_text(encoding="utf-8"))
        assert book["background"] == style["sprite"]
        assert abs(book["width"] / book["height"] - style["pixel_width"] / style["pixel_height"]) < 0.00001
    assert story["sale_rule"]["excluded_visits"] == [6] and story["fixed_drink_gift"]["visit"] == 6
    for path in (PACKAGE / "Textures/Items").glob("xiaoman_cocktail_*.png"):
        assert Image.open(path).size == (16, 32)
    assert not list((PACKAGE / "Books").glob("feeling_*.json"))
    import text_review
    text_review.write(ROOT, texts, story, notes)
    print("PASS: consistent localization, 14 story letters, exact final script rows, fixed music order, static opening budget, isolated TTS tags and native paper geometry.")


if __name__ == "__main__":
    main()
