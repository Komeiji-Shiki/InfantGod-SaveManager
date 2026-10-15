# 幼神·多存档管理 v1.0.3：修复安装脚本乱码

修复在 Windows 自带 PowerShell 5.1 中运行安装命令时，中文乱码并伴随“意外的标记”、缺少引号等语法错误的问题。此前的脚本为不带 BOM 的 UTF-8，会被旧版 PowerShell 按系统 ANSI 编码读取。

安装、卸载、公共函数和构建脚本均已改为 UTF-8 BOM，并为后续编辑加入编码约定。只修改安装入口文件不够，安装器还会读取公共函数脚本。

**下载 `InfantGod-SaveManager-v1.0.3.zip`**，解压到新目录，关闭游戏，在解压目录打开 PowerShell，执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Mod.ps1 -GamePath 'B:\Steam\steamapps\common\幼神 Demo\Build'
```

将 `GamePath` 改成你自己的 `Aistalt.exe` 所在目录，保留路径两侧的引号。无需手动转换编码，也无需升级 PowerShell。

本次仅修复安装包，游戏内插件仍为 1.0.2。已经成功安装并正常使用的玩家无需更新；此前因语法错误未能安装的玩家直接使用新安装包即可。

`InfantGod-SaveManager-v1.0.3-source.zip` 是配套源码，不是可直接安装的包。卸载和存档保留规则见 README。

本次定向验证使用 Windows PowerShell 5.1，复现旧包脚本解析失败，并检查修正后的源码及最终 ZIP 内脚本。未修改游戏功能或重新编译插件。
