# -*- coding: utf-8 -*-
"""界面文案宽度静态检查：找出「写死像素装不下自身文字」的按钮与标签。

背景：项目里所有控件都用写死像素定位。中文 + 百分号的文案比拉丁字母宽得多，
一旦宽度不够，WinForms 会**静默裁掉**后半句——不会报错、不会换行、截图里的人还以为
自己看全了。第五轮审计里「按 10% 步进」被裁成「按 10%」、「100%」被裁成「10」都是这一类。

判定：文字实测宽度 + 内边距 > 控件宽度 → 报错。
字体取 Microsoft YaHei 9pt（设计期默认），DPI 缩放不参与（DPI 变了所有控件一起变，
相对关系不变；真正危险的是天生就装不下的文案）。

用法：
    python tools/ui_text_fit.py            # 扫描并列出所有装不下的控件
    python tools/ui_text_fit.py --quiet    # 只在有问题时输出（给 CI 用）
    python tools/ui_text_fit.py 别的文件.cs  # 指定文件（回验历史版本用）
退出码：0 = 全部装得下；1 = 有装不下的。

已知边界：只认**字符串字面量**。运行时拼出来的文案（如
`(steps[i] > 0 ? "+" : "−") + Math.Abs(steps[i]) + "%"`）扫不到，
这类控件要人工核对——它们恰好也是最容易被裁的一类（见「+50%」那一排）。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DESIGN = os.path.join(ROOT, "src", "App", "UI", "MainForm.Design.cs")

# WinForms Button 的可用宽度扣减。这个数不是理论值，是用三处实测标定的：
#   「100%」32px 文字 → 46px 按钮仍被裁、56px 才完整
#   「倍率 −」33px 文字 → 52px 仍被裁、60px 才完整
#   「+50%」34px 文字 → 46px 仍被裁、56px 才完整
# 即「按钮宽度 - 文字宽度」的余量要 ≥ 20px（边框 + FlatStyle 的内缩 + 字形 advance
# 比 PIL 的度量更宽）。按 20 卡，上面三处都会被判红，56/60 都判绿。
BUTTON_PAD = 20
# 复选框：方框 13px + 间距 4px
CHECKBOX_PAD = 17
# Label 只受左右各 1px 内缩影响
LABEL_PAD = 4


def load_font(size_px=12):
    """9pt ≈ 12px @96dpi。找不到雅黑就退到任意可用的中文字体。"""
    from PIL import ImageFont
    for name in ("msyh.ttc", "msyh.ttf", "simsun.ttc", "simhei.ttf"):
        p = os.path.join("C:/Windows/Fonts", name)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size_px)
            except Exception:
                continue
    return ImageFont.load_default()


# B(group, "文案", x, y, w, h)  /  Primary(B(group, "文案", x, y, w, h), color)
RE_B = re.compile(
    r'B\(\s*\w+\s*,\s*"((?:[^"\\]|\\.)*)"\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)')
# L(group, "文案", x, y, w)
RE_L = re.compile(
    r'L\(\s*\w+\s*,\s*"((?:[^"\\]|\\.)*)"\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(\d+)\s*\)')
RE_K = re.compile(
    r'K\(\s*\w+\s*,\s*"((?:[^"\\]|\\.)*)"\s*,\s*(?:true|false)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(\d+)\s*\)')

# 分组框：G(parent, "标题", x, y, w, h)
RE_G = re.compile(
    r'G\(\s*\w+\s*,\s*"((?:[^"\\]|\\.)*)"\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)')


def main():
    quiet = "--quiet" in sys.argv
    target = DESIGN
    for a in sys.argv[1:]:
        if a.lower().endswith(".cs"):
            target = a
    if not os.path.exists(target):
        print("[ERROR] 找不到 " + target)
        return 2

    font = load_font()
    src = io.open(target, encoding="utf-8").read()

    problems = []
    checked = 0

    def check(kind, text, w, pad, line_no):
        nonlocal checked
        checked += 1
        if not text:
            return
        need = font.getlength(text) + pad
        if need > w + 0.5:
            problems.append((kind, text, w, need, line_no))

    for m in RE_B.finditer(src):
        text, x, y, w, h = m.group(1), int(m.group(2)), int(m.group(3)), int(m.group(4)), int(m.group(5))
        line_no = src[:m.start()].count("\n") + 1
        check("Button", text, w, BUTTON_PAD, line_no)
        # 按钮右缘越出分组框客户区也要报
        if x + w > 300 + 3 and x >= 0:      # 右栏客户区约 300px，见 G() 的 306
            problems.append(("Button-overflow", text, x + w, 300, line_no))

    for m in RE_L.finditer(src):
        text, x, y, w = m.group(1), int(m.group(2)), int(m.group(3)), int(m.group(4))
        line_no = src[:m.start()].count("\n") + 1
        # Label 不 AutoSize 时会自动折行，只要高度够就不算缺陷。
        # 只有「声明了 .Height 并且折完还是放不下」才是真的被裁。
        eol = src.find("\n", m.end())
        stmt = src[m.start():eol if eol > 0 else len(src)]
        hm = re.search(r'\.Height\s*=\s*(\d+)', stmt)
        checked += 1
        need = font.getlength(text) + LABEL_PAD
        if hm:
            h = int(hm.group(1))
            lines = int(-(-need // max(1, w - LABEL_PAD)))       # ceil
            if lines * 16 > h:
                problems.append(("Label-wrap", text, h, lines * 16, line_no))
        elif need > w + 0.5:
            problems.append(("Label", text, w, need, line_no))

    for m in RE_K.finditer(src):
        text, x, y, w = m.group(1), int(m.group(2)), int(m.group(3)), int(m.group(4))
        line_no = src[:m.start()].count("\n") + 1
        # 复选框：方框约占 13px + 间距 4px
        check("CheckBox", text, w, CHECKBOX_PAD, line_no)

    if problems:
        print("发现 %d 处文案装不下（扫描 %d 个控件）：" % (len(problems), checked))
        for kind, text, w, need, line_no in sorted(problems, key=lambda t: t[4]):
            print("  MainForm.Design.cs:%-4d %-16s 宽 %3d < 需要 %5.1f  「%s」"
                  % (line_no, kind, w, need, text))
    elif not quiet:
        print("全部 %d 个控件的文案都装得下。" % checked)

    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
