"""Check source fidelity, frame mapping and unchanged geometry for the high-detail portrait pack."""
from pathlib import Path
import hashlib
import json
import sys
from PIL import Image

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
        expected.paste(original.resize((384,684),Image.Resampling.NEAREST),(0,4))
        assert row["x"] == index % 6 * 384 and row["y"] == index // 6 * 688
        frame = atlas.crop((row["x"],row["y"],row["x"]+384,row["y"]+688))
        assert frame.tobytes() == expected.tobytes(), "Changed artwork or wrong expression/mouth cell"
    assert data["scale"] == 4 and data["frame"] == [384,688] and data["lazy_load"]
    assert not data["sharpening"] and data["resize"].startswith("nearest")
    assert data["sha256"] == "980846867b55c6367e7c988f9737967c71e06e593721eb2cf0f6c1160476e253", "Shop atlas must match the approved nearest fallback"
    tavern = json.loads((ROOT / "assets/tavern-scene/portrait-detail.json").read_text(encoding="utf-8"))
    assert tavern["packaged"] != data["packaged"]
    assert tavern["sha256"] == "6ba7596e421a37ff926d07dafbeab1ff55294757ab84132c39018b306b25e0a3", "Keep the already accepted tavern portrait unchanged"
    assert hashlib.sha256((ROOT / "package/XiaomanVisitor" / tavern["packaged"]).read_bytes()).hexdigest() == tavern["sha256"]
    standard = ROOT / "package/XiaomanVisitor/Textures/xiaoman_atlas.png"
    approved = json.loads((ROOT / "assets/approved-textures.sha256.json").read_text(encoding="utf-8"))
    assert hashlib.sha256(standard.read_bytes()).hexdigest() == approved["files"]["Textures/xiaoman_atlas.png"]
    print("PASS: approved portrait pixels, frame mapping and shared/fallback atlas hashes.")


if __name__ == "__main__":
    main()
