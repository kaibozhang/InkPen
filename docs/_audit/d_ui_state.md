# 维度 D 审计：UI 状态机、线程与持久化（InkPen 0.1.0-M1）

发现 8 条（P0 1 / P1 3 / P2 4）

> 只读审计：未修改 `src/`、`tests/`、`build/`、`tools/`、`data/` 任何文件，未执行 `build.bat` / `test.bat` / `selftest.bat` / `gui_check.bat`，未启动 `dist\InkPen.exe`。唯一写操作是本文件。
> 所有行号均为读文件后确认的行号；布局类结论给出像素推算，并可用 `docs/gui_smoke.png`（12:00 之后拍的当前构建）目视复核；「全项目没有某调用」类结论已用 `grep -rn` 复核过。
> 与维度 C 的分工：C 管串口协议与探测状态机本身（如不发 `?`、WCO 丢失、续写行号语义），本维度只管 UI 侧的状态互斥、生命周期、持久化与绘制资源，不重复。

---

### D1 [P0] 书写中的手动指令守卫只覆盖 jog/试笔/自定义指令/宏，`$H`、设原点、`$$`、改 `$n=v` 全部直通

- 位置：`src/App/UI/MainForm.cs:213-215`（`$H 回零` / `$X 解锁` / `设原点 G10 L20 P1 X0 Y0 Z0` 三个按钮）、`325`（`读取 $$ 参数`）、`326-338`（$ 参数表双击回写 `$n=v`）；守卫本身在 `847-855`，只在 `237`、`244`、`860`、`879`、`907` 五处被调用。
- 触发条件：接真机 → 「▶ 开始书写」→ 确认 → 书写进行中切到「机器」页，点「$H 回零」（或「设原点」/「读取 $$ 参数」/在「$ 参数」页双击 `$102` 改值）。
- 现象：
  - 点「$H 回零」：机器立刻丢开工件坐标回零，而此时笔还压在纸上（落笔 Z），回零行程会在纸上拖出一条长直线；
  - 点「设原点」：`G10 L20 P1 X0 Y0 Z0` 把当前位置强行设为工件零点，之后继续发送的 GCode 坐标整体平移一个偏移量，字会写到纸外；
  - 点「读取 $$」或在参数表改值：`$$` 与 `$n=v` 各回一个 `ok`，而 `StreamController.HandleOk`（`src/App/Core/Grbl/GrblCore.cs:394-404`）把**任何** ok 都当成「自己发的那一行被确认」，于是 `_bufferFree` 提前回补、`_pendingLines` 提前出队 —— 字符计数流控整体错位，连续多发导致 GRBL 127 字节 RX 缓冲溢出，被静默丢行（断笔、错字，且不报错）。
- 为什么是 bug：`ManualBlocked` 的注释（`843-846`）声明的不变量是「书写期间不得有旁路指令插进字符计数流控之间」，`GrblCore.cs:406-409` 的注释也说明「盲发会让 ok 与在途行错位」；这一族命令全部绕过守卫。`$H`/`G10` 还额外破坏「书写期间工件坐标系不变」这一前提。
- 建议修法：在 `WireEvents` 里把所有会 `_grbl.SendCommand(...)` 的入口统一走一个 `SendGuarded(cmd)`（内部 `if (ManualBlocked()) return;`），至少覆盖 `_btnHome` / `_btnZero` / `_btnReadSettings` / `settingsGrid` 的双击回写；`$X` 解锁、`0x18` 软复位、`0x90-0x92` 倍率是单字节实时指令、不产生 ok，可以放行。
- 回归用例：`IsRunning == true` 时依次触发「$H」「$$」「$102=80」，断言串口 TX 序列里一个字节都没有（当前三条都会写出去，必红）。

---

### D2 [P1] `StartWriting` 只查串口和作业，不查「已在书写 / 正在探测」：能把整篇从头再写一遍，也能在探测中途启动书写

- 位置：`src/App/UI/MainForm.cs:1212-1216`（前置检查只有 `_job` / `fromLine` / `_grbl.IsOpen`）、`1239`（`_stream.Start(lines, 0)`，其内部先 `Stop()` 再从 0 发，见 `src/App/Core/Grbl/GrblCore.cs:442-443`）；反向的守卫只做了探测一侧（`MainForm.cs:1004-1013`）；探测窗是非模态的 `_probeDlg.Show(this)`（`1092`），主窗体在探测期间仍可点击。
- 触发条件：A) 书写进行中点「▶ 开始书写」并在确认框点「确定」；B) 网格探测进行中（探针没接线，每点都在等下探超时）点「▶ 开始书写」。
- 现象：
  - A：旧作业被静默掐断，新线程从第 0 行重发，纸上前半篇被再描一遍（笔画加粗、字形重影），进度条从 0 开始，全程没有任何「已在书写」提示；用户若连点两次就得到一张废纸。
  - B：探测的 `G38.2` 下探行与书写 GCode 插在同一个 RX 缓冲里；探测产生的 ok 被流控计成书写的确认行（同上 HandleOk 语义），`_probe.FeedLine` 又要从同一批回报里抢 `[PRB:]` —— 两边都错：探针可能在落笔状态下横移，同时书写流控溢出丢行。
- 为什么是 bug：作业状态机里「运行中」必须是单占有者的互斥态。`ProbePreflight`（`1004`）明确承认了这个不变量（书写中不许探测），但反方向没有对称实现，`_btnStart` 也从来不置灰。
- 建议修法：`StartWriting` 开头补两条 `if (_stream.IsRunning) { Log("已在书写，请先停止"); return; }` / `if (_probe.Running) { Log("探测进行中，不能开始书写"); return; }`；进一步在 run 开始把 `_btnStart.Enabled = false`，在 `OnProgress` 的 finish/stop/error 分支里恢复。
- 回归用例：作业运行中再次 `StartWriting(0)`，断言 TX 序列里不出现第二批 `G21/G90/G94` 头（当前会出现，必红）。

---

### D3 [P1] 探测对话框的「中止」和窗口关闭都不真的中止：`CancelRequested` 是个没人读的字段

- 位置：`src/App/UI/ProbeDialog.cs:21`（属性定义）、`69-75`（按钮只置位 + 变灰）、`117-122`（点 X 也只置位）；真正调 `_probe.Cancel()` 的只有 `src/App/UI/MainForm.cs:271`（调平页的「停止」按钮）。全项目 `grep -rn CancelRequested` 只有上面 3 处命中，无任何读取点。
- 触发条件：点「开始探测」（3×3 网格，探针未接线）→ 在探测对话框里点「中止」；或直接点窗口右上角 X 关掉它。
- 现象：按钮变灰、大标题变成「正在中止…」，但机器仍在按计划逐点下探。`ProbeRunner.Poll` 每点必须等满 `TimeoutMs = 20000`（`src/App/Core/Machine/ProbeRunner.cs:29`），3×3 最坏约 3 分钟才收场；点 X 关窗的情况更糟：探测继续跑，而 `_probeDlg` 仍非空，结束时 `FinishProbe` 还会对已关闭的窗体调 `Finish()`（`ProbeDialog.cs:99-105` 改 `_big.Text` / `_cancel.Enabled`）。用户看到的是「窗口都关了，机器还在动」，且全程只有日志。
- 为什么是 bug：按钮文案承诺「中止」，实现只改了一个没人消费的 flag；`ProbeDialog` 的类注释（`7-9`）说「两者通过 MainForm 的回调连起来」，但取消这条边根本没接。
- 建议修法：`ProbeDialog` 增加 `public Action OnCancel;`，按钮与 `OnFormClosing` 里调用它；`BeginProbe`（`MainForm.cs:1074-1093`）里挂 `OnCancel = _probe.Cancel`，并加 `_probeDlg.FormClosed += delegate { if (_probe.Running) _probe.Cancel(); }`；`FinishProbe` 里对 `_probeDlg` 先判 `IsDisposed`。
- 回归用例：假串口 + `ProbeRunner`：调用「中止」后断言 `Running == false` 且 `OnFinished` 恰好被回调一次（当前 `Running` 仍为 true，必红）。

---

### D4 [P1] 「断开后自动重连」整条链路三处都没接上，勾选它等于没有安全网（默认还是勾选态）

- 位置：
  1. `src/App/UI/MainForm.cs:476`：`if (_reconnectPolicy.Port.Length == 0) return;` —— 而全项目**没有任何一处**给 `_reconnectPolicy.Port` 赋值（`40` 行 new 出来后只有 `476/503/507` 三处读取），所以 `ScheduleReconnect` 永远在第一行返回，`_reconnectTimer` 永不启动（唯一 `Start()` 在 `480`，位于该 return 之后）。
  2. `src/App/Core/Grbl/GrblCore.cs:139-148`：`DataReceived` 的异常只 `Log("RX error")`，不调 `Disconnect()` —— 拔线根本不会触发 `OnConnectionChanged(false)`，`ScheduleReconnect` 连被调用的机会都没有。
  3. `src/App/UI/MainForm.cs:613-664`：`ApplyConfigToUi` 里没有 `_portCombo` / `_baudCombo` 的赋值，而 `834` 行把端口写进了 `_cfg.PortName`（会随「保存配置」落盘，`dist/config/config.json` 里就有 `"PortName"` 字段）。
- 触发条件：选 COM3 连接 → 「保存配置」→ 重启程序；另：书写中拔掉 USB 线。
- 现象：重启后端口框是空的（`RefreshPorts` `817-825` 只会填成「串口列表第一个」），每次都要重选；拔线后状态栏继续显示「已连接」和绿色胶囊（`OnConnectionChanged` 只在 `Disconnect()`/`Connect()` 内发），日志刷一串 `RX error`，界面永远不会出现 `481` 行那句「连接断开，1s 后开始自动重连」，也不会有任何重连尝试。
- 为什么是 bug：`AutoReconnect` 默认 true、被写进配置文件、UI 上有勾选框、`ReconnectPolicy` 有完整的 8 次指数退避与 `Describe()` 文案，整条安全网只差三步接线；用户据此认为掉线会自动恢复并继续书写，实际是静默失效。同时「保存配置→读取配置」对连接参数是无效的（读回来也不显示、不生效）。
- 建议修法：`ToggleConnect` 连接成功时同步 `_reconnectPolicy.Port = port; _reconnectPolicy.Baud = baud;`；`DataReceived` 在 `IOException`/`InvalidOperationException` 后调 `Disconnect()`（由此自然触发 `OnConnectionChanged(false)`）；`ApplyConfigToUi` 补 `_portCombo.Text = _cfg.PortName` 并把 `_baudCombo.Text` 一起回填（`DropDownStyle` 已是 `DropDown`，允许列表外的值）。
- 回归用例：① 读到 `PortName:"COM3"` 的配置后断言 `_portCombo.Text == "COM3"`（当前为空，必红）；② 模拟 `OnConnectionChanged(false)` 后断言 `_reconnectTimer.Enabled == true` 且 `policy.Port == "COM3"`（当前定时器不启动，必红）。

---

### D5 [P2] 画布与状态条每次重绘各泄漏 1 个 `SolidBrush` + 2 个 `StringFormat`

- 位置：`src/App/UI/Canvas/PaperCanvas.cs:376`（`g.FillRectangle(new SolidBrush(Theme.Paper), paper);` —— 未用 using）、`246`（无作业分支里的 `StringFormat sf` 未释放）、`src/App/UI/Canvas/Widgets.cs:127`、`137`（状态徽标两处 `StringFormat` 未释放）。
- 触发条件：接真机书写或播放仿真（笔尖平滑动画 33ms 一帧，`PaperCanvas.cs:67` + `108` 的 `_anim.Start()`；状态条还要在每个进度回调里 `Invalidate`，`MainForm.cs:409`）。
- 现象：每个 `OnPaint` 产生 1~3 个未释放的 GDI+ 对象，只能等终结器回收。连续书写时每秒几十次重绘，句柄峰值与 GC 压力持续抬高，长时间运行表现为界面逐渐发涩、掉帧，极端情况下抛 `GDI+ 中发生一般性错误`。
- 为什么是 bug：同一文件其余二十多处画笔画刷都规规矩矩 `using`（`272`、`283`、`298-300`、`353`、`359`、`373`…），这三处遗漏不是风格问题，而是把资源生命周期交给终结器的错误，且正好落在最高频的绘制路径上（`PaintPaper` 每帧必走）。
- 建议修法：`using (SolidBrush paperBrush = new SolidBrush(Theme.Paper)) g.FillRectangle(paperBrush, paper);`；`StringFormat` 内容是固定的，提成字段复用（或就近 `using`，注意 `DrawString` 用完再释放）。
- 回归用例：连续 `Invalidate()` + `Update()` 1000 次后，用 `GetGuiResources(hProc, 0)` 采样 GDI+ 对象数，断言增量不超过常数（当前随重绘次数线性增长，必红）。

---

### D6 [P2] 书写中改任意参数（甚至只是拖一下「手写感」滑块）就会把 `_job`/画布/进度条换成新作业，而串口仍在发旧 GCode

- 位置：`src/App/UI/MainForm.cs:134-138`（5 个 TrackBar 都接 `ScheduleGenerate`）→ `557-561`（320ms 防抖）→ `745-790`：`760 _job = j`、`761 _pen.SetJob(j)`、`763-764 _canvas.SetJob(j) / _sim.SetJob(j)`、`781-783` 进度条归零；而真正在发的行是 `src/App/Core/Grbl/GrblCore.cs:463` 的列表快照。
- 触发条件：开始书写后，在「设置」页拖动任意一个手写感滑块（字号/位置/笔画/基线/笔锋），或改「安全 Z」、或直接点左栏「⚡ 生成 GCode / 预览」。
- 现象：中型预览立刻换成按新参数重排的文稿（与纸上正在写的那篇已经不是同一份）；`_pen.SetJob` 重置游标、`_canvas.SetJob` 把 `Realtime/ProgressPath/ShowPen` 复位、进度条闪回 0；随后的进度回调把**旧作业的行号**喂给**新 job**（`389` 行 `_pen.SetSentLines(sent)`，`PenTracker` 内部用它索引 `_job.GCode` / `_job.Paths`），于是「第 k / N 笔」和画布上的笔画高亮跳到不相干的笔画；停止后再点「断点续写」，`FindResumeLine` 也是按新稿算的，字符序号与纸上那篇不对应。
- 为什么是 bug：`_job` 同时承担「正在发送的作业」和「当前预览」两个角色，写作业期间它必须是不可变快照；生成动作对运行态没有任何守卫，违反「发送中的作业与展示中的作业一致」这一不变量。
- 建议修法：`_stream.IsRunning` 时把生成请求挂起（`_pendingGenerate = true`，在 finish/stop/error 回调里补跑一次），或生成到临时 `Job` 只刷新 `_canvas`/`_gcodeBox` 而不替换 `_job`/`_pen` 的引用。
- 回归用例：书写中触发一次 `Generate()`，断言 `_job` 引用（或用 `_pen` 记录的作业）与 `_stream` 启动时那一份仍是同一对象（当前被替换，必红）。

---

### D7 [P2] 拦截文案让用户「先暂停再干预」，但暂停态 `IsRunning` 仍为真，照做依然被拒

- 位置：`src/App/UI/MainForm.cs:851`（提示语「…如需干预请先「暂停」」）、`849`（判定条件 `_stream.IsRunning`）、`src/App/Core/Grbl/GrblCore.cs:476-488`（暂停只是置 `_pauseReq` 并 `Thread.Sleep(60)`，worker 线程不退出，故 `IsRunning` 恒真）；该行为被 `tests/InkPen.Tests/LifecycleTests.cs:103-119`（`Pause_KeepsIsRunning_SoManualCommandsStayBlocked`）显式锁定。
- 触发条件：书写中点「暂停」（日志「已请求暂停（等待 IDLE）」）→ 点 X+ / Z− 或「试笔」。
- 现象：仍然只得到同一条「书写进行中，已忽略该手动指令…如需干预请先「暂停」」——用户已经暂停了。想微调笔位只剩「停止」一条路，而停止会丢掉续写位置（还要手工回填「从第几个字续写」）。用户按提示操作两次仍失败，会认为按钮坏了。
- 为什么是 bug：提示语承诺了一条不存在的放行路径 —— 属于「文案与实现明确矛盾」的文档 bug；单测锁的是实现，说明错的是文案。
- 建议修法：二选一 —— 把文案改成「需先点「停止」后再手动操作」；或真的在 `IsPaused` 且 `_pendingLines` 为空时放行 `$J`（把 `ManualBlocked` 拆成 `BlockedForJob()`（禁止 jog/试笔）与 `BlockedForFlow()`（禁止一切非实时指令）两种强度）。
- 回归用例：`IsPaused == true` 时断言「`ManualBlocked()` 的返回值」与「提示语描述的可行操作」一致 —— 提示语含「暂停」则必须返回 false，当前返回 true，必红。

---

### D8 [P2] 写死像素装不下中文：jog 组的「按 10% 步进」被分组框裁掉，同款提示宽度普遍偏窄

- 位置：`src/App/UI/MainForm.Design.cs:527`（`L(jog, "按 10% 步进", 248, 152, 56)`），所在分组框是 `498` 行 `G(p, "手动 jog / 回零 / 进给倍率", 8, 270, 306, 196)`；同类见 `331`（`K(doc, "两端对齐", false, 210, 402, 62)`）。
- 触发条件：默认窗口打开右栏「机器」页即可见（无需任何操作）。
- 现象：像素推算 —— GroupBox 宽 306，扣左右边框各 3px，**客户区宽 300**；该 Label 的 `x = 248`、`w = 56`，右缘 304 已**越出客户区 4px**，因此它最多只有 `300 − 248 = 52px` 的可见宽度。而「按 10% 步进」在 9pt 微软雅黑下约为 按 12 + 空格 4 + `1`7 + `0`7 + `%`11 + 空格 4 + 步 12 + 进 12 ≈ **69px**，比可见宽度多 17px —— 无论 Label 是否 `AutoSize`，都必然截断。`docs/gui_smoke.png` 里该处只剩「按 10%」，与推算吻合。后果不只是难看：这一排「−50% / −10% / +10% / +50%」的语义完全依赖这句说明，说明丢了之后「+50%」很容易被读成「把倍率设为 50%」，而实现是连发 5 次 `0x91`（`MainForm.cs:223-233`）。同款问题：「两端对齐」4 个汉字约 48px + 复选框约 13px + 内边距，约需 64~67px，而框宽只有 62px（截图里只剩「两端」）。
- 为什么是 bug：布局用写死像素 + 中文文案，控件宽度小于自身文本所需宽度，且越过父容器客户区右缘；被裁掉的文字恰好承担解释语义的职责，属于「文案与实现语义脱节」的可见缺陷，而不是纯审美问题。
- 建议修法：「按 10% 步进」左移到 `x = 196` 并把 `w` 放到 96（`196 + 96 = 292 < 300`），或把该提示挪到分组框标题里（`"手动 jog / 回零 / 进给倍率（±10% 步进）"`）；`两端对齐` 的 `w` 放到 70 并把「居中」左移，避免与紧邻控件贴边。
- 回归用例：一个布局契约测试，遍历分组框内所有 `Label`/`CheckBox`，断言 `c.Left + c.PreferredWidth <= parent.ClientSize.Width`（当前 248+69=317 > 300，必红）。
