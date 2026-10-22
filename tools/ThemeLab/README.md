# ThemeLab — Fluent 主题自测台

只做一件事：把 `src/Custodian.App/Themes/` 下**真正的**主题文件链接进来（不复制），
渲染一张 1200x800 的样例页，用眼睛和自检报告验收 Fluent 观感与契约键。

## 链接了什么

| MSBuild 项 | 来源 |
|---|---|
| `Page` | `../../src/Custodian.App/Themes/Fluent.xaml`（`Link="Themes/Fluent.xaml"`） |
| `Page` | `../../src/Custodian.App/Themes/Light.xaml` |
| `Page` | `../../src/Custodian.App/Themes/Dark.xaml` |
| `Compile` | `../../src/Custodian.App/Themes/ThemeManager.cs` |

用 `Page`（而不是 `Resource`）链接：XAML 会像在 App 项目里一样被编译成 BAML，
**构建期就能发现 XAML 错误**，`Fluent.xaml` 里 `Source="Light.xaml"` 这种相对合并也按同一套
pack URI 规则解析。这里没有引用 `Custodian.App` 项目，所以不会和其它并行开发的改动互相干扰。

## 运行

```powershell
dotnet build tools/ThemeLab/ThemeLab.csproj -v q
tools/ThemeLab/bin/Debug/net10.0-windows/ThemeLab.exe render   # 自检 + 出图（默认）
tools/ThemeLab/bin/Debug/net10.0-windows/ThemeLab.exe probe    # 只打印主题切换/滚动接线诊断
tools/ThemeLab/bin/Debug/net10.0-windows/ThemeLab.exe show      # 开窗口看真机效果：Space 切换深浅色，Esc 退出
tools/ThemeLab/bin/Debug/net10.0-windows/ThemeLab.exe tooltip   # 程序化弹出真 ToolTip 并停 4 秒（配屏幕截图验证弹层）
```

`tooltip` 模式用于验证 FluentToolTipStyle 在**真弹层**里的样子（ToolTip 是独立 Popup，
`RenderTargetBitmap` 抓不到；跑起来后可以用 `%TEMP%\custodian-tooltip-live.png` 那种屏幕截图方式抓它）。

`render` 会把 PNG 和报告写到 `%TEMP%`：

- `custodian-theme-light.png` / `custodian-theme-dark.png` —— 1200x800 全图
- `custodian-theme-{light,dark}-detail.png` —— 左栏 + 卡片区 2x
- `custodian-theme-{light,dark}-toolbar.png` —— 工具区 3x（图标、筛选框、ToolTip）
- `custodian-theme-{light,dark}-tooltip.png` —— FluentToolTipStyle 单独出图
- `custodian-theme-report.txt` —— 自检报告（退出码 0 表示全过）

## 自检项

1. `docs/CONTRACTS.md` 第 3 节的每个键都存在，且类型 / `Style.TargetType` 正确；
2. `Light.xaml` 与 `Dark.xaml` 键集合完全一致；
3. `Fluent.xaml`（含 Light/Dark）里所有 `{DynamicResource}` / `{StaticResource}` 引用都能在本字典内解析
   —— 也就是**不引用 Fallback.xaml 独有的键**；
4. 同一棵可视树深浅色来回切，元素实际画刷跟着变（证明"只换值、不重建控件"）；
   `render` 模式下会临时把 `ThemeManager.FollowSystem` 关掉，否则系统异步广播的
   WM_SETTINGCHANGE 可能中途把主题翻回系统设置，导致截图不稳定；
5. ScrollViewerStyle 内部的细滚动条接线正常（滚一下，ScrollBar.Value 跟随）。

## 一个坑（写在这里免得后来人重踩）

`RenderTargetBitmap` 直接渲染一棵**没有连上 PresentationSource** 的视觉树时，
切换主题不会刷新已经解析过的 `DynamicResource`（尤其 Style Setter 里的），看起来像主题没生效。
真实窗口里是正常的。所以样例页先放进一个"透明 + 位于屏幕外"的 `Window` 再渲染
（`Program.CreateHost`），这样离屏截图和真机行为一致。
