# 维度 B 审计：GCode 生成与数值正确性（InkPen 0.1.1-M1）

发现 7 条（P0 0 / P1 3 / P2 4）

> 复算方式：`docs/_audit/tmp_b/sim_gcode.py` 是 `LayoutEngine` + `GCodeBuilder` 的逐行移植（含 RDP、`F()` 的 .NET 四舍五入、
> 连笔判定、续写前缀）。先用 `dist/selftest/` 的 10 份 `.nc` 做逐字节对照（a_mixed / b_free / b_grid / b_pinyin4 / b_ruled /
> b_tianzi / c_vertical / d_multipage / e_drawgrid / resume 全部 byte-identical，多页 26828 行也一致），再在其上跑边界场景
> （`exp_b.py` / `exp_heightmap.py`）。所有数字均可复现。
> 未修改 src/tests/build/tools/data 任何文件，未执行 build.bat / test.bat / selftest.bat / gui_check.bat。
> 只用到 `python tools/validate_gcode.py`（只读）与自己拼的 Python 复算。

---

### B1 [P1] 抬笔 `G0 Z<SafeZ>` 不参与高度补偿：纸面高过 SafeZ 的那一侧，每次抬笔都在纸上压 1~2.5 mm 拖行

- 位置：`src/App/Core/GCodeBuilder.cs:230`、`src/App/Core/GCodeBuilder.cs:238`（两处抬笔）、
  `src/App/Core/GCodeBuilder.cs:374`（续写前缀抬笔）；补偿只作用于落笔：`GCodeBuilder.cs:247-255`、`266-267`、`300-307`
- 触发条件：勾"启用高度图补偿"，纸面整体倾斜 4 mm（探测 3×3、区域 10..200mm 内 Z 从 0 线性升到 4），
  `SafeZ=2`、`WriteZ=0`（把安全高度压到 2 mm 是很常见的省时间设置），正文任意多字。
- 现象：`CompensatedZ() = WriteZ + At()` 只改落笔高度，`SafeZ` 是常量。实测该作业 96 条 `G0 Z2` 抬笔里
  **38 条低于该处纸面所需 Z**，最深 1.769 mm：
  ```
  934  G1 X189.006 Y262.445 F1500     ← 在 4mm 高区落笔画到这里
  935  G0 Z2                          ← 该处纸面需要 Z=3.769，笔尖已压入 1.77mm
  936  (CH:65306;PG:0;LN:1;CI:34;SI:1)
  937  G0 X189.006 Y258.441           ← 带着 1.77mm 压入在纸上横移 4mm
  938  G1 Z3.769 F300                 ← "落笔"反而比"抬笔"高 1.77mm
  ```
  同一份作业里 174 条运动指令的 Z 高于 `SafeZ`；`GCodeValidator` 只在 `SafeZ <= WriteZ`（2 > 0）时告警，
  所以 UI 显示"静态校验通过"。倾斜 8 mm、SafeZ=5 时同理，最大压入 2.537 mm（X=189 处纸面需 Z=7.54）。
- 为什么是 bug：README 的标定契约是"安全 Z > 书写 Z"（`docs/M1_acceptance.md:46` 校准第 3 步），
  而高度图改变了"书写 Z"却不改变"安全 Z"，于是这条不变式在纸面高的一侧被破坏；抬笔就是"离纸"，补偿后它必须
  是 `max(SafeZ, WriteZ+At+余量)`。校验器 `GCodeBuilder.cs:154` 用未补偿的 `WriteZ` 判"撞笔"，正好漏掉这个失效模式。
- 建议修法：抬笔统一走 `LiftZ(job, paperX, paperY) = Math.Max(m.SafeZ, CompensatedZ(job,x,y) + 抬笔余量)`；
  至少把 `G0 Z` 也传给 `CompensatedZ`。校验器同步改成用 `WriteZ + map.MaxZ()` 与 `SafeZ` 比较并报错。
- 回归用例：倾斜 4 mm + SafeZ=2 时断言每条 `G0 Z` 的 Z ≥ 该点 `CompensatedZ + 0.5`（当前 38/96 条为红）。

---

### B2 [P1] 单点对刀直接把 `Status.WX/WY` 当坐标：没收到 WPos 时报文时探测点恒为 (0,0)，机器会自己跑到工件原点

- 位置：`src/App/UI/MainForm.cs:1052`（`double x = _grbl.Status.WX, y = _grbl.Status.WY;`）→
  `src/App/Core/Machine/ProbePlanner.cs:111-119`（`G0 X0 Y0` + `G38.2`）
- 触发条件：连上机器后点"单点对刀"。`GrblStatus.WX/WY` 初值 0（`GrblCore.cs:21`），只有收到带 `WPos:` 的状态报文才会被赋值；
  而常见的 GRBL 默认 `$10=1` 只上报 `MPos`，全项目也没有任何地方主动发 `?` 查询（grep 全文无 `0x3F`/`"?"`），
  所以"从连接到现在一次 WPos 都没收到"是常态。
- 现象：生成的指令是 `G0 Z5` / `G0 X0 Y0` / `G38.2 Z-3 F100`，与本函数注释"就地探测，不移动 XY，避免把笔拖到别处"
  （`MainForm.cs:1051`）完全相反：笔在 SafeZ 上横穿整张纸跑到工件原点，在那里下探 8 mm——
  探到的是夹具/纸边就写回一个错误的书写 Z，探不到就报"未接触到纸面"。
  同一份代码里 `PenTracker.SetStatus` 恰好有正确的兜底（`PenTracker.cs:66-68`：`_sawWPos` 为假时退回 MPos），
  这条路径绕过了它。
- 为什么是 bug：`ProbePlanner` 的契约写明入参是"当前 WPos"（`ProbePlanner.cs:109`），而这里既没确认收到过 WPos，
  也没用 MPos 兜底，等于把"未初始化"当成了坐标 (0,0)——一个合法的真实坐标，无法从日志里看出来。
- 建议修法：`StartSingleProbe` 改用 `PenTracker` 那套选择逻辑（或先 `SendRealtime('?')` 并等一条状态报文，
  超时就拒绝探测）；`ProbePreflight` 里加 `HasPosition` 判定。
- 回归用例：构造 `Raw="<Idle|MPos:100,200,0>"` 的状态后调用单点对刀，断言首条运动指令是 `G0 X100 Y200`（当前 `G0 X0 Y0`，红）。

---

### B3 [P1] 网格探测的绝对基准 `BaseZ` 从不回填书写 Z：只跑网格探测（README 允许"或"）时整篇 Z 偏移一个常数值

- 位置：`src/App/Core/Machine/ProbePlanner.cs:164-176`（`map.BaseZ = baseZ` 后就再没人读）、
  `src/App/Core/Machine/HeightMap.cs:30`（注释自称"仅作显示/**回填**参考"）、
  `src/App/Core/GCodeBuilder.cs:247-255`（`WriteZ + At()`，不含 `BaseZ`、不含 `PressDepthMm`）、
  `src/App/UI/MainForm.cs:1134-1157`（网格探测完成后只写 `_heightMap`，不回写 `_writeZ`；只有单点对刀在 1114-1115 回写）
- 触发条件：README 第 27 行"单点对刀定书写 Z，**或**跑 3×3 网格探测生成高度图"。用户只做网格探测，`WriteZ` 保持默认 0。
- 现象：`ProbePlanner` 的类注释写的公式是"落笔 Z = 探测基准 + 压入量 + 该点高度差"（`ProbePlanner.cs:27-30`），
  实现的却是 `WriteZ + 高度差`。举例：3×3 探测得到 `BaseZ=-12.30`、`PressDepth=0.3`，正确落笔 Z 应是 `-12.00 + 高度差`，
  实际输出 `G1 Z0`（`WriteZ=0` + 0）——笔悬在纸面上方 12.3 mm 空写一遍，纸是白的，GCode 与校验全部正常。
  反方向（WriteZ 标定在纸面下方）则整篇多压 12 mm。
  即使先做了单点对刀，只要对刀点不在探测基准点（网格左下角 (margin,margin)），残差就等于两点高差：
  在 4 mm/190 mm 的倾斜下，两点相距 10 mm 就是 0.21 mm 的系统性压入误差——正好把高度图想消掉的误差又加回去。
- 为什么是 bug：`BaseZ` 的唯一用途在注释里写的是"回填参考"，而全项目没有任何调用点消费它（只在 `Normalize()` 与
  JSON 里出现，`Normalize` 本身也无人调用）；`PressDepthMm` 同样只被序列化，不参与生成。文档允许的流程走不通。
- 建议修法：网格探测完成后 `WriteZ = map.BaseZ + cfg.PressDepthMm`（并在 UI 上把这个值写进"书写 Z"）；或让
  `CompensatedZ` 在启用高度图时用 `BaseZ + PressDepthMm + At()`，`WriteZ` 仅作未探测时的回退。
- 回归用例：`ApplyResults` 后断言 `GCodeBuilder` 的首条落笔 Z == `BaseZ + PressDepth + At(首点)`（当前等于 `WriteZ`，红）。

---

### B4 [P2] `Smooth()` 在 2 行/2 列网格上把整轴梯度抹平；3×3 也会把边缘起伏削掉一半

- 位置：`src/App/Core/Machine/HeightMap.cs:117-140`（3×3 均值，边界邻域按实际格数取平均），
  调用点 `src/App/UI/MainForm.cs:1147`（每次网格探测后无条件 `Smooth()`）
- 触发条件：调平页 `探测行数=2`（控件下限就是 2，`MainForm.Design.cs:564-566`）或 `列数=2`；或默认 3×3 但纸面是单一方向倾斜。
- 现象（`docs/_audit/tmp_b/exp_heightmap.py` 复算）：
  - 3×2 网格、Y 向真实倾斜 1 mm：`Z=[0,0,0, 1,1,1]` → Smooth 后 `[0.5,0.5,0.5, 0.5,0.5,0.5]`，
    `At(0,ymin)=At(0,ymax)=0.500`，**Y 向补偿差 0.000 mm**，而 `Summary()` 会显示"起伏 0.000 mm"，用户以为纸是平的。
  - 2×2 网格、X 向倾斜 1 mm（左列 0 / 右列 1）：Smooth 后同样全是 0.5，起伏 1.000 → 0.000 mm，并且凭空多出 0.5 mm 的恒定压入。
  - 默认 3×3、X 向倾斜 1 mm：`[0,0,1]` 每行 → `[0,0.333,0.5]`，**右边缘的 1.0 mm 只剩 0.5 mm**（边缘均值窗口向内侧偏一格）。
- 为什么是 bug：`Smooth` 的契约是"去掉单点探测毛刺"（`HeightMap.cs:116`），单点毛刺是高频噪声；整轴线性倾斜是低频信号，
  不该被削。行/列数为 2 时窗口必然跨整轴，等于用均值替换掉全部信息，功能静默失效却仍报 `Valid`。
- 建议修法：只在 `Cols>=3 && Rows>=3` 且 `(c,r)` 周围 8 邻域完整时平滑；或对边缘格做"只沿可用的两侧做加权中值"，
  而不是简单平均；探测行列下限提到 3（2 行本来就无法表达一个方向的一阶梯度）。
- 回归用例：3×2 数据 `[0,0,0,1,1,1]` 经 `Smooth()` 后断言 `At(0,maxY)-At(0,minY) > 0.9`（当前 0.000，红）。

---

### B5 [P2] 续写前缀先运动再恢复模态：`G0 Z` 排在 `G21/G90/G94` 之前，与注释声称的"避免沿用未知 G90/G91、G20/G21"相反

- 位置：`src/App/Core/GCodeBuilder.cs:371-376`
- 触发条件：任一次断点续写（`ToText(job, fromLine>0)`）；机器当前处于 G91/G20——宏按钮接受任意 G 代码
  （`Macros.cs:31-35` 默认宏、`MainForm.cs:903-913` 直接下发，不做模态复位），用户存过一个 `G91`/`G20` 宏并点过就会命中。
- 现象：续写文本的前 3 行是：
  ```
  (RESUME FROM LINE 635)
  G0 Z5        ← 此时模态仍是上一次的（G91 时是"相对 +5"，G20 时是 5 英寸=127mm）
  G21
  G90
  ```
  G91 下这条"抬笔"只是相对上移 5 mm（笔可能还在纸里）；G20 下 Z 直接冲到 127 mm 外（撞上限位/丢步）。
  正确顺序必然是"先 G21/G90/G94，再 G0 Z"。
- 为什么是 bug：371-372 行注释明确写"续写前恢复模态状态与抬笔，避免从中途开始时沿用未知的 G90/G91、G20/G21"，实现顺序恰好反过来；
  现有测试 `ToText_ResumePrefix_RestoresModalState` 只断言 `IndexOf("G21")>=0`（任意位置即可），锁不住顺序。
- 建议修法：把 `G0 Z<SafeZ>` 挪到 `G21\nG90\nG94\nF<XYFeed>` 之后，再补一条 `G0 Z` 的绝对恢复。
- 回归用例：断言 `prefix.IndexOf("G90") < prefix.IndexOf("G0 Z")`（当前为假，红）。

---

### B6 [P2] 续写落在"连笔"笔画上时整段没有落笔指令：前缀抬了笔，第一笔却假设笔已经落下

- 位置：`src/App/Core/GCodeBuilder.cs:353-364`（`FindResumeLine` 只按 `CharIndex/StrokeIndex` 找行）+ `366-381`（前缀只抬笔）
  + `228-232`（连笔分支不抬笔，后续 `EmitPath` 也不会再写 `G1 Z`）
- 触发条件：`ConnectDistanceMm` 调到 6（UI 范围 0~10），正文用 `latin.zip` 的 `InkPen 汉字123 abc ，。！`
  （实测 CI=0 的末笔终点 (20.000,273.211) 与 CI=1 的首点 (21.834,277.879) 相距 **5.015 mm**，6 mm 阈值会跨字符连笔）。
- 现象：`FindResumeLine(1)=11`，续写文本下标 6 起是 `(CH:110;...;CI:1;SI:0)` + `G1 X21.834 Y273.211 F1500`，
  **第一条 `G1 Z` 要到下标 18（第 19 行、前缀后第 12 行）才出现**——中间 7 条 XY 在 Z=SafeZ 的状态下空走，
  字符 `n` 的两笔（SI=0 竖、SI=1 拱，共 7 段）完全没画，直到第 3 个字 `k` 的笔画间距大于阈值才重新落笔。
  用户只看到"续写完成了"，纸上是缺字。
- 为什么是 bug：续写的契约是"从断点继续写完剩下的内容"；原流里这些行之所以没有 `G1 Z`，是因为那时笔一直没抬，
  而续写前缀强制抬笔后，段首的"隐含笔已落下"前提就失效了。`GCodeLine.IsPenDown` 已经记录了每行的落笔状态（`Model.cs:125-134`），
  前缀完全有条件据此补一条落笔指令。
- 建议修法：`FindResumeLine` 向前回退到最近一条 `IsPenDown==false` 之后的第一条 `G1 Z`（连同它前面的 `G0 X Y`）；
  或 `ToText` 在 `fromLine` 之后第一条运动行是 `IsPenDown==true` 且段内没有 `G1 Z` 时，在段首插入一条
  `G1 Z<CompensatedZ(该点纸面坐标)> F<ZFeed>`。
- 回归用例：`ConnectDistanceMm=6` 时断言续写文本下标 0..9 内出现 `G1 Z`（当前下标 18 才出现，红）。

---

### B7 [P2] `validate_gcode.py` 的 80 字节口径与 C#/流控不一致：先剔掉 `;` 注释再量长度，含长注释的行离线 PASS、真机必中止

- 位置：`tools/validate_gcode.py:55-63`（`s = strip_gcode_comment(raw)` 之后才 `len(s.replace(" ",""))`），
  对照 `src/App/Core/GCodeBuilder.cs:139`（用原始 `Text` 量）与 `src/App/Core/Grbl/GrblCore.cs:497-504`（`wire = raw.Replace(" ","")`）
- 触发条件：`StartGCode`/`EndGCode`（自由文本，注释行写入 GCode）或手改过的 `.nc` 里有超过 80 字符的行尾注释，例如
  `G1 X10 Y10 ; ` + 90 个字符。
- 现象：实测该文件 `python tools/validate_gcode.py longcomment.nc 210 297` → `错误: 0 警告: 0 / RESULT: PASS`；
  而同一行的去空格长度是 99，`StreamController` 会在真机上 `Abort("GCode 第 N 行 99 字节超过 GRBL 单行缓冲 80 字节")`，
  `GCodeValidator` 也会报错。同一份文件，两个校验器给出相反结论，离线预检形同虚设。
  文件留在 `docs/_audit/tmp_b/longcomment.nc`。
- 为什么是 bug：该函数自己的注释写"Match that rule here so a hand-edited .nc fails OFFLINE instead of at the machine"，
  但发送端只删空格、不删注释，注释字节同样占用 GRBL 的 79/80 字符窗口。
- 建议修法：行长按**原始行**（只去空格）计量，注释剥离只用于 XY/F/Z 的语义分析；两处计量函数抽成一个口径。
- 回归用例：对上述 `longcomment.nc` 断言 `errors` 非空且含"80 字节"（当前 PASS，红）。

---

## 已复核、判定不是缺陷（附算例，避免下一轮重复怀疑）

1. **数值格式化 / 区域设置**：G 代码三个出口全部用 InvariantCulture——`GCodeBuilder.F`（`GCodeBuilder.cs:349`）、
   `ProbePlanner.F`（`ProbePlanner.cs:36`）、`HeightMap.Summary`（`238-239`）；JSON 用 `"R"`+Invariant（`SimpleJson.cs:138`），
   配置反序列化用 Invariant（`ConfigBinder.cs:44-49`），`$` 参数写入前还做了 `Replace(",", ".")`（`MainForm.cs:335`）。
   de-DE/fr-FR 下 G 代码与配置都不受影响。**唯一**未用 Invariant 的是纯显示：`MainForm.cs:443-445` 的 DRO、
   `405` 的 ETA、`GCodeBuilder.cs:148-150` 的报错文案——不下发、不落盘，不计入缺陷。
2. **精度**：`0.###` 是四舍五入（不是截断）——`c_vertical.nc` 里 `111.5625 → "111.563"`（截断会得 111.562）。
   单轴最大误差 0.0005 mm；坐标是每行绝对值，**不随笔画/页累积**，297 mm 页长最坏仍是 0.0005 mm/点，
   比 `SimplifyEpsilonMm=0.02` 的简化容差小 40 倍、比 Z 补偿阈值小 10 倍，可接受。
3. **单行 80 字节**：10 份真实 `.nc` 最长 34 字节；理论极限（纸 1000mm + 负坐标 + Z 三位小数 + F20000）
   `G1 X-1000.123 Y-1000.123 Z-99.999 F20000` = 36 字节。补偿加不出超限行，`MaxLineBytes=80` 只是兜底。
4. **坐标变换**：4 原点角 × 4 反转组合 × 4 个角点/边点，`ToPaper(ToMachine(p))` 64/64 误差 < 1e-9（严格互逆）；
   `ToPaper` 确是先撤 Invert 再撤原点角。校验器的越界判定用 ±(W+1)/±(H+1) 对称窗口，负坐标不会误报。
5. **Z 补偿的坐标口径**：`CompensatedZ` 用**纸面**坐标查表（`GCodeBuilder.cs:267`、`302`），与 `HeightMap.At` 的行列约定
   （`r=0 ↔ MinY`）一致；换原点角/反转轴不会翻转高度图。格线也走 `EmitPath`（`GCodeBuilder.cs:200-201`），同样被正确补偿。
6. **Z 补偿的量化误差**：逐行比较落笔状态下的"指令 Z"与"目标 Z"（3×3 倾斜图 0~3.4 mm），最大 0.00542 mm
   （另一张 0~1.5 mm 图 0.00530 mm），符合 `ZCompEpsilon=0.005` + 打印量化 0.0005 的上界。
7. **双线性插值**：3×4 随机值、20000 个随机点（含区域外）与独立实现最大偏差 4.4e-16；边界按边缘值夹取、不外推。
8. **`lastZ` 跨笔画/跨页保留**、`G1 Z` 每次换笔都重新写绝对落笔 Z（`GCodeBuilder.cs:190`、`286`、`293`）——实现与注释一致，
   未发现"换笔后 Z 漂移"。
9. **格线计入 `Paths`**：`PenDown=true`、`cp=0`，因此状态栏"笔画 N"包含格线；`GCodeTests.Build_IncludesGridOnlyWhenRequested`
   已把该行为写成断言，属既定设计（未计入缺陷）。
