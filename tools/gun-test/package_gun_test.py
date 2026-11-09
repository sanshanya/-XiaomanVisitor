"""Package a separately identified, portrait-only aiming harness. Never changes game settings."""
import argparse
import csv
import hashlib
import json
import shutil
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ID = "sansm.xiaoman.gun_test"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--zip", action="store_true")
    args = parser.parse_args()
    package = ROOT / "package/XiaomanVisitor"
    stage = args.stage.resolve()
    stage.relative_to(ROOT / "release")
    version = ET.parse(package / "manifest.xml").getroot().findtext("ModVersion")
    if not args.zip:
        assert stage.name == "XiaomanGunTest"
        for relative in ("preview.png", "Textures/xiaoman_atlas.png", "Portraits/xiaoman_tavern_atlas.png", "Portraits/xiaoman_gun_atlas.png"):
            target = stage / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(package / relative, target)
        mod = ET.Element("Mod")
        for key, value in (("ID", ID), ("Name", "小满抬枪测试版（独立）"), ("Author", "sansm"), ("ModVersion", version),
                           ("Description", "独立持枪反应测试。请关闭主模组后启用本模组。任意日期开门后，测试小满追加到队尾；Ctrl+Shift+M 可再次邀请。只测试瞄准、口型与收枪，不开放射击或交易，不运行主线剧情、赠礼或酒馆。字幕无配音。恢复上一版原作担心眼神、小汗滴和口型，仅在刘海间的额头增加浅冷色阴影。美术素材来自《雨音に満ちる夜》。")):
            ET.SubElement(mod, key).text = value
        ET.SubElement(ET.SubElement(mod, "GameVersions"), "Version").text = "0.46D"
        ET.SubElement(ET.SubElement(mod, "Assemblies"), "Assembly").text = "XiaomanGunTest.dll"
        ET.SubElement(mod, "Capabilities")
        ET.indent(mod, space="  ")
        ET.ElementTree(mod).write(stage / "manifest.xml", encoding="utf-8", xml_declaration=True)
        translations = json.loads((ROOT / "tools/gun-test/localization.json").read_text(encoding="utf-8"))
        for language, overrides in translations.items():
            with (package / f"Localization/{language}.csv").open(encoding="utf-8-sig", newline="") as source:
                entries = dict(csv.reader(source))
            entries.update(overrides)
            destination = stage / f"Localization/{language}.csv"
            destination.parent.mkdir(parents=True, exist_ok=True)
            with destination.open("w", encoding="utf-8", newline="") as out:
                csv.writer(out, lineterminator="\n").writerows(entries.items())
        (stage / "测试说明.txt").write_text(
            "小满抬枪测试版（独立）\n\n"
            "1. 重启游戏，在模组管理中关闭主模组，开启本测试版。\n"
            "2. 载入存档并开门。任意日期均可，测试小满追加在原生顾客之后。\n"
            "3. 若未出现，营业时按 Ctrl+Shift+M 邀请；同一时刻只排一位。\n"
            "4. 按原游戏方式拿起霰弹枪，观察原作担心表情、小汗滴、刘海间额头浅阴影与三档口型。\n"
            "5. 右键收枪，确认她回应后恢复原对话；可反复测试。\n"
            "6. 左键不会开火。新台词仅字幕；本包不含音乐或配音。\n\n"
            "本次恢复上一版原眼睛与小汗滴，变化仅限刘海间的额头浅阴影。新素材仍由作者实机验收。\n\n"
            "独立 ID：sansm.xiaoman.gun_test\n"
            "本包仅有立绘与持枪测试，没有主线、赠礼、酒馆或交易模块。\n"
            "测试完成后关闭本测试版、重新开启主模组即可。\n"
            "需包含 2026-11-08 Mod API 更新的游戏版本。美术来自《雨音に満ちる夜》。\n",
            encoding="utf-8-sig")
        return

    archive = stage.parent / f"XiaomanGunTest-{version}.zip"
    files = sorted(p for p in stage.rglob("*") if p.is_file())
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as out:
        for path in files:
            out.write(path, "XiaomanGunTest/" + path.relative_to(stage).as_posix())
    with zipfile.ZipFile(archive) as out:
        assert out.testzip() is None
        assert out.read("XiaomanGunTest/manifest.xml") == (stage / "manifest.xml").read_bytes()
    report = dict(mod_id=ID, version=version, file=archive.name, bytes=archive.stat().st_size,
                  sha256=hashlib.sha256(archive.read_bytes()).hexdigest(), gameplay_tested=False,
                  files={str(p.relative_to(stage)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest() for p in files})
    (stage.parent / "test-package.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "files"}, ensure_ascii=True))


if __name__ == "__main__":
    main()
