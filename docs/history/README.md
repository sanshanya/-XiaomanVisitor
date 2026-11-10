# 历史资料索引

本目录与 `docs/design` 用于查历史。当前实现、规则与操作只看 [项目手册](../项目手册.md)。归档保留作者原文，里面的版本号、命令和功能状态不代表现在。

- `开发记录.txt`：原 README.txt。
- `rules`：早期第一周回礼、旧通用对白、剧情节点模板和旧接入说明。
- `source-edits`：旧纸条修订原文与增量补丁。当前三语正文统一在 `story/纸条正文.json`。
- `配音制作说明_旧版.txt`：旧录制过程说明。当前 CLI 以工具 --help 与项目手册为准。
- `开发历史`、`动作台词核对`、`剧情参考_小满与埃莉诺`：原有版本记录、0.7.0 动作审计与写作参考，不作为当前字幕或规则来源。
- `../design`、`story/酒馆设计`：作者的后续酒馆设计与数值候选，尚未作为运行规则接入。

七个无当前调用者的一次性脚本已从工作树移除：prepare_chapter、prepare_release_002、integrate_hat、apply_release_text、generate_voice_batch、prepare_intro_tempo、audit_action_dialogues。它们会应用旧阶段规则、固定旧版本或输出旧阶段的审计结论，不能作为今天的日常制作入口。现有 49 个动作映射由当前定稿生成，并在回归与完整校验中核对。

若确有历史研究需要，可从整理前基线查阅，例如：

```powershell
git show 'baseline/pre-architecture-0.0.3.4:tools/prepare_release_002.py'
```

被移除的历次配音检查报告都是可再生成产物，Git 历史与本机整理前 ZIP 中仍有备份。当前报告归 `.dev/reports`，每次发布单独归档，不再复制进剧情输入。

旧 `export_story_reference.py` 固定导出0.4.2的小满与埃莉诺参考，依赖的旧路径已迁移，也没有当前调用者，故从工作树移除。旧参考文档保留，脚本可从上述基线查阅。原作只读研究仍可使用 `read_narrative_refs.py`；当前小满文本审校统一使用完整校验生成的报告。
