# InkPen M1 离线验收记录

日期：2026-11-11
版本：`0.1.0-M1`（`build/version.txt`）
范围：**离线链路**。不接真机、不发串口字节。

## 1. 验收口径

用户批准「先仿真再真机」。M1 只判定离线链路，真机参数标定另列待办。
验收不使用「编译成功」或「文件存在」作为通过依据，一律以实际执行结果为准。

## 2. 执行结果

| # | 项目 | 命令 / 方法 | 结果 |
|---|---|---|---|
| 1 | 主程序编译（csc 链） | `INKPEN_CI=1 cmd /c build\build.bat` | **PASS**，零错误；`dist/InkPen.exe` 90 KB（92160 B） |
| 2 | GUI 启动 | 启动进程后用 Win32 `GetWindowTextW` 枚举顶层窗口并读取子控件文本 | **PASS**，主窗口 `写字机工作台 InkPen`，三栏控件全部创建，无异常对话框，进程存活 |
| 3 | 端到端自检 | `INKPEN_CI=1 cmd /c build\selftest.bat` | **PASS**，9 作业 / 66 断言 / 10 份 `.nc`，`failed=0` |
| 4 | GCode 文件级校验 | `validate_gcode.py` × 10 份 `.nc`（210×297 mm） | **PASS**，10/10，错误 0 警告 0 |
| 5 | 校验器自身自检 | `validate_gcode.py --selftest` | **PASS**，7/7 |
| 6 | 分发构建 | `INKPEN_DISTRIBUTE=1 build.bat` | **PASS**，`local.zip` 与 `local_report.json` 被排除 |
| 7 | 文档同步 | `cmp` 比对 `NOTICE.md` / `README.md` / `LICENSES/ACKNOWLEDGEMENT.txt` 与 `dist/` | **PASS**，三份一致 |
| 8 | VS 编译链 | `InkPen.sln` + MSBuild 17.14 + vstest 17.14（VS 2022 Build Tools） | **PASS**，编译零错误、`Rebuild` 成功，产物与 csc 链一致 |
| 9 | 单元测试 | `INKPEN_CI=1 cmd /c build\test.bat` | **PASS**，**140 / 140**（M1 初版 98 项，第四轮新增 42 项机器特性测试），0 失败 0 跳过 |
| 10 | 字库方向 | `GlyphOrientationTests`（垂直重心相关性 + 「三」横长 + 「P」圆环位置） | **PASS**，`local` 与 `mmah` 相关系数 **+0.711**；回归验证时换回倒置版会失败于 **-0.732** |
| 11 | GUI 冒烟 | Win32 `EnumWindows`/`EnumChildWindows` + `PIL.ImageGrab` 像素统计 | **PASS**，标题正确、可见外框 **1426×873**（窗口设定 1440×880）、111 个子窗口、无错误框、纸张内部墨迹比 **0.0154**、纸张左右留白 104/122 px（`paper_w=501 canvas_w=727`，居中未裁切） |
| 12 | GUI 交互 | Win32 `SendMessageW(BM_CLICK)` 点「把格线写入 GCode」+「生成 GCode」 | **PASS**，笔画 130→137、GCode 2902→2920 行 |
| 13 | GUI 配置 | 点「保存配置」→ 检查落盘 JSON → 点「读取配置」 | **PASS**，无 BOM、含 `machine/template/text` 三键、读取无对话框 |
| 14 | GUI 调平页 | `gui_check.py level`（隐藏参数 `--tab=level` 直开标签页） | **PASS**，探测/高度图控件全部创建、热力图有像素、无错误框 |

> 环境：VS 2022 Build Tools 17.14 安装于 `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools`，含 `Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools`（带 .NET Framework 4.8 Targeting Pack）、NuGet、`vstest.console.exe`。安装时 `winget` 与官方 bootstrapper 均因环境里同时存在 `HTTPS_PROXY` / `https_proxy` 而失败（`exit code 5002`、`ProcessStartInfo.EnvironmentVariables` 抛「已添加项」）——清除大小写重复的代理变量后重跑才成功，此坑已记入 README。

### 自检覆盖明细（`tools/selftest/SelfTest.cs`，复用生产 `Core` 源码）

| 作业 | 内容 | 页 | 笔画 | GCode 行 | 缺字 |
|---|---|---|---|---|---|
| `a_mixed` | 汉字/数字/拉丁/带调拼音/标点混排 | 1 | 99 | 961 | 0 |
| `b_free` / `b_ruled` / `b_grid` / `b_tianzi` / `b_pinyin4` | 五类稿纸模板 | 1 | 34 | 372 | 0 |
| `c_vertical` | 竖排 | 1 | 99 | 961 | 0 |
| `d_multipage` | 长文稿强制分页 | 4 | 2340 | 26828 | 0 |
| `e_drawgrid` | 格线写入 GCode | 1 | 98 | 504 | 0 |

- 确定性：同种子重建两次生成字节完全一致（17585 B）。
- 断点续写：目标 `CI=18` → GCode 第 634 行，续写文本含 `G21/G90/G94/F/G0 Z` 且短于全文。
- 字库：并集 21666 码点，`LoadErrors=0`。最长实际发送行 **34 字节**（`d_multipage.nc:4702`），远低于 80 字节上限。

## 3. 第一轮修复的缺陷（均为实测发现，非推测）

| 严重度 | 缺陷 | 根因 | 修复 |
|---|---|---|---|
| **P0 启动崩溃** | 双击 exe 只弹「启动失败」对话框，主窗体从未创建 | `MainForm.BuildUi()` 在构造期先设 `SplitterDistance` 再设 `Panel2MinSize=700`，此时 `SplitContainer` 宽仍为默认值，抛 `InvalidOperationException` | 所有 `MinSize`/`SplitterDistance` 延后到 `Shown`，并按 `[Panel1MinSize, Width-Panel2MinSize]` 夹取；`_inner` 同步修复 |
| **P0 流控永久停摆** | `ok` 可能永远匹配不上，在途行不释放，缓冲耗尽后书写卡死 | `GrblClient.ProcessRx()` 每次清空整个 RX 缓冲，`ok\r\n` 被拆到两个 `DataReceived` 事件时变成孤立的 `o` / `k` | 只取完整行，未收齐的半行留在缓冲；`<...>` 状态报告单独成帧（部分固件不跟换行） |
| P1 | `error:` 后盲目继续发送会写出重复笔画 | 流控只把错误写日志 | 新增 `OnStreamFault`，`error:` / `ALARM:` / `<Alarm>` 一律中止**且不重发**，记录最早未确认行号供续写 |
| P1 | 字符计数与实际发送字节不符 | 按含空格长度计数 | 发送前压缩空格，按压缩后长度 +1 计数；`ack` 超时（默认 30 s，可配）检测；暂停期间不计时 |
| P1 | 超长 GCode 行被 GRBL 静默截断写出错字 | 仅按 128 B RX 缓冲校验 | 三处统一按 GRBL 1.1 单行缓冲 80 B 硬拦截（C# 校验器 / Python 校验器 / 流控发送器） |
| **P1 校验器恒失败** | 每份 `.nc` 都报「未发现续写元数据注释」 | `validate_gcode.py` 用 `raw.split(";")[0]`，把 `(CH:...;PG:...)` 内的分号误当行注释 | 只在括号深度 0 处截断 `;` 注释；补 `--selftest` 7 项单元自检 |
| P1 合规 | 专有 `local.zip` 被通配复制进 dist，违反 NOTICE「不随软件分发」 | `build.bat` 用 `*.zip` 通配 | 改为显式白名单；默认构建含 `local.zip` 供本机自用并打印警示，`INKPEN_DISTRIBUTE=1` 产出不含该文件的可分发包 |
| P2 | 禁用字库后覆盖数不下降 | `GlyphRepository.Count` 未检查 `Enabled` | 加 `Enabled` 过滤 |
| P2 | 字库「启用/禁用」按钮点一次后永远无法再启用 | `IsFontEnabled` 恒返回 `true` | 用 `HashSet<string>` 维护禁用集合；`LoadFonts()` 重建仓库后重新应用 |
| P2 | 中止原因不显示 | `MainForm` 从未订阅 `_stream.OnError` | 补订阅，`error` 状态进度条标红并给出续写行号 |
| P2 | UI 文案与 SBOM 矛盾 | 字库页写「三者互不兼容」 | 改为「兼容/同构关系未实测，不假定兼容」 |

## 3.2 第二轮修复的缺陷（引入 MSBuild 链与 92 个单元测试后暴露）

前四项由**新增的单元测试直接抓到**，不是人工目视发现；后几项由反复操作 GUI 复现。

| 严重度 | 缺陷 | 根因 | 修复 |
|---|---|---|---|
| **P0 空引用** | `Rdp.Simplify(null, eps)` 抛 `ArgumentNullException: 参数名 collection` | 守卫写成 `if (pts == null \|\| ...) return new List<Pt>(pts)`，null 仍被传进构造函数 | 拆开：null 先行返回空列表，再判 `Count < 3` |
| **P1 功能错乱** | 模板下拉选「横线稿纸」实际生成「自由排版」 | 枚举顺序 `Free/Ruled/Grid/Tianzi/Pinyin4`，界面顺序是 横线/方格/田字/拼音/自由，代码直接 `(TemplateKind)SelectedIndex` 强转 | 显式映射表 `TPL_KINDS`/`TPL_LABELS` + 公开契约 `TemplateKindFromIndex`/`IndexFromTemplateKind`，补 3 个回归用例 |
| **P1 废纸** | 续写起始字超出文档时，从第 0 行重写整篇 | `FindResumeLine` 越界 `return 0` | 越界返回 `job.GCode.Count`（末尾，什么都不写） |
| P1 互操作 | 配置 JSON 带 UTF-8 BOM，Python `json.load` 直接报错 | `Encoding.UTF8` 写文件默认带 BOM | 写侧改 `UTF8Encoding(false)`；读侧 `Json.Parse` 容忍 BOM；补 BOM 容忍用例 |
| **P1 重笔** | 「停止」后立刻「开始书写」，旧线程会继续发上一份 GCode，与前一份叠写 | `Stop()` 只置 `_stop=true`，`Start()` 又把它复位 | 加作业代号 `_generation`：`Stop`/`Start` 都 +1，旧线程发现代号不符立即静默退出；线程入口快照 `_lines`，杜绝旧线程按旧下标索引新作业的行 |
| P1 流控错位 | 书写中手动 jog / 试笔 / 发自定义指令，会造成 `ok` 与在途行错位、多发溢出 | 无任何守卫 | `ManualBlocked()` 拦截三类手动指令并提示「先暂停」；暂停仍算 `IsRunning`，守卫持续有效 |
| P1 叠页 | 多页文稿所有页笔迹叠在同一张纸上，仿真看不出分页 | 画布只按 `Job.Paths` 全量绘制，无页概念 | `PaperCanvas.PageIndex` 按页过滤格线/空程/笔迹，跨页不连虚线；新增「‹ 页 n/N ›」翻页条；仿真播放自动翻页 |
| P2 跳笔 | 仿真播放一开始就跳过第一笔 | 起始索引写成 `ProgressPath = Step + 2` | 改为从 `-1` 起步进，首帧即第一笔 |
| P2 状态栏 | 中止后状态栏被 `error` 覆盖两次 | `Run()` 结尾无条件 `Progress("error")` | `Abort()` 已发过就 `return`，不重复刷新 |
| P2 可用性 | `$` 参数表不自动刷新、只有 `$100 250.000` 两个裸列，标定时看不懂 | 只在点「读参数」时填一次，且无中文说明 | 新增 `OnSettingsChanged` 事件 + 300 ms 防抖刷新；表头改 参数/值/说明 三列，补 34 个常用 `$` 参数中文说明 |
| P2 首屏 | 启动后中栏空白，像坏了 | 构造期未主动生成 | 构造末尾 `RefreshSettingsGrid(); Generate();` |
| P2 死代码 | `_showPenUp` 字段与其隐藏 CheckBox 无人引用 | 遗留 | 删除 |
| **P1 首屏错位** | 双击打开后纸张偏到右边、被画布右缘切掉约三分之一，看着像渲染坏了 | `Fit()` 只在 `SetJob` 时算一次，而那时窗体还没完成布局（画布比最终宽），`panX` 偏大；之后窗口定尺寸时只重绘、不重算 | 新增 `OnResize`：未手动调整视图则重新 `Fit()`，手动调过则只把纸张夹回可视区；`Zoom` 改为以画布中心为锚点缩放，连点放大不会把纸推出去。GUI 冒烟新增左右留白对称断言（实测 98 / 97 px） |

## 3.3 第三轮修复的缺陷（用户反馈「生成的字上下颠倒，相当于每个字转了 180°」）

用户反馈后没有直接改代码，而是先分层定位：字库二进制 → 排版坐标 → GCode → 画布。
中间走了两段弯路（用 `三` 的长度分布判方向时，恰好抽到 `local.zip` 的**连笔**写法，
一笔写完，三横长度关系不成立；用低分辨率截图肉眼判断又反复误判），
最后改用与字形风格无关的几何不变量才定死。

| 严重度 | 缺陷 | 根因 | 修复 |
|---|---|---|---|
| **P0 整包倒置** | 界面里**每一个字都上下颠倒**（P 的圆环跑到下方、7 的横跑到顶上、三的上横最长） | `tools/import_gfont.py` 把 `.gfont` 坐标缩放到 1024 网格时**只做了缩放和平移，没有镜像 Y**。`.gfont` 原始坐标 Y 向下（实测原始 y 范围 `-600..199`），而 InkPen Glyph v1 规定原点左下、Y 向上，于是 8604 个字整包翻转 | 转换时改为 `y' = GRID - (y*scale + offY)`，重新生成 `local.zip`；`local_report.json` 记录 `flipY: true` |
| 排查方法沉淀 | 这类缺陷不触发任何异常、笔画数与坐标范围全部正常，肉眼与低分辨率截图极易误判 | — | 新增 `GlyphOrientationTests`：用「垂直重心相关系数」判定两套字库是否同向。同向应显著正相关（实测 **+0.711**），整包镜像则为负（实测 **-0.711**）。另加 `mmah`「三」上横短于下横、`latin`「P」圆环在上两个绝对判据 |

**修复前后对照（同一批数据实测）**

| 指标 | 修复前 | 修复后 |
|---|---|---|
| `local.zip` 与 `mmah.zip` 垂直重心相关系数 | **-0.711** | **+0.711** |
| `local.zip` 中 `P` 的圆环相对位置（0=底 1=顶） | 0.23（倒） | **0.77**（与 `latin.zip` 的 0.74 一致） |
| 界面渲染 `P7L三上` | P 的圆环在下、7 的横在底 | P 的圆环在上、7 的横在顶、L 的脚在底、三的下横最长、上正立 |

**测试有效性验证**：把倒置的旧 `local.zip` 换回去重跑，新测试立即失败并打出
`相关系数 = -0.732`，同时提示「检查 tools/import_gfont.py 的坐标变换」。
换回修复版后全通过（当时是 95 项）——证明这条测试不是摆设。

**X 方向补测（复盘后新增）**：原先只守 Y 方向，X 完全无人检查。已补 3 项判据，
并同样做了注入验证：把 `latin.zip` / `local.zip` 就地左右镜像后重跑，
3 项 X 判据全部失败（`相关系数 = -0.432`，恰为 `+0.432` 的镜像）、Y 判据零误报。
完整复盘见 `docs/testing_pitfalls.md`。

**顺带核验的另外两套字库**：`mmah.zip`（「三」上 406 / 中 369 / 下 828，上短下长，正立）、
`latin.zip`（P 圆环 0.74，正立）、`fallback.zip`（与 `mmah` 零交集、并集 21016 字，
抽样 `U+4E05 丅` 重心 0.73 偏上，结构正确）。

修复后界面实拍：`docs/gui_fixed.png`（四行混排：汉字／拉丁／数字／带调拼音／标点，全部正立可读）。

## 3.4 第四轮：A 档功能全量实现 + 实时笔位置 + 视觉升级

用户指令：「弹簧浮动笔架，A 档的全做，画布实时显示笔位置，界面花哨一些」。四件事合并为一轮，实现清单见 `docs/feature_gap_vs_grbl_plotter.md` 文首「实施状态」。

### 新增模块

| 文件 | 职责 |
|---|---|
| `src/App/Core/Machine/HeightMap.cs` | 高度图数据体：`Reset/Get/Set/At/Smooth/Normalize/MinZ/MaxZ/Mean/ToJson/FromJson`；双线性插值，区域外**夹取不外推** |
| `src/App/Core/Machine/ProbePlanner.cs` | 探测点规划（`BuildPoints`/`DefaultArea/BuildSteps/BuildSinglePoint`）+ `[PRB:]` 解析 + 失败点补值 |
| `src/App/Core/Machine/ProbeRunner.cs` | 纯状态机，不持有串口；`Start/Cancel/FeedLine/Poll` + 四个回调 |
| `src/App/Core/Machine/Macros.cs` | 宏按钮模型与占位符展开，`config/macros.json` 持久化 |
| `src/App/Core/Grbl/PenTracker.cs` | 实时笔位置（WPos 优先）+ 已发行号→当前笔画映射；同文件含 `ReconnectPolicy` |
| `src/App/UI/Theme.cs` | 集中配色与 GDI+ 辅助（圆角、发光、按钮样式、字体回退） |
| `src/App/UI/Canvas/Widgets.cs` | `JobStatusBar`（圆环进度 + 坐标胶囊 + 状态徽标）、`HeatmapView`（冷暖热力图 + 图例） |
| `src/App/UI/ProbeDialog.cs` | 探测进度对话框，可取消 |
| `tests/InkPen.Tests/MachineFeatureTests.cs` | 42 项：高度图 / 坐标逆变换 / Z 补偿 / 探测规划 / 探测器状态机 / 笔跟踪 / 重连退避 / 宏 |

### 本轮实测发现的缺陷

| 严重度 | 缺陷 | 根因 | 修复 |
|---|---|---|---|
| **P1 功能静默失效** | 高度图热力图一片空白，且 GCode 里没有任何 Z 补偿 | 两处叠加：①`HeightMap.FromJson` 先赋 `Valid` 再调 `Reset()`，`Reset` 把它清掉；②`IsUsable` 里判了 `Enabled`，而界面回填勾选框时触发 `CheckedChanged` 又把 `Enabled` 清掉。**两处都会让功能「看起来开了但完全没生效」** | `valid` 改为 `Reset()` 之后赋值；`IsUsable` 只管数据是否可用，开关状态归 `MachineConfig.UseHeightMap` 单点管理 |
| P1 视觉崩塌 | 状态条三个坐标胶囊顶出右边界被裁 | 固定宽度排布 | 自适应：先缩胶囊间距，再丢状态徽标 |
| P1 布局溢出 | HUD 信息框写死 250 px，长文案溢出 | 固定宽度 | 按 `MeasureString` 实测，上限 `ClientSize.Width - 40` |
| P1 竖排不可达 | 调平页第三个分组框被面板底部裁掉，热力图看不见 | 三组固定高度累加超出可用高度 | 高度图组 300→256，`_heat` 226→184，说明文字下移到 y=478 |
| P2 窗口被裁 | 1440×900 窗口在工作区 1440×900 上，底部任务栏遮住约 20 px | 窗口尺寸未考虑工作区 | 改 1440×880，并在 `Shown` 时 `ClampToWorkArea()` 夹到 `Screen.WorkingArea` |
| P2 工具栏溢出 | 「笔位」「跟随」两个按钮被挤出可视区 | 按钮用长标签 | 全部改短标签（播放/暂停/单笔/复位/空程/笔位/跟随），完整说明移到 ToolTip |
| P2 文案被裁 | 「自动探测」按钮文字缺一半 | 宽度 52 px 不够 | 70 px，端口框 140→122 让位 |
| P2 溢出组框 | 「断开后自动重连」勾选框溢出连接组框 | 与其它控件挤一行 | 独占一行，连接组高度 138→166 |
| **P2 自检编译断链** | `selftest.bat` 报 `CS0246: 未能找到类型 HeightMap` | `selftest.rsp` 是**逐文件列举**（不同于 `sources.rsp` 用 `/recurse`），新增源文件没有自动进入 | 补入三个新核心文件。这个坑值得记住：新增 `Core` 文件必须同时改两处编译清单 |

### 测试有效性验证（注入验证）

不只跑绿，还故意把实现改坏，确认测试会红。7 处注入中首轮只抓到 6 处：

| 注入 | 首轮结果 | 加固方式 |
|---|---|---|
| `ToPaper` 撤变换顺序反了（先撤原点角再撤 Invert） | ✅ 抓到 | — |
| 高度图行列转置 | ❌ **未抓到** | 原 2×2 网格对称，转置后数据不变；改 3×2 且各列值互异（0/0.2/0.4 与 1/1.2/1.4） |
| 平滑不做归一化 / 插值不外推 | ✅ 抓到 | — |
| 失败点不补值 | ✅ 抓到 | — |
| 探测推进前不抬笔 | ❌ **未抓到** | 原断言只比 `sent[3] == "G0 Z5"`，而下一块自带的第一行也是它；改为断言总行数与 `sent[5]` 的具体值 |
| 笔位置优先 MPos | ✅ 抓到 | — |
| JSON 往返丢 Valid | ✅ 抓到 | — |

还原后回到 140/140 全绿，且 `grep -n "MUTATION" -r src/` 为空。教训已写入 `docs/testing_pitfalls.md`。

### 3.4.1 截图复查发现的视觉缺陷（全部靠看图发现，自动断言抓不到）

自动化只能证明「控件存在、像素够多」，证明不了「好看」。把截图放大逐块看，又修了 7 处：

| 缺陷 | 现象 | 修法 |
|---|---|---|
| **截图截错窗口** | `docs/gui_level.png` 存下来是浏览器和聊天窗口的截图，看着像界面崩了 | 根因是 `SetForegroundWindow` 被 Windows 前台锁忽略，被别的窗口盖住的区域被原样截下。改为 `AttachThreadInput` 到当前前台线程后再抬窗，并轮询 `GetForegroundWindow` 确认；截图前后各校验一次 |
| **截图带进桌面** | 存档图右缘/底缘露出壁纸色与别的窗口文字 | `GetWindowRect` 在 Win10/11 上比可见窗口大一圈；改用 `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` |
| jog 方向键布局错 | 上排只有 `Y+`，第二排 `X− Y− X+`——看着像少一条腿的「＋」，方向直觉错误 | 改为标准十字：上排 `X− Y+ X+`、下排居中 `Y−`；`Z+ Z−` 做右侧竖排一对 |
| HUD 压住纸张 | 左上角信息框盖住纸面顶部一行 | `Fit()` 的上边距从 36 px 加到 72 px，纸张在「扣掉 HUD 的剩余区域」里居中 |
| 比例尺压住纸张 | 原画在 `Height-26`，纸张铺满后正好压在纸底边上 | 移到右上角（HUD 在左上，宽度上限压到画布宽度 62%） |
| 多行框横向滚动条 | `ScrollBars.Both` + `WordWrap=false`，中文长句横向溢出，多出一条永远用不上的横条 | 改 `Vertical` + `WordWrap=true` |
| 「读取配置」文字被裁 | 按钮宽 60 px 装不下四个汉字 | 改成三行布局：导出整行、保存/读取各 126 px |
| 工具栏选中框是系统蓝 | `CheckOnClick` 的「空程」「笔位」显示系统蓝色选中框，与青/琥珀配色打架，旁边「播放」却是纯文字 | 加 `BarRenderer`：悬停极淡青底、选中淡青底 + 细边、标签文字压暗一档 |

## 4. 本轮 SBOM 勘误（`NOTICE.md` / `README.md` / `LICENSES/ACKNOWLEDGEMENT.txt`）

- **经典 Hershey ≠ SIL OFL 1.1**：为自带 USE RESTRICTION 的宽松许可（自由使用 + 致谢 + 禁转 U.S. NTIS 格式），既非 OFL 也非 GPL。OFL 1.1 仅覆盖未随本发行版提供的 29 个 EMS 现代衍生字体。
- 补齐 **USE RESTRICTION 逐字全文**。实际来源是本地快照 `GCodeFromFontHershey.cs` 文件头第 34–60 行（已 diff 校验），并与 LibreCAD / Debian / Fedora 三个外部来源逐字比对；未整篇照搬 Ghostscript 分发说明。
- 致谢覆盖**两份** Hershey 数据：随附的 `latin*.zip` 衍生数据，以及只读源码快照中的内嵌 `string[]` 数组。主程序 `src/App/*.cs` 不内嵌该数组。
- `wqy-unicode.lff` = Apache-2.0 or GPL-3+，属用户自备外部资源；快照共 12 个文件，不含任何 `.lff` / `.svg` 字体文件本体。
- 「三种 `.gfont` 互不兼容」改为「不直接等同、兼容/同构关系未实测」；WriterStudio 仅设计参考、未引入代码或解析器。

## 5. 未完成 / 边界（不伪装为已完成）

### 真机联调（需用户在场）
1. 判定 COM3/4/5 哪个是写字机，读 `$I` / `$$` 确认 GRBL 1.1 / 0.9。
2. 标定 `安全 Z` / `书写 Z` / Z 方向 / `$102`；本机奎享默认舵机参数不可照抄。
3. 校准原点角、X/Y 轴方向、幅面。
4. 试写中文页，验证暂停 / 急停 / 断点续写。
5. **（第四轮新增）** 探针接线与探测验证：探针是否与笔共用 Z 信号、接触时 `[PRB:]` 标志位是否置 1、Z 轴下探 8 mm 是否够、`ProbeFeedMmPerMin=100` 是否合适、`PressDepthMm` 实际取值。
6. **（第四轮新增）** 自动重连实测：拔 USB 后观察退避重连时序，确认重连后「需重设原点」提示有效。

### 尚未实现（M2–M5）
- 四类模板高级编辑器与另存、模板预设持久化。
- 字库管理页的笔画编辑器、缺字手绘补录、`.gfont` 启发式断笔人工修正。
- SVG / DXF / 图片转线稿导入、批量队列、配置备份、崩溃恢复。
- 断点续写发送现场的完整持久化。

### 已知边界（有意为之，非缺陷）
- `preferLocal=false` 时 `local.zip` 仍置于回退链链尾做**缺字兜底**。自检实测 38 个探针字符在该模式下 0 个来自 `local`，常规文稿输出可复现；需要严格跨机器一致时用 `INKPEN_DISTRIBUTE=1` 构建（不含该文件）。这是用户所选「混合字库」策略的语义，未擅自变更。
- `svg/OFL.txt` 是未填写的模板占位符；若将来分发 EMS SVG，需逐文件从 SVG 内嵌元数据提取 credit（**未实测、未完成**）。当前不分发任何 EMS SVG。
- 「29 个 EMS*.svg」「46 个 .lff」数量系上游交叉核对转述，本地快照不含这些目录，未逐一复核。
- Arphic 第 2(b) 条在「字库数据 + 独立上位机」聚合场景下的解释边界，以及 Hershey 致谢措辞与 NTIS 原始发行版的逐字一致性，建议对外分发前**法务复核**。

## 6. 复现命令

```bat
cd D:\Writer\InkPen\build
INKPEN_CI=1 cmd /c test.bat        :: MSBuild 编译 + 140 个单元测试
INKPEN_CI=1 cmd /c build.bat       :: csc 链，产出 dist\InkPen.exe
INKPEN_CI=1 cmd /c selftest.bat    :: 9 作业 / 66 断言 / 10 份 .nc
```
```bat
cd D:\Writer\InkPen
C:\Users\Administrator\AppData\Local\Programs\Python\Python312\python.exe tools\validate_gcode.py --selftest
C:\Users\Administrator\AppData\Local\Programs\Python\Python312\python.exe tools\validate_gcode.py dist\selftest\a_mixed.nc 210 297
cmd /c tools\gui_check\gui_check.bat all    :: 冒烟 + 交互 + 配置 + 调平页，共 4 项
```

`tools/gui_check/gui_check.py` 只用 Win32 消息驱动，不依赖窗口位置与人工点击，可无人值守重复跑；
smoke 把截图写到 `docs/gui_smoke.png`，level 写到 `docs/gui_level.png`。
