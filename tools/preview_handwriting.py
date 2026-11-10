"""Render isolated font trials; never writes the package, release, or installation.

Uses the original paper and actual bundled trial fonts, with fixed subpixel
variations. This is a typography preview, not a screenshot of Unity/TMP.
"""
from pathlib import Path
import csv
import hashlib
import json
import math
import random
import re
import struct
import sys
from functools import lru_cache
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "package/XiaomanVisitor"
TRIAL = ROOT / "experiments/handwriting"
OUTPUT = TRIAL / "previews"
SCALE = 3
WIDTH, HEIGHT = 628, 785
INK = (40, 70, 107, 237)  # #28466B at 93%
BODY_SIZE = 18.0
BODY_SIZES = {(11, 'ja'): 16.5}
TRACKING = 0.02
LINE_HEIGHT = 1.58
PARAGRAPH_GAP = 0.68  # additional line fractions between paragraphs
PARAGRAPH_GAPS = {(5, 'ja'): 0.36, (5, 'en'): 0.36, (6, 'ja'): 0.66,
                  (8, 'ja'): 0.36, (8, 'en'): 0.56,
                  (11, 'zh'): 0.40, (11, 'ja'): 0.24, (11, 'en'): 0.13}
# This longer letter uses the empty space below the salutation, not smaller type.
BODY_BASELINES = {(11, 'en'): 130, (11, 'ja'): 122}
FONTS = {
    "yozai": TRIAL / "fonts/Yozai-Regular.ttf",
    "xiaolai": TRIAL / "fonts/Xiaolai-Regular.ttf",
}
KAOMOJI = set('٩(ˊᗜˋ*)و(´,,•ω•,,)♡(๑•́ ₃ •̀๑)')
FALLBACKS = (Path('C:/Windows/Fonts/segoeui.ttf'),Path('C:/Windows/Fonts/gadugi.ttf'),Path('C:/Windows/Fonts/LeelawUI.ttf'))
LABEL_FONT = "C:/Windows/Fonts/msyh.ttc"  # comparison labels only
OPENING = set("（《「『【〔〈([{“‘")
CLOSING = set("，。！？；：、）》」』】〕〉)]}”’….,!?;:%")
SAMPLES = ((1, "zh"), (6, "ja"), (12, "en"))


@lru_cache(maxsize=None)
def cmap_coverage(path):
    """Read Unicode cmap 4/12 directly, without installing font tools globally."""
    data = path.read_bytes()
    u16 = lambda p: struct.unpack_from(">H", data, p)[0]
    u32 = lambda p: struct.unpack_from(">I", data, p)[0]
    tables = {
        data[12 + 16*i:16 + 16*i]: u32(20 + 16*i)
        for i in range(u16(4))
    }
    base = tables[b"cmap"]
    chars = set()
    for i in range(u16(base + 2)):
        record = base + 4 + 8*i
        platform, encoding = u16(record), u16(record + 2)
        if not (platform == 0 or platform == 3 and encoding in (1, 10)):
            continue
        table = base + u32(record + 4)
        fmt = u16(table)
        if fmt == 12:
            for j in range(u32(table + 12)):
                first, last, glyph = struct.unpack_from(">III", data, table + 16 + 12*j)
                chars.update(range(first + (glyph == 0), last + 1))
        elif fmt == 4:
            n = u16(table + 6) // 2
            ends = table + 14
            starts = ends + 2*n + 2
            deltas = starts + 2*n
            offsets = deltas + 2*n
            for j in range(n):
                first, last = u16(starts + 2*j), u16(ends + 2*j)
                delta, offset = u16(deltas + 2*j), u16(offsets + 2*j)
                for code in range(first, min(last, 0xfffe) + 1):
                    glyph = ((code + delta) & 0xffff) if offset == 0 else u16(
                        offsets + 2*j + offset + 2*(code-first))
                    if glyph:
                        chars.add(code)
    return chars


def read_texts(language):
    with (PACKAGE / f"Localization/{language}.csv").open(
        encoding="utf-8-sig", newline=""
    ) as handle:
        return {k: v.replace("\\n", "\n") for k, v in csv.reader(handle)}


@lru_cache(maxsize=None)
def font_at(path, size):
    return ImageFont.truetype(str(path), round(size * SCALE))


def glyph_path(path, char):
    if ord(char) in cmap_coverage(path):
        return path
    if char in KAOMOJI:
        for fallback in FALLBACKS:
            if fallback.exists() and ord(char) in cmap_coverage(fallback):
                return fallback
    return None


def measured(text, font, size):
    path=Path(font.path)
    if any(glyph_path(path,c)!=path for c in text):
        width=0
        previous_font=None; previous=''
        for char in text:
            selected=glyph_path(path,char)
            if selected is None: raise ValueError(f'Missing letter glyph U+{ord(char):04X}')
            current=font_at(selected,size)
            if current is previous_font:
                width+=(current.getlength(previous+char)-current.getlength(previous)-current.getlength(char))/SCALE
            width+=current.getlength(char)/SCALE
            previous_font,previous=current,char
        return width+max(0,len(text)-1)*TRACKING*size
    return font.getlength(text)/SCALE + max(0, len(text)-1)*TRACKING*size


def wrap(text, font, size, width, language):
    """Keep source paragraphs and English words; observe basic CJK kinsoku."""
    result = []
    paragraphs = re.split(r"\n\s*\n", text)
    for p, paragraph in enumerate(paragraphs):
        paragraph = paragraph.replace("\n", " ").strip()
        units = re.findall(r"\S+\s*", paragraph) if language == "en" else list(paragraph)
        line = ""
        for unit in units:
            candidate = line + unit
            # Reserve space for the tiny fixed handwriting variation.
            if line and measured(candidate.rstrip(), font, size) > width-2:
                if language != "en" and (unit[0] in CLOSING or line[-1] in OPENING):
                    result.append((line[:-1], False))
                    line = line[-1] + unit
                else:
                    result.append((line.rstrip(), False))
                    line = unit.lstrip()
            else:
                line = candidate
        if line:
            result.append((line.rstrip(), p != len(paragraphs)-1))
    return result


def fixed_rng(key):
    return random.Random(int.from_bytes(hashlib.sha256(key.encode()).digest()[:8], "big"))


def write_line(layer, xy, text, path, size, seed, *, slope=0):
    """Draw real glyphs; preserve kerning, no randomly rotated characters."""
    draw = ImageDraw.Draw(layer)
    font = font_at(path, size)
    rng = fixed_rng(seed)
    x, y = xy
    origin_x = x
    previous_font=None
    for index, char in enumerate(text):
        selected=glyph_path(path,char)
        if selected is None: raise ValueError(f'Missing letter glyph U+{ord(char):04X}')
        font=font_at(selected,size)
        if index and previous_font is font:
            previous = text[index-1]
            kern = (font.getlength(previous+char)-font.getlength(previous)
                    -font.getlength(char))/SCALE
            x += kern
        baseline_jitter = rng.uniform(-0.025, 0.025)*size
        yy = y + baseline_jitter + math.tan(math.radians(slope))*(x-origin_x)
        draw.text((round(x*SCALE), round(yy*SCALE)), char,
                  font=font, fill=INK, anchor="ls")
        x += font.getlength(char)/SCALE + TRACKING*size + rng.uniform(-0.006, 0.006)*size
        previous_font=font


def letter_layout(font_key, visit, language, texts, letter_id=None):
    path = FONTS[font_key]
    size = BODY_SIZES.get((visit, language), BODY_SIZE)
    font = font_at(path, size)
    key = letter_id or f"story_note_{visit:02d}"
    body = texts[key+"_body"]
    lines = wrap(body, font, size, 500, language)
    step = size*(1.565 if key=="story_note_04_b" else LINE_HEIGHT)
    gap = PARAGRAPH_GAPS.get((visit, language), PARAGRAPH_GAP)
    y = BODY_BASELINES.get((visit, language), 140 + BODY_SIZE)
    positions = []
    for index, (line, paragraph_end) in enumerate(lines):
        drift = fixed_rng(f"{visit}/{language}/line/{index}").uniform(-0.7, 0.7)
        positions.append((60+drift, y, line))
        y += step * (1+gap if paragraph_end else 1)
    body_bottom = positions[-1][1] + font.getmetrics()[1]/SCALE if positions else 140
    row = dict(font=font_key, visit=visit, language=language, body_size=size, lines=len(lines),
               body_bottom=round(body_bottom, 2), available_bottom=665,
               fits=body_bottom <= 665,
               longest_line=round(max((measured(x[2],font,size)
                                       for x in positions),default=0),2))
    return row, positions


def render_ink_layer(font_key, visit, language, texts, letter_id=None):
    path = FONTS[font_key]
    key = letter_id or f"story_note_{visit:02d}"
    row, positions = letter_layout(font_key, visit, language, texts, key)
    layer = Image.new("RGBA", (WIDTH*SCALE, HEIGHT*SCALE))
    write_line(layer, (60, 60+21), texts[key+"_title"], path, 21,
               f"{visit}/{language}/title")
    for index, (x, y, line) in enumerate(positions):
        write_line(layer, (x,y), line, path, row['body_size'], f"{visit}/{language}/body/{index}")
    signature_size = BODY_SIZE*1.14
    signature = texts[key+"_signature"]
    signature_width = measured(signature, font_at(path, signature_size), signature_size)
    write_line(layer, (560-signature_width, 730), signature, path, signature_size,
               f"{visit}/{language}/signature", slope=1.3)
    return row, layer


def make_letter(font_key, visit, language, texts, save):
    if not save:
        return letter_layout(font_key, visit, language, texts)[0]
    row, layer = render_ink_layer(font_key, visit, language, texts)
    paper = Image.open(PACKAGE / "Textures/Paper/xiaoman_letter_paper.png")
    paper = paper.convert("RGBA").resize(layer.size, Image.Resampling.LANCZOS)
    Image.alpha_composite(paper, layer).convert("RGB").resize(
        (WIDTH, HEIGHT), Image.Resampling.LANCZOS
    ).save(OUTPUT / f"{font_key}_note_{visit:02d}_{language}.png")
    return row


def board(visit, language):
    margin, gap, header, footer = 24, 24, 82, 55
    canvas = Image.new("RGB", (WIDTH*2+margin*2+gap, HEIGHT+header+footer), "#EDF0F3")
    draw = ImageDraw.Draw(canvas)
    title_font = ImageFont.truetype(LABEL_FONT,22)
    sub_font = ImageFont.truetype(LABEL_FONT,14)
    for i, (key,label,detail) in enumerate((
        ("yozai", "A · 悠哉 Regular", "更舒展、安静"),
        ("xiaolai", "B · 小赖 Regular", "更圆润、亲切"),
    )):
        x = margin + i*(WIDTH+gap)
        draw.text((x+2,12),label,fill="#28466B",font=title_font)
        draw.text((x+2,46),detail,fill="#586878",font=sub_font)
        canvas.paste(Image.open(OUTPUT/f"{key}_note_{visit:02d}_{language}.png"),(x,header))
    caption = (f"第{visit:02d}封 · {language.upper()} | 同文案 / 同信纸 / 18px / #28466B 93% / 行距1.58 / 字距0.02em"
               " | 字体排版预览，非游戏截图")
    draw.text((margin,header+HEIGHT+17),caption,fill="#586878",font=sub_font)
    canvas.save(OUTPUT/f"AB_note_{visit:02d}_{language}.png")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    texts = {language:read_texts(language) for language in ("zh","ja","en")}
    all_chars = set("".join(texts[language][f"story_note_{n:02d}_{suffix}"]
                            for language in texts for n in range(1,15)
                            for suffix in ("title","body","signature"))) - set("\n\r\t")
    coverage = {}
    for name, path in FONTS.items():
        chars = cmap_coverage(path)
        missing = sorted(all_chars - {chr(x) for x in chars})
        coverage[name] = dict(required_glyphs=len(all_chars), missing=missing)
    rows = [make_letter(font,visit,language,texts[language],(visit,language) in SAMPLES)
            for font in FONTS for language in texts for visit in range(1,15)]
    for visit, language in SAMPLES:
        board(visit,language)
    version = ET.parse(PACKAGE/"manifest.xml").getroot().findtext("ModVersion")
    state = json.loads((ROOT/"release"/version/"workshop-publish-state.json").read_text(encoding="utf-8"))
    hashes = state["uploaded_files_sha256"]
    changed = [name for name,digest in hashes.items()
               if hashlib.sha256((PACKAGE/name).read_bytes()).hexdigest().lower()!=digest.lower()]
    report = dict(
        preview_only=True, renderer="Pillow with actual font files, not Unity/TMP",
        parameters=dict(ink="#28466B",opacity=237/255,font_size_px=BODY_SIZE,
                        line_height_em=LINE_HEIGHT,tracking_em=TRACKING,
                        paragraph_gap_lines=PARAGRAPH_GAP,baseline_jitter_em=0.025,
                        advance_jitter_em=0.006,line_start_jitter_px=0.7,
                        signature_scale=1.14,signature_slope_degrees=1.3,
                        jitter_is_deterministic=True),
        glyph_coverage=coverage,body_blocks=len(rows),
        overflow=[x for x in rows if not x["fits"]],
        published_files_checked=len(hashes),published_files_changed=changed,
        rows=rows)
    (OUTPUT/"layout-report.json").write_text(
        json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({k:v for k,v in report.items() if k not in ("rows","parameters")},
                     ensure_ascii=False,indent=2))
    if report["overflow"] or changed or any(x["missing"] for x in coverage.values()):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
