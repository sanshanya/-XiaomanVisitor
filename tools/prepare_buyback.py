"""Compile the author's five-act script. Offline; no image edits or voice requests."""
import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "story/支线/永久回购_五幕完整演出剧本_v1.0.md"
ASSETS = ROOT / "assets/permanent-buyback"
MODULE = ROOT / "side-stories/permanent-buyback"
ID = "sansm.xiaoman.buyback_test"
VERSION = "0.1.0"


def compile_script():
    text = SOURCE.read_text(encoding="utf-8-sig")
    cues = [dict(kind="fade", seconds=0.8), dict(kind="knock", seconds=1.8)]
    act = 0
    image = 0
    line_number = 0
    for row in text.splitlines():
        heading = re.match(r"## 第([一二三四五])幕", row)
        if heading:
            act = "一二三四五".index(heading[1]) + 1
            line_number = 0
        if row.startswith("## 结局定格"):
            break
        cg = re.match(r"\*\*【CG\s*0([1-5])", row)
        if cg and int(cg[1]) != image:
            image = int(cg[1])
            cues.append(dict(kind="image", value=image, seconds=0.55, act=act))
        spoken = re.fullmatch(r'(?:\*\*)?“(.*?)”(?:\*\*)?', row)
        if spoken:
            if not act:
                raise ValueError("Spoken line outside an act")
            line_number += 1
            cues.append(dict(kind="line", text=spoken[1], act=act,
                             key=f"buyback_{act:02}_{line_number:02}"))
        elif row == "*停顿两秒。*":
            cues.append(dict(kind="wait", seconds=2.0))
        elif row == "*停顿一秒。*":
            cues.append(dict(kind="wait", seconds=1.0))
        elif row == "*停顿半秒。*":
            cues.append(dict(kind="wait", seconds=0.5))
        elif row == "*短暂停顿。*":
            cues.append(dict(kind="wait", seconds=0.6))
        elif row == "*一声尖锐的金属撞击。*":
            cues.append(dict(kind="impact", seconds=1.2))
        elif row == "*画面逐渐变黑。*":
            cues.extend([dict(kind="black", seconds=1.6), dict(kind="lock", seconds=4.0)])
        elif row == "*很久以后，响起磁带机的机械启动声。*":
            cues.append(dict(kind="tape", seconds=1.0))
    cues.append(dict(kind="title", text="隐藏异常结局：永久回购", seconds=1.5))
    counts = {n: sum(c.get("act") == n and c["kind"] == "line" for c in cues) for n in range(1, 6)}
    if any(n == 0 for n in counts.values()):
        raise ValueError(f"Incomplete acts: {counts}")
    if [c["value"] for c in cues if c["kind"] == "image"] != [1, 2, 3, 4, 5]:
        raise ValueError("CG order must remain 1–5")
    return dict(schema=1, language="zh", source_sha256=hashlib.sha256(text.encode()).hexdigest(),
                japanese_attack_reference="返して……返してよ……！\n私の人生……返せよ……！！\n返せよおお！！！！",
                cues=cues)


def asset_manifest():
    from PIL import Image
    images = []
    for number in range(1, 6):
        path = ASSETS / f"cg_{number:02}.png"
        with Image.open(path) as image:
            image.verify()
        with Image.open(path) as image:
            size = image.size
        images.append(dict(file=path.name, width=size[0], height=size[1],
                           sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    return dict(schema=1, source="作者提供 C:/Users/sansm/Downloads/CG/1.png 至 5.png",
                treatment="原字节保留；无重绘、裁剪或放大", images=images,
                sound_sources={
                    "ambience": "原生 StoreSoundPlayer.storeAmbient（通风声暂用当铺环境声）",
                    "knock": "原生 EmporiumAudioPlayer.ratchet（敲金属门占位，三次）",
                    "door_open": "原生 EmporiumAudioPlayer.airlock_ext_open",
                    "impact": "原生 EmporiumAudioPlayer.ratchet2（撞击占位）",
                    "transaction": "原生 EmporiumAudioPlayer.audioRegister（实际交易成功声）",
                    "lock": "原生 EmporiumAudioPlayer.airlock_ext_close（锁门占位）",
                    "tape": "原生 EmporiumAudioPlayer.audioClickClick（机械启动占位）"})


def encoded(value):
    return json.dumps(value, ensure_ascii=False, indent=2) + "\n"


def prepare(check=False):
    for path, value in ((MODULE / "script.json", compile_script()), (ASSETS / "assets.json", asset_manifest())):
        content = encoded(value)
        if check:
            if not path.is_file() or path.read_text(encoding="utf-8") != content:
                raise ValueError(f"Stale derived input: {path.relative_to(ROOT)}")
        else:
            path.write_text(content, encoding="utf-8")


def stage_package(stage):
    import xml.etree.ElementTree as ET
    stage = stage.resolve()
    stage.relative_to((ROOT / "release").resolve())
    if stage.name != "XiaomanBuybackTest":
        raise ValueError("Wrong independent test directory")
    stage.mkdir(parents=True, exist_ok=True)
    mod = ET.Element("Mod")
    for key, value in (("ID", ID), ("Name", "小满支线：永久回购（独立演出测试）"),
                       ("Author", "sansm"), ("ModVersion", VERSION),
                       ("Description", "独立五幕CG演出测试。关闭主模组及抬枪测试后启用，第一天关店后点击睡觉进入。中文原稿字幕，暂无新配音；正式交易触发尚未接入。Esc退出测试，结尾可重播或返回主菜单。不结算、不修改主线进度、不写存档。")):
        ET.SubElement(mod, key).text = value
    ET.SubElement(ET.SubElement(mod, "GameVersions"), "Version").text = "0.46D"
    ET.SubElement(ET.SubElement(mod, "Assemblies"), "Assembly").text = "XiaomanBuybackTest.dll"
    ET.SubElement(mod, "Capabilities")
    ET.indent(mod, space="  ")
    ET.ElementTree(mod).write(stage / "manifest.xml", encoding="utf-8", xml_declaration=True)
    scenes = stage / "Scenes"
    scenes.mkdir(exist_ok=True)
    for image in asset_manifest()["images"]:
        shutil.copyfile(ASSETS / image["file"], scenes / image["file"])
    shutil.copyfile(MODULE / "script.json", scenes / "script.json")
    shutil.copyfile(ASSETS / "assets.json", scenes / "assets.json")
    shutil.copyfile(MODULE / "测试说明.txt", stage / "测试说明.txt")
    # Reuse an existing approved preview; the supplied CG originals stay untouched.
    shutil.copyfile(ROOT / "package/XiaomanVisitor/preview.png", stage / "preview.png")
    licenses = stage / "Licenses"
    licenses.mkdir(exist_ok=True)
    (licenses / "素材来源.txt").write_text(
        "五张CG由作者提供，原文件1.png至5.png，来源与哈希见Scenes/assets.json。\n"
        "角色小满来自《雨音に満ちる夜 / A Night Filled With the Sound of Rain》。\n"
        "本实验不分发原生游戏程序集或原生音效；运行时引用已安装游戏的音源。\n", encoding="utf-8-sig")


def archive(stage):
    import zipfile
    stage = stage.resolve()
    stage.relative_to((ROOT / "release").resolve())
    destination = stage.parent / f"XiaomanBuybackTest-{VERSION}.zip"
    files = sorted(p for p in stage.rglob("*") if p.is_file())
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as out:
        for path in files:
            out.write(path, stage.name + "/" + path.relative_to(stage).as_posix())
    with zipfile.ZipFile(destination) as out:
        if out.testzip() is not None:
            raise ValueError("Corrupt ZIP")
        for path in files:
            if out.read(stage.name + "/" + path.relative_to(stage).as_posix()) != path.read_bytes():
                raise ValueError("ZIP differs from checked package")
    report = dict(mod_id=ID, version=VERSION, sha256=hashlib.sha256(destination.read_bytes()).hexdigest(),
                  gameplay_tested=False, bytes=destination.stat().st_size,
                  files={p.relative_to(stage).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files})
    (stage.parent / "test-package.json").write_text(encoded(report), encoding="utf-8")
    print(f"ZIP: {destination} ({report['bytes']} bytes)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", action="version", version=VERSION)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--stage", type=Path)
    parser.add_argument("--zip", action="store_true")
    args = parser.parse_args()
    prepare(args.check)
    if args.stage:
        if args.zip:
            archive(args.stage)
        else:
            stage_package(args.stage)
    print("PASS: author lines, five CGs and deterministic offline timeline")


if __name__ == "__main__":
    main()
