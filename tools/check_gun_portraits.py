"""Read-only validation of the optional gun portraits and untouched normal artwork."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[1]


def main():
    manifest = json.loads((ROOT / "assets/gun-portraits/manifest.json").read_text(encoding="utf-8-sig"))
    package = ROOT / "package/XiaomanVisitor"
    for path, expected in ((ROOT / manifest["source"], manifest["source_sha256"]),
                           (package / manifest["base"], manifest["base_sha256"]),
                           (package / manifest["packaged"], manifest["sha256"])):
        assert hashlib.sha256(path.read_bytes()).hexdigest() == expected, path
    assert manifest["base_sha256"] == "6ba7596e421a37ff926d07dafbeab1ff55294757ab84132c39018b306b25e0a3"
    with Image.open(package / manifest["base"]) as original, Image.open(package / manifest["packaged"]) as sheet:
        assert sheet.size == (1152, 1376)
        base = original.convert("RGBA").crop((0, 0, 384, 688))
        alpha = base.getchannel("A").tobytes()
        frames = []
        previous = None
        x0, y0, x1, y1 = manifest["edited_bounds"]
        for row in range(2):
            for mouth in range(3):
                frame = sheet.convert("RGBA").crop((mouth*384, row*688, (mouth+1)*384, (row+1)*688))
                assert frame.getchannel("A").tobytes() == alpha, "Native silhouette changed"
                # Compare RGB separately: an opaque alpha channel hides RGB-only
                # differences when PIL computes a four-channel bounding box.
                bounds = ImageChops.difference(frame.convert("RGB"), base.convert("RGB")).getbbox()
                assert bounds and bounds[0] >= x0 and bounds[1] >= y0 and bounds[2] <= x1 and bounds[3] <= y1, bounds
                frames.append(frame.crop((177, 308, 207, 332)).tobytes())
                if mouth == 0:
                    previous = frame
                else:
                    mouth_bounds = ImageChops.difference(frame.convert("RGB"), previous.convert("RGB")).getbbox()
                    assert mouth_bounds and mouth_bounds[0] >= 177 and mouth_bounds[1] >= 308 \
                        and mouth_bounds[2] <= 207 and mouth_bounds[3] <= 332, "Eyes/brows/sweat jitter between mouth states"
        assert len(set(frames[:3])) == len(set(frames[3:])) == 3, "Speaking mouth states must differ"
    text = json.loads((ROOT / "story/持枪反应.json").read_text(encoding="utf-8"))["localization"]
    assert set(text) == {"zh", "ja", "en"} and all(set(rows) == set(text["zh"]) for rows in text.values())
    print("PASS: six distinct mouth frames, facial-only changes, original body/silhouette, portrait hashes and three-language gun dialogue.")


if __name__ == "__main__":
    main()
