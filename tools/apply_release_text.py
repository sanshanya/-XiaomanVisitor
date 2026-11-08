"""Apply note-only author revisions and reuse unchanged Japanese speech offline."""
from pathlib import Path
import argparse
import csv
import hashlib
import json
import re
import shutil
import sys
import voice_pipeline as voice
import prepare_story

ROOT = Path(__file__).resolve().parents[1]


def quotes(text):
    return "\n".join("> " + line if line else ">" for line in text.splitlines())


def paragraphs(text):
    return [part.strip() for part in re.split(r"\n\s*\n", text.strip()) if part.strip()]


def apply_notes(source):
    raw = source.read_text(encoding="utf-8-sig")
    archive = ROOT / "story/每周纸条修订_0.01_用户原稿.md"
    if source.resolve() != archive.resolve():
        shutil.copyfile(source, archive)
    translation_path = ROOT / "story/纸条译文_0.01.json"
    translations = voice.load(translation_path)
    by_visit = {n["visit"]: n for n in translations["notes"]}
    with (voice.PACKAGE / "Localization/ja.csv").open(encoding="utf-8-sig", newline="") as f:
        current_ja = dict(csv.reader(f))
    sections = list(re.finditer(r"(?m)^## 第 (\d{2}) 次｜[^\n]+", raw))
    assert len(sections) == 14
    for index, section in enumerate(sections):
        n = int(section[1])
        part = raw[section.end():sections[index+1].start() if index+1 < len(sections) else len(raw)]
        cn = part[part.index("**周二｜纸条"):part.index("**日本語｜手紙**")]
        pieces = paragraphs(prepare_story.quoted(cn))
        assert pieces[0] == "店长：" and pieces[-1].startswith("——小满")
        entry = by_visit[n]
        entry["zh"] = "\n\n".join(pieces[1:-1]).replace("小点玻璃门", "小店玻璃门")
        entry.setdefault("ja", current_ja[f"story_note_{n:02d}_body"].replace("\\n", "\n"))
        entry["title_zh"], entry["signature_zh"] = pieces[0], "——小满"
    translations["source_sha256"] = hashlib.sha256(source.read_bytes()).hexdigest()
    translations["corrections"] = [{"visit": 2, "from": "小点玻璃门", "to": "小店玻璃门"}]
    voice.save(translation_path, translations)

    canonical = prepare_story.SOURCE.read_text(encoding="utf-8-sig")
    original_tables = re.findall(r"(?m)^\|.*$", canonical)
    sections = list(re.finditer(r"(?m)^## 第 (\d{2}) 次｜[^\n]+", canonical))
    for index in range(len(sections)-1, -1, -1):
        match = sections[index]; n = int(match[1]); note = by_visit[n]
        start = canonical.index("**周二｜纸条", match.end())
        jp = canonical.index("**日本語｜手紙**", start)
        end = canonical.index("**暗线", jp)
        heading = canonical[start:canonical.index("\n", start)]
        zh = "\n\n".join(["店长：", note["zh"], "——小满"])
        ja = "\n\n".join(["店長さんへ", note["ja"], "みちる"])
        replacement = heading + "\n\n" + quotes(zh) + "\n\n**日本語｜手紙**\n\n" + quotes(ja) + "\n\n"
        canonical = canonical[:start] + replacement + canonical[end:]
    assert re.findall(r"(?m)^\|.*$", canonical) == original_tables, "Spoken/TTS tables must remain unchanged"
    marker = "## 第 06 次｜甜一点也没关系"
    start = canonical.index(marker); end = canonical.index("**周一｜口头台词**", start)
    header = canonical[start:end]
    rule = "- **本次赠酒：** 不进行随机售酒；次日随纸条和磁带免费附送一杯随机强效酒，不要求成交，取代当次普通交易回礼。\n"
    if rule not in header:
        canonical = canonical[:end] + rule + "\n" + canonical[end:]
    prepare_story.SOURCE.write_text(canonical, encoding="utf-8")


def reuse_voice_bindings():
    script = voice.load(voice.DEFAULT_SCRIPT)
    unchanged = {}
    for row in script["lines"]:
        audio = ROOT / row["audio"]
        assert voice.reusable_audio(audio, row, script), row["key"]
        checksum = hashlib.sha256(audio.read_bytes()).hexdigest()
        meta = voice.load(audio.with_suffix(".json"))
        assert meta["sha256"] == checksum
        binding = voice.binding_for(row, script)
        if meta.get("binding") != binding:
            meta.setdefault("binding_history", []).append(meta.get("binding"))
            meta["binding"] = binding
            voice.save(audio.with_suffix(".json"), meta)
        unchanged[row["key"]] = checksum
    packed = voice.pack(voice.DEFAULT_SCRIPT)
    assert len(packed["lines"]) == 107 and packed["pending_lines"] == 0
    assert all(hashlib.sha256((ROOT/r["audio"]).read_bytes()).hexdigest() == unchanged[r["key"]] for r in script["lines"])
    return unchanged


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / "story/每周纸条修订_0.01_用户原稿.md")
    args = parser.parse_args()
    apply_notes(args.source)
    previous = sys.argv
    try:
        sys.argv = ["prepare_story.py"]
        prepare_story.main()
    finally:
        sys.argv = previous
    hashes = reuse_voice_bindings()
    print(json.dumps(dict(updated_letters=14, languages=["zh", "ja", "en"], unchanged_voice_files=len(hashes), tts_requests=0), ensure_ascii=False))


if __name__ == "__main__":
    main()
