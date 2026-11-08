"""Extract the six original drink drawings and resize them to the game's item grid."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import hashlib
import json
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "assets/drinks"
OUT = ROOT / "package/XiaomanVisitor/Textures/Items"
SPECS = [
    ("whale_dream", (120, 80, 396, 510), (16, 32)),
    ("sunset_blue", (110, 95, 390, 500), (16, 32)),
    ("bluebird", (100, 120, 400, 505), (16, 32)),
    ("cosmo_zone", (160, 60, 355, 515), (16, 32)),
    ("sky_after_rain", (120, 50, 360, 525), (16, 32)),
    ("rainy_blue", (110, 135, 410, 465), (16, 32)),
]

# Exact colours observed in native blue_serum, med_bottle_blue, wine_berry and nudka.
# Native objects use a small shared palette instead of hundreds of resampled neon colours.
PALETTE = np.array([
    (41, 34, 52), (39, 52, 86), (53, 82, 113), (62, 105, 121),
    (93, 124, 137), (138, 191, 187), (213, 210, 184),
    (55, 50, 72), (86, 49, 104), (116, 61, 112), (162, 114, 172),
    (68, 70, 76), (95, 104, 104), (136, 148, 138), (168, 174, 165),
], dtype=np.uint8)


def native_pixels(image):
    rgba = np.array(image)
    rgb = rgba[:, :, :3].astype(np.float32)
    luminance = rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    graded = (luminance[:, :, None] + (rgb - luminance[:, :, None]) * 0.55) * 0.80
    graded += np.array([10, 7, 12], dtype=np.float32)
    distance = np.sum((graded[:, :, None, :] - PALETTE.astype(np.float32)) ** 2, axis=3)
    colours = PALETTE[np.argmin(distance, axis=2)]
    opaque = rgba[:, :, 3] >= 80
    # A one-pixel opaque contour makes the glass readable against the counter.
    padded = np.pad(opaque, 1)
    interior = opaque & padded[:-2, 1:-1] & padded[2:, 1:-1] & padded[1:-1, :-2] & padded[1:-1, 2:]
    colours[opaque & ~interior] = PALETTE[0]
    result = np.zeros_like(rgba)
    result[opaque, :3] = colours[opaque]
    result[opaque, 3] = 255
    return Image.fromarray(result)


def extract(source, box):
    image = source.convert("RGBA").crop(box)
    # Only the exact background is removed; enclosed black glass details stay intact.
    mask = Image.new("L", (image.width + 2, image.height + 2))
    pixels = image.load()
    target = mask.load()
    for y in range(image.height):
        for x in range(image.width):
            if max(pixels[x, y][:3]) > 8:
                target[x + 1, y + 1] = 255
    ImageDraw.floodfill(mask, (0, 0), 1)
    alpha = mask.crop((1, 1, image.width + 1, image.height + 1)).point(lambda n: 0 if n == 1 else 255)
    image.putalpha(alpha)
    return image.crop(image.getbbox())


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    (BASE / "cutouts").mkdir(exist_ok=True)
    rows = []
    preview = Image.new("RGB", (6 * 160, 570), (64, 57, 65))
    draw = ImageDraw.Draw(preview)
    font = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 17)
    names = json.loads((BASE / "catalog.json").read_text(encoding="utf-8"))
    for i, (key, box, size) in enumerate(SPECS):
        source = BASE / f"original/cocktail/detail_{i + 1:02}.png"
        with Image.open(source) as image:
            cutout = extract(image, box)
        cutout.save(BASE / f"cutouts/{key}.png")
        max_art_width, max_art_height = (14, 14) if key == "rainy_blue" else (14, 30)
        scale = min(max_art_width / cutout.width, max_art_height / cutout.height)
        resized_size = tuple(max(1, round(n * scale)) for n in cutout.size)
        resized = cutout.convert("RGBa").resize(resized_size, Image.Resampling.LANCZOS).convert("RGBA")
        resized = native_pixels(resized)
        if key in ("whale_dream", "sunset_blue", "bluebird"):
            # At 1x2 cells a thin stem can fall between samples. Restore only empty
            # interior rows with a one-pixel line using the native outline colour.
            pixels = np.array(resized)
            occupied = np.flatnonzero(np.any(pixels[:, :, 3] != 0, axis=1))
            if len(occupied):
                centre = (resized.width - 1) // 2
                for y in range(int(occupied[0]), int(occupied[-1]) + 1):
                    if not np.any(pixels[y, :, 3]):
                        pixels[y, centre, :3] = PALETTE[0]
                        pixels[y, centre, 3] = 255
            resized = Image.fromarray(pixels)
        runtime = Image.new("RGBA", size)
        runtime.alpha_composite(resized, ((size[0] - resized.width) // 2, size[1] - resized.height - 1))
        path = OUT / f"xiaoman_cocktail_{key}.png"
        runtime.save(path, optimize=True)
        # Inspection sheet uses nearest-neighbour enlargement of the actual runtime pixels.
        enlarged = runtime.resize((size[0] * 3, size[1] * 3), Image.Resampling.NEAREST)
        preview.paste(enlarged, (i * 160 + (160 - enlarged.width) // 2, 55), enlarged)
        draw.text((i * 160 + 10, 15), names[i]["name_zh"], font=font, fill=(224, 225, 222))
        draw.text((i * 160 + 10, 260), f"{size[0]} x {size[1]}", font=font, fill=(168, 192, 212))
        draw.text((i * 160 + 10, 310), "Actual pixels", fill=(200, 200, 200))
        preview.paste(runtime, (i * 160 + (160 - size[0]) // 2, 350), runtime)
        rows.append({"key": key, "name_zh": names[i]["name_zh"], "source": str(source.relative_to(ROOT)),
                     "crop": box, "cutout_size": cutout.size, "runtime_size": size,
                     "sprite_key": path.stem, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                     "visible_bbox": runtime.getbbox(),
                     "opaque_colours": len({p[:3] for p in runtime.get_flattened_data() if p[3]}),
                     "processing": "original silhouette; smaller visible size; native 15-colour palette; opaque pixel contour; binary alpha"})
    preview.save(BASE / "runtime-preview.png")
    (BASE / "runtime-manifest.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (BASE / "native-palette.json").write_text(json.dumps(PALETTE.tolist(), indent=2) + "\n", encoding="utf-8")
    print("Prepared six crisp native-palette drink sprites on the same 16x32 / 1x2 grid.")


if __name__ == "__main__":
    main()
