"""Rehearsal contract checks, not a simulation of the game UI."""
import importlib.util
import hashlib
import json
import re
import sys
import unittest
import wave
from array import array
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import buyback_voice
spec = importlib.util.spec_from_file_location("prepare_buyback", ROOT / "tools/prepare_buyback.py")
buyback = importlib.util.module_from_spec(spec)
spec.loader.exec_module(buyback)


class BuybackContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cues = json.loads((buyback.MODULE / "script.json").read_text(encoding="utf-8"))["cues"]

    def test_every_author_line_is_kept_in_order(self):
        manuscript = buyback.SOURCE.read_text(encoding="utf-8-sig")
        acts = manuscript.split("## 第一幕｜", 1)[1].split("## 结局定格", 1)[0]
        author_lines = re.findall("“([^”\n]+)”", acts)
        actual = [c["text"] for c in self.cues if c["kind"] == "line"]
        self.assertEqual(actual, author_lines)
        self.assertEqual(len(actual), 126)
        self.assertIn("前辈也没有机会说了。", actual)
        index = actual.index("我好像明白了。")
        self.assertEqual(actual[index + 1], "原来前辈是想要用自己的生命来作为代价")
        self.assertIn("可现在……我已经放弃了。", actual)
        self.assertEqual(actual[-2:], ["晚安，前辈。", "……明天也要在一起哦。"])

    def test_attack_and_finale_do_not_move_ahead_of_their_art(self):
        images = [(i, c["value"]) for i, c in enumerate(self.cues) if c["kind"] == "image"]
        self.assertEqual([n for _, n in images], [1, 2, 3, 4, 5])
        attack = next(i for i, c in enumerate(self.cues) if c.get("text") == "还给我……还给我啊……！")
        impact = next(i for i, c in enumerate(self.cues) if c["kind"] == "impact")
        missing = next(i for i, c in enumerate(self.cues) if c.get("text") == "前辈也没有机会说了。")
        self.assertLess(images[1][0], missing)
        self.assertLess(missing, impact)
        self.assertLess(impact, attack)
        self.assertLess(attack, images[2][0])
        self.assertLess(impact, images[3][0])
        self.assertEqual(self.cues[impact]["value"], 3)
        parts = self.cues[attack:attack+3]
        self.assertEqual([c["value"] for c in parts], [1, 2, 3])
        self.assertTrue(all(c["transition"] == "attack_segment" for c in parts))
        self.assertEqual([c["text"] for c in parts], ["还给我……还给我啊……！", "我的人生……还给我……！！", "还给我啊——！！！"])
        self.assertTrue(all(0 <= c["impact_seconds"] < c["seconds"] for c in parts))
        self.assertLess(attack+2, images[2][0])
        black = next(i for i, c in enumerate(self.cues) if c["kind"] == "black")
        self.assertLess(images[4][0], black)
        self.assertEqual([c["text"] for c in self.cues[black:] if c["kind"] == "line"],
                         ["晚安，前辈。", "……明天也要在一起哦。"])
        for text, pause in (("只告诉前辈一个人。♡", 2), ("我在您这里，买过两次那种水呢。", 2), ("漂亮吗？", .5)):
            index = next(i for i, c in enumerate(self.cues) if c.get("text") == text)
            self.assertEqual(self.cues[index + 1], dict(kind="wait", seconds=pause))

    def test_checked_artifacts_match_their_author_inputs(self):
        buyback.prepare(check=True)
        assets = buyback.asset_manifest()
        self.assertEqual(len({x["sha256"] for x in assets["images"]}), 5)
        self.assertEqual(assets["images"][1]["width"], 1024)
        self.assertEqual(assets["images"][1]["height"], 1536)

    def test_scene_changes_have_distinct_jobs_and_native_foley(self):
        images = [c for c in self.cues if c["kind"] == "image"]
        self.assertEqual([c["transition"] for c in images],
                         ["door_wipe", "dissolve", "late_reveal", "collapse", "dissolve"])
        self.assertGreater(images[3]["hold"], 0)
        self.assertEqual(sum(c["kind"] == "impact" for c in self.cues), 1)
        sounds = {s["role"]: s["identifier"] for s in buyback.asset_manifest()["sounds"]}
        self.assertEqual(sounds["hit"], "knife_stab")
        self.assertEqual(sounds["tape"], "cassette_insert")

    def test_door_choice_precedes_every_cg_and_preserves_the_shop(self):
        self.assertEqual(self.cues[0]["kind"], "shop")
        self.assertEqual([c["value"] for c in self.cues if c["kind"] == "knock"], [3])
        self.assertLess(self.cues[0]["opacity"], .25)
        self.assertLess(self.cues[0]["x"], .1)
        choices = [i for i, c in enumerate(self.cues) if c["kind"] == "door_choice"]
        self.assertEqual(len(choices), 1)
        first_cg = next(i for i, c in enumerate(self.cues) if c["kind"] == "image")
        self.assertEqual(choices[0], first_cg - 1)
        self.assertEqual(sum(c["kind"] == "line" for c in self.cues[:choices[0]]), 16)

    def test_knife_cg_waits_until_the_line_before_retreat_question(self):
        image = next(i for i, c in enumerate(self.cues) if c["kind"] == "image" and c["value"] == 2)
        self.assertEqual(self.cues[image + 1]["text"], "……咦？")
        self.assertEqual(self.cues[image + 2]["text"], "前辈，为什么要往后退呀？")
        knock = next(c for c in self.cues if c["kind"] == "knock")
        gains = knock["gains"] + knock["retry_gains"]
        self.assertTrue(all(0 < a < b <= 1 for a, b in zip(gains, gains[1:])))

    def test_recorded_three_knocks_preserve_the_reviewed_cc0_source(self):
        with wave.open(str(buyback.ASSETS / "buyback_door_knock.wav"), "rb") as source:
            self.assertEqual((source.getnchannels(), source.getsampwidth(), source.getframerate()), (1, 2, 48000))
            samples = array("h", source.readframes(source.getnframes()))
        peak = max(abs(x) for x in samples) / 32768
        rms = (sum((x / 32768) ** 2 for x in samples) / len(samples)) ** .5
        self.assertGreater(peak, .8)
        self.assertLess(peak, .95)
        self.assertGreater(rms, .03)
        recorded = buyback.asset_manifest()["knock_audio"]["recording"]
        self.assertEqual(recorded["license"], "CC0-1.0")
        self.assertEqual(recorded["author"], "Fabrizio84")
        self.assertEqual(recorded["knock_count"], 3)
        self.assertAlmostEqual(len(samples) / 48000, recorded["trim_seconds"][1] - recorded["trim_seconds"][0])
        knock = next(s for s in buyback.asset_manifest()["sounds"] if s["role"] == "knock")
        self.assertEqual(knock["source"], "mod")
        self.assertGreater(knock["gain"], .8)

    def test_beauty_question_waits_for_two_silent_choices(self):
        question = next(i for i, c in enumerate(self.cues) if c.get("text") == "呐，前辈，我今天美吗？")
        self.assertEqual(self.cues[question + 1]["kind"], "reply_choice")
        self.assertEqual(self.cues[question + 1]["options"], ["……", "……"])
        self.assertEqual(self.cues[question + 2]["text"], "……嘿嘿。")

    def test_performance_requires_manual_dialogue(self):
        script = json.loads((buyback.MODULE / "script.json").read_text(encoding="utf-8"))
        self.assertEqual(script["dialogue_mode"], "manual")
        self.assertEqual(script["cues"][-1]["text"], "隐藏结局：永久回购")

    def test_refusal_branch_preserves_the_new_author_lines(self):
        branch = json.loads(buyback.REFUSAL_SOURCE.read_text(encoding="utf-8"))
        self.assertEqual(branch["retry_line"], "前辈我知道你没睡，求你开开门好不好？")
        self.assertEqual(branch["third_lines"], ["前辈，求求你一定要开门。", "你让我做什么都可以~"])
        self.assertEqual(branch["farewell_line"], "算了，明天我再来找前辈，前辈请耐心等我......。")
        self.assertEqual(branch["news_title"], "酒馆酒水检出违禁品，店主已被刑拘")
        self.assertEqual(branch["news_body"], "近日，治安部门在下层商业街某酒馆出售的酒水中检出违禁成分，涉事店主已被刑事拘留。\n\n目前，该酒馆已被查封，相关酒品及原料均遭扣押。尽管违禁品来源尚未查明，空间站管理处仍已启动遣返程序，预计近日执行。")
        self.assertEqual(branch["note_photo"], "cg_05.png")
        self.assertEqual(branch["note_message"], "我的心上人，请一定要等我~无论你在哪里，我们终将重逢！！！")
        self.assertEqual(branch["schema"], 1)

    def test_music_is_the_approved_intro_not_a_different_cassette(self):
        music = buyback.asset_manifest()["music"]
        track = next(t for t in json.loads((ROOT / "assets/audio/cassette-manifest.json").read_text(encoding="utf-8"))
                     if t["key"] == "intro")
        self.assertEqual(music["identifier"], "rain_intro")
        self.assertEqual(music["file"], "rain_intro.mp3")
        self.assertEqual(music["title"], "小满的雨夜·Intro")
        self.assertTrue(music["loop"])
        self.assertEqual(music["sha256"], track["sha256"])
        self.assertEqual(music["sha256"], hashlib.sha256((ROOT / "package/XiaomanVisitor/Sounds" / music["file"]).read_bytes()).hexdigest())

    def test_all_ending_recordings_keep_authored_subtitles_and_import_provenance(self):
        script = buyback_voice.vp.load(buyback_voice.CATALOG)
        self.assertEqual(len(script["lines"]), 147)
        buyback_voice.checked(script)
        main = [r for r in script["lines"] if r["group"] == "main"]
        self.assertEqual([(r["key"], r["subtitle_templates"]["zh"]) for r in main],
                         [(c["key"], c["text"]) for c in self.cues if c["kind"] == "line"])
        imports = [r for r in script["lines"] if r.get("provider") == "author_recording"]
        self.assertEqual([r["key"] for r in imports], ["buyback_03_30", "buyback_03_31", "buyback_03_32"])
        row = imports[0]
        meta = buyback_voice.vp.load((ROOT / row["audio"]).with_suffix(".json"))
        self.assertEqual(meta["provider"], "author_recording")
        self.assertNotIn("request", meta)
        self.assertEqual(meta["processing"]["effective_speed"], 1)
        self.assertTrue(meta["processing"]["original_unchanged"])
        cue = next(c for c in self.cues if c.get("key") == row["key"])
        self.assertEqual(cue["impact_seconds"], row["blood_at"])
        self.assertLess(cue["impact_seconds"], meta["audio"]["seconds"])
        import numpy as np
        import soundfile as sf
        slices = [sf.read(ROOT / r["audio"], dtype="int16")[0] for r in imports]
        import shutil
        import subprocess
        decoded = subprocess.run([shutil.which("ffmpeg"), "-hide_banner", "-loglevel", "error", "-nostdin",
                                  "-i", str(ROOT / meta["source"]), "-ar", "24000", "-ac", "1",
                                  "-f", "s16le", "-c:a", "pcm_s16le", "pipe:1"], check=True, capture_output=True).stdout
        self.assertTrue(np.array_equal(np.concatenate(slices), np.frombuffer(decoded, dtype="<i2")))

    def test_ending_packer_rejects_the_retired_separate_package(self):
        main = ROOT / "package/XiaomanVisitor"
        before = (main / "Voices/index.json").read_bytes()
        with self.assertRaises(ValueError):
            buyback_voice.pack(ROOT / "release/XiaomanBuybackTest")
        self.assertEqual((main / "Voices/index.json").read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
