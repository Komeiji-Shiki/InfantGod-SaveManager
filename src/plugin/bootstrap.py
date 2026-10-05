"""准备独立项目所需的完整 BepInEx 5 Windows x64 加载器。"""
from pathlib import Path
import urllib.request
import zipfile

root = Path(__file__).resolve().parent / 'deps'
root.mkdir(parents=True, exist_ok=True)
(root / 'feed').mkdir(exist_ok=True)
archive = root / 'BepInEx_win_x64_5.4.23.5.zip'
if not archive.is_file():
    temporary = archive.with_suffix('.download')
    urllib.request.urlretrieve('https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip', temporary)
    if not zipfile.is_zipfile(temporary):
        raise SystemExit('下载结果不是有效的 BepInEx 安装包。')
    temporary.replace(archive)

target = root / 'bepinex'
required = ['BepInEx/core/BepInEx.dll', 'BepInEx/core/0Harmony.dll', 'BepInEx/core/MonoMod.RuntimeDetour.dll', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version']
if not all((target / name).is_file() for name in required):
    # 编译时可能只有两份引用程序集；打包必须补齐正式加载器中的全部文件。
    target.mkdir(exist_ok=True)
    with zipfile.ZipFile(archive) as package:
        for entry in package.infolist():
            if not (target / entry.filename).resolve().is_relative_to(target.resolve()):
                raise SystemExit('加载器压缩包中出现了无效路径。')
        package.extractall(target)
if not all((target / name).is_file() for name in required):
    raise SystemExit('BepInEx 加载器内容不完整。')
print('独立项目的 BepInEx 5 编译与打包依赖已准备完成。')
