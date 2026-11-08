"""Import the author's layered room and validate existing animated portrait frames.

This is an offline asset tool. Only --pack copies the two scene textures into
the runtime package. Previews use existing frames, not generated artwork.
"""
import argparse
import hashlib
import json
import shutil
import wave
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SCENE = ROOT / "assets/tavern-scene"
PACKAGE = ROOT / "package/XiaomanVisitor"
OUTPUT = ROOT / "outputs/酒馆动态立绘验证"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, document):
    path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def current_frames():
    atlas_path = PACKAGE / "Portraits/xiaoman_atlas_hd.png"
    assert digest(atlas_path) == load(ROOT / "assets/portrait-detail.json")["sha256"]
    atlas = Image.open(atlas_path).convert("RGBA")
    assert atlas.size == (2304, 2064)
    return [atlas.crop((i % 6 * 384, i // 6 * 688,
                        (i % 6 + 1) * 384, (i // 6 + 1) * 688)) for i in range(18)]


def import_layers(source):
    layout = load(source / "scene-layout.json")
    width, height = (layout["canvas"][key] for key in ("width", "height"))
    character = layout["character"]
    assert (width, height) == (5016, 2823)
    assert character["source_size"] == [384, 688] and character["scale"] == 1
    assert character["rotation"] == 0 and character["opacity"] == 1
    x, y = character["x"], character["y"]
    assert (x, y) == (2628, 1004)
    background = Image.open(source / "sources/tavern-background-original.png").convert("RGBA")
    enlarged = background.resize((width, height), Image.Resampling.NEAREST)
    provided = Image.open(source / "xiaoman-transparent-original.png").convert("RGBA")
    foreground = Image.open(source / "layers/03-bar-foreground.png").convert("RGBA")
    assert background.size == (1672, 941) and provided.size == (384, 688)
    assert foreground.size == (width, height)
    assert enlarged.tobytes() == Image.open(source / "layers/01-background-3x.png").convert("RGBA").tobytes()
    bounds = (x, layout["bar_occlusion_y_at_character"], x + 384, y + 688)
    occluder = foreground.crop(bounds)
    assert bounds == (2628, 1644, 3012, 1692)
    assert occluder.getchannel("A").getextrema() == (255, 255)

    # The background already contains the entire bar. Only the area overlapping
    # the actor needs a second draw; this retains the exact authored occlusion.
    reduced = enlarged.copy()
    reduced.alpha_composite(provided, (x, y))
    reduced.alpha_composite(occluder, bounds[:2])
    preview = Image.open(source / "xiaoman-tavern-preview.png").convert("RGBA")
    assert reduced.tobytes() == preview.tobytes(), "Compact layers must reproduce the supplied preview exactly"
    SCENE.mkdir(parents=True, exist_ok=True)
    for original, name in ((source / "sources/tavern-background-original.png", "background.png"),
                           (source / "xiaoman-transparent-original.png", "character-reference.png"),
                           (source / "scene-layout.json", "provided-layout.json")):
        shutil.copyfile(original, SCENE / name)
        assert digest(original) == digest(SCENE / name)
    occluder.save(SCENE / "bar-occluder.png", optimize=True)
    recipe = {
        "schema": 1,
        "status": "verified_asset_candidate_not_runtime_integration",
        "source_package": "xiaoman-tavern-complete-package-v2/xiaoman-scene-v2",
        "reference_canvas": [width, height],
        "layer_order": ["background", "animated_character", "bar_occluder"],
        "background": {"file": "background.png", "texture_size": [1672, 941],
                       "reference_scale": 3, "resampling": "nearest", "art_repainted": False},
        "character": {"reference_file": "character-reference.png", "position": [x, y],
                      "size": [384, 688], "atlas": "Portraits/xiaoman_atlas_hd.png",
                      "atlas_grid": [6, 3], "moods": 6, "mouth_states": 3,
                      "use_existing_approved_frames": True},
        "bar_occluder": {"file": "bar-occluder.png", "position": list(bounds[:2]),
                         "size": [384, 48], "covers_current_actor_footprint_only": True},
        "viewport": {"fit": "contain", "preserve_aspect": True,
                     "shared_transform_for_all_layers": True},
        "integration": {"opening_game_day": 1, "planned_release_opening_game_day": 3, "independent_night_actor_required": True,
                        "reuse_voice_envelope": True, "modify_native_customer_queue": False},
        "supplied_preview_sha256": digest(source / "xiaoman-tavern-preview.png"),
        "compact_layers_reproduce_supplied_preview_exactly": True,
        "files": {p.name: digest(p) for p in sorted(SCENE.iterdir())
                  if p.is_file() and p.name in ("background.png", "character-reference.png",
                                               "provided-layout.json", "bar-occluder.png")}
    }
    save(SCENE / "scene.json", recipe)


def validate(recipe):
    for name, sha in recipe["files"].items():
        assert digest(SCENE / name) == sha, name
    frames = current_frames()
    provided = np.asarray(Image.open(SCENE / "character-reference.png").convert("RGBA"))
    current = np.asarray(frames[0])
    assert np.array_equal(provided[:, :, 3], current[:, :, 3]), "Actor geometry/alpha changed"
    visible = (provided[:, :, 3] > 0) | (current[:, :, 3] > 0)
    delta = np.abs(provided[:, :, :3].astype(np.int16) - current[:, :, :3].astype(np.int16))
    assert int(delta[visible].max()) <= 1, "Reference differs from approved mod artwork"
    provided_layout = load(SCENE / "provided-layout.json")
    assert provided_layout["character"]["x"] == recipe["character"]["position"][0]
    assert provided_layout["character"]["y"] == recipe["character"]["position"][1]
    assert recipe["layer_order"] == ["background", "animated_character", "bar_occluder"]
    # All existing expression/mouth variants share the same full-frame geometry.
    assert all(frame.size == (384, 688) for frame in frames)
    assert len({hashlib.sha256(frame.tobytes()).digest() for frame in frames}) == 18
    manifest = SCENE / "runtime-manifest.json"
    if manifest.exists():
        for file in load(manifest)["files"]:
            assert digest(PACKAGE / file["packaged"]) == file["sha256"] == digest(SCENE / file["source"])
    before = 3 * 5016 * 2823 * 4
    scene_memory = (1672 * 941 + 384 * 48) * 4
    return {
        "source_layers_reconstruct_exactly": recipe["compact_layers_reproduce_supplied_preview_exactly"],
        "existing_frame_count": len(frames), "same_actor_alpha": True,
        "reference_vs_current_max_rgb_delta": int(delta[visible].max()),
        "reference_vs_current_changed_pixels": int((np.any(provided != current, axis=2) & visible).sum()),
        "original_three_full_canvas_rgba_bytes": before,
        "prepared_background_and_occluder_rgba_bytes": scene_memory,
        "existing_portrait_atlas_shared_not_in_scene_byte_count": True,
        "runtime_package_written_by_this_tool": False,
        "runtime_integration_tested": False,
        "preview_is_game_capture": False,
    }


def speech_timeline():
    document = load(PACKAGE / "Voices/index.json")
    line = next(row for row in document["lines"] if row["line_key"] == "intro_1" and row["language"] == "ja")
    path = PACKAGE / "Sounds" / (line["sound_key"] + ".wav")
    with wave.open(str(path), "rb") as audio:
        assert (audio.getnchannels(), audio.getsampwidth()) == (1, 2)
        rate = audio.getframerate()
        samples = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(np.float64) / 32768
    hop = max(1, round(rate * .04))
    levels = np.array([np.sqrt(np.mean(samples[i:i + hop] ** 2)) for i in range(0, len(samples), hop)])
    peak = float(levels.max())
    quiet, wide = max(.0035, peak * .08), peak * .32
    duration = len(samples) / rate

    def mouth_at(seconds):
        if seconds < 0 or seconds >= duration:
            return 0
        index = min(len(levels) - 1, int(seconds / (hop / rate)))
        level = (levels[max(0, index - 1)] + 2 * levels[index] + levels[min(len(levels) - 1, index + 1)]) * .25
        if level <= quiet:
            return 0
        cadence = (0, 1, 2, 1)[min(3, int((seconds % .48) / .12))]
        return min(1, cadence) if level < wide else cadence

    return duration, mouth_at


def render_preview(recipe, report):
    OUTPUT.mkdir(parents=True, exist_ok=True)
    background = Image.open(SCENE / "background.png").convert("RGBA").resize((5016, 2823), Image.Resampling.NEAREST)
    frames = current_frames()
    occluder = Image.open(SCENE / "bar-occluder.png").convert("RGBA")
    portrait_xy = tuple(recipe["character"]["position"])
    bar_xy = tuple(recipe["bar_occluder"]["position"])
    full = background.copy()
    full.alpha_composite(frames[0], portrait_xy)
    full.alpha_composite(occluder, bar_xy)
    full.save(OUTPUT / "小满酒馆_现有立绘静态合成.png", optimize=True)
    # A close view makes the eye and mouth changes visible in chat; no camera
    # crop is baked into the room assets or proposed in-game scene composition.
    bounds = (2500, 940, 3140, 1756)
    crop = background.crop(bounds)
    cache = []
    for frame in frames:
        image = crop.copy()
        image.alpha_composite(frame, (portrait_xy[0] - bounds[0], portrait_xy[1] - bounds[1]))
        image.alpha_composite(occluder, (bar_xy[0] - bounds[0], bar_xy[1] - bounds[1]))
        cache.append(image.convert("RGB"))
    duration, mouth_at = speech_timeline()
    timeline = []
    frame_indices = []
    for i in range(round((duration + 2.4) / .04)):
        t = i * .04
        mouth = mouth_at(t - 1.2)
        mood = 0
        for start in (.60, 3.6, duration + 1.5):
            phase = t - start
            if 0 <= phase < .24:
                mood = 1 if phase < .06 or phase > .18 else 2
        index = mood * 3 + mouth
        timeline.append(cache[index])
        frame_indices.append(index)
    assert any(i % 3 == 1 for i in frame_indices) and any(i % 3 == 2 for i in frame_indices)
    assert any(i // 3 == 2 for i in frame_indices) and frame_indices[-1] == 0
    timeline[0].save(OUTPUT / "小满酒馆_动态立绘验证.webp", save_all=True,
                     append_images=timeline[1:], duration=40, loop=0,
                     lossless=True, quality=100, method=4)
    report.update(preview_seconds=round(len(timeline) * .04, 2),
                  preview_voice_key="intro_1", preview_voice_regenerated=False,
                  preview_audio_envelope="same 40ms RMS thresholds/cadence as PortraitTiming.SpeechEnvelope",
                  preview_blink=True, preview_mouth_states=[0, 1, 2], preview_end_mouth=0)
    save(OUTPUT / "素材接入核对.json", report)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--import-from", type=Path)
    parser.add_argument("--preview", action="store_true")
    parser.add_argument("--pack", action="store_true")
    args = parser.parse_args()
    if args.import_from:
        import_layers(args.import_from)
    recipe = load(SCENE / "scene.json")
    if args.pack:
        files = []
        target = PACKAGE / "Scenes/Tavern"
        target.mkdir(parents=True, exist_ok=True)
        for name in ("background.png", "bar-occluder.png"):
            shutil.copyfile(SCENE / name, target / name)
            files.append(dict(source=name, packaged=(target / name).relative_to(PACKAGE).as_posix(),
                              sha256=digest(target / name), bytes=(target / name).stat().st_size))
        save(SCENE / "runtime-manifest.json", dict(schema=1, files=files, opening_game_day=1, planned_release_opening_game_day=3,
             texture_memory_bytes=(1672*941 + 384*48)*4, existing_portraits_reused=True))
    report = validate(recipe)
    if args.preview:
        render_preview(recipe, report)
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
