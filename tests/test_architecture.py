"""Keep native side effects out of the persisted domain models."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class ArchitectureTests(unittest.TestCase):
    def test_domain_does_not_call_native_runtime(self):
        forbidden = re.compile(r"\b(?:UnityEngine|HarmonyLib|ModHelper|PlayerStore|StoreStation|GameItem)\b")
        for source in (ROOT / "src/Domain").glob("*.cs"):
            with self.subTest(source=source.name):
                code = re.sub(r"//[^\n]*|/\*.*?\*/", "", source.read_text(encoding="utf-8-sig"), flags=re.S)
                self.assertIsNone(forbidden.search(code), "Native calls belong in src/Runtime")
