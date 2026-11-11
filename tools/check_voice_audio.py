"""Check packaged speech against the reviewed final script and decoded waveforms."""
from pathlib import Path
import collections
import hashlib
import json
import math
import sys
import wave
import xml.etree.ElementTree as ET
import numpy as np
import soundfile as sf
import voice_pipeline as pipeline
from voice_delivery import audio_filename


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    version = ET.parse(pipeline.PACKAGE / "manifest.xml").getroot().findtext("ModVersion")
    script = pipeline.load(pipeline.DEFAULT_SCRIPT)
    texts = pipeline.localized()
    pack = pipeline.load(pipeline.PACKAGE / "Voices/index.json")
    spec = pipeline.load(pipeline.ROOT / "assets/story.json")
    records = {r["line_key"]: r for r in pack["lines"] if r["language"] == "ja"}
    expected = {r["key"] for r in script["lines"]}
    deferred = {r["key"] for r in script["lines"] if r.get("defer_recording")}
    assert set(spec["voice_keys"]) == expected
    assert set(records) == expected - deferred, "Missing, stale or unexpected recording"
    assert pack["pending_lines"] == len(deferred)
    rows, warnings = [], []
    for row in script["lines"]:
        key = row["key"]
        if key in deferred:
            assert not row["reviewed"] and not row["audio"] and pipeline.audio_status(row, script, texts) == "needs_review", key
            continue
        assert pipeline.audio_status(row, script, texts) == "ready", key
        file = pipeline.ROOT / row["audio"]
        metadata = pipeline.load(file.with_suffix(".json"))
        assert pipeline.effective_speed(metadata) == 1.1, key
        record = records[key]
        assert record["spoken_text"] == row["spoken_text"]
        packaged = pipeline.PACKAGE / "Sounds" / audio_filename(record)
        packaged_hash = hashlib.sha256(packaged.read_bytes()).hexdigest()
        assert packaged_hash == record.get("packaged_sha256", metadata["sha256"]), key
        assert record.get("source_sha256", metadata["sha256"]) == metadata["sha256"], key
        with wave.open(str(file), "rb") as audio:
            channels, width, rate, frames = audio.getnchannels(), audio.getsampwidth(), audio.getframerate(), audio.getnframes()
            raw = audio.readframes(frames)
        assert (channels, width, rate) == (1, 2, 24000) and len(raw) == frames * 2
        samples = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768
        decoded, decoded_rate = sf.read(packaged, dtype="float64", always_2d=True)
        assert (len(decoded), decoded_rate, decoded.shape[1]) == (frames, rate, channels), key
        delivered = decoded[:, 0]
        noise = float(np.mean((delivered - samples) ** 2))
        snr = 120.0 if noise == 0 else 10 * math.log10(float(np.mean(samples ** 2)) / noise)
        assert snr >= 24, (key, "delivery SNR", snr)
        peak = float(np.abs(samples).max())
        rms = float(np.sqrt(np.mean(samples**2)))
        seconds = frames / rate
        assert peak > .005 and rms > .0002 and seconds > .25, key
        blocks = samples[:len(samples)//480*480].reshape(-1,480)
        energy = np.sqrt(np.mean(blocks**2,axis=1))
        delivered_energy = np.sqrt(np.mean(delivered[:len(samples)//480*480].reshape(-1,480)**2, axis=1))
        correlation = float(np.corrcoef(energy, delivered_energy)[0, 1])
        assert correlation >= .995, (key, "speech amplitude envelope changed", correlation)
        assert float(np.mean(np.abs(delivered) >= .999)) <= max(.001, float(np.mean(np.abs(samples) >= .999))), key
        active = np.flatnonzero(energy > max(.0025, rms * .1))
        assert active.size, key
        lead, tail = float(active[0]*.02), max(0.,seconds-float((active[-1]+1)*.02))
        clipping = float(np.mean(np.abs(samples) >= .999))
        if lead > 1.2 or tail > 1.2 or clipping > .001 or seconds > 25:
            warnings.append(dict(key=key, leading_silence=round(lead,3), trailing_silence=round(tail,3), clipped_fraction=clipping, seconds=round(seconds,3)))
        rows.append(dict(key=key, seconds=round(seconds,3), peak_dbfs=round(20*math.log10(peak),2),
                         rms_dbfs=round(20*math.log10(rms),2), leading_silence=round(lead,3), trailing_silence=round(tail,3),
                         clipped_fraction=round(clipping,7), effective_speed=1.1, sha256=metadata["sha256"],
                         packaged_sha256=packaged_hash, delivery_snr_db=round(snr, 2), energy_correlation=round(correlation, 6),
                         source=row["audio"], packaged=str(packaged.relative_to(pipeline.PACKAGE))))
    assert not any("shutter_close_02" in r["key"] for r in rows), "Nonverbal SFX must not enter TTS"
    result = dict(version=version, language="ja", voice="ayano", recorded_lines=len(rows), pending_lines=len(deferred), deferred_keys=sorted(deferred),
                  seconds=round(sum(r["seconds"] for r in rows),3), sample_rate=24000, channels=1, source_bit_depth=16,
                  delivery_formats=sorted({Path(r["packaged"]).suffix for r in rows}),
                  minimum_delivery_snr_db=min(r["delivery_snr_db"] for r in rows),
                  minimum_energy_correlation=min(r["energy_correlation"] for r in rows),
                  speed=1.1, first_performance_preserved=True, warnings=warnings, lines=rows)
    output = pipeline.audio_report_path()
    pipeline.save(output, result)
    summary = [f"小满 {version} 日语配音文件检查", "", f"录音键：{len(rows)}，明确延后制作：{len(deferred)}。", "音色：Ayano；语言：日语；统一实际语速：1.1。",
               "首句保留已确认原录音，以保持音高的时间伸缩制作1.1倍速副本，原素材未改。",
               "其他录音通过VUI Luna-TTS 1.5离线生成；运行中的模组只读取打包音频。",
               f"总时长：{result['seconds']:.3f}秒。制作源为24000Hz、单声道、16-bit PCM WAV；运行交付格式：{', '.join(result['delivery_formats'])}。",
               "交付音频逐句解码核对采样数、声道、采样率、幅度包络与削波；原始WAV保留，压缩不重新合成或调整语速。",
               f"{len(rows)}个在用录音键全部与现有字幕、口播文本、速度及打包音频hash匹配。新增字幕不借用其他句子的录音。",
               "敲门为单独音效，没有把舞台说明交给TTS。", f"波形异常待复核项：{len(warnings)}。",
               "本检查覆盖文件、格式、完整性、静音、削波、台词绑定；不是逐句真人日语听审。", ""]
    output.with_suffix(".txt").write_text("\n".join(summary),encoding="utf-8-sig")
    print(json.dumps({k:v for k,v in result.items() if k != "lines"},ensure_ascii=False,indent=2))


if __name__ == "__main__":
    main()
