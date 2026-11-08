"""Register the generated crown to the original cut edge, exporting only the added strip.

The generated full portrait is a reference for the crown only. None of its face,
hair, clothes, or body pixels are used in the shipped atlas.
"""
from pathlib import Path
from PIL import Image
import json

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets/patches"
source = Image.open(ROOT / "reference/source-candidates/girl2/normal.png").convert("RGBA").crop((342, 0, 682, 576))
generated = Image.open(OUT / "xiaoman_hat_generated.png").convert("RGBA")

# Landmarks measured in the generated crown: at row 115 the continuous opaque
# edge spans x=274..678. The original cut edge spans x=96..239 (144 pixels).
scale = 405 / 144
left = 274 - 96 * scale
bottom = 116
crop = (left, bottom - 32 * scale, left + 340 * scale, bottom)
crown = generated.transform((340, 32), Image.Transform.AFFINE,
    (scale, 0, crop[0], 0, scale, crop[1]), resample=Image.Resampling.NEAREST)

# Match the three exact colors already used at the source hat's cut edge. This
# removes generated gradient/alpha fringes, retaining the source game's palette.
palette = [(0, 0, 0), (183, 220, 255), (107, 151, 217)]
pixels = crown.load()
for y in range(crown.height):
    for x in range(crown.width):
        r, g, b, a = pixels[x, y]
        if a < 200:
            pixels[x, y] = (0, 0, 0, 0)
        else:
            color = min(palette, key=lambda c: (r-c[0])**2 + (g-c[1])**2 + (b-c[2])**2)
            pixels[x, y] = (*color, 255)

# Retain only the component connected to the portrait, removing isolated output
# flecks in the transparent margin without altering the original source layer.
connected = set()
pending = [(x, 31) for x in range(340) if pixels[x, 31][3]]
while pending:
    x, y = pending.pop()
    if (x, y) in connected or x < 0 or x >= 340 or y < 0 or y >= 32 or not pixels[x, y][3]:
        continue
    connected.add((x, y))
    pending.extend([(x-1, y), (x+1, y), (x, y-1), (x, y+1)])
for y in range(32):
    for x in range(340):
        if (x, y) not in connected:
            pixels[x, y] = (0, 0, 0, 0)
crown.save(OUT / "xiaoman_hat_crown.png", optimize=True)
preview = Image.new("RGBA", (340, 608))
preview.alpha_composite(crown, (0, 0))
preview.alpha_composite(source, (0, 32))
preview.save(OUT / "xiaoman_repaired_preview.png")
info = {"mode": "built-in image_gen", "generated_reference": "xiaoman_hat_generated.png",
        "output": "xiaoman_hat_crown.png", "size": [340, 32], "generated_anchor_row": 115,
        "generated_anchor_span": [274, 678], "original_anchor_span": [96, 239],
        "palette": palette, "original_body_pixels_used_unchanged": True}
(OUT / "hat-integration.json").write_text(json.dumps(info, indent=2), encoding="utf-8")
print("Crown bbox:", crown.getbbox(), "; original portrait preserved at (0,32).")
