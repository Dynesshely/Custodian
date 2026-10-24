# Custodian — Agent 指引

面向在本仓库工作的 AI agent。只记录「不看代码就会踩坑」的约定和流程，代码风格与架构见 `README.md`。

## 发版流程（用户说「发版 / 升级版本 / 走发版流程」时执行）

1. **改版本号**（必须同步的三处）：
   - `Directory.Build.props` → `<Version>x.y.z</Version>`
   - `installer/custodian.nsi` → 默认 `!define APP_VERSION "x.y.z"`（CI 会用 tag 覆盖，但本地打包依赖它）
   - `README.md` 若出现具体版本号则同步（目前是 `<version>` 占位，一般无需改）
2. **补 NSIS 脚本的 UTF-8 BOM**：编辑工具写回会吞掉 BOM，`makensis` 会报 `Bad text encoding` / 中文变成乱码。
   用 `[System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding($true)))` 写回。
3. **提交**：`chore(release): vX.Y.Z`（本仓库提交信息用中文）。
4. **打 tag 并推送**：`git tag -a vX.Y.Z -m "..."` + `git push origin main --tags`。
   推送 tag 会触发 `.github/workflows/release.yml`：自动 publish 自包含单文件、makensis 打包、
   校验版本号、生成 SHA256、创建/更新 GitHub Release。
5. **本地也出一份安装包**（先停掉正在运行的 Custodian，它锁着 `bin/Release` 下的 dll）：
   ```powershell
   dotnet publish src/Custodian.App/Custodian.App.csproj -c Release -r win-x64 --self-contained true `
     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
     -o artifacts/publish
   makensis installer/custodian.nsi
   ```
6. **静默安装**：`artifacts/Custodian-x.y.z-Setup.exe /S`（退出码 0）。
   静默安装**刻意不创建桌面快捷方式**，桌面快捷方式数量应为 0。
7. **启动已安装实例**：`%LOCALAPPDATA%\Programs\Custodian\Custodian.exe`（正式档案，配置在 `~/.custodian`）。
8. **核对并汇报**：安装版 FileVersion、卸载项 `DisplayVersion`、桌面快捷方式 0 个、CI run 是否 success、
   Release 附件（安装包 + `custodian-x.y.z-setup.sha256`）是否齐全。

## 版本号语义（按用户措辞）

- 「patch」→ x.y.(z+1)；「minor」→ x.(y+1).0；「major」→ (x+1).0.0。

## 开发实例与配置隔离

- `tools/run-dev.ps1` 构建 Release 并用 `--debug` 启动。
- 带 `--debug`（或 `CUSTODIAN_DEBUG=1`）的实例是**开发档案**：读 `~/.custodian/settings.json`，
  写 `%TEMP%\custodian-dev\settings.json`，绝不修改正式配置（`StoragePaths`）。
- 自动化验证只走应用内调试通道 `tools/custodian-debug.ps1 "<命令>"`；**禁止**系统级键鼠注入。

## 已知坑

- 编辑 `installer/custodian.nsi` 后必须补回 UTF-8 BOM（见步骤 2）。
- `dotnet publish` / `dotnet build -c Release` 之前要停掉运行中的实例，否则 MSB3026/MSB3027 文件占用失败。
- UI 回归用调试命令：`anim`（动画状态/采样）、`focus`（谁拿了键盘焦点）、`settings`（设置与存储路径）、
  `backdrop`（毛玻璃快照状态）；截图用 `snapshot` / `anim introstill` / `anim ringstill` / `settingshot`。
- 主窗口所有子控件都是 `Focusable=False`（纯方向键模型）；新加控件时不要让它抢键盘焦点，否则动画期间焦点框会错位。
