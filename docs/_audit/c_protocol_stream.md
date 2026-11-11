# 维度 C 审计：串口协议、流控与探测状态机（InkPen 0.1.0-M1）

发现 8 条（P0 1 / P1 6 / P2 1）

> 只读审计：未修改 `src/`、`tests/`、`build/`、`tools/`、`data/` 任何文件，未执行 `build.bat` / `test.bat` / `selftest.bat` / `gui_check.bat`。
> 唯一写操作是本文件与 `docs/_audit/tmp_c/sim_resume_cursor.py`（C3 的复算脚本，`python docs/_audit/tmp_c/sim_resume_cursor.py` 可直接重跑）。
> 报文类结论均以「具体字节串」给出；凡涉及「全项目没有某调用」的结论均以 `grep -rn --include=*.cs src tests tools` 复核过。
> 与维度 B 的分工：B2 把「全项目无 `0x3F`」当作前提来解释单点对刀为何取到 (0,0)；本条 C1 是这个前提本身（状态管道整体不可用），C2 则是 MPos/WPos 的坐标系问题，二者不重复。

---

### C1 [P0] 从不发状态查询 `?`（0x3F）：整条 RX 状态管线从头到尾没有被启动过

- 位置：`src/App/Core/Grbl/GrblCore.cs:139-148`（唯一 RX 入口）、`189-195`（**只有**收到以 `<` 开头的行才更新 `Status` 并触发 `OnStatus`）；发送侧 `282-287`；订阅侧 `src/App/UI/MainForm.cs:374`
- 触发条件：连接后一直正常通信即可。全项目写入串口的单字节实时指令只有 `0x18`（`GrblCore.cs:112/609`、`MainForm.cs:216`）、`0x21`（`GrblCore.cs:481/607`）、`0x7E`（`GrblCore.cs:575`）、`0x90-0x92`（`MainForm.cs:218-220/230-231`）；`grep -rn "0x3F\|0x3f" src tests tools` **零命中**，也没有任何 `SendCommand("?")`。也就是说 TX 永远是 `$I\n` / `$$` / `G1X..` 这一族，没有那个单字节 `?`。
- 现象：GRBL 1.1 只在收到 `?` 时才回 `<Idle|MPos:...|Bf:15,128|Ov:100,100,100>`，不会主动推送。于是：
  - `Status` 永久停在 `Unknown / (0,0,0)`，`OnStatus` 一次都不触发 → DRO 三轴恒显示 `—`（`MainForm.cs:443-446` 是唯一写入点，`UI/Canvas/Widgets.cs:186` 用 `HasPos` 决定显示 `—`），状态胶囊恒为「已连接」（`Widgets.cs:173`），`Bf` 恒 −1、`Ov` 恒 100（`MainForm.cs:449-451`）；
  - `_pen.HasPosition` 永远为 false（只有 `PenTracker.cs:73` 会置真）→ 画布「实时笔位置」勾了也不画（`MainForm.cs:463` 直接 return）；
  - `ProbePreflight` 的 ALARM 前置检查（`MainForm.cs:1014`）永远看到 `Unknown`，永不补发 `$X`。
- 为什么是 bug：`GrblClient` 的类注释（`GrblCore.cs:45`）与 `OnStatus` / `Status` 的公开契约声明了「实时状态」，`ParseStatus` 连 `<Door:1>`、`<Bf:>`、`<Ov:>` 都实现好了，但生产代码里没有任何一处触发它——解析器有完整单测（`GrblProtocolTests` 全绿），管道却整条是死代码。这类「测试绿、功能不存在」的失效比解析错误更难发现，因为它连日志都不留。
- 建议修法：在 `GrblClient` 内加状态轮询（连接成功后每 100~200 ms `SendRealtime(0x3F)`，书写/探测期间加密到 50~100 ms），并记录 `LastStatusUtc`；`Ui` 侧在超过 1 s 没有状态报文时把胶囊置为「状态超时」而不是继续显示「已连接」。
- 回归用例：用记录写入字节的假串口连接后，断言 500 ms 内至少写过一次 `0x3F` 且 `Status.State != GrblState.Unknown`（当前 TX 里一次都没有，必红）。

---

### C2 [P1] 丢掉 `WCO:` 字段、把 MPos 当 WPos 用：点过「零点」之后，笔位置整体偏移一个工件偏移

- 位置：`src/App/Core/Grbl/GrblCore.cs:236-243`（只识别 `MPos:`/`WPos:`/`Ov:`/`Bf:`，`WCO:` 被静默丢弃）、`src/App/Core/Grbl/PenTracker.cs:66-68`（`_sawWPos` 为假就退回 MPos，注释断言「此时说明用户没设 G10 偏移，两者等价」）
- 触发条件：点一次「零点」按钮，它发的正是 `G10 L20 P1 X0 Y0 Z0`（`MainForm.cs:215`）；随后收到
  `<Idle|MPos:160.000,210.000,0.000|WCO:150.000,200.000,0.000|Bf:15,128|Ov:100,100,100>`
  （等价公式 `WPos = MPos − WCO`，真实工件坐标是 (10,10)）。默认 `$10=1` 的机器只回 MPos/WCO、不回 WPos（B2 同结论）。
- 现象：`_sawWPos=false` → `Paper = CoordTransform.ToPaper(new Pt(160,210), ...)`。笔尖真实在纸面 (10,10)，画布上却画在 (160,210)——往往直接落在纸外或另一行字上；`CompletedPaths`、`DistanceToCurrentStart()` 随之全错，用户只能靠这个画布判断「写到哪了 / 该不该停机」。
- 为什么是 bug：MPos 与 WPos 只有在 `WCO == 0` 时才相等，而本程序**自己的**设零点流程（`G10 L20`）必然把 WCO 设成非零——那条「两者等价」的注释与本程序的用法直接矛盾。更强的证据是 `ParseStatus` 明明在报文的 `|WCO:...|` 段上走了过去（`GrblCore.cs:236-243` 的循环体没有对应分支），信息就在手里被扔掉。
- 建议修法：`GrblStatus` 增加 `WCOX/WCOY/WCOZ` 并在 `ParseStatus` 里解析 `WCO:`；`PenTracker.SetStatus` 改成「有 `WPos:` 用 WPos；否则若收到过 WCO 用 `MPos−WCO`；两者都没有则退回 MPos 但把 `HasPosition` 置 false（明确告诉 UI「这是不可信的机器坐标」）」。
- 回归用例：喂 `<Idle|MPos:160,210,0|WCO:150,200,0>`（无 WPos），断言 `t.Paper == (10,10)`（当前 (160,210)，必红）。

---

### C3 [P1] 断点续写的 `sent` 是「块内相对行号」，却被当成绝对行号喂给笔跟踪和续写提示

- 位置：`src/App/UI/MainForm.cs:389`（`_pen.SetSentLines(Math.Min(sent, ...))`）、`1222-1239`（`ToText(_job, fromLine)` 切块后 `_stream.Start(lines, 0)`）、`src/App/Core/Grbl/GrblCore.cs:459/473/523`（`Sent` 从 `_startLine` 起算，是块内下标）、`GrblCore.cs:426`（`FailedLineNumber` 同为块内号）、`MainForm.cs:399`（提示「可从 GCode 第 N 行续写」）
- 触发条件：从第 601 行续写（`fromLine = 600`，块内 400 行）。复算见 `docs/_audit/tmp_c/sim_resume_cursor.py`：`SetSentLines(600)` 先得到 `CurrentPath=1`，紧接着第一条 `Progress("start")`（`sent=0`）把它打回 `CurrentPath=-1 / 已完成 0`，之后一路 0→1→2。
- 现象：
  - 进度条第 1 秒从 600/1000 掉到 0/400，画布笔尖跳回第一笔，随后「第 k / N 笔」全程少报 2 笔；
  - 更严重的是流控中止时：`FailedLineNumber` 是块内号，UI 在 `MainForm.cs:399` 让用户「从 GCode 第 8 行续写」，用户照做就从全文第 8 行开始 → 把最前面 8 个字重写一遍（或反向漏掉 592 行）。
- 为什么是 bug：`StreamController.Sent` 的契约是「本作业列表内下标」（`Start(lines, 0)`），而 `PenTracker.SetSentLines` 与 `FailedLineNumber` 的契约是 `job.GCode` 的绝对下标（`PenTracker.cs:84` 直接拿它索引 `_job.GCode`）。两条契约在 `fromLine > 0` 时必然错位，缺少的正是 `+fromLine` 这一次换算。
- 建议修法：`StartWriting` 保存 `_sentOffset = fromLine`，进度回调改喂 `_pen.SetSentLines(_sentOffset + sent)`；中止提示里的行号同样加偏移（或让 `StreamController.Start` 接受「绝对起始行号 + 全量列表」，从根上统一语义）。
- 回归用例：`fromLine=600` 起送，断言首次进度回调后 `pen.CurrentPath == 1` 且 `bar.Progress >= 600`（当前 `-1` / `0`，必红）。

---

### C4 [P1] 自动重连从未真正运行：`ReconnectPolicy.Port` 无人赋值；即便补上，排程里的 `Reset()` 也会让退避永不累积

- 位置：`src/App/UI/MainForm.cs:40`（`new ReconnectPolicy()`）、`476`（`Port.Length == 0` 处直接 `return`）、`477`（每次排程都 `Reset()`）、`507`（`Connect(_reconnectPolicy.Port, ...)`）；`src/App/Core/Grbl/GrblCore.cs:99`（`Connect()` 第一句就是 `Disconnect()`）、`136`（`Disconnect` 回调 `OnConnectionChanged(false)`）、`MainForm.cs:357`（该回调里调 `ScheduleReconnect`）
- 触发条件：拔 USB / 机器断电，`IsOpen` 变 false 触发 `OnConnectionChanged(false)`。全仓库对 `_reconnectPolicy.Port` 有 3 处读（476/503/507）、**0 处写**。
- 现象：`ScheduleReconnect()` 在 476 行静默返回，连「连接断开，1s 后开始自动重连」这行日志（481 行）都不会打，勾选框「断开后自动重连」完全无效，掉线只能靠用户自己发现。
  如果只补一句 `_reconnectPolicy.Port = port`，则暴露出第二层：`TryReconnect → Connect → Disconnect → OnConnectionChanged(false) → ScheduleReconnect → Reset()`（477 行）会在**每次尝试时**把 `Attempts` 清零，`NoteAttempt()` 永远返回 true，退避固定 2 s、`MaxAttempts = 8` 永不耗尽——日志里那句「最多 8 次」是假的。
- 为什么是 bug：`ReconnectPolicy` 的全部设计（`PenTracker.cs:138-159`，含单测 `NoteAttempt_StopsAtMax`）依赖「Attempts 只增不减、成功才清零」；排程函数却把它当「每次都从零开始」用。而 `Port` 这个唯一入口没接线，使这个模块在生产代码里一次都没被执行过，单测却全绿。
- 建议修法：`ToggleConnect()` 连接成功时写 `_reconnectPolicy.Port/Baud`；`ScheduleReconnect()` 只在「已连接 → 断开」的**边沿**复位（用 `_wasOpen` 标志），并加 `if (_reconnecting) return;` 防止 `Connect()` 内部那次 `Disconnect()` 回调把重连逻辑重新卷进来。
- 回归用例：跨 `Connect→Disconnect→Connect` 两轮后断言 `Attempts` 单调递增、第 9 次 `NoteAttempt()` 返回 false（当前 Port 为空时根本走不到；补 Port 后计数器每轮被清零，必红）。

---

### C5 [P1] 暂停后点「停止」，GRBL 留在 Hold；下一次「开始」把整份 GCode 灌进一台不动的机器

- 位置：`src/App/Core/Grbl/GrblCore.cs:564-568`（`Pause` 只发 `0x21` feed hold）、`570-577`（只有 `Resume` 才发 `0x7E`）、`592-602`（`Stop` 只翻标志 + Join，不发 cycle start、也不看 GRBL 状态）、`440-447`（`Start` 复位 `_pauseReq/IsPaused` 却不发 cycle start）；`src/App/UI/MainForm.cs:297`（暂停按钮）、`300-308`（停止按钮）
- 触发条件：书写中 → 点「暂停」（TX `0x21`，随后 `?` 若可用会看到 `<Hold:0|...>`）→ 点「停止」→ 点「开始」。此时 TX 里全是 `G1X..`，没有 `~`（0x7E）。
- 现象：GRBL 处于 Hold 状态，串口照常收行并逐行回 `ok`，但运动不执行。字符计数流控看到 ok 就继续发，`Sent` 一路涨到 100%，`Progress("finish")` 打出「书写完成」，机器一动不动（或只走完 Hold 前已进 planner 的那几行）。用户得到一张白纸 + 一条「已完成」日志；若之后有人手动点「继续」补发 0x7E，笔会在错位的位置开始横移。
- 为什么是 bug：`Stop` 的语义是「这个作业结束了，机器与软件回到同一个可再开始的状态」，当前它只停软件、不停 GRBL 的状态语义。`Start` 的注释（`GrblCore.cs:441-447`）通篇只处理 `_stop/_pauseReq` 这些**本地**标志，把「GRBL 可能还在 Hold」这个显式状态漏掉了——这是「软件状态机」与「固件状态机」之间缺少同步点。
- 建议修法：`Stop()` 里若 `IsPaused`（或 `Status.State == Hold`）先发 `0x7E`，等状态离开 Hold 再返回；或让 `Start()` 在检测到 Hold 时先补一条 cycle start。急停路径（`EmergencyStop`，`GrblCore.cs:604-611`）保持 feed hold + soft reset 不变，两者要显式区分。
- 回归用例：按 Pause → Stop → Start 驱动后，断言 Start 之后的 TX 字节流里出现 `0x7E`（当前没有，必红）。

---

### C6 [P1] 书写中未拦截 `$H`/`$X`/`G10`/`$$`/`$n=v`：它们多出来的 `ok` 被当成 GCode 行的 ack，字符计数超发丢行

- 位置：`src/App/Core/Grbl/GrblCore.cs:394-404`（`HandleOk` 无条件 `_sentLengths.Dequeue()`，不区分这个 ok 是谁的）；`src/App/UI/MainForm.cs:213-215`（`$H`/`$X`/`G10 L20`）、`325`（`$$`）、`335`（写 `$n=v`）——这些入口都没有 `ManualBlocked()`，而 jog / 宏 / 自定义指令有（`MainForm.cs:237/244/860/879`）
- 触发条件：书写进行中，在「机器」页点「解锁」：TX 追加 `$X\n`，RX 回 `ok\r\n`。此刻 `_pendingLines` 里排着约 8 行在途 GCode（典型 `G1X12.345Y67.89` → 压缩后 14 字节，`+1` 计 15 字节额度）。
- 现象：这个 ok 把「最早一行在途 GCode」的 15 字节额度还给了 `_bufferFree`，但 GRBL 并没有确认那一行 → 控制台认为缓冲多了 15 字节 → 多塞一行 → GRBL 的 127 字节 RX 缓冲溢出，后来那行被整行丢弃（**漏一笔画**，日志里只会看到 ok 计数与进度对不上）；若该固件对溢出回 `error:`，`HandleFault` 还会把整个作业 Abort，用户看到的失败行号也偏了一行。
- 为什么是 bug：GRBL 的 `ok` 是**按发送顺序逐行** ack 的，字符计数流控的全部正确性建立在「一个 ok 恰好对应一行已发送的 GCode」上。`ManualBlocked` 的注释（`MainForm.cs:843-846`）自己就写明「这些命令会插进字符计数流控之间，让 ok 与在途行错位」，实现却漏掉了同页最常用的 `$` 系列 —— 注释与实现不一致。
- 建议修法：`ManualBlocked()` 覆盖 `$H/$X/G10/$$/$n=v`；更稳妥的是让 `StreamController` 维护 `_manualAcks` 计数抵扣：写着作业时收到的、无法与在途行配对的 ok 只减 `_manualAcks`，不动 `_sentLengths`。
- 回归用例：用记录 TX 长度的假 client（GRBL 一个 ok 都不给），注入 1 条游离 `ok` 后，断言累计发送字节数仍 `<= BufferSize`（当前会变成约 142 > 127，必红）。

---

### C7 [P1] 探测「停止」只翻标志：G38.2 继续以探测速度下压，笔/探针顶在纸上，且没有抬笔收尾

- 位置：`src/App/Core/Machine/ProbeRunner.cs:51-57`（`Cancel()` 只置 `Cancelled/Running=false` 并回调，**一条指令都不发**）；`src/App/UI/MainForm.cs:271`（按钮只调 `Cancel()` + 停看门狗）
- 触发条件：网格探测进行到某点的 `G38.2 Z-3 F100` 途中，用户点「停止探测」（对面正是 `Send("G38.2 Z-3 F100")` 已入 GRBL 缓冲、正在下行）。
- 现象：`Send` 通道完好却一条收尾指令都不发：机器继续以 F100 下探到 `SafeZ − travel` 才停，然后**停在下探终点没有抬笔**（笔尖压在纸里，弹簧笔架被压到底）；紧接着的 `[PRB:...]` 因为 `Running=false` 在 `MainForm.cs:379` 被直接丢弃，于是「探到了但没人记录」。用户必须手动抬笔，否则下一次移动就是贴纸横移。
- 为什么是 bug：`Cancel` 的语义是「中止并收尾」，而 `ProbeRunner` 是唯一知道当前块该回到哪个 Z 的地方——`_steps[_index].Lines[0]` 就是那条抬笔行，另外三条分支（`ProbeRunner.cs:90/105/122`）都在发它，唯独取消分支不发。这也违反 `ProbePlanner` 类注释「每个块以抬笔开头、结束必须抬笔」（`ProbePlanner.cs:78-80`）。
- 建议修法：`Cancel()` 里补 `Send("G0 Z" + SafeZ)`（SafeZ/抬笔行在 `Start` 时缓存），必要时先发 `0x21` 让机器先停再抬；`Cancel` 之后的 PRB 仍应被吞掉，但抬笔必须发出去。
- 回归用例：Start 两点探测 → 第 1 点进行中 `Cancel()` → 断言 `sent` 末尾追加了一条 `G0 Z<SafeZ>`（当前没有，必红）。

---

### C8 [P2] 探测超时后迟到的 `[PRB:]` 被当作下一个点的结果：整条链错位一格，最后一点的真实 Z 永远丢失

- 位置：`src/App/Core/Machine/ProbeRunner.cs:76-93`（`FeedLine` 不校验这行属于第几个点，直接写 `_steps[_index]`）、`113-124`（`Poll` 超时后已 `Send` 抬笔 + 下一块并 `Advance`）
- 触发条件：`TimeoutMs = 20000`，第 k 点卡住（探针没接触，G38.2 还在慢速下行 / 尚未判 error）→ 20 s 时 `Poll` 判失败并推进到 k+1（同时把 `G0 Z<SafeZ>` 和 k+1 的三行排进 GRBL 队列）→ 约 0.3 s 后 k 点的 `<[PRB:120.000,80.000,-3.000:0]>` 才到。
- 现象：这条 PRB 被记到 k+1 头上（`Ok=false, Z=0`），k+1 的真实 PRB 又被记到 k+2 头上，逐点错位一格；由于每次误配都会 `Send` 抬笔 + `Advance`，`_index` 会提前到达 `_steps.Count`，`OnFinished` 在最后两个点**仍在排队下探**时就触发（`MainForm.cs:1098` 停看门狗、`FinishProbe` 开始写高度图）——最后一点的真实 Z 永远没人收。`OkCount` 看上去「几乎都成功」，高度图却是整体错一格 + 末尾缺失，表现为整幅笔压忽深忽浅。
- 为什么是 bug：探针回报没有任何序号/相关性校验，而超时路径已经「先斩后奏」地把状态往前推了一格；`[PRB:]` 的到达时刻与 20 s 超时判定天然存在竞争窗口（这也是 GRBL 里 G38.2 的回报唯一带外状态）。`ProbeRunner` 的类注释声称「等 [PRB:] 回报」，实现却只是「收到任意一条 PRB 就当前点结案」。
- 建议修法：给每个 `ProbeStep` 一个自增 `Seq`；`Poll` 超时后进入 `_discardNextPrb = true`，下一条 PRB 只做丢弃不计入任何点；或改为「按已发块数」而非「按收到 PRB 数」推进。
- 回归用例：`TimeoutMs=1` 触发超时后再喂 k 点的 `[PRB:...:0]`，断言 `Current` 只前进 1 步、该 PRB 不被记入 k+1、`OnFinished` 不提前触发（当前会再前进一次，必红）。
