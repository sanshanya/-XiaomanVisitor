"""Asset/script contracts important to the first-week change; no game writes."""
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
        texts[language] = dict(rows)
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
        assert all(sorted(re.findall(r"\{\d+\}", texts[lang][key])) == sorted(placeholders) for lang in ("en", "ja")), key
    assert texts["zh"]["gift_note_body"] == texts["zh"]["story_note_01_body"]
    assert texts["zh"]["gift_note_title"] == "店长："
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
    assert len({row["key"] for row in story["lines"]}) == 125
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
    assert tavern_map["phase"] == "renovation_placeholder" and not tavern_map["enterable"]
    assert tavern_map["image_key"] == "sansm.xiaoman.visitor:" + Path(tavern_map["packaged"]).stem
    assert current == expected, "Approved texture changed, missing or unexpected"
    detail = json.loads((ROOT / "assets/portrait-detail.json").read_text(encoding="utf-8"))
    assert hashlib.sha256((PACKAGE / detail["packaged"]).read_bytes()).hexdigest() == detail["sha256"]
    assert list(Image.open(PACKAGE / detail["packaged"]).size) == detail["size"] == [2304,2064]
    print(f"PASS: original {len(baseline['files'])} PNGs and paper unchanged; tavern preview and two pink notebook states verified.")
    english = json.loads((ROOT / "story/英文剧情与动作_0.01.json").read_text(encoding="utf-8"))
    notes = json.loads((ROOT / "story/纸条译文_0.01.json").read_text(encoding="utf-8"))["notes"]
    assert set(english) == {line["key"] for line in story["lines"]}
    assert all(texts["en"][key].replace("\\n", "\n") == value for key, value in english.items())
    for note in notes:
        for lang in ("zh", "ja", "en"):
            assert texts[lang][f"story_note_{note['visit']:02d}_body"].replace("\\n", "\n") == note[lang]
    for book_file in (PACKAGE / "Books").glob("*.json"):
        book = json.loads(book_file.read_text(encoding="utf-8"))
        assert book["background"] == style["sprite"]
        assert abs(book["width"] / book["height"] - style["pixel_width"] / style["pixel_height"]) < 0.00001
    assert story["sale_rule"]["excluded_visits"] == [6] and story["fixed_drink_gift"]["visit"] == 6
    for path in (PACKAGE / "Textures/Items").glob("xiaoman_cocktail_*.png"):
        assert Image.open(path).size == (16, 32)
    assert not list((PACKAGE / "Books").glob("feeling_*.json"))
    print("PASS: consistent localization, 14 story letters, exact final script rows, fixed music order, static opening budget, isolated TTS tags and native paper geometry.")


if __name__ == "__main__":
    main()
