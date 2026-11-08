"""Conservative reference rendering of native paper JSON; game art stays unchanged."""
from pathlib import Path
import csv
import json
import math
import sys
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "package/XiaomanVisitor"
FONTS = {"zh": "C:/Windows/Fonts/msyh.ttc", "ja": "C:/Windows/Fonts/YuGothM.ttc", "en": "C:/Windows/Fonts/segoeui.ttf"}


def wrap(text, font, width, language):
    lines = []
    for paragraph in text.split("\n"):
        if not paragraph:
            lines.append(""); continue
        units = paragraph.split(" ") if language == "en" else list(paragraph)
        line = ""
        for unit in units:
            candidate = line + (" " if language == "en" and line else "") + unit
            if line and font.getlength(candidate) > width:
                if language != "en" and (unit in "，。！？；：、）》」』】" or line[-1] in "（《「『【"):
                    lines.append(line[:-1]); line = line[-1] + unit
                else:
                    lines.append(line); line = unit
            else:
                line = candidate
        lines.append(line)
    return lines


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    if (PACKAGE / "Letters/index.json").exists():
        import check_handwriting
        check_handwriting.main()
        return
    style = json.loads((ROOT / "assets/paper-style.json").read_text(encoding="utf-8"))
    folder = ROOT / "release/0.01/纸条预览"
    folder.mkdir(exist_ok=True)
    rows = []
    for language in ("zh", "ja", "en"):
        with (PACKAGE / f"Localization/{language}.csv").open(encoding="utf-8-sig", newline="") as f:
            texts = {k: v.replace("\\n", "\n") for k, v in csv.reader(f)}
        for n in range(1, 15):
            book = json.loads((PACKAGE / f"Books/story_note_{n:02d}.json").read_text(encoding="utf-8"))
            scale = book["width"] / book["designWidth"]
            image = Image.open(PACKAGE / style["packaged"]).convert("RGB").resize((round(book["width"]), round(book["height"])), Image.Resampling.LANCZOS)
            draw = ImageDraw.Draw(image)
            for element in book["pages"][0]["elements"]:
                font = ImageFont.truetype(FONTS[language], round(element["fontSize"] * scale))
                width, available = element["w"] * scale, element["h"] * scale
                lines = wrap(texts[element["loc"]], font, width, language)
                line_height = math.ceil(sum(font.getmetrics()) * 1.05)
                needed = len(lines) * line_height
                if element["id"] == "body":
                    rows.append(dict(visit=n, language=language, lines=len(lines), needed=needed, available=round(available, 2), fits=needed <= available))
                for i, line in enumerate(lines):
                    x = element["x"] * scale
                    if element.get("align") == "right":
                        x += width - font.getlength(line)
                    draw.text((x, element["y"] * scale + i * line_height), line, font=font, fill=element["color"], anchor="lt")
            if (n, language) in ((1, "zh"), (6, "ja"), (12, "en")):
                image.save(folder / f"note_{n:02d}_{language}.png")
    report = dict(renderer="Reference layout with system fonts; not an in-game screenshot.", body_blocks=len(rows), overflow=[r for r in rows if not r["fits"]], rows=rows)
    (folder / "layout-report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "rows"}, ensure_ascii=False, indent=2))
    if report["overflow"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
