"""Extract custom BGM from the user's installed Steam game, preserving Ogg bytes."""
from pathlib import Path
from collections import defaultdict
import hashlib
import json
import sys
import soundfile as sf
import numpy as np
from mutagen.oggvorbis import OggVorbis

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path(r"E:\app\steam\steamapps\common\A Night Filled With the Sound of Rain")
OUTPUT = ROOT / "assets/audio/original-bgm"

def main():
    sys.stdout.reconfigure(encoding="utf-8")
    system = json.loads((SOURCE / "data/System.json").read_text(encoding="utf-8-sig"))
    key = bytes.fromhex(system["encryptionKey"])
    uses = defaultdict(set)
    def walk(value, filename):
        if isinstance(value, dict):
            if value.get("code") == 241 and value.get("parameters"):
                uses[value["parameters"][0].get("name", "")].add(filename)
            for name, obj in value.items():
                if name == "titleBgm" and isinstance(obj, dict):
                    uses[obj.get("name", "")].add(filename + ":titleBgm")
                walk(obj, filename)
        elif isinstance(value, list):
            for obj in value:
                walk(obj, filename)
    for path in (SOURCE / "data").glob("*.json"):
        walk(json.loads(path.read_text(encoding="utf-8-sig")), path.name)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    rows = []
    for path in sorted((SOURCE / "audio/bgm").glob("*.ogg*")):
        name = path.name.removesuffix("_").removesuffix(".ogg")
        if not (name.startswith("EG") or name == "49tsukino_hikari"):
            continue  # Exclude RPG Maker's unused Battle/Town/Ship sample library.
        raw = path.read_bytes()
        if raw.startswith(b"RPGMV"):
            body = bytearray(raw[16:])
            for index in range(16):
                body[index] ^= key[index]
            raw = bytes(body)
        if not raw.startswith(b"OggS"):
            raise ValueError(f"Not an Ogg stream after decoding: {path}")
        target = OUTPUT / f"{name}.ogg"
        target.write_bytes(raw)
        audio = OggVorbis(target)
        decoded, samplerate = sf.read(target, dtype="float32", always_2d=True)
        peak = float(np.max(np.abs(decoded)))
        if not np.isfinite(decoded).all() or peak <= 0:
            raise ValueError(f"Invalid or silent audio: {path}")
        row = {"source": str(path), "file": target.name,
               "seconds": round(audio.info.length, 3), "sample_rate": samplerate,
               "channels": decoded.shape[1], "peak": round(peak, 5),
               "tags": dict(audio.tags or {}), "used_in": sorted(uses.get(name, [])),
               "sha256": hashlib.sha256(raw).hexdigest()}
        rows.append(row)
        print(json.dumps({"file": row["file"], "seconds": row["seconds"],
                          "artist": row["tags"].get("artist", []), "used_in": row["used_in"]}, ensure_ascii=False))
    (OUTPUT.parent / "bgm-manifest.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Extracted and decoded {len(rows)} custom BGM files without transcoding.")

if __name__ == "__main__":
    main()
