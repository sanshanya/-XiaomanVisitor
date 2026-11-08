"""Keep the original portrait layers intact; add a separately repaired crown above them."""
from pathlib import Path
import json
import hashlib
import shutil
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / "reference/source-candidates/girl2"
OUT = ROOT / "assets"
MOD = ROOT / "package/XiaomanVisitor"
CROP = (342, 0, 682, 576)
HEAD_PADDING = 32
FRAME_WIDTH = 340
FRAME_HEIGHT = 576 + HEAD_PADDING
HAT_PATCH = OUT / "patches/xiaoman_hat_crown.png"
GAME_WIDTH = 96
GAME_HEIGHT = 172
GAME_BODY_HEIGHT = 162
GAME_HEAD_PADDING = GAME_HEIGHT - GAME_BODY_HEIGHT
MOODS = [
    ("neutral", "normal_1_0", "normal_3_0", None),
    ("soft", "normal_1_1", "normal_3_0", None),
    ("closed", "normal_1_2", "normal_3_0", None),
    ("smile", "normal_1_3", "normal_3_0", None),
    ("shy", "normal_1_1", "normal_3_1", "normal_4_1"),
    ("worried", "normal_1_0", "normal_3_1", None),
]

def main():
    for folder in [OUT / "layers", OUT / "portraits", OUT / "game-ready", MOD / "Textures"]:
        folder.mkdir(parents=True, exist_ok=True)
    layers = {p.stem: Image.open(p).convert("RGBA") for p in RAW.glob("*.png")}
    crown = Image.open(HAT_PATCH).convert("RGBA")
    if crown.size != (FRAME_WIDTH, HEAD_PADDING):
        raise ValueError(f"Crown patch must be {FRAME_WIDTH}x{HEAD_PADDING}, got {crown.size}")
    manifest = {"source_game": "雨音に満ちる夜 -A night filled with the sound of rain-",
                "source_folder": "img/pictures/busts/girl2", "crop": CROP,
                "frame_width": FRAME_WIDTH, "frame_height": FRAME_HEIGHT,
                "head_padding": HEAD_PADDING, "hat_repair": "patches/xiaoman_hat_crown.png",
                "original_layers_preserved": True, "rest_expression": "original normal.png, no facial overlays",
                "rest_mouth_layer": "normal_2_0", "runtime_flat_mouth_min_size": [2, 1],
                "runtime_frame_width": GAME_WIDTH, "runtime_frame_height": GAME_HEIGHT,
                "runtime_body_height": GAME_BODY_HEIGHT, "runtime_head_padding": GAME_HEAD_PADDING,
                "runtime_resize": "premultiplied-alpha Lanczos", "columns": 3,
                "rows": [m[0] for m in MOODS], "files": []}
    for path in sorted(RAW.glob("*.png")):
        shutil.copy2(path, OUT / "layers" / path.name)
    atlas = Image.new("RGBA", (GAME_WIDTH * 3, GAME_HEIGHT * len(MOODS)))
    stills = []
    for row, (name, eyes, brows, blush) in enumerate(MOODS):
        for mouth in range(3):
            im = layers["normal"].copy()
            # Preserve the exact original neutral portrait for the resting frame.
            # Other frames still use the original eyes/brows and straight closed mouth.
            if name != "neutral" or mouth != 0:
                for part in [eyes, brows, f"normal_2_{mouth}", blush]:
                    if part:
                        im.alpha_composite(layers[part])
            original = im.crop(CROP)
            im = Image.new("RGBA", (FRAME_WIDTH, FRAME_HEIGHT))
            im.alpha_composite(crown, (0, 0))
            im.alpha_composite(original, (0, HEAD_PADDING))
            path = OUT / "portraits" / f"xiaoman_{name}_{mouth}.png"
            im.save(path, optimize=True)
            # Pre-filter once at build time instead of asking the world camera to
            # point-sample a 576px body down to approximately 162 logical pixels.
            # RGBa prevents dark fringes from transparent RGB during the resize.
            resized = im.convert("RGBa").resize((GAME_WIDTH, 171), Image.Resampling.LANCZOS).convert("RGBA")
            runtime = Image.new("RGBA", (GAME_WIDTH, GAME_HEIGHT))
            runtime.alpha_composite(resized, (0, 1))
            if mouth == 0:
                # Keep the original short, flat mouth readable on the target grid.
                # Project its source bounding box to at least 2x1 pixels, using its
                # original black color instead of substituting a smiling expression.
                source_mouth = layers["normal_2_0"]
                mx0, my0, mx1, my1 = source_mouth.getbbox()
                scale_x = GAME_WIDTH / FRAME_WIDTH
                scale_y = 171 / FRAME_HEIGHT
                x = round((mx0 - CROP[0]) * scale_x)
                y = round((my0 + HEAD_PADDING) * scale_y + 1)
                w = max(2, round((mx1 - mx0) * scale_x))
                h = max(1, round((my1 - my0) * scale_y))
                color = source_mouth.getpixel(((mx0 + mx1) // 2, (my0 + my1) // 2))
                ImageDraw.Draw(runtime).rectangle((x, y, x + w - 1, y + h - 1), fill=color)
            # Remove near-transparent resampling ringing outside the silhouette.
            runtime.putalpha(runtime.getchannel("A").point(lambda a: 0 if a < 16 else a))
            runtime.save(OUT / "game-ready" / path.name, optimize=True)
            atlas.alpha_composite(runtime, (mouth * GAME_WIDTH, row * GAME_HEIGHT))
            manifest["files"].append({"file": path.name, "mood": name, "mouth": mouth,
                                       "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                                       "runtime_sha256": hashlib.sha256((OUT / "game-ready" / path.name).read_bytes()).hexdigest()})
            if mouth == 0:
                stills.append(im)
    atlas.save(MOD / "Textures/xiaoman_atlas.png", optimize=True)
    (OUT / "asset-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    sheet = Image.new("RGB", (6 * 230, 460), (25, 28, 43))
    draw = ImageDraw.Draw(sheet)
    for index, (im, mood) in enumerate(zip(stills, MOODS)):
        im = im.resize((210, 376), Image.Resampling.NEAREST)
        sheet.paste(im, (index * 230 + 4, 52), im)
        draw.text((index * 230 + 16, 23), mood[0], fill=(176, 205, 255))
    sheet.save(OUT / "expressions-preview.png")
    # The mod menu thumbnail uses the extracted portrait and a simple backdrop.
    preview = Image.new("RGBA", (512, 512), (22, 29, 51, 255))
    d = ImageDraw.Draw(preview)
    d.rectangle((12, 12, 499, 499), outline=(107, 165, 213), width=2)
    portrait = stills[0].copy()
    portrait.thumbnail((320, 500), Image.Resampling.NEAREST)
    preview.alpha_composite(portrait, (96, 18))
    preview.convert("RGB").save(MOD / "preview.png")
    frames = [stills[0]] * 18 + [stills[1], stills[2], stills[1]] + [stills[0]] * 9 + [stills[3]] * 7
    frames[0].save(OUT / "xiaoman-preview.gif", save_all=True, append_images=frames[1:], duration=120,
                   loop=0, disposal=2, transparency=0)
    print(f"Prepared {len(manifest['files'])} portraits and {atlas.width}x{atlas.height} atlas.")

if __name__ == "__main__":
    main()
