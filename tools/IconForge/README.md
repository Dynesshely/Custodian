# IconForge

Custodian 应用图标（Window 标题栏 / Exe / 任务栏共用）的离线生成器。
不依赖网络、不引用任何 NuGet 包，只用 BCL + WPF（`DrawingVisual` + `Geometry` + 渐变画刷 +
`RenderTargetBitmap` + `PngBitmapEncoder`）。

## 用法

```powershell
cd Custodian
dotnet build tools/IconForge/IconForge.csproj -v q

$exe = "tools/IconForge/bin/Debug/net10.0-windows/IconForge.exe"
& $exe generate     # 生成 assets/ 下的 ico + 256/1024 png，并自动自检
& $exe inspect      # 打印 ICO 条目表 + 结构校验（可传路径）
& $exe verify       # 校验 assets/custodian.svg 与光栅几何一致
& $exe preview      # 各尺寸预览 / 最近邻放大 / 深浅任务栏模拟，写到 %TEMP%\custodian-preview
```

## 产出（assets/）

| 文件 | 说明 |
|---|---|
| `custodian.ico` | 16, 20, 24, 32, 40, 48, 64（32bpp BMP 条目，含 AND mask）+ 128, 256（PNG 条目） |
| `custodian-256.png` | 与 ico 中 256 条目字节一致 |
| `custodian-1024.png` | 设计稿 |
| `custodian.svg` | 手写矢量源，几何与光栅一致（`verify` 校验） |

## 设计

- 底板：superellipse squircle（`|x|^4.6 + |y|^4.6 = 1`，16 段三次贝塞尔，偏差 < 0.04 设计单位），
  渐变 `#6A74FF → #3C47DC → #1D2472`，顶部 15% 白微高光 + 左上柔光 + 底部压暗 + 5 单位内描边（≥64px 才画）。
- 符号：白色开口圆环（缺口朝东 ±33°，读作 C = Custodian / 看守）+ 环内白色圆角方块（沙盒里的应用）。
- 尺寸适配：16px 底板满幅、符号放大 1.18 并去掉高光层（保证 2px 环宽 / 3px 方块）；
  20/24 用 4x 超采样，32/40 用 2x；≥64 直接 1:1 渲染并启用细节层。
- 符号中心固定在设计坐标 (512, 512)，因此在 16/20/24/32/40/48/64 上都落在整数像素中心。

## 不做的事

- 不改 `.slnx` / `Directory.Build.props`，不引入依赖。
- 不把预览图写进仓库（默认写 `%TEMP%`）。