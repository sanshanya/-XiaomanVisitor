"""Build independent shop/room atlases from approved source pixels; no sharpening."""
from pathlib import Path
import hashlib
import json
import sys
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
FRAME = (384,688)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    source = json.loads((ROOT / "assets/asset-manifest.json").read_text(encoding="utf-8"))
    assert source["runtime_frame_width"] == 96 and source["runtime_frame_height"] == 172
    assert len(source["files"]) == 18
    profiles = (
        ("shop", "Portraits/xiaoman_atlas_hd.png", "assets/portrait-detail.json", Image.Resampling.NEAREST,
         "nearest from approved original composites", "original pixel edges", 0),
        ("tavern", "Portraits/xiaoman_tavern_atlas.png", "assets/tavern-scene/portrait-detail.json", Image.Resampling.BILINEAR,
         "premultiplied-alpha bilinear from approved original composites", "natural edges and softer dithering", 0),
    )
    for name, packaged, metadata, filter_mode, resize, appearance, viewport in profiles:
        atlas = Image.new("RGBA",(2304,2064)); entries = []
        for index,row in enumerate(source["files"]):
            path = ROOT / "assets/portraits" / row["file"]
            assert hashlib.sha256(path.read_bytes()).hexdigest() == row["sha256"]
            original = Image.open(path).convert("RGBA")
            if name == "shop":
                # Restore the exact earlier pixel-preserving rendering without
                # passing the artwork through the later softening operation.
                resized = original.resize((384,684),filter_mode)
            else:
                resized = original.convert("RGBa").resize((384,684),filter_mode).convert("RGBA")
            frame = Image.new("RGBA",FRAME); frame.paste(resized,(0,4))
            x,y = index%6*384,index//6*688
            atlas.paste(frame,(x,y))
            entries.append(dict(index=index,file=row["file"],mood=row["mood"],mouth=row["mouth"],
                                source_sha256=row["sha256"],x=x,y=y))
        target = ROOT / "package/XiaomanVisitor" / packaged
        target.parent.mkdir(parents=True,exist_ok=True); atlas.save(target,optimize=True)
        manifest = dict(packaged=packaged,sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
                        bytes=target.stat().st_size,size=[2304,2064],frame=list(FRAME),scale=4,
                        columns=6,rows=3,source_frame=[340,608],resize=resize,sharpening=False,
                        appearance=appearance,lazy_load=True,viewport_height_above=viewport,
                        world_geometry_unchanged=True,usage="native_nearest_fallback" if name == "shop" else "native_shop_and_tavern",frames=entries)
        (ROOT / metadata).write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(name,manifest["sha256"],manifest["bytes"])


if __name__ == "__main__":
    main()
