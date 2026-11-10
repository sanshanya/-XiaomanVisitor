"""Bake approved Yozai letter ink only; runtime lazily loads one localized layer.

The paper, UI controls, original text/localization and saved item IDs stay native.
Run again after editing letter CSVs. Fonts are authoring inputs, not runtime deps.
"""
import hashlib
import json
import shutil
import sys
from PIL import Image
import preview_handwriting as layout


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    target = layout.PACKAGE / "Letters"
    target.mkdir(exist_ok=True)
    font = layout.FONTS["yozai"]
    chars = layout.cmap_coverage(font)
    rows = []
    for language in ("zh", "ja", "en"):
        texts = layout.read_texts(language)
        folder = target / language
        folder.mkdir(exist_ok=True)
        letters=[(visit,f"story_note_{visit:02d}") for visit in range(1,15)]+[(3,'story_note_03_b'),(4,'story_note_04_b')]
        for visit,key in letters:
            required = "".join(texts[key+"_"+part] for part in ("title","body","signature"))
            missing = {c for c in required if not c.isspace() and layout.glyph_path(font,c) is None}
            assert not missing, (key, language, missing)
            row, ink = layout.render_ink_layer("yozai", visit, language, texts,letter_id=key)
            assert row["fits"], row
            # Match the original paper's native resolution; no repeated paper bitmaps.
            ink = ink.resize((1122, 1402), Image.Resampling.LANCZOS)
            # Preserve the 256 coverage levels of a single ink colour without
            # storing redundant RGB per pixel. Keep opacity at the approved 93%.
            alpha = ink.getchannel("A").point(lambda a: min(a,237))
            ink = Image.frombytes("P",alpha.size,alpha.tobytes())
            ink.putpalette(list(layout.INK[:3])*256)
            ink.info["transparency"] = bytes(range(256))
            path = folder / (key + ".png")
            ink.save(path, optimize=True)
            rows.append(dict(id=key, language=language,
                             file=path.relative_to(layout.PACKAGE).as_posix(),
                             sha256=digest(path), bytes=path.stat().st_size,
                             text_sha256=hashlib.sha256(required.encode("utf-8")).hexdigest(),
                             layout=row))
    document = dict(schema=1,font="Yozai Regular",font_sha256=digest(font),
                    kaomoji_fallbacks=[dict(file=p.name,sha256=digest(p)) for p in layout.FALLBACKS],
                    authoring_font="experiments/handwriting/fonts/Yozai-Regular.ttf",
                    renderer="tools/prepare_handwriting.py",width=1122,height=1402,
                    ink="#28466B",opacity=237/255,body_size_px=18,
                    line_height_em=layout.LINE_HEIGHT,tracking_em=layout.TRACKING,
                    paragraph_gap_lines=layout.PARAGRAPH_GAP,
                    baseline_jitter_em=0.025,advance_jitter_em=0.006,
                    line_start_jitter_px=0.7,signature_scale=1.14,
                    signature_slope_degrees=1.3,jitter_is_deterministic=True,
                    languages=["zh","ja","en"],
                    aliases={"thank_you_note":"story_note_01"},letters=rows)
    (target/"index.json").write_text(json.dumps(document,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    licenses = layout.PACKAGE / "Licenses"
    licenses.mkdir(exist_ok=True)
    shutil.copyfile(font.with_name("Yozai-Regular-OFL.txt"),licenses/"Yozai-Regular-OFL.txt")
    (licenses/"字体说明.txt").write_text(
        "小满信件字体：悠哉 Regular / Yozai Regular v0.868\n"
        "字体作者及原始来源：https://github.com/lxgw/yozai-font\n"
        "许可：SIL Open Font License 1.1，完整授权及版权声明见 Yozai-Regular-OFL.txt。\n"
        "发布包仅含本模组固定信件的文字图层，不包含完整字体文件。\n"
        "第二、五封信颜文字的少数字符在制作时使用Windows的Segoe UI、Gadugi和Leelawadee UI字形回退；正文仍为悠哉，不附带这些字体文件。\n"
        "这些文字图层在制作阶段生成；玩家不需要安装字体或联网。\n",encoding="utf-8")
    print(json.dumps(dict(letters=len(rows),bytes=sum(r["bytes"] for r in rows),
                         max_file=max(r["bytes"] for r in rows),font_in_package=False),indent=2))


if __name__ == "__main__":
    main()
