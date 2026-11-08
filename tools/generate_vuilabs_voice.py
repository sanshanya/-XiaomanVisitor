"""Generate one WAV with a key supplied only through the process environment."""
import argparse
import hashlib
import io
import json
import os
import sys
import urllib.error
import urllib.request
import wave
from pathlib import Path


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--text", required=True)
    parser.add_argument("--voice", default="ayano")
    parser.add_argument("--language", default="ja")
    parser.add_argument("--speed", type=float, default=1.1)
    parser.add_argument("--text-normalization", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    key = os.environ.get("VUILABS_API_KEY", "").strip()
    if not key:
        raise SystemExit("VUILABS_API_KEY is missing from the process environment.")
    if args.output.exists():
        raise SystemExit("Output already exists; choose a new name to avoid generating twice.")
    payload = {
        "voice_id": args.voice,
        "generate_text": args.text,
        "language": args.language,
        "model_id": "luna-tts-1.5",
        "text_normalization": args.text_normalization,
        "speed": args.speed,
        "volume": 0,
        "audio_format": "wav",
    }
    req = urllib.request.Request(
        "https://api.vuilabs.cn/v1/text-to-speech",
        data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
        headers={"X-API-Key": key, "Content-Type": "application/json", "Accept": "audio/wav"},
        method="POST",
    )
    print(f"Requesting {args.voice} ({args.language}, luna-tts-1.5)...", flush=True)
    try:
        with urllib.request.urlopen(req, timeout=90) as response:
            content_type = response.headers.get("Content-Type", "").split(";")[0].strip().lower()
            data = response.read()
            headers = {name: response.headers.get(name) for name in (
                "X-Luna-Resolved-Model", "X-Luna-Billable-Characters",
                "X-Audio-Sample-Rate", "X-Audio-Channels", "X-Audio-Bit-Depth")}
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace").replace(key, "[REDACTED]")
        raise SystemExit(f"TTS HTTP {error.code}: {detail[:600]}") from None
    except urllib.error.URLError as error:
        raise SystemExit("TTS connection failed: " + str(error.reason).replace(key, "[REDACTED]")) from None
    if content_type == "application/json":
        result = json.loads(data)
        message = str(result.get("error_message", "Unknown API error")).replace(key, "[REDACTED]")
        raise SystemExit(f"TTS business error {result.get('error_code')}: {message}")
    if content_type != "audio/wav" or not data:
        raise SystemExit(f"Unexpected or empty audio response: {content_type}")
    with wave.open(io.BytesIO(data), "rb") as audio:
        frames, rate, channels, width = audio.getnframes(), audio.getframerate(), audio.getnchannels(), audio.getsampwidth()
        samples = audio.readframes(frames)
        if frames < 1 or len(samples) != frames * channels * width:
            raise SystemExit("Audio response contains incomplete WAV data.")
    if (rate, channels, width) != (24000, 1, 2):
        raise SystemExit(f"Unexpected WAV format: {rate}Hz, {channels}ch, {width * 8}bit")
    if headers["X-Luna-Resolved-Model"] not in (None, "luna-tts-1.5"):
        raise SystemExit("The endpoint returned an unexpected model.")
    if not any(samples):
        raise SystemExit("Audio response contains only silence.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(data)
    metadata = {"provider": "VUI Labs", "request": payload, "response_headers": headers,
                "audio": {"seconds": round(frames / rate, 3), "sample_rate": rate,
                          "channels": channels, "bit_depth": width * 8, "bytes": len(data)},
                "sha256": hashlib.sha256(data).hexdigest()}
    args.output.with_suffix(".json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output), **metadata["audio"], "model": headers["X-Luna-Resolved-Model"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
