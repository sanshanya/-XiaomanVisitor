"""Rehearsal contract checks, not a simulation of the game UI."""
import importlib.util
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
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
        self.assertEqual(len(actual), 122)
        self.assertIn("可现在……我已经放弃了。", actual)
        self.assertEqual(actual[-2:], ["晚安，前辈。", "……明天也要在一起哦。"])

    def test_attack_and_finale_do_not_move_ahead_of_their_art(self):
        images = [(i, c["value"]) for i, c in enumerate(self.cues) if c["kind"] == "image"]
        self.assertEqual([n for _, n in images], [1, 2, 3, 4, 5])
        attack = next(i for i, c in enumerate(self.cues) if c.get("text") == "把我的人生还回来啊——！！！")
        impact = next(i for i, c in enumerate(self.cues) if c["kind"] == "impact")
        self.assertLess(images[2][0], attack)
        self.assertLess(attack, impact)
        self.assertLess(impact, images[3][0])
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


if __name__ == "__main__":
    unittest.main()
