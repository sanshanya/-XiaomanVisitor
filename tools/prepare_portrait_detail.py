"""Build high-resolution frames from approved original composites, without redrawing faces."""
from pathlib import Path
import hashlib
import json
import sys
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SCALE = 4
FRAME = (96 * SCALE, 172 * SCALE)
COLUMNS, ROWS = 6, 3


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    source = json.loads((ROOT / "assets/asset-manifest.json").read_text(encoding="utf-8"))
    assert source["runtime_frame_width"] == 96 and source["runtime_frame_height"] == 172
    assert len(source["files"]) == 18
    atlas = Image.new("RGBA", (FRAME[0] * COLUMNS, FRAME[1] * ROWS))
    entries = []
    for index, row in enumerate(source["files"]):
        path = ROOT / "assets/portraits" / row["file"]
        assert hashlib.sha256(path.read_bytes()).hexdigest() == row["sha256"], "Original composite changed"
        original = Image.open(path).convert("RGBA")
        # Exact 4x of the existing sprite geometry, but sample from the original
        # 340x608 artwork instead of magnifying the prefiltered 96x172 result.
        frame = Image.new("RGBA", FRAME)
        # A light linear resample softens the original single-pixel dithering.
        # Premultiplied alpha avoids dark fringes; no sharpening or blur filter.
        natural = original.convert("RGBa").resize((FRAME[0], 171 * SCALE), Image.Resampling.BILINEAR).convert("RGBA")
        frame.paste(natural, (0, SCALE))
        box = [(index % COLUMNS) * FRAME[0], (index // COLUMNS) * FRAME[1]]
        atlas.paste(frame, tuple(box))
        entries.append(dict(index=index, file=row["file"], mood=row["mood"], mouth=row["mouth"],
                            source_sha256=row["sha256"], x=box[0], y=box[1]))
    target = ROOT / "package/XiaomanVisitor/Portraits/xiaoman_atlas_hd.png"
    target.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(target, optimize=True)
    manifest = dict(packaged="Portraits/xiaoman_atlas_hd.png", sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
                    bytes=target.stat().st_size, size=list(atlas.size), frame=list(FRAME), scale=SCALE,
                    columns=COLUMNS, rows=ROWS, source_frame=[340,608], resize="premultiplied-alpha bilinear from approved original composites",
                    sharpening=False, appearance="natural edges and softer dithering",
                    lazy_load=True, viewport_height_above=1080, world_geometry_unchanged=True, frames=entries)
    (ROOT / "assets/portrait-detail.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key:manifest[key] for key in ("packaged","bytes","size","frame","lazy_load")},indent=2))


if __name__ == "__main__":
    main()
