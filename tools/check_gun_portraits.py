"""Read-only validation of the optional gun portraits and untouched normal artwork."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[1]


def main():
    manifest = json.loads((ROOT / "assets/gun-portraits/manifest.json").read_text(encoding="utf-8-sig"))
    package = ROOT / "package/XiaomanVisitor"
    assert manifest["method"] == "original-layer-composition" and not manifest["generated_face_used"]
    original_sources = {
        "assets/layers/normal_1_1.png": "5b71ef05cd274547f5e7909b4b9f3dd4f44d0748cd5bd63865edb14aa0e9fbc2",
        "assets/layers/normal_3_1.png": "a0fc66928abfe051843c65fca960215ac8b3963a6229ce9de6e5cf891e09e809",
        "assets/layers/normal_4_2.png": "7f25f9ff26b3c3c666000b3c400d5fb5cdaa607a5c41cdf0648813cc1de278ad",
    }
    assert {row["path"]: row["sha256"] for row in manifest["sources"]} == original_sources
    expected_files = [(ROOT / path, digest) for path, digest in original_sources.items()]
    expected_files.extend(((package / manifest["base"], manifest["base_sha256"]),
                           (package / manifest["packaged"], manifest["sha256"])))
    for path, expected in expected_files:
        assert hashlib.sha256(path.read_bytes()).hexdigest() == expected, path
    assert manifest["base_sha256"] == "6ba7596e421a37ff926d07dafbeab1ff55294757ab84132c39018b306b25e0a3"
    with Image.open(package / manifest["base"]) as original, Image.open(package / manifest["packaged"]) as sheet:
        assert sheet.size == (1152, 1376)
        base = original.convert("RGBA").crop((0, 0, 384, 688))
        alpha = base.getchannel("A").tobytes()
        frames = []
        row_frames = []
        x0, y0, x1, y1 = manifest["edited_bounds"]
        mx0, my0, mx1, my1 = manifest["mouth_bounds"]
        for row in range(2):
            row_frames.append([])
            for mouth in range(3):
                frame = sheet.convert("RGBA").crop((mouth*384, row*688, (mouth+1)*384, (row+1)*688))
                row_frames[row].append(frame)
                assert frame.getchannel("A").tobytes() == alpha, "Native silhouette changed"
                # Compare RGB separately: an opaque alpha channel hides RGB-only
                # differences when PIL computes a four-channel bounding box.
                bounds = ImageChops.difference(frame.convert("RGB"), base.convert("RGB")).getbbox()
                assert bounds and bounds[0] >= x0 and bounds[1] >= y0 and bounds[2] <= x1 and bounds[3] <= y1, bounds
                frames.append(frame.crop((mx0, my0, mx1, my1)).tobytes())
                expected_index = 15 + mouth
                ex, ey = expected_index % 6 * 384, expected_index // 6 * 688
                expected = original.convert("RGBA").crop((ex, ey, ex+384, ey+688))
                assert frame.crop((mx0, my0, mx1, my1)).tobytes() == expected.crop((mx0, my0, mx1, my1)).tobytes(), "Original mouth shape or position changed"
                if row == 0:
                    # Only the source sweat layer may differ from an approved
                    # worried frame. A generated face cannot satisfy this check.
                    sweat_bounds = ImageChops.difference(frame.convert("RGB"), expected.convert("RGB")).getbbox()
                    assert sweat_bounds and sweat_bounds[0] >= 237 and sweat_bounds[1] >= 296 \
                        and sweat_bounds[2] <= 249 and sweat_bounds[3] <= 311, sweat_bounds
                else:
                    eye_bounds = ImageChops.difference(frame.convert("RGB"), row_frames[0][mouth].convert("RGB")).getbbox()
                    assert eye_bounds and eye_bounds[0] >= 69 and eye_bounds[1] >= 174 \
                        and eye_bounds[2] <= 305 and eye_bounds[3] <= 284, ("Eyes/brows escaped the original layer coordinates", eye_bounds)
                if mouth:
                    mouth_bounds = ImageChops.difference(frame.convert("RGB"), row_frames[row][0].convert("RGB")).getbbox()
                    assert mouth_bounds and mouth_bounds[0] >= mx0 and mouth_bounds[1] >= my0 \
                        and mouth_bounds[2] <= mx1 and mouth_bounds[3] <= my1, "Eyes/brows/sweat jitter between mouth states"
        assert len(set(frames[:3])) == len(set(frames[3:])) == 3, "Speaking mouth states must differ"
    text = json.loads((ROOT / "story/持枪反应.json").read_text(encoding="utf-8"))["localization"]
    assert set(text) == {"zh", "ja", "en"} and all(set(rows) == set(text["zh"]) for rows in text.values())
    print("PASS: six original-layer frames, exact original mouth/face baseline, fixed layer registration, unchanged normal artwork/body/silhouette, three-language dialogue. Art intensity still awaits review.")


if __name__ == "__main__":
    main()
