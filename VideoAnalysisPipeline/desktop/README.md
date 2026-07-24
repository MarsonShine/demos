# VideoAnalysisDesktop

这是 `VideoAnalysisPipeline` 的 Windows 桌面交付层。操作人员只选择输入、
输出目录并查看进度；固定的 MOD 720p 参数、Python 运行时、FFmpeg 和模型由
发布包提供。

## 给操作人员：只安装，不构建

操作人员不需要阅读本文件的构建章节，也不需要安装 Python 或运行命令。请从发布页
下载 `VideoAnalysisDesktop-Setup-x64-版本号.exe`，双击安装，然后阅读安装后的
开始菜单“用户使用手册”。安装包已经包含桌面程序、Python 运行时、FFmpeg 和模型。

维护人员应通过 GitHub Actions 的 **Build Windows installer** 工作流（推送 `v*`
标签或手动触发）生成该安装包；工作流会把最终 `.exe` 放到工作流产物，并在标签发布
时附加到 GitHub Release。

如果发布机使用 Visual Studio 将程序发布到自定义目录（例如
`F:\Publish\VideoAnalysisDesktop`），请使用
`installer\build-installer.ps1` 并传入该发布目录和**已 stage 的发布版 engine**。
这个脚本仅供发布人员使用，会在缺少 `engine.manifest.json`、模型或 Inno Setup 时停止并
给出明确提示；终端用户永远不运行它。

请不要使用 Visual Studio 的 ClickOnce 发布（其输出含 `.application` 和 `setup.exe`）。
它不会打包本产品的 engine、FFmpeg、模型和 ProgramData 权限。项目已提供
`Folder-win-x64` 发布配置，选择它会得到可由 Inno Setup 打包的自包含文件夹输出。

## Git 与运行时

`desktop/engine/` 是**生成物**，已经被根目录 `.gitignore` 忽略，不能提交。
它不是终端用户在启动时执行 `pip install` 生成的目录：发布机使用
`build/stage_engine.py` 构建并校验一个可搬运的 Python 3.12 runtime，安装器再
将它带到客户机器。这样安装和运行过程不依赖客户机器上的 Python、pip 或网络。

如果某个旧分支曾经提交过 engine，先在该分支执行一次：

```powershell
git rm -r --cached desktop/engine
```

再提交 `.gitignore` 和构建脚本；本工作区当前的 engine 尚未被 Git 跟踪。

从源码目录启动 WPF 项目时，程序可识别旧式开发虚拟环境
`desktop/engine/python/Scripts/python.exe`，但仅限同级仓库同时包含
`video_analysis_pipeline/` 和 `pipeline_config.json` 的情况。它用于本地调试，
不会降低已安装版本对发布版 manifest 的校验要求。

## 发布构建

发布机需要 .NET 10 SDK、Python 3.12、Inno Setup 6（`iscc`）以及 FFmpeg。
推荐把 CPython embeddable zip、完整 wheelhouse 与已下载模型作为离线构件保存。

```bat
desktop\build\build_all.bat --clean-engine ^
  --python-zip C:\artifacts\python-3.12.10-embed-amd64.zip ^
  --wheelhouse C:\artifacts\wheels-win_amd64-cp312 ^
  --models-dir C:\artifacts\models ^
  --ffmpeg-dir C:\artifacts\ffmpeg
```

仅在受控、联网的发布机上，才可以用 `--allow-network` 代替这些离线构件。
`--skip-models` 只能用于开发验证，不能生成安装包。

发布前脚本会依次执行 C# 测试、发布 WPF、stage engine、Python 测试、engine
self-check 和 Inno Setup 编译。安装包输出到 `desktop/installer/output/`。

## 功能冒烟测试

不需要为了验证桌面封装而执行完整的生产输入目录。选择一个与生产数据隔离的、
只包含少量代表性媒体文件的测试目录，并让该目录的任务完整结束即可。验收条件为：

- 预检成功，且应用能启动任务；
- 测试目录中的每个文件均产生成功事件和预期 MOD 输出；
- 最终显示任务成功，且可打开输出目录。

这是对桌面调用链、运行时、FFmpeg、模型与输出落盘的冒烟验证；实际生产批处理仍会
处理操作人员所选目录中的全部文件。不要用大型生产目录作为桌面端回归测试数据。

## 安装后的目录

- `{app}\app`：WPF 应用
- `{app}\engine`：只读 Python、依赖与 FFmpeg
- `%ProgramData%\Company\VideoAnalysisDesktop\models`：离线模型
- `%ProgramData%\Company\VideoAnalysisDesktop\jobs`：任务、日志、SQLite 台账
- `%ProgramData%\Company\VideoAnalysisDesktop\cache`：可再生缓存
- `%ProgramData%\Company\VideoAnalysisDesktop\config`：管理员维护的加密凭据

普通操作人员运行应用时不需要管理员权限。首次或更新 Azure OpenAI 凭据时，点击
**Admin Settings**；应用会单独启动一个经过 UAC 提升的设置窗口。密钥不会进入
命令行、普通 JSON 配置或任务数据库。
