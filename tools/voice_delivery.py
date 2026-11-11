"""Offline delivery encoding. Reviewed WAV masters are never changed."""
import hashlib
import json
import shutil
import subprocess
from pathlib import Path

import soundfile as sf


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audio_filename(record):
    """Native ModLoader keys are filename stems; reject paths or ambiguous suffixes."""
    name = record.get("audio_file", record["sound_key"] + ".wav")
    if Path(name).name != name or Path(name).stem != record["sound_key"] or Path(name).suffix not in (".wav", ".ogg"):
        raise ValueError("Invalid delivery filename: " + name)
    return name


class VoiceDelivery:
    def __init__(self, root):
        self.root = root
        profile = root / "assets/voice-delivery.json"
        self.profile = json.loads(profile.read_text(encoding="utf-8")) if profile.is_file() else None
        self.ready = {}
        if self.profile is not None:
            p = self.profile
            if (p.get("schema") != 1 or p.get("codec") != "libvorbis" or not 0 <= p.get("quality", -1) <= 10
                    or p.get("sample_rate") != 24000 or p.get("channels") != 1):
                raise ValueError("Unsupported voice delivery profile")
            self.ffmpeg = shutil.which("ffmpeg")
            if not self.ffmpeg:
                raise ValueError("Install FFmpeg with libvorbis for offline voice delivery encoding")
            encoder = subprocess.run([self.ffmpeg, "-version"], check=True, capture_output=True, text=True).stdout.splitlines()[0]
            self.profile_hash = hashlib.sha256(json.dumps(dict(profile=p, encoder=encoder), sort_keys=True).encode()).hexdigest()

    def encode(self, source):
        source_hash = digest(source)
        if self.profile is None:
            return source, source_hash
        if source_hash in self.ready:
            return self.ready[source_hash]
        cache = self.root / ".dev/cache/voice-delivery" / self.profile_hash / (source_hash + ".ogg")
        receipt = cache.with_suffix(".json")
        try:
            valid = cache.is_file() and json.loads(receipt.read_text(encoding="utf-8"))["sha256"] == digest(cache)
        except (OSError, ValueError, KeyError):
            valid = False
        if not valid:
            cache.parent.mkdir(parents=True, exist_ok=True)
            temp = cache.with_suffix(".tmp.ogg")
            try:
                subprocess.run([self.ffmpeg, "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", str(source),
                                "-map_metadata", "-1", "-c:a", "libvorbis", "-q:a", str(self.profile["quality"]),
                                "-ar", "24000", "-ac", "1", "-fflags", "+bitexact", str(temp)],
                               check=True, capture_output=True)
                before, after = sf.info(source), sf.info(temp)
                if (before.frames, before.samplerate, before.channels) != (after.frames, after.samplerate, after.channels):
                    raise ValueError("Delivery encoding changed duration/sample layout: " + source.name)
                temp.replace(cache)
                receipt.write_text(json.dumps(dict(source_sha256=source_hash, profile_sha256=self.profile_hash,
                                                   sha256=digest(cache)), indent=2) + "\n", encoding="utf-8")
            finally:
                temp.unlink(missing_ok=True)
        self.ready[source_hash] = (cache, digest(cache))
        return self.ready[source_hash]
