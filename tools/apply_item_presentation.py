"""Apply item and map UI text; no story, letter, music or voice changes."""
import csv
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def apply_to(localizations):
    for filename in ("item-presentation.json", "tavern-map-localization.json"):
        changes = json.loads((ROOT / "assets" / filename).read_text(encoding="utf-8"))
        for language, entries in changes.items():
            missing = set(entries) - set(localizations[language])
            assert all(all(key in changes[lang] for lang in ("zh", "ja", "en")) for key in missing), "New UI keys must cover all three languages"
            localizations[language].update(entries)


def main():
    files = {lang:ROOT/f"package/XiaomanVisitor/Localization/{lang}.csv" for lang in ("zh","ja","en")}
    localizations = {}
    for lang,path in files.items():
        with path.open(encoding="utf-8-sig",newline="") as source:
            localizations[lang] = dict(csv.reader(source))
    apply_to(localizations)
    for lang,path in files.items():
        with path.open("w",encoding="utf-8",newline="") as target:
            csv.writer(target,lineterminator="\n").writerows(localizations[lang].items())
    print("Updated inventory and map UI text in zh, ja and en; story text and voice bindings unchanged.")


if __name__ == "__main__":
    main()
