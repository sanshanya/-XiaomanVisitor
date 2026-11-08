"""Prepare conservative music delivery copies from unchanged source masters."""
from pathlib import Path
import argparse
import concurrent.futures
import hashlib
import json
import math
import re
import shutil
import subprocess
import sys
import numpy as np
import soundfile as sf
from mutagen.id3 import ID3
from mutagen.mp3 import MP3

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "package/XiaomanVisitor"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def compare(source, candidate):
    error = source_energy = target_energy = 0.0
    source_peak = target_peak = 0.0
    source_clipped = target_clipped = count = 0
    with sf.SoundFile(source) as a, sf.SoundFile(candidate) as b:
        assert a.samplerate == b.samplerate and a.channels == b.channels == 2, "Audio format changed"
        assert len(a) == len(b), "Decoded sample count changed"
        frames, rate, channels = len(a), a.samplerate, a.channels
        for x in a.blocks(blocksize=131072, dtype="float64", always_2d=True):
            y = b.read(len(x), dtype="float64", always_2d=True)
            assert x.shape == y.shape and np.isfinite(x).all() and np.isfinite(y).all()
            source_energy += float(np.sum(x*x)); target_energy += float(np.sum(y*y))
            error += float(np.sum((x-y)**2)); count += x.size
            source_peak = max(source_peak, float(np.max(np.abs(x))))
            target_peak = max(target_peak, float(np.max(np.abs(y))))
            source_clipped += int(np.count_nonzero(np.abs(x) >= 1.0))
            target_clipped += int(np.count_nonzero(np.abs(y) >= 1.0))
    assert count and source_energy > 0 and target_energy > 0
    return dict(frames=frames, sample_rate=rate, channels=channels, seconds=round(frames/rate, 3),
                waveform_snr_db=None if error == 0 else round(10*math.log10(source_energy/error), 3),
                level_change_db=round(10*math.log10(target_energy/source_energy), 4),
                source_peak=round(source_peak, 6), peak=round(target_peak, 6),
                source_clipped_fraction=source_clipped/count, clipped_fraction=target_clipped/count)


def optimize(apply=False):
    profile = json.loads((ROOT / "assets/audio/music-encoding.json").read_text(encoding="utf-8"))
    manifest_path = ROOT / "assets/audio/cassette-manifest.json"
    originals = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert len(originals) == 14 and profile["encoder"] == "libmp3lame"
    stage = ROOT / "outputs/audio-compression/selected"
    stage.mkdir(parents=True, exist_ok=True)
    for row in originals:
        assert re.fullmatch(r"[a-z0-9_]+", row["key"])
        source = Path(row["source"])
        assert source.is_file() and digest(source) == row.get("source_sha256", row["sha256"]), row["key"]

    def process(row):
        source = Path(row["source"])
        target = stage / ("rain_" + row["key"] + ".mp3")
        master_hash = digest(source)
        info = MP3(source)
        cover_bytes = sum(len(image.data) for image in info.tags.getall("APIC")) if info.tags else 0
        method, fallback = "copy", None
        if row["key"] in profile["keep_original_keys"]:
            shutil.copyfile(source, target)
            fallback = "Preserve the approved rain mix's exact audio data."
        else:
            subprocess.run([profile["ffmpeg"], "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-i", str(source), "-map", "0:a:0", "-vn", "-map_metadata", "-1", "-c:a", profile["encoder"],
                "-q:a", str(profile["vbr_quality"]), "-id3v2_version", "0", "-write_id3v1", "0", str(target)], check=True)
            method = "mp3_vbr"
        metrics = compare(source, target)
        if method == "mp3_vbr" and ((metrics["waveform_snr_db"] is not None and metrics["waveform_snr_db"] < profile["minimum_waveform_snr_db"])
                or abs(metrics["level_change_db"]) > 0.15
                or metrics["clipped_fraction"] > metrics["source_clipped_fraction"] + 0.00001):
            fallback = "Conservative waveform/level/clipping guard chose unchanged MPEG audio frames."
            shutil.copyfile(source, target)
            if cover_bytes:
                ID3(target).delete()
                method = "metadata_only"
            else:
                method = "copy"
            metrics = compare(source, target)
            assert metrics["waveform_snr_db"] is None and metrics["level_change_db"] == 0
        encoded_info = MP3(target)
        assert target.stat().st_size <= source.stat().st_size and digest(source) == master_hash
        if method != "copy":
            assert not encoded_info.tags or not encoded_info.tags.getall("APIC")
        processing = dict(method=method, encoder=profile["encoder"] if method == "mp3_vbr" else None,
                          vbr_quality=profile["vbr_quality"] if method == "mp3_vbr" else None,
                          source_bytes=source.stat().st_size, packaged_bytes=target.stat().st_size,
                          original_cover_bytes=cover_bytes, average_bitrate=encoded_info.info.bitrate,
                          sample_rate_preserved=True, stereo_preserved=True, decoded_frames_preserved=True,
                          fallback=fallback)
        updated = dict(row, source_sha256=master_hash, sha256=digest(target), peak=metrics["peak"],
                       seconds=metrics["seconds"], processing=processing)
        audit = dict(key=row["key"], title=row["title"], source_sha256=master_hash,
                     packaged_sha256=updated["sha256"], processing=processing, comparison=metrics)
        print(f"{row['key']}: {source.stat().st_size/1048576:.2f} -> {target.stat().st_size/1048576:.2f} MiB / {method}", flush=True)
        return updated, audit, target

    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        prepared = list(pool.map(process, originals))
    if apply:
        # Validate every candidate before modifying any packaged audio or manifest.
        for row, audit, target in prepared:
            shutil.copyfile(target, PACKAGE / row["packaged"].replace("\\", "/"))
        manifest_path.write_text(json.dumps([r for r, _, _ in prepared], ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    before = sum(a["processing"]["source_bytes"] for _, a, _ in prepared)
    after = sum(a["processing"]["packaged_bytes"] for _, a, _ in prepared)
    report = dict(profile=profile, applied=apply, tracks=len(prepared), source_bytes=before,
                  packaged_bytes=after, saved_bytes=before-after, saved_percent=round((before-after)/before*100, 2),
                  all_sources_unchanged=True, all_decoded_frames_preserved=True,
                  listening_review="Waveform checks only; not a human listening review.",
                  rows=[a for _, a, _ in prepared])
    (ROOT / "outputs/audio-compression/music-report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k not in ("rows", "profile")}, ensure_ascii=False, indent=2))
    return report


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Copy all verified candidates into the mod package.")
    args = parser.parse_args()
    optimize(args.apply)


if __name__ == "__main__":
    main()
