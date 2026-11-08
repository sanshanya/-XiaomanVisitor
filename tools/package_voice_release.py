"""Prepare an offline native-mod release; never logs into Steam or uploads."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
import voice_pipeline as pipeline


def digest(file):
    return hashlib.sha256(file.read_bytes()).hexdigest()


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--with-audio", action="store_true", help="Also export a separately playable voice ZIP.")
    parser.add_argument("--skip-installed-check", action="store_true", help="Prepare files while the running game still holds the installed DLL.")
    args = parser.parse_args()
    root, package = pipeline.ROOT, pipeline.PACKAGE
    manifest = ET.parse(package / "manifest.xml").getroot()
    version = manifest.findtext("ModVersion")
    assert re.fullmatch(r"[0-9]+(?:\.[0-9]+)*", version), "Invalid release version"
    assert manifest.findtext("ID") == "sansm.xiaoman.visitor", "Keep the save-data namespace stable"
    assert manifest.find("Capabilities") is not None and not list(manifest.find("Capabilities"))
    script = pipeline.load(pipeline.DEFAULT_SCRIPT)
    report = pipeline.load(root / f"story/配音音频检查_{version}.json")
    pack = pipeline.load(package / "Voices/index.json")
    assert pack["pending_lines"] == 0 and len(pack["lines"]) == len(script["lines"]) == report["recorded_lines"]
    assert report["version"] == version and not report["warnings"]
    output = root / "release" / version
    page_path = output / "工坊页面.json"
    page = pipeline.load(page_path) if page_path.exists() else {}
    confirmed = page.get("status") == "confirmed_by_user"
    if confirmed:
        assert manifest.findtext("Name") == page["title"]
        assert manifest.findtext("Description") == page["description_plain"]
    tapes = pipeline.load(root / "assets/audio/cassette-manifest.json")
    assert len(tapes) == 14
    for tape in tapes:
        assert digest(package / tape["packaged"].replace("\\", "/")) == tape["sha256"], tape["key"]
    files = sorted(f for f in package.rglob("*") if f.is_file())
    assert len(files) < 50000 and (package / "preview.png").stat().st_size < 1024 * 1024
    allowed = {".dll", ".xml", ".png", ".csv", ".json", ".wav", ".mp3", ".ogg", ".txt"}
    listed_dlls = {e.text for e in manifest.findall("Assemblies/Assembly")}
    checksums = {}
    installed = Path(r"E:\app\steam\steamapps\common\Probably Stolen\Mods\XiaomanVisitor")
    for file in files:
        name = file.relative_to(package).as_posix()
        assert file.suffix.lower() in allowed and not any(p.startswith(".") for p in Path(name).parts), name
        assert file.suffix.lower() != ".dll" or name in listed_dlls, "Unlisted assembly: " + name
        checksums[name] = digest(file)
        if not args.skip_installed_check:
            assert digest(installed / name) == checksums[name], "Installed mismatch: " + name
    if not args.skip_installed_check:
        assert {f.relative_to(installed).as_posix() for f in installed.rglob("*") if f.is_file()} == set(checksums)

    output.mkdir(parents=True, exist_ok=True)
    archive = output / f"XiaomanVisitor-{version}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for file in files:
            z.write(file, "XiaomanVisitor/" + file.relative_to(package).as_posix())
    archives = [archive]
    if args.with_audio:
        audio_archive = output / f"小满日语配音_1.1倍速_{version}.zip"
        audio_manifest = dict(version=version, voice="Ayano", language="ja", speed=1.1,
                              seconds=report["seconds"], alias={"xm_01_01": "intro_1"}, lines=[], sfx=["SFX/xm_shutter_knock.wav"])
        by_key = {row["key"]: row for row in report["lines"]}
        index = [f"小满 v3.4 终版日语配音 / {version}", "", "Ayano；有效语速1.1倍。", ""]
        with zipfile.ZipFile(audio_archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for row in script["lines"]:
                key = row["key"]
                source = root / row["audio"]
                assert digest(source) == by_key[key]["sha256"]
                z.write(source, key + ".wav")
                audio_manifest["lines"].append(dict(key=key, file=key + ".wav", seconds=by_key[key]["seconds"],
                    sha256=by_key[key]["sha256"], subtitles=row["subtitle_templates"], spoken_ja=row["spoken_text"]))
                index.extend([f"[{key}] {by_key[key]['seconds']:.3f}s", "中文：" + row["subtitle_templates"]["zh"],
                              "日语：" + row["spoken_text"], ""])
            z.write(package / "Sounds/xm_shutter_knock.wav", "SFX/xm_shutter_knock.wav")
            z.writestr("voice_manifest.json", json.dumps(audio_manifest, ensure_ascii=False, indent=2).encode("utf-8"))
            z.writestr("录音索引.txt", "\n".join(index).encode("utf-8-sig"))
        archives.append(audio_archive)
    results = []
    for file in archives:
        with zipfile.ZipFile(file) as z:
            assert z.testzip() is None
        results.append(dict(file=file.name, bytes=file.stat().st_size, sha256=digest(file)))
    publication_path = output / "workshop-publish-state.json"
    publication = pipeline.load(publication_path) if publication_path.exists() else {}
    uploaded_id = publication.get("publishedfileid")
    uploaded_matches = publication.get("status") == "published" and publication.get("uploaded_files_sha256") == checksums
    release = dict(version=version, mod_id=manifest.findtext("ID"), game_versions=[e.text for e in manifest.findall("GameVersions/Version")],
                   status="published" if uploaded_matches else "prepared_update_not_uploaded" if uploaded_id else "prepared_not_uploaded", title=manifest.findtext("Name"),
                   title_status="confirmed_by_user" if confirmed else "pending_user_choice",
                   description_status="confirmed_by_user" if confirmed else "pending_user_choice",
                   voice_lines=len(pack["lines"]), voice_speed=1.1, installed_verified=not args.skip_installed_check,
                   text_languages=["zh", "ja", "en"], voice_languages=["ja"],
                   music_seconds=round(sum(t["seconds"] for t in tapes), 3), final_track=tapes[-1]["title"],
                   content_folder=str(package), archives=results, files=checksums)
    if uploaded_id:
        release["workshop"] = dict(publishedfileid=uploaded_id, url=publication["url"],
                                   uploaded_content_matches=uploaded_matches, visibility=publication.get("visibility"))
    gallery = []
    for entry in page.get("gallery", []):
        image = root / entry["file"]
        assert image.is_file(), image
        gallery.append(dict(entry, bytes=image.stat().st_size, sha256=digest(image)))
    if gallery:
        release["gallery"] = gallery
    pipeline.save(output / "release-manifest.json", release)
    (output / "SHA256SUMS.txt").write_text("".join(f"{a['sha256']}  {a['file']}\n" for a in results), encoding="utf-8")
    vdf = output / "workshop-item.vdf.draft"
    escape = lambda s: s.replace("\\", "\\\\").replace('"', '\\"')
    description = page["description_bbcode"].replace("\n", "") if confirmed else manifest.findtext("Description").replace("\n", " ")
    note_path = output / "workshop-update-note.txt"
    change_note = note_path.read_text(encoding="utf-8").strip() if note_path.exists() else f"{version} 首次发布"
    update_fields = dict(contentfolder=str(package), previewfile=str(package / "preview.png"),
                         title=manifest.findtext("Name"), description=description, changenote=change_note)
    if not vdf.exists():
        fields = dict(appid="4348910", publishedfileid="0", contentfolder=str(package), previewfile=str(package / "preview.png"),
                      visibility="2", title=update_fields["title"], description=description, changenote=update_fields["changenote"])
        vdf.write_text('"workshopitem"\n{\n' + "".join(f'    "{k}" "{escape(v)}"\n' for k, v in fields.items()) + "}\n", encoding="utf-8")
    else:
        # Keep any actual published item ID and visibility choice on future runs.
        body = vdf.read_text(encoding="utf-8")
        for key, value in update_fields.items():
            pattern = r'(?m)^(\s*"' + re.escape(key) + r'"\s+)"(?:\\.|[^"\\])*"[ \t]*$'
            body, count = re.subn(pattern, lambda match: match.group(1) + '"' + escape(value) + '"', body)
            assert count == 1, "Missing or repeated VDF field: " + key
        vdf.write_text(body, encoding="utf-8")
    actual_vdf = output / "workshop-item.vdf"
    if actual_vdf.exists():
        actual_vdf.write_text(vdf.read_text(encoding="utf-8"), encoding="utf-8")
    print(json.dumps({k: v for k, v in release.items() if k != "files"}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
