# 深空小满：可能很迷醉？

《Probably Stolen / 深空当铺》的小满角色模组。[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3835383562)

**开发与需求的统一入口：[项目手册](docs/项目手册.md)。** 当前版本只以 [运行清单](package/XiaomanVisitor/manifest.xml) 为准。`main` 保存工坊发布基线，已发布版本以 `v*` 标签留档；开发分支为 `develop/tavern`。已验证的立绘方案保存在 `baseline/native-render-0.0.3.3`。

```powershell
# 首次准备：需要自己的游戏安装、Git LFS、uv 与 .NET 8 运行时
& .\tools\setup-dev.ps1 -GameRoot 'E:\app\steam\steamapps\common\Probably Stolen'

# 日常代码检查：编译、原生兼容、关键规则和离线配音工具
& .\tools\verify-dev.ps1

# 改素材、文本或准备发布时：增加全部素材与录音校验
& .\tools\verify-dev.ps1 -Full
```

检查不会安装、调用付费配音或上传工坊。安装和发布是独立操作，见项目手册。

- [实机验收](docs/实机验收.md)：作者验证画面、进出场和交互。
- [渲染复盘与性能取舍](docs/渲染问题复盘与性能取舍.md)：已确认的原因、日志证据和测量边界。
- [历史资料索引](docs/history/README.md)：旧规则、旧记录和一次性脚本的位置，不能作为当前需求。

源码在 `src/Domain`、`src/Runtime`、`src/Generated`；测试在 `tests`；定稿输入在 `story` 与 `assets`；可打包资源在 `package/XiaomanVisitor`。报告、工具缓存、备份与发布 ZIP 放在不入库的 `.dev` / `release`。音频、图片和字体使用 Git LFS。

素材来自《雨音に満ちる夜 / A Night Filled With the Sound of Rain》；字体许可随运行包保留。本仓库未为原作素材授予新的开源许可。
