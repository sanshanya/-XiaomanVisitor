"""Preserve the approved performance and make a pitch-preserving 1.1x copy."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import wave
import voice_pipeline as pipeline


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    root = pipeline.ROOT
    source = root / "assets/audio/voice/xiaoman_intro_1_ja_ayano.wav"
    target = source.with_name("xiaoman_intro_1_ja_ayano_1p1.wav")
    source_digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if not target.exists():
        subprocess.run([r"C:\mpv\ffmpeg.exe", "-hide_banner", "-loglevel", "error", "-nostdin", "-i", str(source),
                        "-af", "atempo=1.1", "-ar", "24000", "-ac", "1", "-c:a", "pcm_s16le", str(target)], check=True)
    with wave.open(str(target), "rb") as audio:
        seconds = audio.getnframes() / audio.getframerate()
        info = dict(seconds=round(seconds, 3), sample_rate=audio.getframerate(), channels=audio.getnchannels(),
                    bit_depth=audio.getsampwidth() * 8, bytes=target.stat().st_size)
    script = pipeline.load(pipeline.DEFAULT_SCRIPT)
    row = next(r for r in script["lines"] if r["key"] == "intro_1")
    meta = pipeline.load(source.with_suffix(".json"))
    meta["processing"] = dict(method="ffmpeg atempo", factor=1.1, effective_speed=1.1,
                              source_audio=source.relative_to(root).as_posix(), source_sha256=source_digest)
    meta["audio"] = info
    meta["sha256"] = hashlib.sha256(target.read_bytes()).hexdigest()
    row.update(audio=target.relative_to(root).as_posix(), speed=1.1, text_normalization=False)
    script["text_normalization"] = True
    meta["binding"] = pipeline.binding_for(row, script)
    pipeline.save(target.with_suffix(".json"), meta)
    pipeline.save(pipeline.DEFAULT_SCRIPT, script)
    assert pipeline.audio_status(row, script, pipeline.localized()) == "ready"
    assert hashlib.sha256(source.read_bytes()).hexdigest() == source_digest
    print(json.dumps(dict(key="intro_1", effective_speed=1.1, original_preserved=True, api_requests=0, **info)))


if __name__ == "__main__":
    main()
