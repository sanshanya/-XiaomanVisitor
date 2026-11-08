"""Verify the actual published ink assets, source text and deterministic layout."""
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
from PIL import Image
import preview_handwriting as layout


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    index = json.loads((layout.PACKAGE/"Letters/index.json").read_text(encoding="utf-8"))
    assert index["schema"] == 1 and index["font"] == "Yozai Regular"
    assert index["aliases"] == {"thank_you_note":"story_note_01"}
    assert len(index["letters"]) == 42
    assert {r["file"] for r in index["letters"]} == {
        f"Letters/{language}/story_note_{n:02d}.png"
        for language in ("zh","ja","en") for n in range(1,15)}
    texts = {lang:layout.read_texts(lang) for lang in ("zh","ja","en")}
    rows = []
    for entry in index["letters"]:
        language, key = entry["language"],entry["id"]
        text = "".join(texts[language][key+"_"+p] for p in ("title","body","signature"))
        assert hashlib.sha256(text.encode("utf-8")).hexdigest() == entry["text_sha256"], key
        path = layout.PACKAGE/entry["file"]
        assert hashlib.sha256(path.read_bytes()).hexdigest() == entry["sha256"], path
        assert path.stat().st_size == entry["bytes"]
        image = Image.open(path)
        assert image.size == (1122,1402) and image.mode == "P"
        image = image.convert("RGBA")
        alpha = image.getchannel("A")
        assert alpha.getextrema() == (0,237) and alpha.getbbox()
        assert len(image.getcolors(1122*1402)) <= 238
        assert image.getbbox()[0] > 65 and image.getbbox()[2] < 1050
        row, _ = layout.letter_layout("yozai", int(key[-2:]),language,texts[language])
        assert row["fits"]
        rows.append(row)
    assert not list(layout.PACKAGE.rglob("*.ttf")), "Full authoring fonts must stay outside the release"
    assert (layout.PACKAGE/"Licenses/Yozai-Regular-OFL.txt").is_file()
    version = ET.parse(layout.PACKAGE/"manifest.xml").getroot().findtext("ModVersion")
    folder = layout.ROOT/"release"/version/"纸条预览"
    folder.mkdir(parents=True,exist_ok=True)
    paper = Image.open(layout.PACKAGE/"Textures/Paper/xiaoman_letter_paper.png").convert("RGBA")
    for visit,language in layout.SAMPLES:
        ink = Image.open(layout.PACKAGE/f"Letters/{language}/story_note_{visit:02d}.png").convert("RGBA")
        # Composite the very same transparent bytes that Unity will load.
        Image.alpha_composite(paper,ink).convert("RGB").resize(
            (628,785),Image.Resampling.LANCZOS).save(folder/f"note_{visit:02d}_{language}.png")
    report = dict(renderer="Original paper + packaged Yozai ink PNG; not an in-game screenshot",
                  body_blocks=len(rows),overflow=[],rows=rows,
                  total_ink_bytes=sum(r["bytes"] for r in index["letters"]),
                  max_resident_texture_bytes=1122*1402*4,
                  lazy_loaded=True,fonts_in_package=False)
    (folder/"layout-report.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print("PASS: 42 localized Yozai ink layers match the current letters and hashes; all fit, indexed alpha is intact, and full fonts stay outside the package.")


if __name__ == "__main__":
    main()
