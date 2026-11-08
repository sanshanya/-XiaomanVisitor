"""Read-only research of the installed game's original Eleanor localization."""
from pathlib import Path
import json
import sys
import UnityPy

sys.stdout.reconfigure(encoding="utf-8")
root = Path(__file__).resolve().parents[1]
bundles = Path(r"E:\app\steam\steamapps\common\Probably Stolen\Probably Stolen_Data\StreamingAssets\aa\StandaloneWindows64")

def table(filename, name):
    env = UnityPy.load(str(bundles / filename))
    return next(o.read_typetree() for o in env.objects
                if o.type.name == "MonoBehaviour" and o.peek_name() == name)

shared = table("localization-assets-shared_assets_all.bundle", "Dialogue Shared Data")
localized = table("localization-string-tables-chinese(simplified)(zh)_assets_all.bundle", "Dialogue_zh")
keys = {r["m_Id"]: r["m_Key"] for r in shared["m_Entries"]}
rows = [{"key": keys.get(r["m_Id"], ""), "text": r.get("m_Localized", "")}
        for r in localized["m_TableData"] if "eleanor" in keys.get(r["m_Id"], "").lower()]
(root / "reference/eleanor-original-dialogue.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
for prefix in ["dialog_eleanor_intro_", "dialog_eleanor3_", "dialog_eleanor6_", "dialog_eleanor7_", "dialog_eleanor8_"]:
    for row in [r for r in rows if r["key"].startswith(prefix)][:4]:
        print(row["key"], row["text"])
for row in rows:
    if row["key"] in ["dialog_eleanor8_return", "dialog_eleanor8_leak", "dialog_eleanor9_free_1", "dialog_eleanor9_rev_1", "dialog_eleanor9_free_2", "dialog_eleanor9_rev_2"]:
        print(row["key"], row["text"])
print("Saved", len(rows), "original Eleanor dialogue entries for research.")
