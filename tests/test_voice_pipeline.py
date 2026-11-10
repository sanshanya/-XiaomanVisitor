"""Offline regression checks. No credentials or network calls are used."""
import hashlib
import sys
import tempfile
import unittest
import wave
from pathlib import Path
from unittest import mock
sys.path.insert(0,str(Path(__file__).resolve().parents[1] / "tools"))
import voice_pipeline as pipeline


class VoicePipelineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="xiaoman-voice-test-")
        self.root = Path(self.temp.name).resolve()
        assert self.root.is_relative_to(Path(tempfile.gettempdir()).resolve())
        self.package = self.root / "package/XiaomanVisitor"
        (self.package / "Sounds").mkdir(parents=True)
        self.patch_root = mock.patch.object(pipeline, "ROOT", self.root)
        self.patch_package = mock.patch.object(pipeline, "PACKAGE", self.package)
        self.patch_root.start(); self.patch_package.start()
        self.texts = {"ja": {"intro_1": "こんばんは。", "shopping": "予算は{0}です。"},
                      "zh": {"intro_1": "你好。", "shopping": "预算{0}。"},
                      "en": {"intro_1": "Hello.", "shopping": "Budget {0}."}}
        self.patch_texts = mock.patch.object(pipeline, "localized", return_value=self.texts)
        self.patch_texts.start()
        self.script_path = self.root / "story/script.json"
        self.row = {"key": "intro_1", "spoken_text": "こんばんは。", "reviewed": True, "generic": False,
                    "subtitle_templates": pipeline.templates_for("intro_1", self.texts), "audio": "assets/intro.wav"}
        self.script = {"schema": 1, "language": "ja", "voice_id": "ayano", "model_id": "luna-tts-1.5", "lines": [self.row]}
        audio = self.root / self.row["audio"]
        audio.parent.mkdir(parents=True)
        with wave.open(str(audio), "wb") as stream:
            stream.setparams((1, 2, 24000, 0, "NONE", "not compressed"))
            stream.writeframes(b"\x10\x00" * 240)
        self.metadata = {"request": {"voice_id": "ayano", "model_id": "luna-tts-1.5", "language": "ja", "generate_text": "こんばんは。"},
                         "binding": pipeline.binding_for(self.row, self.script), "sha256": hashlib.sha256(audio.read_bytes()).hexdigest()}
        pipeline.save(audio.with_suffix(".json"), self.metadata)
        pipeline.save(self.script_path, self.script)

    def tearDown(self):
        self.patch_texts.stop(); self.patch_package.stop(); self.patch_root.stop()
        self.temp.cleanup()

    def test_approved_audio_packs_without_api(self):
        with mock.patch.object(pipeline.subprocess, "run", side_effect=AssertionError("Unexpected paid API request")):
            result = pipeline.pack(self.script_path)
            pipeline.generate(self.script_path, ["intro_1"])
        self.assertEqual(len(result["lines"]), 1)
        self.assertEqual(result["pending_lines"], 0)
        self.assertTrue((self.package / "Sounds" / (result["lines"][0]["sound_key"] + ".wav")).exists())

    def test_changed_text_disables_old_recording_and_preserves_original(self):
        pipeline.pack(self.script_path)
        self.texts["zh"]["intro_1"] = "晚上好。"
        result = pipeline.pack(self.script_path)
        self.assertEqual(result["lines"], [])
        self.assertTrue((self.root / self.row["audio"]).exists())
        self.assertEqual(list((self.package / "Sounds").glob("xm_voice_*.wav")), [])

    def test_identical_pcm_keeps_separate_dialogue_bindings_with_one_clip(self):
        self.texts['ja']['second_line']=self.texts['ja']['intro_1']
        self.texts['zh']['second_line']='又见面了。'
        self.texts['en']['second_line']='Hello again.'
        second=dict(self.row,key='second_line',subtitle_templates=pipeline.templates_for('second_line',self.texts),audio='')
        self.script['lines'].append(second)
        pipeline.save(self.script_path,self.script)
        with mock.patch.object(pipeline.subprocess,'run',side_effect=AssertionError('Unexpected paid request')):
            pipeline.generate(self.script_path,['second_line'])
        result=pipeline.pack(self.script_path)
        self.assertEqual(len(result['lines']),2)
        self.assertEqual(len({r['sound_key'] for r in result['lines']}),1)
        self.assertEqual(len(list((self.package/'Sounds').glob('xm_voice_*.wav'))),1)

    def test_refresh_keeps_manual_text_until_subtitles_change(self):
        before = pipeline.export_script(self.script_path, refresh=True)
        self.assertTrue(before["lines"][0]["reviewed"])
        self.texts["ja"]["intro_1"] = "こんばんは。また来ました。"
        changed = pipeline.export_script(self.script_path, refresh=True)
        self.assertFalse(changed["lines"][0]["reviewed"])
        self.assertEqual(changed["lines"][0]["audio"], "")
        self.assertEqual(changed["lines"][0]["previous_audio"], self.row["audio"])

    def test_unreviewed_and_dynamic_lines_never_call_api(self):
        self.row["reviewed"] = False
        pipeline.save(self.script_path, self.script)
        with mock.patch.object(pipeline.subprocess, "run", side_effect=AssertionError("Unexpected paid API request")):
            pipeline.generate(self.script_path, [])
        self.assertEqual(pipeline.audio_status(self.row, self.script, self.texts), "needs_review")
        self.row.update(key="shopping", spoken_text="今日は少し買い物をしたくて。", reviewed=True,
                        subtitle_templates=pipeline.templates_for("shopping", self.texts))
        self.assertEqual(pipeline.validate_line(self.row, self.script, self.texts), "needs_generic_spoken_text")
        self.row["generic"] = True
        self.assertEqual(pipeline.validate_line(self.row, self.script, self.texts), "ready")
        self.row["spoken_text"] = "{0}です。"
        self.assertEqual(pipeline.validate_line(self.row, self.script, self.texts), "needs_spoken_text")

    def test_reviewed_translation_change_reuses_identical_speech(self):
        self.texts["zh"]["intro_1"] = "晚上好。"
        self.row["subtitle_templates"] = pipeline.templates_for("intro_1", self.texts)
        self.row["previous_audio"], self.row["audio"] = self.row["audio"], ""
        pipeline.save(self.script_path, self.script)
        with mock.patch.object(pipeline.subprocess, "run", side_effect=AssertionError("Unexpected paid API request")):
            pipeline.generate(self.script_path, ["intro_1"])
        updated = pipeline.load(self.script_path)
        self.assertEqual(pipeline.audio_status(updated["lines"][0], updated, self.texts), "ready")

    def test_pack_preserves_other_language_recordings_and_music(self):
        pipeline.save(self.package / "Voices/index.json", {"schema": 1, "lines": [{"language": "en", "sound_key": "xm_voice_en_old"}],
                                                           "pending_by_language": {"en": 2}})
        (self.package / "Sounds/xm_voice_en_old.wav").write_bytes(b"existing")
        music = self.package / "Sounds/rain_title.mp3"
        music.write_bytes(b"music")
        result = pipeline.pack(self.script_path)
        self.assertEqual(len(result["lines"]), 2)
        self.assertEqual(result["pending_lines"], 2)
        self.assertEqual(music.read_bytes(), b"music")
        self.assertTrue((self.package / "Sounds/xm_voice_en_old.wav").exists())

    def test_changed_audio_is_not_packed(self):
        (self.root / self.row["audio"]).write_bytes(b"corrupt")
        self.assertEqual(pipeline.audio_status(self.row, self.script, self.texts), "audio_hash_mismatch")

    def test_performance_text_is_separate_and_invalidates_old_audio(self):
        self.row["generate_text"] = "こんばんは。[chuckles]"
        self.assertEqual(pipeline.audio_status(self.row, self.script, self.texts), "stale_recording")
        self.metadata["request"]["generate_text"] = self.row["generate_text"]
        pipeline.save((self.root / self.row["audio"]).with_suffix(".json"), self.metadata)
        self.assertEqual(pipeline.audio_status(self.row, self.script, self.texts), "ready")
        result = pipeline.pack(self.script_path)  # Existing on-disk script still has plain text.
        self.assertEqual(result["lines"], [])
        self.assertNotIn("[chuckles]", self.texts["ja"]["intro_1"])

    def test_speed_change_and_approved_original_override(self):
        self.script["speed"] = 1.1
        self.assertEqual(pipeline.audio_status(self.row, self.script, self.texts), "stale_recording")
        self.row["speed"] = 1.0
        self.assertEqual(pipeline.audio_status(self.row, self.script, self.texts), "ready")

    def test_refresh_preserves_speed_and_generation_settings(self):
        self.script.update(speed=1.1, text_normalization=True)
        pipeline.save(self.script_path, self.script)
        refreshed = pipeline.export_script(self.script_path, refresh=True)
        self.assertEqual(refreshed["speed"], 1.1)
        self.assertTrue(refreshed["text_normalization"])

    def test_identical_speech_across_keys_reuses_audio_without_api(self):
        self.texts["zh"]["second_line"] = "另一处的同一句话。"
        self.texts["ja"]["second_line"] = "こんばんは。"
        other = dict(self.row, key="second_line", audio="", subtitle_templates=pipeline.templates_for("second_line", self.texts))
        self.script["lines"].append(other)
        pipeline.save(self.script_path, self.script)
        with mock.patch.object(pipeline.subprocess, "run", side_effect=AssertionError("Duplicate paid API request")):
            pipeline.generate(self.script_path, ["second_line"])
        result = pipeline.pack(self.script_path)
        self.assertEqual(len(result["lines"]), 2)
        self.assertEqual(result["pending_lines"], 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
