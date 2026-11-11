# InkPen 第三方来源与许可声明（SBOM）

程序：**写字机工作台 InkPen 0.1.0-M1**
基于 **GRBL-Plotter 1.8.1.2**（C#/.NET Framework 4.8/WinForms）二次开发。
仅自用、不对外分发；本声明为后续合规发布预留。

---

## 1. 上游与衍生关系

| 项 | 内容 |
|---|---|
| 上游 | `svenhb/GRBL-Plotter`，master，1.8.1.2（2026-10-20 提交；README 标注 1.8.1.0 / 2026-08-15） |
| 上游许可 | **GPL-3.0-or-later**（源码文件头 + 仓库 LICENSE） |
| 本项目做法 | **不编译上游 csproj**（上游 Issues #465/#468/#341 报告依赖缺失/编译困难；本机亦缺 `v4.0.30319` Targeting Pack 与 NuGet）。只把 `src/Upstream/GRBL-Plotter/` 下若干源码作为只读参考，抽取算法重写为 `src/App/Core/Grbl/` 独立类，剥离 WinForms / Properties.Settings / NLog / 多语言资源耦合 |
| 保留 | 所有衍生文件保留原版权头 (C) Sven Hasemann；`LICENSES/GPL-3.0.txt` 随附 |

抽取对照：

| InkPen 文件 | 上游参考文件 | 提取内容 |
|---|---|---|
| `Core/Grbl/GrblCore.cs` (`StreamController`) | `MachineControl/ControlSerialFormStreaming.cs` | 两级缓冲、字符计数流控（RX 127 B）、`ok` 推进确认、暂停等 IDLE、`StreamEventArgs` 进度 |
| `Core/Grbl/GrblCore.cs` (`GrblClient`) | `MachineControl/GrblRelated.cs` | `ParseStatus`、`GetPosition` 等价解析、错误/报警码、`$$` 设置、实时命令字节表、`pollInterval=200` |
| `UI/MainForm.cs` (`$ 参数` 页) | `MachineControl/GrblSetupForm.cs` | `$$` 读表 → 表格 → 双击发 `$nr=val` |
| `UI/MainForm.cs` (连接) | `MachineControl/ControlSerialFormConnect.cs` | 连接/断开/复位流程 + `$I` 识别 |
| `Core/GCode/GCodeBuilder.cs` | `Pen_down_up_Z.ini` | Z 轴抬落笔 `G0/G1 Z` + `G4` 延时模板 |
| `UI/Canvas/PaperCanvas.cs` | `MachineControl/GCode2DViewpaths.cs` | pen-up / pen-down 分层绘制、包围盒、Y 翻转思路（新写为轻量双缓冲控件） |
| `tools/build_glyph_db.py` | `GCodeCreation/CreateFromForm/GCodeFromFontHershey.cs` | Hershey `M`/`L` 字形表与语义 |
| 未采用 | `GCodeCreation/GraphicCollectText.cs` | 该系统字体轮廓方案非单线、无笔顺，不适用于写字机 |

**未引入**上游任何外部 DLL（AForge / SharpDX / DXFLib / FastColoredTextBox / NLog / QRCoder / TabletControl 等），主程序零外部依赖、纯 BCL。

---

## 2. 组件清单

| 组件 | 来源 | 许可 | 随附文件 | 用途 |
|---|---|---|---|---|
| 主程序（上游衍生） | GRBL-Plotter | GPL-3.0-or-later | `LICENSES/GPL-3.0.txt` | 算法骨架 |
| 汉字笔顺主库 `mmah.zip` | `chanind/hanzi-writer-data` v2.0.1 / Make Me A Hanzi | **Arphic Public License** | `LICENSES/ARPHICPL.TXT`、中英文版 | 9574 字，带正确笔顺中心线 |
| 缺字回退 `fallback.zip` | `LingDong-/chinese-hershey-font` STRK-Kaiti | **MIT** | `LICENSES/MIT-chinese-hershey-font.txt` | 11442 个补缺字形（20976 总量中扣除主库已有） |
| 拉丁/数字/标点 `latin*.zip` | 经典 Hershey 字形表（Futural / Scripts；自有宽松许可 / free use + acknowledgement），由离线工具读取 GRBL-Plotter C# 内嵌 `string[]` 转换 | **(a)** 数据 = 自带「致谢 / 禁转 NTIS 格式」USE RESTRICTION 的宽松许可（自由使用 + 致谢；**非 OFL、非 GPL**）；**(b)** SIL OFL 1.1 **仅**覆盖 29 个 EMS*.svg 现代衍生字体（**本项目不分发**）；**(c)** 内嵌该表的 GRBL-Plotter C# 源码容器 = GPL-3.0-or-later。详见下方拆解 | `LICENSES/ACKNOWLEDGEMENT.txt`（含逐字 USE RESTRICTION）、`LICENSES/OFL-1.1.txt`（仅作出处链记录，不覆盖随附数据） | 115 字形 |
| 拼音声调 `pinyin.zip` | 本项目自造 | 自有 | — | 45 个预组合带调元音 |
| 本机手写库 `local.zip` | 本机奎享雕刻 4.2 `.gfont` 导入 | **专有，仅本机使用，不随软件分发** | — | 8604 字真人连笔风格 |
| WriterStudio | `ZhengXieGang/WriterStudio` | **GPL-3.0-or-later**（仅设计思想参考；与主程序 GPL-3.0-or-later 兼容） | — | 未引入任何代码、未引入 Python 运行时依赖；未引入是用户范围决策，**非**许可障碍 |

> **字体数据与代码许可分离**（对上表 `latin*.zip` 一行的拆解，三者互不混同）：
> - **(a) 经典 Hershey 字形数据**：Dr. A. V. Hershey / U.S. National Bureau of Standards 的数据，许可性质为**自带 USE RESTRICTION 的宽松许可**（自由使用 / free use + acknowledgement；Fedora 归类 `Licensing:HersheyFontLicense`，版权 `Copyright: 1967 Dr. A. V. Hershey, James Hurt`；亦常按「公有领域（带 caveats）」描述）——分发须随附致谢（署名 Dr. A. V. Hershey 与 NBS，以及数字化格式作者 James Hurt / Cognition, Inc.），且不得以 U.S. NTIS 原始格式再分发。它**既不是 SIL OFL，也不是 GPL**；与 `data/glyphs/schema.json` 的 "free use + acknowledgement" 一致。逐字原文见 `LICENSES/ACKNOWLEDGEMENT.txt`。
> - **(b) SIL OFL 1.1**：**仅**覆盖 Evil Mad Scientist / Sheldon B. Michaels 2016 年新增的 29 个现代衍生雕刻字体（"EMS" 系列，`EMS*.svg`）。InkPen **不分发**任何 EMS*.svg / OFL 字体；`LICENSES/OFL-1.1.txt` 仅作出处链与衍生关系记录，并不覆盖 `latin*.zip` 数据。
> - **(c) 内嵌容器**：收录上述字形表的 GRBL-Plotter C# 源码（`GCodeFromFontHershey.cs`）为 **GPL-3.0-or-later**；InkPen 主程序作为其衍生，随附 `LICENSES/GPL-3.0.txt`。
>
> **Hershey 字形数据致谢（覆盖两份副本）**：本软件含源自 Dr. A. V. Hershey 的字形数据，覆盖对象为 ① 随发行版提供、exe 运行时加载的 `latin*.zip` 衍生字形数据；② 只读源码快照 `src/Upstream/GRBL-Plotter/GCodeCreation__CreateFromForm__GCodeFromFontHershey.cs` 内嵌的 `public static readonly string[]` 字形数组（随完整源码提供时）——后者是上游中继 `data/fonts/svg/Hershey*.svg`（11 个）、`data/fonts/lff/` 中 Hershey 衍生 lff 之外的**第三份 Hershey 数据副本**。相应 USE RESTRICTION 致谢须署名 Dr. A. V. Hershey / U.S. National Bureau of Standards 与 James Hurt / Cognition, Inc.，并声明禁转 U.S. NTIS 格式；**逐字全文随发行版附于 `LICENSES/ACKNOWLEDGEMENT.txt`**（由 `build.bat` 一并 xcopy 至 `dist/LICENSES/`），本声明将其整体纳入。**主程序 `src/App/*.cs` 不内嵌任何 Hershey 数组，exe 运行时只加载自有 ZIP**；plan-arch「不复制 string[]、只提取 M/L 解析算法」在主程序层面已实现，源码快照层面仍保留一份副本，故如实标注。
>
> GRBL-Plotter `data/fonts/README.md` 只是来源与做法说明，不构成新许可证。上游源码快照共 12 个文件，**不含** `lff/` / `svg/` 目录、不含任何 `.lff` / `.svg` 字体文件本体（仅 `data__fonts__README.md` 与 `data__papersize.xml`），故不产生「随快照分发字体文件」的许可义务。关于 LibreCAD `.lff` 字体：**GPLv2 仅指 LibreCAD 程序整体**（LibreCAD `licenses/readme.md`：源码「released as a whole under the GPLv2」），**不是**字体文件的许可；其字体逐文件异构许可按类别为（依 LibreCAD `licenses/readme.md` 与 GRBL-Plotter `data/fonts/README.md` 交叉）：**SIL OFL 1.1**（amiri / GOST / Noto Sans / unicode_scripts 等）、**KST32B v2.0**（AZOmix 系列）、**Hershey ACK / USE RESTRICTION**（romansi.lff 等自 Hershey 字形派生者）、**public domain**（QCad CXF 非 Hershey 部分、Kochi Gothic 拉丁等）、**Dot-Matrix © 2017-2019 Sven Hasemann**（GRBL-Plotter 自带；此类别出自 GRBL-Plotter `data/fonts/README.md`，非 LibreCAD readme）。其中 `wqy-unicode.lff`（WenQuanYi）为 **Apache-2.0 or GPL-3+**（`Copyright: 2011, WenQuanYi Project Contributors`；新版已由 `wqy-microhei.lff` / WenQuanYi Micro Hei 取代，后者为 Apache-2.0 or GPLv3+ with font exception），属**用户自备**的外部资源——GRBL-Plotter 因体积原因不分发、InkPen 亦不引入，**不进入 InkPen 随发行版 `LICENSES/`**。InkPen **不分发上游 `data/fonts/` 中 46 个 `.lff` 的任何一个**、不分发任何 EMS/OFL 字体文件，仅使用源码内嵌的经典 Hershey 数据表，并按上表 (a) 保留逐字致谢。

---

## 3. Arphic Public License 三层口径

1. **正文义务（确证，见随附原文）**
   - 第 1 条：复制/分发须在所有复本中保持 `ARPHICPL.TXT` 不变。
   - 第 2(a) 条：每个修改过的文件须插入明显声明，说明**如何及何时**修改。
   - 第 2(b) 条：修改物整体须按本授权书条款向所有第三方「自由使用」开放。
   - 第 2(c) 条：交互式程序启动时须显示版权、无担保、可再分发声明及许可证查阅方式。
   - 第 7/8 条：无担保、损害赔偿免责。
2. **官网/发行版声明（非条款）**：文鼎 1999 年发布说明中对「单独贩售字库」的商业表述，不属 `ARPHICPL.TXT` 法律条款。
3. **风险提示（推测，建议法务复核）**：第 2(b) 条「修改物整体自由使用」与「独立可辨识部分除外」的边界，在「字库数据 + 独立上位机程序」的聚合场景下存在解释空间。

**本项目实测核验**：`hanzi-writer-data` 的 `APL/` 目录只是同一份许可证的多语言版本（english / zh_CN.UTF-8 / zh_TW.UTF-8 / big5 / gb），**无额外条款**。中文版明确写「『自由使用』…而非指价格。如果您愿意，可以对服务收取费用」——原文**无「不得单独售卖」明文**，且明确允许对服务收费。

**执行口径（按最严）**：
- 随附 `ARPHICPL.TXT` 原文（含中英文版本），不修改许可证文本本身；
- `data/glyphs/mmah.zip` 内 `_SOURCE.txt` 声明修改内容与日期：`y+124` 平移、Catmull-Rom 平滑、重编码为 InkPen Glyph v1；
- 衍生字库数据保持同条款开放、不加额外限制；不单独售卖字库数据；
- 程序「关于」信息显示版权 / 无担保 / 再分发声明与本许可证位置；
- 用户当前选择「仅自用、不分发」，上述对外义务在分发时才触发，现已提前准备。

---

## 4. 字库格式与命名（重要：不同 `.gfont` 不直接等同，兼容/同构关系未实测）

| 名称 | 实际结构 | 本项目关系 |
|---|---|---|
| 奎享雕刻 `.gfont` | 43,340 B 私有前缀 + ZIP(Deflate, 8604 条) + 每字 `uint16 码点 + uint32 点数 + 大端 float32 点流`；无笔顺/抬笔标记 | **只做本机只读导入**，转换为自有格式 |
| WriterStudio `.gfont` | 单线笔画字体包；与奎享 `.gfont` **同名、产品定位相近，存在同源线索**；二者结构兼容/同构关系**未实测** | **不直接等同、不消费**：InkPen 不解析、不产出、不假定兼容 |
| InkPen 自有格式 | **`InkPen Glyph v1`**：标准 ZIP，条目名 = 十进制 Unicode 码点，条目 = `uint16 笔画数 + float32 advance + 每笔 uint16 点数 + 小端 float32 (x,y)`；显式笔画边界、1024 网格、Y-up | 本项目的 `data/glyphs/*.zip` |

**命名边界与未实测的兼容关系**：三种同名/近名格式**不直接等同**；奎享 `.gfont` 与 WriterStudio `.gfont` 存在同源线索，但二者结构兼容 / 同构关系**未实测**，本声明不作「已证不兼容」也不作「已证兼容」的结论。`.gfont` 转换走**自写** `tools/import_gfont.py`（跳过 43,340 B 私有前缀 → 按码点取 ZIP 条目 → 解大端 float32；离线字库构建脚本，不随 exe 分发，笔画分隔 / 抬笔标记为启发式必做项），仅解析**本机奎享** `.gfont` 并产出 `local.zip`（InkPen Glyph v1），不产出任何 `.gfont`；WriterStudio 仅作设计参考（design-reference-only），**未引入其代码、未采用任何 WriterStudio `.gfont` 解析器、未引入 Python 运行时依赖**，InkPen **不消费** WriterStudio `.gfont`。WriterStudio 同为 GPL-3.0-or-later（与主程序兼容），未引入其代码是用户的范围决策，**不是**许可障碍。`docs/` 与本声明为命名契约；保留 `.zip` 扩展名是为了零依赖可直接解压，并以 `_SOURCE.txt` 与 `schema.json` 自描述。

---

## 5. 坐标系约定（一次翻转，避免二次颠倒）

- 字库层：`hanzi-writer-data` medians 实测 Y-up、值域 `[-124, 900]`（`三` 三横 meanY = `[666, 426, 170]` 自上而下递减），统一 `y' = y + 124` → `[0, 1024]`；`chinese-hershey-font` 为 (0,0) 左上、y-down，统一 `y' = (1-y)*1024`。字库层**固定 Y-up 0..1024**，不含任何方向翻转。
- 排版层：纸面 mm，Y-up，原点左下。
- 末端各翻一次：`PaperCanvas.ToScreen` 翻到屏幕 Y-down；`CoordTransform.ToMachine` 按原点角 / 轴反向决定机床方向。绝不两处重复翻转。

---

## 6. 未确证 / 待实施验证 与 发行前动作清单

- 本机 COM3/4/5 中哪一个是写字机、GRBL 版本、`$102` 等参数 → 真机探测。
- Z 安全高度 / 书写高度 / 方向 / 进给 → 真机标定（本机奎享默认 `grbl_type=Servo` 舵机，与用户 Z 轴步进硬件不同，参数不可照抄）。
- `.gfont` 笔画边界 / 抬笔标记为**启发式必做项**、笔顺未校验 → 自写 `tools/import_gfont.py`（跳过 43 KB 前缀）推断，C# 笔画编辑器人工修正（M4）。
- 奎享 `.gfont` 与 WriterStudio `.gfont` 的结构兼容 / 同构关系**未实测**（仅有同名与同源线索）；本项目按「不直接等同、不消费」处理，不作已证不兼容或已证兼容的结论（见 §4）。WriterStudio 仅作设计参考；**未采用任何 WriterStudio 解析器**，`.gfont` 走自写 `tools/import_gfont.py`。

**架构口径（确定陈述，已核验）**：主程序 `src/App/*.cs` **不含** Hershey 内嵌数组，exe 运行时加载的 `latin*.zip` 是衍生字形数据；只读上游快照 `src/Upstream/GRBL-Plotter/` 中含一份内嵌 `string[]` 字形数据副本（随完整源码提供时）。ACKNOWLEDGEMENT 的致谢覆盖对象 = 随附的 `latin*.zip` 衍生数据 + 只读源码快照中的内嵌数组。快照共 12 个文件，**不含** `lff/` / `svg/` 目录、不含任何 `.lff` / `.svg` 字体文件本体（仅 `data__fonts__README.md` 与 `data__papersize.xml`）。`data/glyphs/schema.json` 中 latin 的许可为 "free use + acknowledgement"，与本声明用词一致。**wqy-unicode.lff 仍属「用户自备外部资源、不随附」的独立层面**，与「上游 `data/fonts/` 字体未随快照进入工作区」是不同层面，不可混标。

**发行前动作清单**
- 【已完成】补经典 Hershey USE RESTRICTION 逐字全文（仅取 USE RESTRICTION 段 + 两条致谢，不整篇照搬 Ghostscript 说明）→ `LICENSES/ACKNOWLEDGEMENT.txt`；§2 逐类标注 LibreCAD `.lff` 许可类别。
- 【待办 / 未实测】**EMS SVG 逐文件提取内嵌 credit**：`svg/OFL.txt` 为**未填写模板占位符**（抓得 `Copyright (c) , (), with Reserved Font Name .`，具体版权人**未确证**），不能作为各 EMS SVG 的版权依据；须依 EMS README「Individual credits and license information are embedded within the SVG fonts in this repository」逐文件从 SVG 内嵌元数据提取。Debian 收录的 `HersheySansMed.svg` 文件头内嵌的是 **Hershey USE RESTRICTION 而非 OFL**（已确证；同型 `HersheySerifBoldItalic.svg` 已核验），再次印证经典 Hershey / EMS 两类不可混标。此项**尚未实测、尚未完成**；InkPen 不分发 EMS SVG，故为「若将来分发」的前置动作。
- 【待核 / 未逐一复核】「29 个 `EMS*.svg`」「46 个 `.lff`」的数量与逐文件清单：本地快照不含 `data/fonts/svg/`、`data/fonts/lff/`，系依据上游交叉核对（LibreCAD `licenses/readme.md` / Debian `copyright` / GRBL-Plotter `data/fonts/README.md` / EMS "Hershey Text" 页面 / Fedora `Licensing:HersheyFontLicense`）转述，未在本地逐一复核；`wqy-unicode.lff` 的 Apache-2.0 / GPL-3+ 已由上列 LibreCAD / Debian 权威来源确认。InkPen 不分发上述任何字体文件，其逐文件许可不进入随发行版 `LICENSES/`。
- 【终验建议】随附许可证原文再复核一次：上述结论为一手交叉核对，但 explore-font 未独立复验。
- `LICENSES/ACKNOWLEDGEMENT.txt` 中经典 Hershey 的「USE RESTRICTION」为本地 `src/Upstream/GRBL-Plotter/GCodeCreation__CreateFromForm__GCodeFromFontHershey.cs` 文件头的**逐字原文**（并与 LibreCAD `hershey.readme` / Debian `hershey.txt` / Fedora wiki 的 USE RESTRICTION 段逐字一致）；若对外分发时需与 NTIS 原始发行版致谢措辞做逐字一致性比对，建议**待法务复核**（verbatim wording to be confirmed）。本声明未虚构任何许可原文。
