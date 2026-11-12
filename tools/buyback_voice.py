"""Main-mod ending voices. Only the explicit generate command requests paid TTS."""
import argparse
import csv
import hashlib
import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

import voice_pipeline as vp
from voice_delivery import VoiceDelivery, digest

ROOT = vp.ROOT
SOURCE = ROOT / "story/支线/永久回购_日语配音.json"
MODULE = ROOT / "side-stories/permanent-buyback"
CATALOG = MODULE / "voice-script.json"
ASSETS = ROOT / "assets/permanent-buyback/voice"
OUTPUT = ROOT / "outputs/buyback-voice"
ACT_NAMES = {1: "门外的轻语", 2: "水的味道", 3: "把人生还给我", 4: "重新温柔", 5: "永久回购"}


def texts_for(script):
    return {lang: {r["key"]: r["subtitle_templates"][lang] for r in script["lines"]}
            for lang in ("zh", "ja")}


def export():
    source = vp.load(SOURCE)
    cues = [c for c in vp.load(MODULE / "script.json")["cues"] if c["kind"] == "line"]
    if {c["key"] for c in cues} != set(source["main"]):
        raise ValueError("Japanese lines must match all authored main dialogue keys")
    previous = {r["key"]: r for r in vp.load(CATALOG)["lines"]} if CATALOG.exists() else {}
    rows = []

    def add(key, zh, ja, group, act=0):
        if not zh or not ja:
            raise ValueError("Empty subtitle: " + key)
        row = dict(key=key, subtitle_templates=dict(zh=zh, ja=ja), spoken_text=ja,
                   generate_text=source.get("generate_overrides", {}).get(key, ja),
                   reviewed=True, generic=False, audio="", group=group, act=act,
                   delivery=source["acts"][str(act)] if act else "少女自然口语；前置逐渐不安，门外拒绝分支带恳求。")
        old = previous.get(key, {})
        if all(old.get(k) == row[k] for k in ("subtitle_templates", "generate_text", "spoken_text")):
            row["audio"] = old.get("audio", "")
        elif old.get("audio"):
            row["previous_audio"] = old["audio"]
        if key in source["imported_attack"]["keys"]:
            row["provider"] = "author_recording"
            row["speed"] = 1.0
            parts = vp.load(ROOT / "assets/permanent-buyback/direction.json")["attack"]["segments"]
            part = next(part for part in parts if part["key"] == key)
            row["attack_ordinal"] = parts.index(part) + 1
            row["segment_start"], row["segment_end"] = part["start"], part["end"]
            row["blood_at"] = part["blood_at"]
        rows.append(row)

    for c in cues:
        add(c["key"], c["text"], source["main"][c["key"]], "main", c["act"])
    for group in ("prelude", "refusal"):
        zh = vp.load(ROOT / "story/支线" / ("永久回购_前置.json" if group == "prelude" else "永久回购_不开门分支.json"))
        for field, ja in source[group].items():
            left, right = zh[field], ja
            if isinstance(right, list):
                if not isinstance(left, list) or len(left) != len(right):
                    raise ValueError("Mismatched branch length: " + field)
                for i, (cn, jp) in enumerate(zip(left, right)):
                    add(f"buyback_{group}_{field}_{i}", cn, jp, group)
            else:
                add(f"buyback_{group}_{field}", left, right, group)
    script = {k: source[k] for k in ("schema", "language", "voice_id", "model_id", "speed", "text_normalization")}
    script["lines"] = rows
    for row in rows:
        state = vp.validate_line(row, script)
        if state != "ready":
            raise ValueError(row["key"] + ": " + state)
        text = row["generate_text"]
        if sum(2 if '\u3000' <= c <= '\u9fff' else 1 for c in text) > 500:
            raise ValueError("TTS input too long: " + row["key"])
    vp.save(CATALOG, script)
    return script


def import_attack(path=None):
    source = vp.load(SOURCE)["imported_attack"]
    original = ASSETS / "reference/attack.mp3"
    path = Path(path) if path else original
    if not path.is_file():
        raise ValueError("Provide --attack with the author's original MP3")
    original.parent.mkdir(parents=True, exist_ok=True)
    if path.resolve() != original.resolve():
        if original.exists() and digest(original) != digest(path):
            raise ValueError("A different author recording already exists; preserve it first")
        shutil.copyfile(path, original)
    script = vp.load(CATALOG)
    master = ROOT / ".dev/cache" / ("buyback-attack-" + digest(original)[:16] + ".wav")
    master.parent.mkdir(parents=True, exist_ok=True)
    if not master.exists():
        subprocess.run([shutil.which("ffmpeg"), "-hide_banner", "-loglevel", "error", "-nostdin", "-n",
                        "-i", str(original), "-map_metadata", "-1", "-ar", "24000", "-ac", "1",
                        "-c:a", "pcm_s16le", str(master)], check=True)
    pcm, rate = sf.read(master, dtype="int16")
    parts = []
    for row in script["lines"]:
        if row["key"] not in source["keys"]:
            continue
        start, end = round(row["segment_start"] * rate), round(row["segment_end"] * rate)
        part = pcm[start:end]
        parts.append(part)
        recording = ASSETS / "ja" / (row["key"] + ".slice-" + digest(original)[:16] + f"-{start}-{end}.wav")
        recording.parent.mkdir(parents=True, exist_ok=True)
        if not recording.exists(): sf.write(recording, part, rate, subtype="PCM_16")
        actual, actual_rate = sf.read(recording, dtype="int16")
        if actual_rate != rate or not np.array_equal(part, actual):
            raise ValueError("Imported voice slice differs from the original recording")
        info = sf.info(recording)
        vp.save(recording.with_suffix(".json"), dict(
            provider="author_recording", source=original.relative_to(ROOT).as_posix(),
            source_sha256=digest(original), source_name=source["source_name"],
            processing=dict(resample_rate=rate, effective_speed=1.0, original_unchanged=True,
                            slice_start_frame=start, slice_end_frame=end, original_frames=len(pcm)),
            binding=vp.binding_for(row, script), sha256=digest(recording),
            audio=dict(seconds=info.duration, sample_rate=info.samplerate, channels=info.channels,
                       sample_width_bytes=2, frames=info.frames, bytes=recording.stat().st_size)))
        row["audio"] = recording.relative_to(ROOT).as_posix()
    if not np.array_equal(np.concatenate(parts), pcm):
        raise ValueError("Attack slices must reconstruct the full original PCM without gaps or overlaps")
    vp.save(CATALOG, script)


def status(row, script):
    if row.get("provider") != "author_recording":
        return vp.audio_status(row, script, texts_for(script))
    if not row.get("audio"):
        return "missing_audio"
    recording = (ROOT / row["audio"]).resolve()
    if not recording.is_relative_to(ASSETS.resolve()) or not recording.is_file():
        return "missing_import"
    meta = vp.load(recording.with_suffix(".json"))
    original = (ROOT / meta["source"]).resolve()
    if (not original.is_relative_to(ASSETS.resolve()) or not original.is_file()
            or digest(original) != meta["source_sha256"] or digest(recording) != meta["sha256"]
            or meta.get("provider") != "author_recording"
            or meta["processing"]["effective_speed"] != 1.0
            or meta["binding"] != vp.binding_for(row, script)):
        return "invalid_import"
    info = sf.info(recording)
    if (not 0 <= row["blood_at"] < info.duration or info.samplerate != 24000 or info.channels != 1
            or abs(info.duration - (row["segment_end"] - row["segment_start"])) > 1/24000):
        return "invalid_import_timing"
    return "ready"


def checked(script):
    missing = [r["key"] + ": " + status(r, script) for r in script["lines"] if status(r, script) != "ready"]
    if missing:
        raise ValueError("Unfinished recordings: " + ", ".join(missing))


def pack(stage):
    """Append the ending to the existing main-mod index; keep its other voices."""
    stage = Path(stage).resolve()
    if stage != (ROOT / "package/XiaomanVisitor").resolve():
        raise ValueError("Ending voices belong to the existing main package")
    script = vp.load(CATALOG)
    checked(script)
    index_path = stage / "Voices/index.json"
    index = vp.load(index_path)
    index["lines"] = [r for r in index["lines"] if not r["line_key"].startswith("buyback_")]
    encoder = VoiceDelivery(ROOT)
    sounds = stage / "Sounds"
    keep = set()
    clips = {}
    for row in script["lines"]:
        source = ROOT / row["audio"]
        sha = digest(source)
        name = clips.setdefault(sha, "buyback_voice_ja_" + row["key"] + "_" + sha[:12])
        encoded, packaged = encoder.encode(source)
        # Breath/laugh takes can lose more high-frequency detail than normal
        # speech. Keep those masters lossless instead of lowering the quality bar.
        before, rate = sf.read(source, dtype="float64")
        after, delivered_rate = sf.read(encoded, dtype="float64")
        if rate != delivered_rate or before.shape != after.shape:
            raise ValueError("Ending voice delivery changed the sample layout")
        signal = float(np.mean(before ** 2))
        noise = float(np.mean((before - after) ** 2))
        if noise > 0 and 10 * np.log10(signal / noise) < 24:
            encoded, packaged = source, sha
        target = sounds / (name + encoded.suffix)
        if not target.is_file() or digest(target) != packaged:
            shutil.copyfile(encoded, target)
        keep.add(target.name)
        index["lines"].append(dict(line_key=row["key"], language="ja", sound_key=name,
                                   audio_file=target.name, source_sha256=sha, packaged_sha256=packaged,
                                   spoken_text=row["spoken_text"], subtitle_templates=list(row["subtitle_templates"].values()),
                                   generic=False))
    for old in sounds.glob("buyback_voice_*.*"):
        if old.suffix in (".wav", ".ogg") and old.name not in keep:
            old.unlink()
    vp.save(index_path, index)


def timestamp(seconds):
    ms = round(seconds * 1000)
    return f"{ms//3600000:02}:{ms//60000%60:02}:{ms//1000%60:02},{ms%1000:03}"


def preview_pcm(row):
    pcm, sr = sf.read(ROOT / row["audio"], dtype="float32")
    if sr != 24000 or pcm.ndim != 1:
        raise ValueError("Audition masters must use the same sample layout")
    # Only attenuate hot takes in listening exports. Masters and pacing stay intact.
    rms = float(np.sqrt(np.mean(pcm.astype(np.float64) ** 2)))
    gain = min(1.0, .13 / rms) if rms > 0 else 1.0
    gain = min(gain, .97 / max(float(np.max(np.abs(pcm))), .0001))
    return pcm * gain, gain


def short_preview(rows, name):
    chunks, subtitles = [], []
    frames = 0
    for row in rows:
        pcm, _ = preview_pcm(row)
        begin, end = frames/24000, (frames+len(pcm))/24000
        subtitles.append(f"{len(subtitles)+1}\n{timestamp(begin)} --> {timestamp(end)}\n{row['subtitle_templates']['zh']}\n{row['spoken_text']}\n")
        pause = np.zeros(0 if row.get("attack_ordinal") in (1, 2) else 12000, dtype=np.float32)
        chunks.extend((pcm, pause))
        frames += len(pcm) + len(pause)
    write_preview(np.concatenate(chunks), OUTPUT / (name + ".mp3"), 24000)
    (OUTPUT / (name + ".srt")).write_text("\n".join(subtitles), encoding="utf-8-sig")


def audition():
    script = vp.load(CATALOG)
    checked(script)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    rate = 24000
    full = []
    full_length = 0
    positions = []
    for act in range(1, 6):
        chunks, subs = [], []
        local_length = 0
        for row in script["lines"]:
            if row["act"] != act:
                continue
            pcm, gain = preview_pcm(row)
            start, end = local_length / rate, (local_length + len(pcm)) / rate
            subs.append(f"{len(subs)+1}\n{timestamp(start)} --> {timestamp(end)}\n{row['subtitle_templates']['zh']}\n{row['spoken_text']}\n")
            positions.append(dict(key=row["key"], act=act, start=full_length/rate+start,
                                  end=full_length/rate+end, zh=row["subtitle_templates"]["zh"], ja=row["spoken_text"],
                                  source=row["audio"], audition_gain=round(gain, 5)))
            chunks.append(pcm)
            # A slightly longer beat around shots and the scream, short pauses
            # elsewhere. No narration or invented spoken stage directions.
            pause = 0 if row.get("attack_ordinal") in (1, 2) else .85 if row.get("provider") == "author_recording" else .38
            chunks.append(np.zeros(round(pause * rate), dtype=np.float32))
            local_length += len(pcm) + len(chunks[-1])
        pcm = np.concatenate(chunks)
        stem = f"第{act}幕_{ACT_NAMES[act]}"
        write_preview(pcm, OUTPUT / (stem + ".mp3"), rate)
        (OUTPUT / (stem + ".srt")).write_text("\n".join(subs), encoding="utf-8-sig")
        full.append(pcm)
        full.append(np.zeros(rate, dtype=np.float32))
        full_length += len(pcm) + rate
    write_preview(np.concatenate(full), OUTPUT / "永久回购_五幕完整试听.mp3", rate)
    subtitles = [f"{i}\n{timestamp(r['start'])} --> {timestamp(r['end'])}\n{r['zh']}\n{r['ja']}\n"
                 for i, r in enumerate(positions, 1)]
    (OUTPUT / "永久回购_五幕完整试听.srt").write_text("\n".join(subtitles), encoding="utf-8-sig")
    with (OUTPUT / "逐句试听索引.csv").open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["台词键", "幕", "完整试听起点", "完整试听终点", "中文字幕", "日语配音", "源录音", "试听增益"])
        for r in positions:
            w.writerow([r["key"], r["act"], timestamp(r["start"]), timestamp(r["end"]), r["zh"], r["ja"], r["source"], r["audition_gain"]])
    vp.save(OUTPUT / "audition-index.json", dict(schema=1, seconds=full_length/rate, lines=positions))
    selection = {"buyback_" + key for key in ("01_01", "01_02", "01_14", "02_17", "02_22", "02_23",
                 "03_09", "03_12", "03_13", "03_19", "03_29", "03_30", "03_31", "03_32", "04_01", "04_10", "04_11",
                 "04_12", "04_15", "05_10", "05_11", "05_12", "05_20", "05_21")}
    short_preview([r for r in script["lines"] if r["key"] in selection], "永久回购_情绪衔接试听")
    short_preview([r for r in script["lines"] if r["act"] == 0], "前置与不开门分支试听")
    with (OUTPUT / "配音总表.csv").open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["台词键", "组别", "幕", "中文字幕", "日语字幕", "实际录制稿", "演出指导", "秒数", "音频来源", "源文件"])
        for r in script["lines"]:
            w.writerow([r["key"], r["group"], r["act"], r["subtitle_templates"]["zh"], r["spoken_text"], r["generate_text"],
                        r["delivery"], sf.info(ROOT / r["audio"]).duration,
                        "作者原录音/原速" if r.get("provider") == "author_recording" else "Ayano/日语/1.1x", r["audio"]])
    print(f"Audition ready: {len(positions)} main lines, {full_length/rate:.1f} seconds; {OUTPUT}")


def write_preview(pcm, target, rate):
    temporary = ROOT / ".dev/cache/buyback-preview.wav"
    temporary.parent.mkdir(parents=True, exist_ok=True)
    sf.write(temporary, pcm, rate, subtype="PCM_16")
    subprocess.run([shutil.which("ffmpeg"), "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                    "-i", str(temporary), "-map_metadata", "-1", "-c:a", "libmp3lame", "-b:a", "192k",
                    str(target)], check=True)
    temporary.unlink()


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("command", choices=("export", "import", "generate", "status", "audition"))
    p.add_argument("--attack", type=Path)
    p.add_argument("--keys", nargs="*")
    args = p.parse_args()
    if args.command == "export":
        print(f"Exported {len(export()['lines'])} ending dialogue bindings; offline")
    elif args.command == "import":
        import_attack(args.attack)
    elif args.command == "generate":
        script = vp.load(CATALOG)
        keys = args.keys or [r["key"] for r in script["lines"] if r.get("provider") != "author_recording"]
        if any(r.get("provider") == "author_recording" and r["key"] in keys for r in script["lines"]):
            raise ValueError("Do not synthesize over the author's recording")
        vp.generate(CATALOG, keys, texts=texts_for(script), audio_directory=ASSETS / "ja")
    elif args.command == "audition":
        audition()
    else:
        script = vp.load(CATALOG)
        states = [dict(key=r["key"], status=status(r, script)) for r in script["lines"]]
        vp.save(ROOT / ".dev/reports/buyback-voice-status.json", states)
        print({s: sum(r["status"] == s for r in states) for s in sorted({r["status"] for r in states})})


if __name__ == "__main__":
    main()
