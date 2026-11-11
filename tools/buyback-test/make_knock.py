"""Prepare the CC0 recorded three-knock sequence, entirely offline."""
import argparse
import hashlib
import io
import json
import shutil
import subprocess
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "assets/permanent-buyback"
RATE = 48000


def pcm(samples):
    out = io.BytesIO()
    with wave.open(out, "wb") as wav:
        wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(RATE)
        wav.writeframes(np.round(samples * 32767).astype("<i2").tobytes())
    return out.getvalue()


def make_knock(ffmpeg):
    meta = json.loads((ASSETS / "knock-source.json").read_text(encoding="utf-8"))
    source = ASSETS / meta["file"]
    if hashlib.sha256(source.read_bytes()).hexdigest() != meta["source_sha256"]:
        raise ValueError("Recorded door-knock source differs from the reviewed file")
    decoded = subprocess.run([ffmpeg, "-v", "error", "-i", str(source), "-ac", "1", "-ar", str(RATE),
                              "-f", "f32le", "pipe:1"], check=True, capture_output=True)
    recording = np.frombuffer(decoded.stdout, dtype="<f4").astype(np.float64)
    start, end = (round(s * RATE) for s in meta["trim_seconds"])
    samples = recording[start:end].copy()
    if len(samples) != end - start or meta["knock_count"] != 3:
        raise ValueError("Incomplete three-knock recording")
    samples -= samples.mean()
    # Preserve the real impacts and spacing; only remove edit clicks and set headroom.
    fade_in, fade_out = round(RATE * .003), round(RATE * .025)
    samples[:fade_in] *= np.linspace(0, 1, fade_in)
    samples[-fade_out:] *= np.linspace(1, 0, fade_out)
    samples *= .86 / np.max(np.abs(samples))
    return pcm(samples), samples


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", default=shutil.which("ffmpeg"))
    args = parser.parse_args()
    if not args.ffmpeg:
        parser.error("ffmpeg is required for offline recording preparation")
    data, samples = make_knock(args.ffmpeg)
    output = ASSETS / "buyback_door_knock.wav"
    output.write_bytes(data)
    preview = np.zeros(round(RATE * 2.1))
    preview[:len(samples)] = samples * .675
    target = ROOT / ".dev/buyback/door-knocks-recorded-preview.wav"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(pcm(preview))
    print(f"Recorded three-knock sequence: {output} ({len(data)} bytes, {len(samples) / RATE:.2f}s)")
    print(f"Peak={np.max(np.abs(samples)):.3f}; RMS={np.sqrt(np.mean(samples ** 2)):.4f}")
