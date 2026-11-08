"""Build a read-only Xiaoman / vanilla Eleanor writing reference from local game files."""
from pathlib import Path
import csv
import hashlib
import io
import json
import re
import shutil
import sys
import zipfile

import UnityPy

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r"E:\app\steam\steamapps\common\Probably Stolen")
OUT = ROOT / "story/剧情参考_小满与埃莉诺"
EL = OUT / "02_埃莉诺_原版"
CODE = EL / "原版逻辑摘录"
BUNDLES = GAME / "Probably Stolen_Data/StreamingAssets/aa/StandaloneWindows64"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def closing_brace(text, opening):
    depth, i, state = 0, opening, "code"
    while i < len(text):
        c = text[i]
        following = text[i + 1] if i + 1 < len(text) else ""
        if state == "line":
            if c == "\n": state = "code"
        elif state == "comment":
            if c == "*" and following == "/": state, i = "code", i + 1
        elif state in ("string", "char"):
            if c == "\\": i += 1
            elif c == ('"' if state == "string" else "'"): state = "code"
        elif state == "verbatim":
            if c == '"':
                if following == '"': i += 1
                else: state = "code"
        elif c == "/" and following == "/": state, i = "line", i + 1
        elif c == "/" and following == "*": state, i = "comment", i + 1
        elif c == '"': state = "verbatim" if i > 0 and text[i - 1] == "@" else "string"
        elif c == "'": state = "char"
        elif c == "{": depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0: return i + 1
        i += 1
    raise ValueError("Unclosed C# method")


def selected_methods(type_name):
    source = ROOT / f"reference/{type_name}.current.cs"
    text = source.read_text(encoding="utf-8-sig")
    pattern = re.compile(r"(?m)^\s*(?:public|private|internal|protected)\s+(?:static\s+)?[^\n;={}]+?\b(\w+)\([^\n]*\)\s*\n\s*\{")
    result = []
    for match in pattern.finditer(text):
        name = match.group(1)
        if "eleanor" not in name.lower(): continue
        opening = text.find("{", match.start(), match.end())
        end = closing_brace(text, opening)
        start = match.start()
        while start < len(text) and text[start] in "\r\n": start += 1
        result.append({"class": type_name, "method": name,
                       "line": text.count("\n", 0, start) + 1, "code": text[start:end]})
    declarations = [line for line in text.splitlines() if re.match(r"\s*public (?:const )?(?:int|bool|string)\s+", line)
                    and re.search(r"eleanor|nutrifruitSeedsFreed", line, re.I) and "(" not in line]
    return result, declarations


def localisation():
    shared_path = BUNDLES / "localization-assets-shared_assets_all.bundle"
    locale_path = BUNDLES / "localization-string-tables-chinese(simplified)(zh)_assets_all.bundle"
    shared = {}
    env = UnityPy.load(str(shared_path))
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour": continue
        tree = obj.read_typetree()
        if "m_Entries" in tree and "m_TableCollectionName" in tree:
            shared[tree["m_TableCollectionName"]] = {row["m_Id"]: row["m_Key"] for row in tree["m_Entries"]}
    rows = []
    env = UnityPy.load(str(locale_path))
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour": continue
        tree = obj.read_typetree()
        if "m_TableData" not in tree: continue
        table = obj.peek_name().removesuffix("_zh")
        if table not in shared: continue
        for row in tree["m_TableData"]:
            key = shared[table].get(row["m_Id"])
            if key: rows.append({"table": table, "key": key, "text": row.get("m_Localized", "")})
    return rows, [shared_path, locale_path]


def speaker(key):
    if "_choice_" in key: return "玩家选项"
    if key.endswith("_player"): return "店长（PLAYER）"
    if "_arrest_sec_" in key or "_bail_" in key: return "治安人员"
    if "_uncle_" in key: return "自称叔叔的男子"
    if "_threat_" in key: return "黑市打手"
    return "埃莉诺"


def numbered(prefix, count):
    return [f"{prefix}{i}" for i in range(1, count + 1)]


def groups():
    return [
        ("01 初次来访：食物与上层区身份", numbered("dialog_eleanor_intro_", 7)),
        ("02 第二次来访：滞留下层区与日用品", numbered("dialog_eleanor2_", 9)),
        ("03 第三次来访：变卖随身物品", numbered("dialog_eleanor3_", 3) + ["dialog_eleanor3_no_family"] + [f"dialog_eleanor3_{i}" for i in range(4, 9)]),
        ("04 自称叔叔的男子：交付补给箱", numbered("dialog_eleanor_uncle_", 7) + ["dialog_eleanor_uncle_handed"]),
        ("05 揭穿并举报男子：治安对话", ["dialog_eleanor_uncle_arrest_sec_1", "dialog_eleanor_uncle_arrest_client_1", "dialog_eleanor_uncle_arrest_sec_2", "dialog_eleanor_uncle_arrest_client_2", "dialog_eleanor_uncle_arrest_sec_3"]),
        ("06 警告来访：识破骗局后的致谢", ["dialog_eleanor_warned_player"] + numbered("dialog_eleanor_warned_", 5) + ["dialog_eleanor_warned_leave"]),
        ("07 第四次主线来访：困境与援助", numbered("dialog_eleanor4_", 9) + ["dialog_eleanor4_choice_credits", "dialog_eleanor4_accept"]),
        ("08 第五次主线来访：拾荒与重新谋生", numbered("dialog_eleanor5_", 11)),
        ("09 补给箱栽赃：埃莉诺被捕", ["dialog_eleanor_arrest_sec_1", "dialog_eleanor_arrest_client_1", "dialog_eleanor_arrest_sec_2", "dialog_eleanor_arrest_client_2"]),
        ("10 治安员保释分支", numbered("dialog_eleanor_bail_", 3) + ["dialog_eleanor_bail_choice_pay", "dialog_eleanor_bail_paid", "dialog_eleanor_bail_choice_vouch", "dialog_eleanor_bail_vouched", "dialog_eleanor_bail_declined"]),
        ("11 获释后的来访", numbered("dialog_eleanor_freed_", 4)),
        ("12 第六次主线来访：筹钱请侦探", numbered("dialog_eleanor6_", 6)),
        ("13 可重复拾荒来访及交易反馈", numbered("dialog_eleanor_scav_", 3) + ["dialog_eleanor6_to_go", "dialog_eleanor_scav_all_done", "dialog_eleanor_scav_enough_1", "dialog_eleanor_scav_enough_2"]),
        ("14 第七次主线来访：侦探与数据盘", numbered("dialog_eleanor7_", 13)),
        ("15 黑市打手的威胁与交易条件", numbered("dialog_eleanor_threat_", 6)),
        ("16 第八次主线来访：归还或公开数据盘", ["dialog_eleanor8_player"] + numbered("dialog_eleanor8_", 4) + ["dialog_eleanor8_choice_return", "dialog_eleanor8_return_1", "dialog_eleanor8_return_2", "dialog_eleanor8_choice_leak", "dialog_eleanor8_leak_1", "dialog_eleanor8_leak_2", "dialog_eleanor8_leak_3"]),
        ("17 结局A：归还数据盘后的重聚", numbered("dialog_eleanor9_free_", 3) + ["dialog_eleanor9_free_gift", "dialog_eleanor9_free_gift_2", "dialog_eleanor9_free_bye"]),
        ("18 结局B：公开数据盘后的革命军路线", numbered("dialog_eleanor9_rev_", 3) + ["dialog_eleanor9_rev_gift", "dialog_eleanor9_rev_gun", "dialog_eleanor9_rev_bye"]),
        ("19 两种结局共用的感谢和告别", ["dialog_eleanor9_thanks"] + numbered("dialog_eleanor9_farewell_", 3)),
        ("20 掏枪时的通用回应", ["dialog_eleanor_gun_drawn"]),
    ]


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    CODE.mkdir(parents=True, exist_ok=True)
    methods = []
    source_files = []
    for type_name in ["StoreClientUniqueList", "StoreClientData", "StoreClientManager", "EventListUnique"]:
        selected, declarations = selected_methods(type_name)
        assert selected, type_name
        methods.extend(selected)
        content = [f"{type_name}：原版埃莉诺相关逻辑摘录", "", "来自当前本机Assembly-CSharp.dll的只读反编译。", "只供阅读，省略了其他成员与上下文，不是可独立编译的模组源码。", ""]
        content += declarations + [""]
        for method in selected:
            content += [f"// 原始反编译行号 {method['line']}，{method['method']}", method["code"], ""]
        (CODE / f"{type_name}_Eleanor.cs.txt").write_text("\n".join(content), encoding="utf-8")
        source_files.append(ROOT / f"reference/{type_name}.current.cs")
    schedule = ROOT / "reference/StorylineSchedule.current.cs"
    text = schedule.read_text(encoding="utf-8-sig").split("\nYou are not using the latest version")[0]
    (CODE / "StorylineSchedule.cs.txt").write_text(text, encoding="utf-8")
    source_files.append(schedule)

    all_rows, localisation_sources = localisation()
    references = {}
    for method in methods:
        for key in dict.fromkeys(re.findall(r'"((?:dialog|ui|name|mech|event)_[^"]+)"', method["code"])):
            references.setdefault(key, []).append(method["class"] + "." + method["method"])
    original = [row for row in all_rows if row["table"] == "Dialogue" and "eleanor" in row["key"].lower()]
    assert len(original) >= 140
    for row in original:
        row["speaker_hint"] = speaker(row["key"])
        row["source_methods"] = references.get(row["key"], [])
    lookup = {row["key"]: row for row in original}
    assert len(lookup) == len(original)

    ordered, seen = [], set()
    readable = ["埃莉诺原版对话全文（简体中文）", "", "按节点与分支整理；各分支并列，不表示一周目会按本文顺序全部播放。", "文本本身、富文本标签与{0}等占位符保持原样；说话人是剧情角色提示，界面显示名见关联文本。", "保释时的拒绝选项复用其他角色命名的通用文本，另列在关联文本文件中。", ""]
    for title, keys in groups():
        readable.extend(["=" * 64, title, "=" * 64, ""])
        for key in keys:
            assert key in lookup, key
            assert key not in seen, key
            seen.add(key)
            row = lookup[key]
            row["group"] = title
            ordered.append(row)
            readable += [f"[{key}] {row['speaker_hint']}", row["text"], ""]
    remaining = [row for row in original if row["key"] not in seen]
    if remaining:
        readable += ["其他本地化条目（需要结合调用处判断是否使用）", ""]
        for row in remaining:
            row["group"] = "其他条目"
            ordered.append(row)
            readable += [f"[{row['key']}] {row['speaker_hint']}", row["text"], ""]
    assert len(ordered) == len(original)
    (EL / "02_按节点对话全文.txt").write_text("\n".join(readable), encoding="utf-8")
    (EL / "05_全部对话_原始.json").write_text(json.dumps(ordered, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    with (EL / "04_全部对话_原始.csv").open("w", encoding="utf-8-sig", newline="") as file:
        writer = csv.writer(file)
        writer.writerow(["节点", "文本ID", "说话人提示", "中文原文", "原版调用函数"])
        for row in ordered: writer.writerow([row["group"], row["key"], row["speaker_hint"], row["text"], "; ".join(row["source_methods"])])
    extras = [row for row in all_rows if ("eleanor" in row["key"].lower() or row["key"] in references)
              and not (row["table"] == "Dialogue" and row["key"] in lookup)]
    extra_text = ["埃莉诺原版关联选项、角色名、通知和事件文字", "", "有些选项复用了非eleanor命名的文本，仍由原版埃莉诺函数引用；这是原版复用关系。", ""]
    for row in extras:
        extra_text += [f"[{row['table']} / {row['key']}]", row["text"], ""]
    (EL / "06_关联选项与通知.txt").write_text("\n".join(extra_text), encoding="utf-8")
    (EL / "07_关联文本_原始.json").write_text(json.dumps(extras, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    xiao = OUT / "01_小满"
    xiao.mkdir(exist_ok=True)
    for name in ["小满_现有对话_0.4.2.txt", "小满_剧情节点模板.txt"]:
        shutil.copyfile(ROOT / "story" / name, xiao / name)
    provenance = {"date": "2026-11-07", "scope": "local vanilla Probably Stolen, no subscribed mod dialogue",
                  "xiaoman_version": "0.4.2", "eleanor_dialogue_count": len(original),
                  "related_text_count": len(extras), "source_method_count": len(methods),
                  "sources": [{"path": str(path), "sha256": sha(path)} for path in
                              [GAME / "Probably Stolen_Data/Managed/Assembly-CSharp.dll", *localisation_sources, *source_files]]}
    (OUT / "来源与版本.json").write_text(json.dumps(provenance, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Exported {len(original)} vanilla Eleanor dialogue entries, {len(extras)} related texts and {len(methods)} source methods.")
    print("Unclassified dialogue entries:", len(remaining))
    print("Dialogues without a direct method reference:", [row["key"] for row in original if not row["source_methods"]])
    print("Reference directory:", OUT)

    required = [OUT / "00_阅读说明.txt", EL / "01_剧情流程与分支.txt", EL / "03_节点写作参考.txt"]
    assert all(path.is_file() for path in required)
    assert all(row["text"] and row["source_methods"] for row in ordered)
    assert len({row["key"] for row in ordered}) == len(ordered)
    with (EL / "04_全部对话_原始.csv").open(encoding="utf-8-sig", newline="") as file:
        exported = list(csv.DictReader(file))
    assert {row["文本ID"]: row["中文原文"] for row in exported} == {row["key"]: row["text"] for row in ordered}
    readable_text = (EL / "02_按节点对话全文.txt").read_text(encoding="utf-8")
    assert all(f"[{row['key']}]" in readable_text and row["text"] in readable_text for row in ordered)
    inventory = [{"file": path.relative_to(OUT).as_posix(), "bytes": path.stat().st_size, "sha256": sha(path)}
                 for path in sorted(OUT.rglob("*")) if path.is_file() and path.name != "文件校验.json"]
    (OUT / "文件校验.json").write_text(json.dumps(inventory, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    archive = ROOT / "story/剧情参考_小满与埃莉诺_2026-11-07.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
        for path in sorted(OUT.rglob("*")):
            if path.is_file(): zipped.write(path, Path(OUT.name) / path.relative_to(OUT))
    with zipfile.ZipFile(archive) as zipped:
        assert zipped.testzip() is None
        assert all(not name.startswith("/") and ".." not in Path(name).parts for name in zipped.namelist())
        assert not any(name.lower().endswith((".dll", ".exe", ".es3")) for name in zipped.namelist())
        for row in inventory:
            data = zipped.read(OUT.name + "/" + row["file"])
            assert hashlib.sha256(data).hexdigest() == row["sha256"], row["file"]
        print("ZIP verified:", len(zipped.namelist()), "files,", round(archive.stat().st_size / 1024, 1), "KiB")
    print(archive)


if __name__ == "__main__":
    main()
