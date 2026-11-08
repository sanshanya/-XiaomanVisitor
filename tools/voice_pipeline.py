"""Offline export/adopt/pack by default; only the explicit generate command calls TTS."""
import argparse
import csv
import hashlib
import json
import re
import shutil
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "package/XiaomanVisitor"
DEFAULT_SCRIPT = ROOT / "story/配音脚本_ja.json"
IGNORED_PREFIXES = ("cfg_", "ui_", "gift_", "collection_", "cocktail_", "drink_", "story_note_")


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def save(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temp.replace(path)


def effective_speed(metadata):
    return metadata.get("processing", {}).get("effective_speed", metadata.get("request", {}).get("speed", 1.0))


def localized():
    result = {}
    for lang in ("zh", "en", "ja"):
        with (PACKAGE / f"Localization/{lang}.csv").open(encoding="utf-8-sig", newline="") as f:
            result[lang] = {k: v.replace("\\n", "\n") for k, v in csv.reader(f)}
    return result


def templates_for(key, texts):
    return {lang: rows[key] for lang, rows in texts.items() if key in rows}


def export_script(path, refresh=False):
    path = Path(path)
    if path.exists() and not refresh:
        raise ValueError("Script exists. Use --refresh to merge changes and flag changed lines for review.")
    previous_document = load(path) if path.exists() else {}
    old = {row["key"]: row for row in previous_document.get("lines", [])}
    texts = localized()
    rows = []
    spec_path = ROOT / "assets/story.json"
    selected_keys = set(load(spec_path)["voice_keys"]) if spec_path.is_file() else None
    for key in texts["zh"]:
        if key == "name" or key.startswith(IGNORED_PREFIXES):
            continue
        if selected_keys is not None and key not in selected_keys:
            continue
        templates = templates_for(key, texts)
        previous = old.get(key)
        if previous and previous.get("subtitle_templates") == templates:
            rows.append(previous)
            continue
        jp = templates.get("ja", "")
        dynamic = "{" in jp or "}" in jp
        row = {"key": key, "subtitle_templates": templates,
               "spoken_text": "" if dynamic else jp,
               "generic": False, "reviewed": False, "audio": "",
               "note": "含动态参数：请编写不含数字或占位符的自然泛指配音，并设置generic=true。" if dynamic else "剧情与日文定稿后将reviewed改为true。"}
        if previous:
            row["previous_audio"] = previous.get("audio", "")
            row["note"] = "字幕已修改，请重新审核配音；旧音频保留在素材目录，不自动绑定。"
        rows.append(row)
    result = {"schema": 1, "language": previous_document.get("language", "ja"),
              "voice_id": previous_document.get("voice_id", "ayano"),
              "model_id": previous_document.get("model_id", "luna-tts-1.5"),
              "speed": previous_document.get("speed", 1.0 if previous_document else 1.1),
              "text_normalization": previous_document.get("text_normalization", True), "lines": rows}
    save(path, result)
    return result


def validate_line(row, script, texts=None):
    if script.get("model_id") != "luna-tts-1.5" or script.get("language") not in ("ja", "zh", "en"):
        return "unsupported_config"
    if not row.get("reviewed"):
        return "needs_review"
    if texts is not None and row.get("subtitle_templates") != templates_for(row["key"], texts):
        return "outdated_script"
    spoken = row.get("spoken_text", "")
    if not spoken.strip() or "{" in spoken or "}" in spoken:
        return "needs_spoken_text"
    generate_text = row.get("generate_text", spoken)
    if not generate_text.strip() or "{" in generate_text or "}" in generate_text or re.match(r"\s*\[", generate_text):
        return "invalid_generate_text"
    dynamic = any("{" in text or "}" in text for text in row["subtitle_templates"].values())
    if dynamic and not row.get("generic"):
        return "needs_generic_spoken_text"
    return "ready"


def audio_status(row, script, texts=None):
    state = validate_line(row, script, texts)
    if state != "ready":
        return state
    if not row.get("audio"):
        return "missing_audio"
    path = (ROOT / row["audio"]).resolve()
    if not path.is_relative_to(ROOT.resolve()) or not path.is_file() or not path.with_suffix(".json").is_file():
        return "missing_audio"
    meta = load(path.with_suffix(".json"))
    request = meta.get("request", {})
    if request.get("generate_text") != row.get("generate_text", row["spoken_text"]) or request.get("voice_id") != script["voice_id"]:
        return "stale_recording"
    if effective_speed(meta) != row.get("speed", script.get("speed", 1.0)):
        return "stale_recording"
    if request.get("text_normalization", True) != row.get("text_normalization", script.get("text_normalization", True)):
        return "stale_recording"
    if request.get("language") != script["language"] or request.get("model_id") != script["model_id"]:
        return "stale_recording"
    binding = meta.get("binding", {})
    if binding != binding_for(row, script):
        return "stale_binding"
    if hashlib.sha256(path.read_bytes()).hexdigest() != meta.get("sha256"):
        return "audio_hash_mismatch"
    with wave.open(str(path), "rb") as audio:
        frames = audio.getnframes()
        if frames <= 0 or len(audio.readframes(frames)) != frames * audio.getnchannels() * audio.getsampwidth():
            return "invalid_audio"
    return "ready"


def binding_for(row, script):
    return {"key": row["key"], "language": script["language"],
            "subtitle_templates": row["subtitle_templates"], "generic": bool(row.get("generic"))}


def reusable_audio(path, row, script):
    path = Path(path)
    if not path.is_file() or not path.with_suffix(".json").is_file():
        return False
    meta = load(path.with_suffix(".json"))
    request = meta.get("request", {})
    return (request.get("generate_text") == row.get("generate_text", row["spoken_text"]) and request.get("voice_id") == script["voice_id"]
            and effective_speed(meta) == row.get("speed", script.get("speed", 1.0))
            and request.get("text_normalization", True) == row.get("text_normalization", script.get("text_normalization", True))
            and request.get("model_id") == script["model_id"] and request.get("language") == script["language"]
            and hashlib.sha256(path.read_bytes()).hexdigest() == meta.get("sha256"))


def adopt(path, key, audio, approve=False):
    script = load(path)
    row = next(r for r in script["lines"] if r["key"] == key)
    audio = Path(audio).resolve()
    if not audio.is_relative_to(ROOT.resolve()):
        raise ValueError("Place approved recordings inside the project assets directory first.")
    metadata = load(audio.with_suffix(".json"))
    request = metadata.get("request", {})
    if request.get("generate_text") != row.get("generate_text", row["spoken_text"]) or request.get("voice_id") != script["voice_id"]:
        raise ValueError("Approved audio does not match this script text and voice.")
    if not approve and not row.get("reviewed"):
        raise ValueError("Only adopt reviewed audio; --approve records explicit approval.")
    row["reviewed"] = True
    state = validate_line(row, script, localized())
    if state != "ready":
        raise ValueError(state)
    row["audio"] = audio.relative_to(ROOT).as_posix()
    metadata["binding"] = binding_for(row, script)
    save(audio.with_suffix(".json"), metadata)
    state = audio_status(row, script, localized())
    if state != "ready":
        raise ValueError(state)
    save(path, script)


def generate(path, keys):
    script, texts = load(path), localized()
    selected = set(keys or [])
    known = {row["key"] for row in script["lines"]}
    if selected - known:
        raise ValueError("Unknown line keys: " + ", ".join(sorted(selected - known)))
    for row in script["lines"]:
        if selected and row["key"] not in selected:
            continue
        state = validate_line(row, script, texts)
        if state != "ready":
            print(f"SKIP {row['key']}: {state}")
            continue
        if audio_status(row, script, texts) == "ready":
            print(f"CACHED {row['key']}: no API request")
            continue
        generate_text = row.get("generate_text", row["spoken_text"])
        speed = row.get("speed", script.get("speed", 1.0))
        normalize = row.get("text_normalization", script.get("text_normalization", True))
        request_key = hashlib.sha256(json.dumps({"text": generate_text, "voice": script["voice_id"], "speed": speed, "normalization": normalize,
            "language": script["language"], "model": script["model_id"]}, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest()[:16]
        safe_key = re.sub("[^a-z0-9_]", "_", row["key"].lower())
        audio = ROOT / "assets/audio/voice" / script["language"] / f"{safe_key}.{request_key}.wav"
        if not audio.exists():
            previous = [ROOT / r for r in (row.get("audio"), row.get("previous_audio")) if r]
            previous += [ROOT / other["audio"] for other in script["lines"] if other.get("audio") and other["key"] != row["key"]]
            cached = next((p for p in previous if p.resolve().is_relative_to(ROOT.resolve()) and reusable_audio(p, row, script)), None)
            if cached:
                audio.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(cached, audio)
                shutil.copyfile(cached.with_suffix(".json"), audio.with_suffix(".json"))
                print(f"CACHED {row['key']}: identical reviewed speech, refreshed subtitle binding")
            else:
                command = [sys.executable, str(ROOT / "tools/generate_vuilabs_voice.py"), "--text", generate_text,
                           "--voice", script["voice_id"], "--language", script["language"], "--speed", str(speed), "--output", str(audio)]
                if not normalize:
                    command.append("--no-text-normalization")
                subprocess.run(command, check=True)
        # Exact-text cached audio can be rebound only after the new script was reviewed.
        metadata = load(audio.with_suffix(".json"))
        metadata["binding"] = binding_for(row, script)
        save(audio.with_suffix(".json"), metadata)
        row["audio"] = audio.relative_to(ROOT).as_posix()
        save(path, script)  # Each completed line survives a later network failure.
        if audio_status(row, script, texts) != "ready":
            raise ValueError("Generated recording did not pass validation: " + row["key"])


def pack(path):
    script, texts = load(path), localized()
    sounds = PACKAGE / "Sounds"
    previous_path = PACKAGE / "Voices/index.json"
    previous = load(previous_path) if previous_path.exists() else {}
    records = [r for r in previous.get("lines", []) if r["language"] != script["language"]]
    pending = dict(previous.get("pending_by_language", {}))
    report, kept = [], {r["sound_key"] + ".wav" for r in records}
    ready = 0
    for row in script["lines"]:
        state = audio_status(row, script, texts)
        report.append({"key": row["key"], "status": state})
        if state != "ready":
            continue
        audio = ROOT / row["audio"]
        digest = hashlib.sha256(audio.read_bytes()).hexdigest()
        sound = "xm_voice_" + script["language"] + "_" + re.sub("[^a-z0-9_]", "_", row["key"].lower()) + "_" + digest[:12]
        target = sounds / (sound + ".wav")
        if not target.exists() or hashlib.sha256(target.read_bytes()).hexdigest() != digest:
            shutil.copyfile(audio, target)
        kept.add(target.name)
        records.append({"line_key": row["key"], "language": script["language"], "sound_key": sound,
                        "spoken_text": row["spoken_text"], "subtitle_templates": list(row["subtitle_templates"].values()),
                        "generic": bool(row.get("generic"))})
        ready += 1
    for old in sounds.glob("xm_voice_*.wav"):
        if old.name not in kept and old.resolve().parent == sounds.resolve():
            old.unlink()  # Only this pipeline's prefixed WAVs; never music or author originals.
    pending[script["language"]] = len(report) - ready
    result = {"schema": 1, "lines": records, "pending_lines": sum(pending.values()), "pending_by_language": pending}
    save(PACKAGE / "Voices/index.json", result)
    save(ROOT / f"story/配音制作状态_{script['language']}.json", {"ready": ready, "pending": pending[script["language"]], "lines": report})
    return result


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["export", "adopt", "generate", "pack", "status"])
    parser.add_argument("--script", type=Path, default=DEFAULT_SCRIPT)
    parser.add_argument("--refresh", action="store_true")
    parser.add_argument("--keys", nargs="*")
    parser.add_argument("--key")
    parser.add_argument("--audio", type=Path)
    parser.add_argument("--approve", action="store_true")
    args = parser.parse_args()
    try:
        if args.command == "export":
            data = export_script(args.script, args.refresh)
            print(f"Exported {len(data['lines'])} lines to {args.script}; no API requests.")
        elif args.command == "adopt":
            if not args.key or not args.audio:
                raise ValueError("adopt needs --key and --audio")
            adopt(args.script, args.key, args.audio, args.approve)
            print(f"Adopted approved {args.key}; no API requests.")
        elif args.command == "generate":
            generate(args.script, args.keys)
        elif args.command == "pack":
            data = pack(args.script)
            print(f"Packaged {len(data['lines'])} recording(s); {data['pending_lines']} pending; no API requests.")
        else:
            script, texts = load(args.script), localized()
            print(json.dumps([{ "key": r["key"], "status": audio_status(r, script, texts)} for r in script["lines"]], ensure_ascii=False, indent=2))
    except (ValueError, KeyError, StopIteration, OSError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error)) from None


if __name__ == "__main__":
    main()
