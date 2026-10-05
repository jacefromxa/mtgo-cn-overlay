# MTGO CN Overlay (万智牌 MTGO 桌面悬浮翻译助手)

> 专为 **Magic: The Gathering Online (MTGO)** 打造的实时中文悬浮翻译工具。
> 继承自 [mtg-cn-browser](https://github.com/jacefromxa/mtg-cn-browser) 的完备数据字典与精美排版渲染。

---

## 🌟 特性亮点

* 🛡️ **100% 账号安全（零封号风险）**：
  纯屏幕视觉识别方案。**不读写内存、不注入 DLL、不模拟按键**，运行机制与 Snipaste / OBS 截图录屏完全相同，严格符合 Daybreak / WotC 服务条款。
* ⚡ **极速本地 OCR + 3.7 万词库模糊容错**：
  采用 Windows 硬件加速本地 OCR，结合 37,000+ 卡牌离线字典。内置 OCR 形近字纠错（如 `L1ghtn1ng B0lt` $\to$ `Lightning Bolt`）与长度分桶 Levenshtein 剪枝，多行文本分词识别率大幅提升。
* 🎴 **海量中文数据与双面卡完备支持**：
  内置 37,082 张卡牌的中文名、英文名、法术力费用、类别、完整规则换行文本与风味文字。支持双面卡（DFC）、冒险卡、连体卡。
* 🪄 **原生级视觉体验与离线 Mana 符号**：
  完美支持 `{W}`、`{U}`、`{B}`、`{R}`、`{G}`、`{T}` 等官方彩色法术力符号。字体已完全本地化打包，**100% 离线可用**，不依赖任何外部 CDN。
* 🖱️ **无焦点打断（WS_EX_NOACTIVATE）与鼠标穿透**：
  悬浮窗完全不抢夺 MTGO 焦点，不影响玩家在关键战局中按 F2 / F4 / F6 让过优先权或结算异能。
* 🖥️ **高分屏 DPI 自适应**：
  全面支持 PerMonitorV2 高 DPI 屏幕缩放换算，无论是 100%、125%、150% 还是 200% 缩放，浮窗定位均精准无偏移。
* 📌 **Windows 系统托盘集成与交互式框选校准**：
  后台静默运行于任务栏托盘，支持右键切换取词模式、一键锁定浮窗位置、调整透明度、以及全屏拖拽框选校准 MTGO 的 Card Preview 预览窗格。

---

## 🎯 三大取词运行模式

1. **Card Preview 停靠窗锚定模式（推荐）**：
   * MTGO 拥有自带的卡牌放大预览窗格（Card Preview）。只要鼠标掠过卡牌，该区域就会立即显示高清正向大图。
   * **超低能耗**：采用 64 位快速色彩哈希采样，仅当预览区画面发生变化时才调用 OCR，平均每秒 0 次冗余计算。
   * 右键托盘图标选择「📐 框选校准 Card Preview 区域...」，在屏幕上鼠标左键拖动框选 MTGO 的预览窗即可完成校准并自动保存。
2. **光标悬停动态跟随模式**：
   * 鼠标在战场卡牌上静止 150ms 自动触发屏幕取词与翻译浮窗，离开后自动隐藏。
3. **按住 Ctrl 快捷键模式**：
   * 仅在按住 `Ctrl` 键且鼠标悬停时才触发取词，彻底防止误触干扰。

---

## 🛠️ 系统托盘右键菜单

运行后程序将静默驻留在 Windows 任务栏系统托盘（右下角）：
* **🎯 取词模式**：自由切换「光标悬停取词」、「Card Preview 停靠窗锚定」、「按住 Ctrl 键取词」
* **📐 框选校准 Card Preview 区域...**：进入全屏校准蒙版，鼠标拖拽框选预览窗
* **📌 锁定浮窗当前位置**：浮窗固定在当前坐标，不再跟随鼠标跳动
* **🎨 浮窗透明度**：支持 100%、94%（推荐）、85%、75% 等档位
* **👁️ 测试显示示例卡牌**：一键预览闪电击等卡牌排版效果
* **📁 打开配置文件目录**：快速查看或备份 `config.json`
* **❌ 退出程序**：安全退出

---

## 📁 项目结构

```
mtgo-cn-overlay/
├── src/                      # C# (.NET 8) WPF 核心工程
│   ├── MTGOOverlay/
│   │   ├── Native/           # Win32 API 交互、窗口句柄追踪与系统托盘管理
│   │   ├── Capture/          # 屏幕局部截取、预览窗采样哈希与 DPI 换算
│   │   ├── Ocr/              # Windows.Media.Ocr 本地引擎与双线性超分辨率预处理
│   │   ├── Data/             # 3.7 万词库加载、OCR 形近字纠错与长度分桶 Levenshtein
│   │   ├── Models/           # 卡牌数据模型与 AppConfig 配置持久化
│   │   ├── CalibrationWindow/# Card Preview 区域交互式拖拽校准窗口
│   │   └── MainWindow.xaml   # 置顶穿透浮窗与 WebView2 渲染宿主
├── web/                      # 浮窗前端渲染层 (HTML / CSS / JS)
│   ├── index.html            # 浮窗页面容器
│   ├── overlay.css           # 浮窗磨砂玻璃样式与 CSS 变量
│   ├── render.js             # 符号转换、英文卡名 Title Case、多面排版与通信桥
│   ├── css/                  # 离线法术力字体样式表 (mana.min.css)
│   └── fonts/                # 离线 Mana 字体文件 (woff2 / woff / ttf)
├── data/
│   └── en2zhs.json.gz        # 37,082 张卡牌离线字典
├── test/
│   ├── benchmark.mjs         # 引擎性能基准测试脚本
│   └── TestRunner/           # C# 引擎单元与集成验证程序
├── docs/
│   └── ARCHITECTURE.md       # 系统详细架构设计文档
└── README.md
```

---

## 🚀 快速启动与构建

### 前置要求
* 操作系统：Windows 10 (19041+) 或 Windows 11
* 运行环境：[.NET 8.0 SDK / Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
* WebView2 运行时（Windows 10/11 通常已自带）

### 构建与启动
```powershell
# 1. 还原并构建主工程
dotnet build src/MTGOOverlay/MTGOOverlay.csproj

# 2. 运行主程序
dotnet run --project src/MTGOOverlay/MTGOOverlay.csproj

# 3. 执行 C# 引擎全量验证测试
dotnet run --project test/TestRunner/TestRunner.csproj

# 4. 执行 Node.js 基准测试
node test/benchmark.mjs
```

---

## 📊 性能指标

| 指标项 | 实测耗时 / 资源消耗 | 说明 |
| :--- | :--- | :--- |
| **字典解压与全索引加载** | ~380 ms | 包含 37,082 张完整卡牌规则与分桶索引 |
| **单次精确卡牌查询** | < 0.001 ms | $O(1)$ 哈希命中 |
| **OCR 形近字纠错查询** | < 0.05 ms | 规则映射后 $O(1)$ 纠错命中 |
| **模糊匹配（编辑距离 $\le 3$）** | ~3 - 8 ms | 长度分桶 Levenshtein 剪枝 |
| **Card Preview 停靠窗轮询** | 0.01% CPU | 64 位采样色彩哈希无变化时不调用 OCR |
| **离线资源依赖** | 0 KB 网络流量 | 纯本地运行，免联网与 CDN 延迟 |
