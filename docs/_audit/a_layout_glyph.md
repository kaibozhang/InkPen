# 维度 A 审计：排版与字库（InkPen 0.1.0-M1）

发现 8 条（P0 0 / P1 2 / P2 6）

> 复算方式：`docs/_audit/tmp_a/sim_layout.py` 是 `LayoutEngine.LayoutHorizontal` 的逐行移植（含 Rng=0），
> 用 `data/glyphs/*.zip` 里真实的 advance 跑边界场景；所有数字均可用该脚本复现。
> 未修改任何 src/tests/build/tools/data 文件，未执行 build/test/selftest/gui_check。

---

### A1 [P1] 行尾悬挂标点没有上限：连续标点会把字形推出纸外

- 位置：`src/App/Core/Layout/LayoutEngine.cs:95-101`
- 触发条件：横排、默认 A4（210×297，四边 15mm，字号 10mm，字距 1mm），正文为 `"我"×16 + "。。。"`（一行正好排满，行尾 3 个全角标点）。
- 现象：避头尾分支对行首禁则字符**直接跳过换行判定**，只累加 `x += adv`。实测三个句号的字身框依次落在 x=191 / 202 / 213（adv=11）：第 2 个已越过 195 的右边界 7mm，第 3 个越过纸宽 210mm；第 3 个句号的实际墨迹在 215.6–216.8mm，**出纸 5.6mm**。GCode 照常输出这些坐标（`GCodeValidator` 只有 ±1mm 的软容差，且只报错不阻断）。同理 `"！"`、`"…"`、`"，。"` 都会触发。
- 为什么是 bug：第 100 行注释写的是"允许**轻微**越界把标点挤在行尾"，实现却是每个禁则字符都可以再挂一个 `adv`，越界量无上限（N 个标点就越界 (N−1)×adv）。同时这是唯一一条能绕过 95 行边界检查的推进路径。
- 建议修法：悬挂最多允许一个字。在 98 行分支里加 `hangCount`（或直接判断 `x + adv > right` 时是否已有悬挂），超出就并入 115 行的普通换行分支；或把最终 X 钳到 `right`。
- 回归用例：断言页内所有 `PlacedGlyph` 满足 `g.X <= MarginLeft + usableW`（当前第 2 个句号 X=202 即红）。

---

### A2 [P1] 行尾禁则把开引号下移时撞上换页：字形留在上一页页底，与同行的字相距 256mm

- 位置：`src/App/Core/Layout/LayoutEngine.cs:102-114`（配合 `123-129`）
- 触发条件：横排，15 个整行排满后，第 16 行（当页最后一行）形如 `"A"×23 + "（（"`（用 `latin.zip` 的 `（`，advance=1024）。即"行尾出现开引号/开括号"且这一行恰好是本页最后一行。
- 现象：104-113 行把 `（` 从 `line` 摘出、`moved.Y -= lineH` 使其 Y=16；紧接着 123 行判定 `y < bottom`(25) 触发换页，125-128 行只复位了"下一个字要用的 y"，**没有复位 `moved.Y`**。结果下一页里：`（` 在 (15, 16)，与它同一行（`LineIndex` 都被 `FlushLine` 改写成 0）的后续字在 (26, 272)，两者纵向相距 256mm，`LineIndex` 元数据完全看不出异常。附带后果：`BuildGrid` 把 y=16 当成一条基线，给该页画一条贴着页底的横线。
- 为什么是 bug：换页必须把"已下移但尚未 flush 的字形"一起搬到新页起点，否则这个字形就落在两页的缝里。当前它既不在上一页的行里，也不在新页的行上。
- 建议修法：把 `if (y < bottom)` 的换页判断提到 `moved.Y -= lineH` 之前；或在换页分支里同步 `moved.Y = y`（并清掉它的旧基线）。
- 回归用例：jitter=0 时，同一 `LineIndex` 的所有字形 `|maxY − minY| < 1e-6`（当前 16 vs 272，必红）。

---

### A3 [P2] 两端对齐 + 首行缩进：首行右端固定溢出右边距（溢出量 = 缩进）

- 位置：`src/App/Core/Layout/LayoutEngine.cs:219-227`（起因在 `54`）
- 触发条件：勾选"两端对齐"，`FirstLineIndentMm = 8`，正文任取 ≥2 个汉字（如 `"我"×15`），默认纸张。
- 现象：`FlushLine` 用 `extra = usableW - used` 算补量，等于假设本行从 `MarginLeft` 起排；但首行起点是 `MarginLeft + indent`。实测首行末字 X=192、字身右缘 202，而右边界是 195——**稳定溢出 7mm**（恰好 `indent − CharGap`，与字数无关：`lastX ≡ MarginLeft + indent + usableW − CharGap`）。缩进调到 50mm 时最后几个字直接进纸边。
- 为什么是 bug：两端对齐的目标右端是 `MarginLeft + usableW`，补量必须相对本行实际起点计算；居中（222 行）有同样的偏移（只是视觉上不明显）。
- 建议修法：`FlushLine` 增加 `lineStartX` 参数，`extra = (MarginLeft + usableW) - (lineStartX + used)`；居中/两端对齐共用。
- 回归用例：Justify+indent 下断言 `last.X + last.AdvanceMm <= MarginLeft + usableW + 1e-6`（当前 203 > 195，必红）。

---

### A4 [P2] 横排页底多扣一个字号：每页少排一行，与竖排不一致

- 位置：`src/App/Core/Layout/LayoutEngine.cs:55-56`（判定在 `123`，对照 `185`）
- 触发条件：默认 A4、字号 10、行距 6，输入 17 行文本（`string.Join("\n", Enumerable.Repeat("我我我我我", 17))`）。
- 现象：`y` 是字身**底边**（`GCodeBuilder` 用 `pg.Y + g.Y/1024*SizeMm`，`BuildGrid` 也从 `baseY` 向上画一格），字形占 `[y, y+FontSize]`。可页底判定用的是 `y < MarginBottom + FontSize`（25），等于要求字底离页边至少 `MarginBottom + FontSize`。实测可排 17 行（最后一行 y=16 ≥ 15），实际只排 16 行（最后一行 y=32），**每页底部白扔 16mm 一整行**；20 行文本因此被拆成 2 页。竖排（185 行）用的是 `y < MarginBottom`，两方向差一个字号。
- 为什么是 bug：`usableH` 在 21 行算出来后**从未使用**；顶部边界正好是 `MarginTop`（首行字顶 = H−MarginTop），底部却多让一个字号，属于典型的 off-by-one-row，不是排版留白设计。
- 建议修法：`bottom = t.MarginBottom`；或按可用高度算行数 `rows = floor((usableH − FontSize) / lineH) + 1` 后统一判定。
- 回归用例：17 行文本 `pages.Count == 1`（当前 2，必红）。

---

### A5 [P2] 方格/田字格格线忽略首行缩进：首行每个字都跨在格线上

- 位置：`src/App/Core/Layout/LayoutEngine.cs:291`（起因在 `54`）
- 触发条件：模板选"方格稿纸"（或田字格），`FirstLineIndentMm = 8`，默认字号 10 / 字距 1（格宽 11）。
- 现象：格线永远从 `mL=15` 起按 11 排列（15/26/37…），首行字形却从 23 起按 11 排列（23/34/45…），整行错位 8mm；第一个字的字身 `[23,33]` 横跨 26 这条格边，即"一字一格"的稿件里首行每个字都压在格线上。
- 为什么是 bug：方格稿纸的契约是一字一格；缩进改变了字形起点却没有让格线跟随（也没有把缩进吸附到整格），首行与后续行形成两套错开的栅格。
- 建议修法：优先把缩进吸附到整格：`indent = Math.Round(indent / cell) * cell`，排版与 `BuildGrid` 共用同一值；或让 `BuildGrid` 接收首行起点、首行单独按缩进画格。
- 回归用例：Grid+indent 下断言 `(g.X − MarginLeft) % cell ≈ 0`（当前 8 % 11 = 8，必红）。

---

### A6 [P2] 空格推进没有任何边界检查：行首空格串会把后面整字推到纸外

- 位置：`src/App/Core/Layout/LayoutEngine.cs:80-83`
- 触发条件：正文行以空格开头（粘贴带缩进的文本，或手打若干空格）。字号 60mm 时 7 个半角空格即越界（0.5×60=30mm/个，x=15+210=225）；字号 10mm 时需 40 个。
- 现象：空格分支只做 `x += FontSizeMm * 0.5`，既不判右边界也不换行。紧随其后的字因为 `line.Count == 0` 永远进不了 95 行的换行分支，被直接放在 x=225（纸宽 210）——**整字画在纸外**；且此后该"行"的换行基准整体右移，后续字全部偏出右边距。
- 为什么是 bug：空格也是排版推进，必须和字形受同一条右边界约束；当前它是唯一能绕过 95 行检查的推进路径（禁则分支至少还受 `line.Count>0` 与标点表限制）。
- 建议修法：空格走同一套检查：`x + spaceW > right` 时按普通换行处理；行首（`line.Count == 0`）的空格直接丢弃。
- 回归用例：任意前置空格的文本，所有字形 `g.X + g.SizeMm <= PaperWidth`（当前 225，必红）。

---

### A7 [P2] 代理对被当成两个字符：缺字多记一条、字宽多占一格

- 位置：`src/App/Core/Glyph/GlyphRepository.cs:106-108`（起因在 `LayoutEngine.cs:61` / `87` / `90`）
- 触发条件：文稿含任意非 BMP 字符（emoji、扩展 B 区汉字，如 U+20000「𠀀」）。
- 现象：排版按 `char` 迭代，一个码点被拆成高/低代理两个 `char` 分别 `Get`，两次都失败 → `MissingChars` 里多出两条互不成对的孤立代理（`"\uD840"`、`"\uDC00"`），信息栏"缺字 N 个"因此多算 1；同时 `x` 前进 `2 × FontSizeMm`，一个不可见字符吃掉两个字宽。`Get(char)` 本身也无法表达非 BMP 码点，将来字库即使收录了也永远取不到。
- 为什么是 bug：Unicode 文本元素 ≠ UTF-16 代码单元；缺字统计与宽度推进都按代码单元计数，是同一处的双重计数，且把非法字符串写进了 `MissingChars`（该列表会进状态栏文本，也可能被配置/日志序列化）。
- 建议修法：`GlyphRepository` 增加 `Get(int codePoint, out string source)` 重载；排版循环用 `char.IsHighSurrogate` + `char.ConvertToUtf32` 合并代理对，合并后只记一条缺字、只推进一格。
- 回归用例：文本 `"𠀀"` → `MissingChars.Count == 1` 且该页只有 1 个字宽的推进（当前 2 和 2，必红）。

---

### A8 [P2] `LoadErrors` 只增不清：重复 Load / 反复 Get 失败会无限累积同一条错误

- 位置：`src/App/Core/Glyph/GlyphRepository.cs:25`、`61`、`129`
- 触发条件：a) 字库目录缺 `pinyin.zip` 时连续两次 `Load`（UI 上每次切换"拉丁字体"或"本机手写库优先"都会走 `MainForm.LoadFonts()` → `Load`，见 `MainForm.cs:712-724`）；b) 某条 ZIP 条目解码失败后被反复 `Get`（同一篇长文里出现 N 次就追加 N 条）。
- 现象：`LoadErrors` 是 readonly 字段，`Dispose`/`Load` 都不清空，`Load` 每次只追加；`Get` 的 `catch` 也是每次调用都 `Add` 且不缓存失败结果。第二次 Load 后同一句"缺少字库: pinyin.zip"出现两次；`MainForm.cs:723` 会把整个列表一次性 `Log` 出去，切几次字体日志就重复几遍；解码失败时下一次切字体会一次刷出上千条相同的行。
- 为什么是 bug：`Load` 的语义是"重建字库状态"（`Dispose` 已清空 `_packs`、`Enabled` 复位），只有错误列表跨 Load 保留，与同函数内其它状态处理不一致；`GlyphRepositoryTests.Load_Twice_DoesNotLeakOldPacks` 只断言了 `Count`，正好漏掉这一面。
- 建议修法：`Load` 开头 `LoadErrors.Clear()`；`Get` 失败按 `(pack, codepoint)` 去重（或缓存失败标记），避免每次调用重复追加。
- 回归用例：连续 `Load` 两次后断言 `LoadErrors.Distinct().Count() == LoadErrors.Count`（当前 2 vs 1，必红）。

---

## 待确认（契约不明，未计入主清单）

1. **竖排标点是否应做"竖排位移"**：`LayoutVertical`（149-211）对 `。，、！` 不做任何重定位/旋转。数据实测 `latin.zip` 的 `。` 墨迹中心在 x≈0.32em、y≈0.19em（字身左下角，横排位置），竖排中文习惯应落在右上。若产品约定"竖排标点沿用横排位置"，则不算缺陷；否则这是竖排输出的系统性错位。需要产品/字体侧确认契约。
2. **竖排下"居中/两端对齐"完全不生效**：`FlushLine` 只在横排的 66/106/117/145 行被调用，`LayoutVertical` 从不调用它，勾选后输出逐字节相同。是"未实现"还是"有意不支持"？UI 上两个复选框在竖排时并未禁用，建议至少置灰。
3. **方格/田字格竖边被画两遍**：16 个格子的 32 条竖边只有 17 个不同位置，每格的左右边各画一次、相邻格共享边重复；`DrawGrid` 时同一线段走两遍笔（墨色加重 + GCode 行数增加）。现象确定，但影响面只在"把格线写入 GCode"，未挤进前 8 条。

## 已复核但判定不是缺陷

- `FirstLineIndentMm` 只作用于整篇第一行、第二行不缩进：`LayoutTests.FirstLineIndent_ShiftsOnlyFirstLine` 已把该行为写成断言，属既定设计（Word 语义的"每段首行缩进"在此未采用）。
- 竖排列推进用 `FontSize+LineGap`、行内推进用 `FontSize+CharGap`：两处变量用对了，不是错用。
- `adv = Advance/1024*FontSize + CharGap` 与 `GCodeBuilder` 的 `g.X/1024*SizeMm`：单位一致（1024 网格、Y 向上、pg.Y 为字身底边），没有 mm/格 混用。
- `Rng` 每次 `Layout` 重新播种、`xorshift32` 不会退化到 0，同种子可复现；`GlyphRepository` 同码点返回同一实例、`SetEnabled` 后 `Count`/`Get` 同步变化。
- `fallback.zip` 607 个字形包围盒越出 1024 网格（`tmp_a/probe_packs.py` 实测），属数据问题且已在 `docs/glyph_report.md` 记录，非排版代码缺陷。
