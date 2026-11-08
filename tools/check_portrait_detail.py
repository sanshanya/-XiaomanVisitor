"""Check source fidelity, frame mapping and unchanged geometry for the high-detail portrait pack."""
from pathlib import Path
import hashlib
import json
import sys
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    data = json.loads((ROOT / "assets/portrait-detail.json").read_text(encoding="utf-8"))
    source = json.loads((ROOT / "assets/asset-manifest.json").read_text(encoding="utf-8"))
    path = ROOT / "package/XiaomanVisitor" / data["packaged"]
    assert hashlib.sha256(path.read_bytes()).hexdigest() == data["sha256"]
    atlas = Image.open(path).convert("RGBA")
    assert atlas.size == (2304,2064) and len(data["frames"]) == len(source["files"]) == 18
    moods = source["rows"]
    for index, row in enumerate(data["frames"]):
        assert row["index"] == index and row["mood"] == moods[index // 3] and row["mouth"] == index % 3
        original_file = ROOT / "assets/portraits" / row["file"]
        assert hashlib.sha256(original_file.read_bytes()).hexdigest() == row["source_sha256"] == source["files"][index]["sha256"]
        original = Image.open(original_file).convert("RGBA")
        expected = Image.new("RGBA", (384,688))
        expected.paste(original.convert("RGBa").resize((384,684),Image.Resampling.BILINEAR).convert("RGBA"),(0,4))
        assert row["x"] == index % 6 * 384 and row["y"] == index // 6 * 688
        frame = atlas.crop((row["x"],row["y"],row["x"]+384,row["y"]+688))
        assert frame.tobytes() == expected.tobytes(), "Changed artwork or wrong expression/mouth cell"
    assert data["scale"] == 4 and data["frame"] == [384,688] and data["lazy_load"]
    assert not data["sharpening"] and data["resize"].startswith("premultiplied-alpha bilinear")
    standard = ROOT / "package/XiaomanVisitor/Textures/xiaoman_atlas.png"
    approved = json.loads((ROOT / "assets/approved-textures.sha256.json").read_text(encoding="utf-8"))
    assert hashlib.sha256(standard.read_bytes()).hexdigest() == approved["files"]["Textures/xiaoman_atlas.png"]
    # This is an asset comparison at identical screen size, not an in-game capture.
    old = Image.open(standard).convert("RGBA").crop((0,0,96,172)).resize((384,688),Image.Resampling.NEAREST)
    new = atlas.crop((0,0,384,688))
    preview = Image.new("RGBA",(800,728),(38,33,46,255)); draw = ImageDraw.Draw(preview)
    draw.text((16,12),"Previous low-resolution frame enlarged",fill="white")
    draw.text((416,12),"Original detail / same size and expression",fill="white")
    preview.alpha_composite(old,(8,32));preview.alpha_composite(new,(408,32))
    output = ROOT / "experiments/hires/portrait-comparison.png"
    output.parent.mkdir(parents=True,exist_ok=True)
    preview.convert("RGB").save(output,optimize=True)
    print("PASS: all 18 HD frames use a light linear resample of the approved originals; no sharpening; standard atlas and world geometry unchanged.")


if __name__ == "__main__":
    main()
