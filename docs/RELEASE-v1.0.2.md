# 幼神·多存档管理 v1.0.2

独立的多槽、多手动存档点 Mod。主菜单“载入游戏”与游戏内开始菜单“存档/读取”提供接近全屏的存档卡片界面。

- 新增和重命名存档槽，每次手动保存生成独立存档点，保留缩略图、剧情位置、最近选项、备注及分支来源。
- 原有快速保存继续使用；“快速加载（F9）”读取当前槽最近一次成功的手动保存或快速保存。
- 修复滚动卡片时主题系统恢复旧黑底的问题，以及扩展开始菜单后退出被裁切、菜单未完整收起的问题。
- 补充保存及恢复直播观众显示、辩论血条与角色顺序所需的状态。

下载 **InfantGod-SaveManager-v1.0.2.zip**，退出游戏后解压，在安装包目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Mod.ps1 -GamePath 'B:\Steam\steamapps\common\幼神 Demo\Build'
```

卸载使用同目录的 `Uninstall-Mod.ps1`。已有存档、手动点和缩略图保留。使用第四槽及之后的新增槽时，卸载前先读取原始前三槽中的一个并退出游戏，具体说明见 README。

`InfantGod-SaveManager-v1.0.2-source.zip` 为配套源码。原创代码采用 MIT；原作派生场景标签采用 CC BY-NC-SA 4.0，原作者为 colsalley。第三方加载器保留各自许可证。
