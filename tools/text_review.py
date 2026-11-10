"""Export checked current text for writers; reports are never authoring inputs."""
import json
import re
import xml.etree.ElementTree as ET

import weekly_story_source


def instruction_outputs(root):
    """Render package documentation from the same version/story/voice inputs."""
    package = root / 'package/XiaomanVisitor'
    manifest = ET.parse(package / 'manifest.xml').getroot()
    script = json.loads((root / 'story/配音脚本_ja.json').read_text(encoding='utf-8'))
    snapshot = json.loads((root / 'assets/story.json').read_text(encoding='utf-8'))
    through = max(weekly_story_source.SOURCES)
    bound = sum(bool(row.get('audio')) for row in script['lines'])
    actions = snapshot['actions']
    sfx = sum(a['kind'] == 'sfx' for a in actions)
    values = dict(mod_name=manifest.findtext('Name'), version=manifest.findtext('ModVersion'),
                  revised_through=through,
                  legacy_chapters='、'.join(str(e['index']) for e in snapshot['episodes'] if e['index'] > through),
                  voice_bound=bound, voice_deferred=len(script['lines'])-bound,
                  action_slots=len(actions), spoken_actions=len(actions)-sfx, sfx_slots=sfx)
    templates = json.loads((root / 'assets/package-instructions.json').read_text(encoding='utf-8'))
    rendered = {}
    for name, template in templates.items():
        for key, value in values.items():
            template = template.replace('{{'+key+'}}', str(value))
        assert '{{' not in template, f'Unknown package instruction token: {name}'
        rendered[name] = template
    return rendered


def prepare_instructions(root):
    for name, content in instruction_outputs(root).items():
        (root / 'package/XiaomanVisitor' / name).write_text(content, encoding='utf-8')


def write(root, texts, story, notes):
    languages = ('zh', 'ja', 'en')
    package = root / 'package/XiaomanVisitor'
    for name, expected in instruction_outputs(root).items():
        assert (package / name).read_text(encoding='utf-8') == expected, f'Stale {name}; run tools/prepare_story.py'
    runtime = '\n'.join(p.read_text(encoding='utf-8-sig') for p in (root / 'src/Runtime').glob('*.cs'))
    literal_ui = {k for k in re.findall(r'"((?:ui_|cfg_)\w+)"', runtime) if not k.endswith('_')}
    missing = sorted(literal_ui - texts['zh'].keys())
    assert not missing, f'Runtime UI keys missing from localization: {missing}'
    script = json.loads((root / 'story/配音脚本_ja.json').read_text(encoding='utf-8'))
    voices = {row['key']: row for row in script['lines']}
    lines = {row['key']: row for row in story['lines']}

    def speech(key):
        row = lines[key]
        voice = voices.get(row.get('voice_alias', key), {})
        return dict(key=key, mood=row['mood'], text={lang: texts[lang][key] for lang in languages},
                    voice='recording_bound' if voice.get('audio') else 'deferred',
                    voice_alias=row.get('voice_alias'))

    speeches = {key: speech(key) for key, row in lines.items()
                if not row.get('reference') and row['kind'] == 'speech'}
    pending = sum(not bool(row.get('audio')) for row in voices.values())
    through = max(weekly_story_source.SOURCES)
    chapters = [dict(chapter, revision='current' if chapter['index'] <= through else 'legacy_v3.4')
                for chapter in story['episodes']]
    letters = []
    for note in notes:
        base = f'story_note_{note["visit"]:02d}'
        for key in (base, base+'_b'):
            if key+'_title' not in texts['zh']:
                continue
            letters.append(dict(key=key, source=note.get('source'), text={lang: {
                field: texts[lang][key+'_'+field] for field in ('title', 'body', 'signature')
            } for lang in languages}))
    ui_keys = [k for k in texts['zh'] if k.startswith(('ui_', 'cfg_', 'cocktail_', 'collection_', 'gift_', 'drink_'))]
    news_keys = [k for k in texts['zh'] if re.fullmatch(r'xm_w\d+_news_(?:title|body)', k)]
    report = dict(version=ET.parse(package / 'manifest.xml').getroot().findtext('ModVersion'),
                  editable=False, revised_through=through, localization_keys=len(texts['zh']),
                  runtime_literal_ui_keys=len(literal_ui), missing_runtime_ui_keys=missing,
                  voice=dict(bound=len(voices)-pending, deferred=pending), chapters=chapters,
                  speech=list(speeches.values()), letters=letters,
                  interface={k: {lang: texts[lang][k] for lang in languages} for k in ui_keys+news_keys})
    pending_chapters = [str(e['index']) for e in chapters if e['revision'] != 'current']
    warnings = []
    if pending_chapters:
        warnings.append('第'+ '、'.join(pending_chapters)+'章仍为v3.4旧稿，不代表新版酒馆剧情已定稿。')
    if pending:
        warnings.append(f'{pending}条日语口头稿明确待审/待录；本报告不生成配音。')
    report['remaining_work'] = warnings

    readable = [f'当前文本审校 · {report["version"]}', '由完整校验生成；修改文案请回到项目手册列出的定稿输入，勿编辑本报告。',
                f'三语键：{len(texts["zh"])}；已绑定录音：{len(voices)-pending}；待录：{pending}。', *warnings, '']
    chapter_keys = set()
    for chapter in chapters:
        n = chapter['index']
        state = '当前重构稿' if chapter['revision'] == 'current' else '旧稿待重构'
        readable += [f'第{n:02d}次｜{chapter["title"]}｜{state}',
                     f'{chapter["credits"]} CR · {chapter["demand"]} · {chapter["track"]}']
        keys = chapter['opening']+[chapter['accept'], chapter['goodbye']]
        if chapter['cocktail']:
            keys.append(chapter['cocktail'])
        for key in keys:
            chapter_keys.add(key)
            row = speeches[key]
            readable.append(f'{key} · {row["mood"]} · {row["voice"]}\n{row["text"]["zh"]}')
        for letter in letters:
            if letter['key'] in (f'story_note_{n:02d}', f'story_note_{n:02d}_b'):
                readable += ['纸条｜'+letter['key'], '\n\n'.join(letter['text']['zh'][f] for f in ('title', 'body', 'signature'))]
        readable.append('')
    readable.append('其余口头台词与差分｜按ID并列，不代表同一次会按此顺序全部播放')
    for key, row in speeches.items():
        if key not in chapter_keys:
            readable.append(f'{key} · {row["mood"]} · {row["voice"]}\n{row["text"]["zh"]}')
    readable += ['', '界面、物品与新闻｜含动态占位符的内容在游戏中按实际数值填入']
    readable += [f'{key}\n{texts["zh"][key]}' for key in ui_keys+news_keys]
    out = root / '.dev/reports'
    out.mkdir(parents=True, exist_ok=True)
    (out / 'text-review.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    (out / '当前文本审校.txt').write_text('\n\n'.join(readable)+'\n', encoding='utf-8')
    print(f'PASS: {len(literal_ui)} runtime UI keys resolve; text review saved to .dev/reports/当前文本审校.txt.')
