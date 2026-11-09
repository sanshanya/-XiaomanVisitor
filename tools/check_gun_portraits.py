"""Read-only validation of the optional gun portraits and untouched normal artwork."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[1]


def main():
    manifest = json.loads((ROOT / "assets/gun-portraits/manifest.json").read_text(encoding="utf-8-sig"))
    package = ROOT / "package/XiaomanVisitor"
    assert manifest["method"] == "original-layers-forehead-shadow" and not manifest["generated_face_used"]
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
                if mouth:
                    mouth_bounds = ImageChops.difference(frame.convert("RGB"), row_frames[row][0].convert("RGB")).getbbox()
                    assert mouth_bounds and mouth_bounds[0] >= mx0 and mouth_bounds[1] >= my0 \
                        and mouth_bounds[2] <= mx1 and mouth_bounds[3] <= my1, "Eyes/brows/sweat jitter between mouth states"
        assert len(set(frames[:3])) == len(set(frames[3:])) == 3, "Speaking mouth states must differ"
        # The author chose the previous expressions again. Pin every pixel of
        # that atlas except the small forehead region, including both eye states.
        assert manifest["forehead_bounds"] == [102, 142, 282, 214]
        previous_hash = "c385cb9d53cf1c5942097f8c8c80bd9fb70f7626be2ad84dac0d2217ebc2bbce"
        assert manifest["previous_masked_rgba_sha256"] == previous_hash
        masked = bytearray(sheet.convert("RGBA").tobytes())
        for row in range(2):
            for column in range(3):
                for y in range(142, 214):
                    offset = ((row*688+y)*1152+column*384+102)*4
                    masked[offset:offset+180*4] = bytes(180*4)
        assert hashlib.sha256(masked).hexdigest() == previous_hash, "Eyes, sweat, mouth or other previous pixels changed outside the forehead"
        original_worried = original.convert("RGBA").crop((3*384, 2*688, 4*384, 3*688))
        assert row_frames[0][0].crop((167, 142, 212, 214)).tobytes() == original_worried.crop((167, 142, 212, 214)).tobytes(), "Central bangs changed with the forehead shadow"
        shaded_pixels = 0
        upper_darkening, upper_pixels, lower_darkening, lower_pixels = 0, 0, 0, 0
        for y in range(142, 214):
            for x in range(102, 282):
                p, q = original_worried.getpixel((x, y)), row_frames[0][0].getpixel((x, y))
                assert all(0 <= p[i]-q[i] <= 116 for i in range(3)), "Forehead shading lost its original color detail"
                skin = any(all(abs(p[i]-color[i]) <= 2 for i in range(3))
                           for color in ((153, 176, 231), (135, 115, 166)))
                skin_or_highlight = 105 <= p[0] <= 165 and 105 <= p[1] <= 182 and 155 <= p[2] <= 250
                if not skin_or_highlight:
                    assert p == q, "Hair or brow ink was changed by the forehead tint"
                if skin and 158 <= y < 178:
                    upper_darkening += sum(p[i]-q[i] for i in range(3))/3
                    upper_pixels += 1
                if skin and 198 <= y < 214:
                    lower_darkening += sum(p[i]-q[i] for i in range(3))/3
                    lower_pixels += 1
                shaded_pixels += any(p[i]-q[i] >= 3 for i in range(3))
        assert shaded_pixels >= 100, "Forehead tint is absent"
        assert upper_pixels >= 100 and lower_pixels >= 100, "Forehead sample regions are missing"
        assert upper_darkening/upper_pixels >= 40, "Forehead roots are still too faint"
        assert upper_darkening/upper_pixels >= lower_darkening/lower_pixels + 20, "Shadow must concentrate above the eyebrows"
    text = json.loads((ROOT / "story/持枪反应.json").read_text(encoding="utf-8"))["localization"]
    assert set(text) == {"zh", "ja", "en"} and all(set(rows) == set(text["zh"]) for rows in text.values())
    print(f"PASS: original eyes/sweat/mouth and pixels outside the forehead unchanged; {shaded_pixels} shaded pixels, darker roots and earlier lower fade; normal artwork/body/silhouette and three-language dialogue unchanged.")


if __name__ == "__main__":
    main()
