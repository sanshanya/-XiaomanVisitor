"""Read-only validation of the optional gun portraits and untouched normal artwork."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[1]


def count_rgb(image, box, predicate):
    region = image.crop(box).convert("RGB")
    pixels = region.load()
    return sum(predicate(*pixels[x, y]) for y in range(region.height) for x in range(region.width))


def main():
    manifest = json.loads((ROOT / "assets/gun-portraits/manifest.json").read_text(encoding="utf-8-sig"))
    package = ROOT / "package/XiaomanVisitor"
    assert manifest["method"] == "manual-native-pixel-art" and not manifest["generated_face_used"]
    original_sources = {
        "assets/layers/normal_1_2.png": "14e49b7a941f984f015e247b6f5497f824400e7c5206f2dfa83db2eb1d46663a",
        "assets/layers/normal_3_1.png": "a0fc66928abfe051843c65fca960215ac8b3963a6229ce9de6e5cf891e09e809",
        "assets/portraits/xiaoman_neutral_0.png": "81986ab60e60b39bc041724f30c0aa96178ed48dc6ae5976bc6df37431ced652",
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
                if mouth:
                    mouth_bounds = ImageChops.difference(frame.convert("RGB"), row_frames[row][0].convert("RGB")).getbbox()
                    assert mouth_bounds and mouth_bounds[0] >= mx0 and mouth_bounds[1] >= my0 \
                        and mouth_bounds[2] <= mx1 and mouth_bounds[3] <= my1, "Eyes/brows/sweat jitter between mouth states"
        assert len(set(frames[:3])) == len(set(frames[3:])) == 3, "Speaking mouth states must differ"
        # These visual requirements reject the previous 'normal face plus sweat'
        # asset even if its dimensions, hashes and runtime bindings all pass.
        for row, expression in enumerate(row_frames):
            frame = expression[0]
            white_ratios, iris_ratios = [], []
            for box in ((90, 216, 171, 283), (209, 216, 290, 283)):
                white = lambda r, g, b: r >= 180 and g >= 200 and b >= 225
                ratio = count_rgb(frame, box, white) / count_rgb(base, box, white)
                assert ratio >= 3, "Fear must visibly widen both eyes, beyond adding sweat"
                white_ratios.append(round(ratio, 2))
            for box in ((116, 231, 156, 277), (226, 231, 266, 277)):
                blue = lambda r, g, b: r < 120 and b-r > 90 and b-g > 35
                ratio = count_rgb(frame, box, blue) / count_rgb(base, box, blue)
                assert .3 < ratio < .9, "Keep visible blue irises, smaller than the neutral expression"
                iris_ratios.append(round(ratio, 2))
            shadow_pixels = 0
            for y in range(195, 228):
                for x in range(100, 288):
                    p, q = base.getpixel((x, y)), frame.getpixel((x, y))
                    skin = any(all(abs(p[i]-color[i]) < 3 for i in range(3))
                               for color in ((153, 176, 231), (135, 115, 166)))
                    shadow_pixels += skin and all(q[i] <= p[i]-10 for i in range(3))
            assert shadow_pixels >= 500, "Forehead shadow must be visible on the original skin"
            print(f"Fear row {row}: eye-white ratios={white_ratios}; blue-iris ratios={iris_ratios}; shaded forehead pixels={shadow_pixels}.")
    text = json.loads((ROOT / "story/持枪反应.json").read_text(encoding="utf-8"))["localization"]
    assert set(text) == {"zh", "ja", "en"} and all(set(rows) == set(text["zh"]) for rows in text.values())
    print("PASS: six visibly frightened frames, original mouths, stationary eyes while speaking, visible forehead shading, unchanged normal artwork/body/silhouette and three-language dialogue. Gameplay review remains with the author.")


if __name__ == "__main__":
    main()
