# 维度 E：工程健壮性与一致性（横向审计）

- 审计对象：`D:\Writer\InkPen`，C# 5 / WinForms / .NET Framework 4.8，零外部 DLL
- 范围：`src/App/**`、`build/**`、`tools/**`、`docs/*.md`、`README.md`、`NOTICE.md`、`.gitignore`（不含 `src/Upstream/**`）
- 口径：只读。未运行 `build.bat` / `test.bat` / `selftest.bat` / `gui_check.bat`，未启动 `dist\InkPen.exe`
- 取证方式：Read/Grep/Glob + Python 全量扫描（编码/BOM/换行/行长/字段引用计数）+ PowerShell 只读查询（ACP、程序集版本）

**发现 8 条（P0 1 / P1 4 / P2 3）**，另有配置项孤儿清单 6 项。

---

### E1 [P0] 真机发送与导出取的是 TextBox 内容，长稿件被 32767 字符上限静默截断
- 位置：`src/App/UI/MainForm.cs:1223`（发送源）、`src/App/UI/MainForm.cs:801`（导出）、`src/App/UI/MainForm.Design.cs:643`（控件创建）、`src/App/UI/MainForm.Design.cs:223-235`（`T()` 工厂）
- 触发条件 / 证据：
  - `StartWriting`：`string src = fromLine > 0 ? GCodeBuilder.ToText(_job, fromLine) : _gcodeBox.Text;`——起始行为 0 时不再从 `_job.GCode` 取，而是从 UI 文本框取。
  - `ExportGCode`：`File.WriteAllText(d.FileName, _gcodeBox.Text, Encoding.ASCII);`。
  - `T()` 只设置 Font/Multiline/ScrollBars/WordWrap/AcceptsReturn/BorderStyle，**全项目没有一处设置 `MaxLength`**（`grep -rn "MaxLength" src/App` 无结果，默认 32767）。
  - 本仓库自检样例 `dist/selftest/d_multipage.nc` = **499,053 字符 / 26,828 行**（README 验收文档里明确提到这份 4 页大稿），是默认上限的 15 倍。
- 现象与影响：`Generate()` 里 `_gcodeBox.Text = ToText(j, 0)` 赋值时被截到 32767 字符（约 1,760 行，占 26,828 行的 6.6%）；随后「开始书写」把这份**缺了 93%** 的文本发给机器，进度条还按截断后的行数显示「已完成」。导出 `.nc` 同样是残缺文件。截断点通常落在某行中间，会向 GRBL 发送半条指令（如 `G1 X12`），造成位置错误或报错停笔。
- 为什么是 bug：机器指令的来源应是 `Job.GCode`，不该是显示控件；即便要支持「手动编辑 GCode 优先」，也必须先把上限放开并与作业长度对齐。同一函数里 `fromLine > 0` 分支用作业、`fromLine == 0` 分支用控件，两条路径行为不一致，续写正确、从头写错误，属于典型「只在长稿上才炸」的静默数据丢失。
- 建议修法：`T()` 创建后对 `_txt`/`_gcodeBox`/`_logBox` 统一 `MaxLength = 0`（或 `int.MaxValue`）；发送改走 `GCodeBuilder.ToText(_job, fromLine)`，仅当 `_gcodeBox` 被用户改动过（`TextChanged` 标记）时才用文本框内容；导出同理。
- 回归用例 / 防复发检查：`gui_check` 增加一条断言——生成 4 页大稿后 `_gcodeBox.TextLength == GCodeBuilder.ToText(job, 0).Length`（或直接断言 `_gcodeBox.MaxLength == 0`），并在 `ExportGCode` 前比对导出行数与 `job.GCode.Count`。

### E2 [P1] 「自动重连」永远不会触发：策略里的 Port 从未被赋值
- 位置：`src/App/UI/MainForm.cs:476`（唯一的守卫）、`src/App/UI/MainForm.cs:40`（实例化）、`src/App/Core/Grbl/PenTracker.cs:135-136`（`Port`/`Baud` 字段）
- 触发条件 / 证据：
  - `grep -rn "_reconnectPolicy" src/App` 共 11 处，全部是 `Reset()/Port.Length/Port/Baud/Describe()/NoteAttempt()` 的**读**；没有任何一处写 `_reconnectPolicy.Port = ...` 或 `.Baud = ...`。
  - `ScheduleReconnect()` 第一段就 `if (_reconnectPolicy.Port.Length == 0) return;`，`Port` 初值为 `""`（`PenTracker.cs:135`），因此本函数 100% 提前返回，`_reconnectTimer` 从不 `Start()`。
  - 文档明确宣称已实现：`README.md:36`「自动重连 … 指数退避 1→2→4→8→16→30 s 封顶，默认 8 次」、`docs/feature_gap_vs_grbl_plotter.md:17`「A3 自动重连 ✅」；而 `docs/M1_acceptance.md:197` 又把它列在「未完成 / 边界 → 真机联调」里的待实测项，说明这条路径从未被真正跑通。
- 现象与影响：拔 USB / 机器休眠后，状态栏只显示「连接断开」，不会重连；长时间无人值守书写会在半途停住（笔可能停在纸上）。用户勾着「断开后自动重连」却完全没有效果。
- 为什么是 bug：这是「说得到做不到」——退避算法、计时器、日志文案、提示「需重新设原点」全都写好了，唯独没有把连接参数交给策略，等于整条链路悬空。现有单测只测 `ReconnectPolicy` 本身（纯状态机），不测它与 `GrblClient` 的接线，所以 140 个用例全绿。
- 建议修法：在 `ToggleConnect()` 连接成功后（`MainForm.cs:835` 分支）写 `_reconnectPolicy.Port = port; _reconnectPolicy.Baud = baud;`；或在 `ScheduleReconnect()` 里从 `_cfg.PortName/BaudRate` 兜底并回填。
- 回归用例 / 防复发检查：加一条静态检查进构建脚本：`grep -q "_reconnectPolicy.Port =" src/App/UI/MainForm.cs` 否则失败；或补一条 UiContract 用例「连接后策略 Port 等于当前端口」。

### E3 [P1] 构建清单硬编码 `D:\Writer\InkPen`，换目录/路径含空格即构建失败
- 位置：`build/sources.rsp:3`、`build/sources.rsp:16`、`build/selftest.rsp:3`、`build/selftest.rsp:12-20`
- 触发条件 / 证据：
  - `sources.rsp`：`/out:D:\Writer\InkPen\dist\InkPen.exe`、`/recurse:D:\Writer\InkPen\src\App\*.cs`
  - `selftest.rsp`：`/out:D:\Writer\InkPen\dist\InkPen.SelfTest.exe` + 9 条 `D:\Writer\InkPen\...` 源文件
  - 同目录的 `build.bat:8` 却算 `set "ROOT=%~dp0.."`，`pack.bat` 也用 `%ROOT%`；即打包步骤可移植、编译步骤不可移植。
  - `README.md:107/114/126`、`docs/M1_acceptance.md:214/220` 的验收命令也写死 `cd /d D:\Writer\InkPen`。
- 现象与影响：仓库拷到 `D:\InkPen`、`E:\work\InkPen`，或解压到 `D:\My Projects\InkPen`（含空格）后双击 `build.bat`：轻则 `error CS2001: 未能找到源文件`，含空格时 csc 会把 `/recurse:D:\My` 与 `Projects\InkPen\src\App\*.cs` 当成两个参数，`/out:` 同理。这是"换机器就跑不起来"的硬故障，且 `build.bat` 的报错只会说 Compile failed，不提示是路径问题。
- 为什么是 bug：rsp 是响应文件，路径不能靠 bat 的引号保护（rsp 里没有引号机制），必须由 bat 传入或改用相对路径。现在 `/out` 还指向固定盘符，分发给别人时即使编译成功也会往 `D:\Writer\InkPen\dist` 写（若目录不存在直接失败）。
- 建议修法：`sources.rsp` 只留开关与 `/r:`，源码与输出改由 `build.bat` 以 `"%CSC%" @sources.rsp /out:"%OUT%\InkPen.exe" /recurse:"%ROOT%\src\App\*.cs"` 追加；或改相对路径（csc 支持相对 `/out:` 与 `/recurse:`，cwd 由 bat 控制）。
- 回归用例 / 防复发检查：CI 增加一步「克隆到含空格路径后跑 build.bat」；静态检查 `grep -nE "[A-Za-z]:\\\\" build/*.rsp` 必须为空。

### E4 [P1] 源码 UTF-8 无 BOM、rsp 无 `/codepage`，构建产物是否乱码取决于构建机和编译器
- 位置：`build/sources.rsp:1-16`、`build/selftest.rsp:1-20`（均无 `/codepage:65001`）、全部 `.cs` 文件（无 BOM）
- 触发条件 / 证据：
  - Python 全量扫描（`src/App`、`build`、`tools`、`docs`、`tests`）：**所有文本文件 BOM 均为 `-`**，`.cs` 全部 `utf8-ok` 且 `nonascii`（含中文注释/字面量），没有一个带 BOM。
  - PowerShell 查询：`[System.Text.Encoding]::Default.CodePage = 936`，`HKLM\...\Nls\CodePage\ACP = 936`，即本机 ANSI 代码页是 GBK。
  - `dist/InkPen.exe` 内中文是正确的 UTF-16（`"写字机工作台".encode('utf-16le') in exe == True`），说明**当前这份产物**是以 UTF-8 解码的（Roslyn/MSBuild 链会先试 UTF-8）。
  - `build.bat:9` 用的是 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（in-box C# 5 编译器，配合 `/langversion:5`），该编译器对无 BOM 源文件按系统 ANSI 代码页解码，除非给 `/codepage`。
- 现象与影响：在 ACP=936 的机器上走 `build.bat`，UTF-8 中文被按 GBK 解码：界面文案变成 `鍐欏瓧鏈哄伐浣滃彴` 一类乱码；更糟的是 GBK 尾字节范围 0x40–0xFE 覆盖 `"`(0x22) 与 `\`(0x5C)，某些汉字序列会吞掉紧随的引号/反斜杠，导致 `CS1010/CS1002`（引号未闭合）或字符串内容错位——即「同一份源码，csc 链编不过或编出乱码，MSBuild 链正常」，与 `README.md:22/87` 声称的「两条链产物一致」相矛盾。
- 为什么是 bug：构建结果依赖机器区域设置，属于不可复现构建；而且乱码不会报错，只在界面上体现，最容易被当成"字体问题"排查掉。
- 建议修法：两个 rsp 各加一行 `/codepage:65001`（或给所有 `.cs` 加 UTF-8 BOM）；CI 里固定用 `/codepage:65001`，避免依赖 ACP。
- 回归用例 / 防复发检查：构建脚本断言 `grep -c "/codepage:65001" build/*.rsp == 2`；或加一条“生成的 exe 中必须能找到 `写字机工作台` 的 UTF-16 序列”的冒烟检查。

### E5 [P1] 配置/宏/高度图都是"直接覆盖写"，读失败又静默回落默认值——断电即丢标定
- 位置：`src/App/UI/MainForm.cs:690-692`（config）、`src/App/UI/MainForm.cs:974-975`（macros）、`src/App/UI/MainForm.cs:1181-1182`（heightmap）、`src/App/UI/MainForm.cs:700-708`（读取失败只记日志）
- 触发条件 / 证据：
  ```csharp
  Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
  File.WriteAllText(_configPath, ConfigBinder.Serialize(...), Utf8NoBom);   // 先截断再写
  ...
  catch (Exception ex) { Log("读取配置失败: " + ex.Message); }              // 然后继续用默认 _cfg
  ```
  三处写文件均为 `File.WriteAllText` 原地覆盖，没有 `.tmp` + `File.Replace`，也没有 `.bak`；读取时 `catch` 之后不弹窗、不阻止写入，`_cfg` 保持 `new MachineConfig()` 的默认值（SafeZ=5 / WriteZ=0 / 原点左下 / 不反向）。
- 现象与影响：保存瞬间断电、磁盘满、杀进程，`config.json` 变成半截 JSON；下次启动解析失败 → 用户此前的幅面、原点角、X/Y 反向、Z 标定、ack 超时**全部静默回默认**，日志框里只有一行小字。用户按「开始书写」时以为标定还在，实际可能按错误方向/错误 Z 落笔，直接划纸。
- 为什么是 bug：这是唯一持久化标定结果的通道，却是非原子的写 + 静默降级的读；两者叠加把"一次掉电"放大成"机器按未标定参数动作"。README:39 声称「保存配置 → 下次直接复用」，此路径下不成立。
- 建议修法：写 `config.json.tmp` → `Flush(true)` → `File.Replace(tmp, target, target + ".bak")`；`LoadConfig` 失败时优先读 `.bak`，两者都失败则弹窗明确告知"配置已损坏，已使用默认值，请重新标定"并禁止直接开始书写。
- 回归用例 / 防复发检查：单测注入只读目录/磁盘满（写临时目录后 `File.SetAttributes(ReadOnly)`）断言旧文件仍可解析；另加一条截图/UI 断言：传入半截 JSON 时必须出现警告对话框。

### E6 [P2] `selftest.rsp` 与 `sources.rsp` 源清单漂移；`/recurse` 还会把 `obj` 里的生成文件编进去
- 位置：`build/selftest.rsp:12-20`、`build/sources.rsp:16`、`src/App/obj/Release/.NETFramework,Version=v4.8.AssemblyAttributes.cs`
- 触发条件 / 证据：
  - `src/App/**` 共 **20** 个 `.cs`；`selftest.rsp` 只列 **8** 个 App 文件 + `tools/selftest/SelfTest.cs`。缺失的 Core 文件是：`Core/Config/ConfigBinder.cs`、`Core/Grbl/GrblCore.cs`、`Core/Grbl/PenTracker.cs`、`Core/Machine/ProbeRunner.cs`（其余 6 个 UI 文件 + `Program.cs`/`AssemblyInfo.cs` 不参与自检属合理）。
  - `sources.rsp` 用 `/recurse:...\src\App\*.cs` 自动收纳；`selftest.rsp` 逐文件手写 → 新增文件必须手工补两处，天然会漂。
  - 现有 `src/App/obj/Release/.NETFramework,Version=v4.8.AssemblyAttributes.cs` 正好命中 `/recurse` 的匹配式：`build.bat` 的编译单元会随「之前是否跑过 MSBuild」而变化；按 `src/App/**` 做静态检查的脚本也会把生成物算进去。
- 现象与影响：`ConfigBinder`（配置往返）、`GrblCore`（协议/流控）、`PenTracker`、`ProbeRunner` 这四个文件里的编译错误**不会**被 `selftest.bat` 发现，只有跑 `test.bat` 才暴露；一旦 `SelfTest.cs` 将来引用其中任一类型，`selftest.bat` 立刻 CS0246，而 `build.bat` 却正常——正是历史上踩过的"自检清单漏文件"。
- 为什么是 bug：两条构建链对"源文件集合"的定义不一致，且其中一条靠手工维护；`/recurse` 顺带把 MSBuild 生成的中间文件拉进 csc 编译单元，使 `build.bat` 的结果依赖于"之前是否跑过 MSBuild"（若将来 `obj` 里出现自动 `AssemblyInfo.cs`，会直接 `CS0579 重复特性`）。
- 建议修法：`selftest.rsp` 也改成 `/recurse:"%ROOT%\src\App\*.cs"` + `/main:InkPen.SelfTest` 式入口（或把 Core 与 UI 分目录，自检只 recurse Core）；两处 `obj/bin` 通过把 `BaseIntermediateOutputPath` 移出 `src/App` 解决。
- 回归用例 / 防复发检查：脚本比对 `git ls-files 'src/App/**/*.cs'` 与 rsp 展开后的文件集，差集非空即失败；`selftest.bat` 结束前 `grep` 编译输出中不应出现 `obj\`。

### E7 [P2] 版本号三处不一致，且 `version.txt` 的测试数已过期
- 位置：`src/App/Properties/AssemblyInfo.cs:11-12`、`build/version.txt:1,7`、`README.md:10`
- 触发条件 / 证据：
  - `AssemblyInfo.cs`：`AssemblyVersion("0.1.0.0")` / `AssemblyFileVersion("0.1.0.0")`
  - `build/version.txt`：`InkPen 0.1.1-M1`，且第 7 行写 `unit tests (95/95)`
  - `README.md:10`：`0.1.1-M1`；README 的测试表统计 = **140** 条
  - 实测 `grep -rh "TestMethod" tests/InkPen.Tests/*.cs | wc -l` = **140**；`dist/InkPen.exe` 资源里只有 `0.1.0.0`，没有 `0.1.1`
- 现象与影响：资源管理器/文件属性/杀软清单里显示 0.1.0.0，README 与 version.txt 写 0.1.1-M1，用户报 bug 时无法判断跑的是哪一版；`version.txt` 的 95/95 会让人误以为测试覆盖退化了三分之一。打包脚本 `pack.bat` 把 `version.txt` 原样拷进 `dist`，错误信息会随分发包扩散。
- 为什么是 bug：版本是唯一的"产物身份"，三处不一致等于没有身份；这类偏差还会掩盖"改了代码但版本没动"的发布事故。
- 建议修法：以 `build/version.txt` 为单一来源，构建时用 `/define` 或生成 `AssemblyVersion.cs`；三个数字（AssemblyVersion / AssemblyFileVersion / 展示串）由脚本同步；测试数改为构建时实测写入，不手写。
- 回归用例 / 防复发检查：构建后 `(Get-Item dist\InkPen.exe).VersionInfo.FileVersion` 必须以 `version.txt` 的 `0.1.1` 开头，否则 `build.bat` 返回非 0。

### E8 [P2] 画布每帧泄漏 GDI 对象，点阵背景逐帧重绘
- 位置：`src/App/UI/Canvas/PaperCanvas.cs:376`（未释放的 `SolidBrush`）、`PaperCanvas.cs:246`（未释放的 `StringFormat`）、`PaperCanvas.cs:359-364`（点阵）、`PaperCanvas.cs:67`（33 ms 动画定时器）
- 触发条件 / 证据：
  ```csharp
  g.FillRectangle(new SolidBrush(Theme.Paper), paper);   // 每帧 new，无 using
  StringFormat sf = new StringFormat();                  // 空态每帧 new，无 Dispose
  for (int y = 12; y < r.Height; y += 26)
      for (int x = 12; x < r.Width; x += 26)
          g.FillRectangle(dot, x, y, 1.6f, 1.6f);        // 727×800 画布 ≈ 840 次/帧
  ```
- 现象与影响：实时跟随笔尖时 `_anim` 以 30 fps 持续 `Invalidate()`，即每帧至少 1 个未释放的 Brush（靠终结器回收）+ 840 次 `FillRectangle` + 整页笔画重绘（每条 `WritePath` 还要新建 `PointF[]`）。长时间书写（几十分钟）会看到 GDI 句柄缓慢增长、拖动/缩放掉帧；GDI 句柄是进程级稀缺资源，达到上限后整个界面绘制失败。
- 为什么是 bug：`SolidBrush` 持有原生 GDI+ 句柄，未 Dispose 时只有 GC 触发终结器才释放，而 30 fps 的分配速度远高于终结器吞吐；点阵背景是静态图案，完全不该每帧重算。
- 建议修法：`using (SolidBrush paperBrush = ...)` 或把常用 Brush/Pen 提为字段并 `Dispose`；点阵背景缓存成 `Bitmap`（`OnResize` 时重建）后一次 `DrawImage`；`StringFormat` 改为静态只读并复用。
- 回归用例 / 防复发检查：写一个 10 分钟压力脚本（`--tab=machine` + 模拟 30 fps `Invalidate`），前后 `GetGuiResources(hProcess, GR_GDIOBJECTS)` 差值必须 < 50；或静态检查 `OnPaint` 路径内 `new SolidBrush(` 不得出现在非 `using` 语句中。

---

## 配置项孤儿清单

判定标准：字段/控件存在，但没有任何**消费方**读取（写入、序列化不算）。

| # | 位置 | 现象 | 证据 | 影响 |
|---|---|---|---|---|
| 1 | `MachineConfig.RapidFeed`（`Model.cs:91`） | 定义了 3000 mm/min，无任何代码读取 | `grep -rn "\.RapidFeed" src/App` 0 命中；`G0` 行从不带 `F`（`GCodeBuilder.cs:282/230`） | 用户以为能调快空程，实际 GRBL 用 `$110-112` 的最大速率，改这个值 GCode 一字不变 |
| 2 | `MachineConfig.AutoDetect`（`Model.cs:101`） | 字段与「自动探测」按钮同名，但字段从不被读 | 唯一命中 `MainForm.cs:187` 是 `_grbl.AutoDetect(...)` 方法调用，不是 `_cfg.AutoDetect` | 配置里存了 `AutoDetect:true`，启动时并不会自动探测端口，纯装饰 |
| 3 | `MachineConfig.PortName` / `BaudRate`（`Model.cs:99-100`） | 只在「连接」时写入（`MainForm.cs:834`），`ApplyConfigToUi` 从不回填 `_portCombo/_baudCombo` | `ApplyConfigToUi`（`MainForm.cs:613-664`）里没有 `_portCombo.Text = _cfg.PortName`；`dist/config/config.json` 里 `"PortName":""` | 端口/波特率存了也读不回来 → 每次启动都要重选端口；「记住上次端口」形同虚设 |
| 4 | `TemplateConfig.Name`（`Model.cs:62`） | 默认 `"横线稿纸"`，切换模板时也不更新 | 源码中无 `_tpl.Name` 读写；`ApplyUiToConfig`（`MainForm.cs:566-576`）没有给 `Name` 赋值；只有 `ConfigBinder` 的 `FieldInfo.Name` 命中（不是该字段） | `config.json` 永远保存 `"横线稿纸"`，即使当前是田字格/拼音四线；将来做「模板另存为」时会立刻撞上 |
| 5 | `HeightMap.Enabled`（`HeightMap.cs:18`） | 写入（`MainForm.cs:609`、`1148`）、序列化（`HeightMap.ToJson`），但补偿判定不使用它 | `GCodeBuilder.CompensatedZ`（`GCodeBuilder.cs:247-255`）只看 `m.UseHeightMap` 与 `map.IsUsable`；`grep "\.Enabled"` 无 `map.Enabled` 读取 | 高度图 JSON 里的 `enabled` 是"假开关"：手改成 true 也不会生效，排查时极易误判 |
| 6 | `TemplateConfig.MarginLeft/Right/Top/Bottom`（`Model.cs:65`） | 布局代码读，但 UI 完全没有对应控件 | `grep -n "Margin" src/App/UI/MainForm.Design.cs` 无命中；`BuildDocTab` 里只有字号/字距/行距/首行缩进 | 用户只能手改 `config.json` 才能调页边距；界面上"排版参数"看起来齐全，实际缺一项最常用的 |

**UI 控件反向检查（有控件无逻辑）：未发现。** 脚本扫描 `MainForm.Design.cs` 中 `B()` 创建的 36 个按钮 + `K()` 创建的 9 个复选，共 45 个：44 个绑定了 `Click/CheckedChanged/MouseDown/DoubleClick`；唯一没绑事件的 `_autoReconnect` 是开关型复选，代码在 `MainForm.cs:475/486/600/650` 直接读 `.Checked`，属于正常用法。`_ovFine`、`_macroBtn` 通过 `foreach` + `Tag` 批量接线，`_btnHm*`、`_btnProbe*` 均已在 `WireEvents()` 中接线。

---

### 附：本次检查通过的项目（供后续复用）
- 编码：全部源文件为 UTF-8、无 BOM、LF；`.bat`/`.rsp` 纯 ASCII（cmd OEM 代码页不会破坏解析；`test.bat`/`pack.bat` 注释也明确写了这条约束）。
- 零外部 DLL：`find dist -name "*.dll"` 为空；`sources.rsp` 与 `InkPen.csproj` 只引用 .NET Framework 自带程序集，两处 `/r:` 列表一致。
- 不写注册表：`grep -rn "Registry" src/App` 0 命中。
- 不联网：`grep -rn "HttpClient|WebClient|HttpWebRequest|Socket|https?://" src/App` 0 命中。
- 80 字节硬拦截三处一致：`GCodeValidator`（`GCodeBuilder.cs:139-142`）、`StreamController`（`GrblCore.cs:499-505`）、`validate_gcode.py:59-63` 都是「去空格后 >80 即失败」。
- 断点续写：`GCodeBuilder.FindResumeLine` 越界返回末尾（不返回 0），`ToText(fromLine)` 补发 `G90/G21/G94/F` 模态恢复，逻辑与 README 一致。
- 文件路径拼接：App 内全部走 `AppDomain.CurrentDomain.BaseDirectory` + `Path.Combine`，无相对 cwd 依赖；写文件前均有 `Directory.CreateDirectory`。
- MSBuild 工程清单：`InkPen.csproj` 与 `InkPen.Tests.csproj` 均用 `**\*.cs` + 排除 `obj/bin`，20 个源文件全部覆盖，无遗漏。
