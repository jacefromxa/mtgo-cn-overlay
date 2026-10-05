# MTGO CN Overlay (万智牌 MTGO 桌面悬浮翻译助手)

> 专为 **Magic: The Gathering Online (MTGO)** 打造的实时中文悬浮翻译工具。
> 继承自 [mtg-cn-browser](https://github.com/jacefromxa/mtg-cn-browser) 的完备数据字典与精美排版渲染。

---

## 特性亮点

* 🛡️ **100% 账号安全（零封号风险）**：
  纯屏幕视觉识别方案。**不读写内存、不注入 DLL、不模拟按键**，原理与 Snipaste / OBS 截图录屏完全相同，严格符合 Daybreak / WotC 服务条款。
* ⚡ **极速本地 OCR + 3.7 万词库模糊容错**：
  采用 Windows 硬件加速本地 OCR 识别，结合 37,000+ 卡牌英文名字典模糊纠错（Levenshtein / BK-Tree），即使 OCR 字符产生微小形近字偏差（如 `L1ghtn1ng B0lt`），也能瞬时精准纠错为标准卡牌。
* 🎴 **海量中文数据与双面卡完备支持**：
  内置 37,082 张卡牌的中文名、法术力费用、类别、完整规则换行文本与风味文字。支持双面卡（DFC）、冒险卡、连体卡。
* 🪄 **原生级视觉体验与 Mana 符号**：
  完美支持 `{W}`、`{U}`、`{B}`、`{R}`、`{G}`、`{T}` 等官方彩色法术力符号，暗色磨砂玻璃背景，支持高分屏 DPI 自适应。
* 🖱️ **无焦点打断（WS_EX_NOACTIVATE）**：
  悬浮窗完全不抢夺 MTGO 焦点，不影响玩家在关键战局中按 F2 / F4 / F6 让过优先权或结算异能。

---

## 推荐运行模式

1. **Card Preview 停靠窗锚定模式（推荐）**：
   * MTGO 拥有自带的卡牌放大预览窗格（Card Preview）。只要鼠标掠过卡牌，该区域就会立即显示高清正向大图。
   * 工具监控该窗格，识别率近乎 100%，CPU 占用几乎为 0。
2. **光标悬停动态跟随模式**：
   * 鼠标在战场卡牌上静止 150ms 自动触发屏幕取词与翻译浮窗。
3. **快捷键触发模式**：
   * 按住 `Ctrl` 或鼠标侧键即时取词，彻底防止误触。

---

## 项目结构

```
mtgo-cn-overlay/
├── src/                      # C# (.NET 8) WPF 核心工程
│   ├── MTGOOverlay/
│   │   ├── Native/           # Win32 API 交互与窗口追踪
│   │   ├── Capture/          # 屏幕截取与 DPI 缩放适配
│   │   ├── Ocr/              # Windows.Media.Ocr 本地引擎
│   │   ├── Data/             # 字典加载与模糊匹配算法
│   │   └── OverlayWindow/    # 置顶穿透窗口与 WebView2
├── web/                      # 浮窗前端渲染层 (HTML / CSS / JS)
│   ├── index.html            # 浮窗页面容器
│   ├── overlay.css           # 浮窗样式与 CSS 变量
│   └── render.js             # 符号转换、分面排版与通信桥
├── data/
│   └── en2zhs.json.gz        # 37,000+ 卡牌离线字典
├── docs/
│   └── ARCHITECTURE.md       # 系统详细设计文档
└── README.md
```

---

## 技术栈

* **宿主应用**：.NET 8 WPF (C#)
* **Webview 容器**：Microsoft.Web.WebView2
* **OCR 核心**：`Windows.Media.Ocr.OcrEngine` (Windows 10/11 内置硬件加速)
* **纠错匹配**：Levenshtein / BK-Tree 模糊索引
* **前端展示**：HTML5 + CSS3 + Mana Font
