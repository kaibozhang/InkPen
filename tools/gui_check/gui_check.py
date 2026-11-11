# -*- coding: utf-8 -*-
"""InkPen 界面自动化验收（无人工目视）。

三段，各自起一个干净进程，互不影响：
  1) smoke   主窗能起来、控件齐全、无错误对话框、画布真的画出墨迹（像素统计）
  2) interact 真点「把格线写入 GCode」+「生成 GCode」，确认 GCode 行数真的变多
  3) config  真点「保存配置」，检查落盘 JSON（无 BOM、三个键），再点「读取配置」

只用 Win32 消息（EnumWindows / BM_CLICK），不需要鼠标位置，可无人值守重复跑。
依赖：Python 3.8+、Pillow（仅 smoke 用）。
用法：python tools\\gui_check\\gui_check.py [smoke|interact|config|all]
"""
import ctypes
import ctypes.wintypes as wt
import json
import os
import re
import subprocess
import sys
import time

u = ctypes.windll.user32
EnumProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
BM_CLICK = 0x00F5

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
EXE = os.path.join(ROOT, "dist", "InkPen.exe")
CFG = os.path.join(ROOT, "dist", "config", "config.json")
SHOT = os.path.join(ROOT, "docs", "gui_smoke.png")

MAIN_MARK = u"\u5199\u5b57\u673a\u5de5\u4f5c\u53f0"   # 写字机工作台


# ---------------- Win32 helpers ----------------

def text_of(h):
    n = u.GetWindowTextLengthW(h)
    if n <= 0:
        return ""
    b = ctypes.create_unicode_buffer(n + 1)
    u.GetWindowTextW(h, b, n + 1)
    return b.value


def cls_of(h):
    b = ctypes.create_unicode_buffer(256)
    u.GetClassNameW(h, b, 256)
    return b.value


def top_windows():
    out = []

    def cb(h, l):
        t = text_of(h)
        if t:
            out.append((h, t))
        return True

    u.EnumWindows(EnumProc(cb), 0)
    return out


def children(h):
    out = []

    def cb(c, l):
        out.append((c, cls_of(c), text_of(c)))
        return True

    u.EnumChildWindows(h, EnumProc(cb), 0)
    return out


def find_main(timeout=25.0):
    end = time.time() + timeout
    while time.time() < end:
        for h, t in top_windows():
            if MAIN_MARK in t:
                return h, t
        time.sleep(0.3)
    return None, None


def click(main, label, exact=False):
    for h, cls, t in children(main):
        if "BUTTON" not in cls:
            continue
        if (t == label) if exact else (label in t):
            u.SendMessageW(h, BM_CLICK, 0, 0)
            return True
    return False


def error_dialogs():
    # 主窗标题含「写字机工作台」，错误框标题恰好是 "InkPen"，必须精确匹配
    return [t for _, t in top_windows() if t.strip() == "InkPen"]


def info_text(main):
    for _, cls, t in children(main):
        if "STATIC" in cls and re.match(r"^\u9875 \d+", t):   # 页 N
            return t
    return ""


def gcode_lines(main):
    m = re.search(r"GCode (\d+) \u884c", info_text(main))     # GCode N 行
    return int(m.group(1)) if m else -1


SWP_NOSIZE = 0x0001
SWP_NOMOVE = 0x0002
SWP_SHOWWINDOW = 0x0040
HWND_TOPMOST = -1
HWND_NOTOPMOST = -2


def raise_window(hwnd, timeout=3.0):
    """把目标窗口真正抬到最前，并确认它拿到了前台。

    只调 SetForegroundWindow 是不够的：本进程不是前台进程时，Windows 的前台锁
    会直接忽略该调用。结果就是「截图截到了被别的窗口盖住的区域」——曾经抓出一张
    浏览器与聊天窗口的图存进 docs/，看着像界面崩了，其实是没抬到前面。

    可靠做法是先把本线程的输入队列 attach 到当前前台线程，此时 SetForegroundWindow
    才被放行；抬完立刻 detach。
    """
    k32 = ctypes.windll.kernel32
    try:
        u.ShowWindow(hwnd, 9)                    # SW_RESTORE
    except Exception:
        pass
    end = time.time() + timeout
    while time.time() < end:
        attached = False
        try:
            fg = u.GetForegroundWindow()
            tid_fg = u.GetWindowThreadProcessId(fg, None) if fg else 0
            tid_me = k32.GetCurrentThreadId()
            if tid_fg and tid_fg != tid_me:
                attached = bool(u.AttachThreadInput(tid_fg, tid_me, True))
            u.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                           SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW)
            u.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                           SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW)
            u.BringWindowToTop(hwnd)
            u.SetForegroundWindow(hwnd)
            u.SetFocus(hwnd)
            if attached:
                u.AttachThreadInput(tid_fg, tid_me, False)
        except Exception:
            pass
        if u.GetForegroundWindow() == hwnd:
            return True
        time.sleep(0.15)
    return False


def launch(*args):
    if not os.path.exists(EXE):
        print("ERROR: %s not found, run build\\build.bat first" % EXE)
        sys.exit(2)
    p = subprocess.Popen([EXE] + list(args))
    main, title = find_main()
    if main is None:
        print("RESULT=FAIL reason=main window not found")
        print("TITLES=", [t for _, t in top_windows()][:10])
        p.terminate()
        sys.exit(1)
    if not raise_window(main):
        print("WARN=window not raised to foreground (screenshot may be wrong)")
    time.sleep(1.5)
    return p, main, title


def frame_rect(hwnd):
    """返回窗口的**可见**外框矩形（不含 DWM 阴影/透明拖拽边）。

    直接 GetWindowRect 在 Win10/11 上会比看得见的窗口大一圈，截图会把
    桌面背景（壁纸色、别的窗口文字）一起带进来，存档图看着像界面漏了一块。
    DWMWA_EXTENDED_FRAME_BOUNDS(9) 才是屏幕上真实的可见边界。
    """
    rect = wt.RECT()
    try:
        dwmapi = ctypes.windll.dwmapi
        hr = dwmapi.DwmGetWindowAttribute(wt.HWND(hwnd), ctypes.c_uint(9),
                                          ctypes.byref(rect), ctypes.sizeof(rect))
        if hr == 0 and rect.right > rect.left and rect.bottom > rect.top:
            return rect
    except Exception:
        pass
    u.GetWindowRect(hwnd, ctypes.byref(rect))
    return rect


def grab_window(hwnd, tries=3):
    """截当前窗口。截前重新抬一次前台，截后校验前台仍是它，避免抓到别的窗口。"""
    from PIL import ImageGrab
    img = None
    for _ in range(tries):
        raise_window(hwnd, 1.5)
        time.sleep(0.35)
        r = frame_rect(hwnd)
        img = ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom))
        if u.GetForegroundWindow() == hwnd:
            return img
    print("WARN=screenshot may capture another window (foreground mismatch)")
    return img


def paper_box(img):
    """在画布区域内定位纸张（近白矩形），返回 (left_gap, right_gap, paper_w, canvas_w, bbox)。

    画布现在是深色渐变，不能再拿固定背景色去抠图；改为「近白像素成片出现」的列/行判定。
    左右留白必须基本对称、且纸张不能贴边——历史上 Fit() 用了过期尺寸，
    纸张跑到右侧被裁掉一半，肉眼很难发现，必须机器量。
    """
    w, h = img.size
    x0, x1 = int(w * 0.235), int(w * 0.745)
    y0, y1 = int(h * 0.16), int(h * 0.80)
    px = img.load()

    def white(p):
        return p[0] >= 240 and p[1] >= 238 and p[2] >= 230

    cols = []
    for x in range(x0, x1):
        n = sum(1 for y in range(y0, y1, 4) if white(px[x, y]))
        if n > ((y1 - y0) // 4) * 0.45:
            cols.append(x)
    if not cols:
        return None
    p0, p1 = cols[0], cols[-1]

    rows = []
    for y in range(y0, y1):
        n = sum(1 for x in range(p0, p1 + 1, 4) if white(px[x, y]))
        if n > ((p1 - p0) // 4) * 0.45:
            rows.append(y)
    if not rows:
        return None
    r0, r1 = rows[0], rows[-1]

    return (p0 - x0, x1 - p1, p1 - p0, x1 - x0, (p0, r0, p1, r1))


# ---------------- 1) smoke ----------------

def check_smoke():
    p, main, title = launch()
    try:
        dialogs = error_dialogs()
        kids = [t for _, _, t in children(main)]

        rect = frame_rect(main)
        w, h = rect.right - rect.left, rect.bottom - rect.top

        img = grab_window(main)
        os.makedirs(os.path.dirname(SHOT), exist_ok=True)
        img.save(SHOT)

        must = [u"\u8f93\u5165\u6587\u7a3f",                 # 输入文稿
                u"\u7a3f\u7eb8\u6a21\u677f",                 # 稿纸模板
                u"\u751f\u6210 GCode / \u9884\u89c8",         # 生成 GCode / 预览
                u"\u5bfc\u51fa GCode \u6587\u4ef6",           # 导出 GCode 文件
                u"\u5f00\u59cb\u4e66\u5199",                 # 开始书写
                u"\u8fde\u63a5",                             # 连接
                u"\u5b9e\u65f6\u72b6\u6001 DRO",             # 实时状态 DRO
                u"\u5b8f\u6309\u94ae",                       # 宏按钮
                u"\u624b\u52a8 jog / \u56de\u96f6"]           # 手动 jog / 回零
        missing = [m for m in must if not any(m in k for k in kids)]

        rgb = img.convert("RGB")
        cen = paper_box(rgb)

        # 墨迹只能在纸面上量。画布改成深色之后，「整块中栏的深色像素」恒为大片深色，
        # 那种判据会永远通过——等于没有检测。这里改成只统计纸张内部的深色像素。
        if cen is None:
            dark, ratio = 0, 0.0
        else:
            p0, r0, p1, r1 = cen[4]
            ins = rgb.crop((p0 + (p1 - p0) // 12, r0 + (r1 - r0) // 12,
                            p1 - (p1 - p0) // 12, r1 - (r1 - r0) // 12)).convert("L")
            dark = sum(1 for px in ins.getdata() if px < 110)
            ratio = dark / float(ins.width * ins.height)

        print("MAIN_TITLE=", title)
        print("SIZE=", w, "x", h)
        print("ERROR_DIALOGS=", dialogs)
        print("CHILD_COUNT=", len(kids))
        print("MISSING_CONTROLS=", missing)
        print("PAPER_INK_PIXELS=", dark, "RATIO=%.4f" % ratio)
        if cen is None:
            print("PAPER_CENTER= not measurable")
        else:
            lg, rg, pw, cw = cen[0], cen[1], cen[2], cen[3]
            print("PAPER_CENTER= left_gap=%d right_gap=%d paper_w=%d canvas_w=%d" % (lg, rg, pw, cw))
        print("SCREENSHOT=", SHOT)

        # 纸张内的墨迹比例：正文约占 1%~5%。低于 0.0015 说明画布是空的
        ok = (not missing) and (not dialogs) and w > 900 and ratio > 0.0015
        # 纸张必须完整落在画布内、且左右留白基本对称
        if cen is None:
            ok = False
        else:
            lg, rg, pw, cw = cen[0], cen[1], cen[2], cen[3]
            if min(lg, rg) < 8 or abs(lg - rg) > max(24, pw * 0.10):
                print("PAPER_CENTER= OFF-CENTER/CLIPPED")
                ok = False
        print("RESULT=", "PASS" if ok else "FAIL")
        return ok
    finally:
        time.sleep(0.2)
        p.terminate()


# ---------------- 2) interact ----------------

def check_interact():
    p, main, _ = launch()
    try:
        before = info_text(main)
        n_before = gcode_lines(main)
        print("BEFORE:", before)

        ok_check = click(main, u"\u628a\u683c\u7ebf\u5199\u5165 GCode")   # 把格线写入 GCode
        ok_gen = click(main, u"\u751f\u6210 GCode")                       # 生成 GCode
        time.sleep(2.0)

        n_after = gcode_lines(main)
        print("CHECKBOX_CLICKED=", ok_check)
        print("GENERATE_CLICKED=", ok_gen)
        print("AFTER:", info_text(main))

        # 勾上格线后应多出格线笔画与对应 GCode 行
        ok = ok_check and ok_gen and n_after > n_before > 0
        print("GCODE_LINES_BEFORE=", n_before, "AFTER=", n_after)
        print("RESULT=", "PASS" if ok else "FAIL")
        return ok
    finally:
        time.sleep(0.2)
        p.terminate()


# ---------------- 3) config ----------------

def check_config():
    if os.path.exists(CFG):
        os.remove(CFG)

    p, main, _ = launch()
    try:
        saved = click(main, u"\u4fdd\u5b58\u914d\u7f6e", True)     # 保存配置
        time.sleep(1.0)

        exists = os.path.exists(CFG)
        has_bom = True
        keys = []
        if exists:
            raw = open(CFG, "rb").read()
            has_bom = raw.startswith(b"\xef\xbb\xbf")
            keys = sorted(json.loads(raw.decode("utf-8-sig")).keys())

        reloaded = click(main, u"\u8bfb\u53d6\u914d\u7f6e", True)   # 读取配置
        time.sleep(0.8)
        dialogs = error_dialogs()

        print("SAVE_CLICKED=", saved)
        print("CONFIG_FILE_EXISTS=", exists)
        print("HAS_BOM=", has_bom)
        print("KEYS=", keys)
        print("RELOAD_CLICKED=", reloaded)
        print("ERROR_DIALOGS=", dialogs)

        ok = (saved and exists and not has_bom and not dialogs and reloaded
              and "template" in keys and "machine" in keys and "text" in keys)
        print("RESULT=", "PASS" if ok else "FAIL")
        return ok
    finally:
        time.sleep(0.2)
        p.terminate()


# ---------------- 4) level ----------------

def check_level():
    """打开「调平」页并截图。

    高度图/探测是新增的主功能，必须确认控件真的建出来了（TabPage 是懒加载的，
    没显示过的页面在以前的 smoke 里根本查不到），且热力图区域不是空白一片。
    """
    from PIL import ImageGrab

    p, main, _ = launch("--tab=level")
    try:
        kids = [t for _, _, t in children(main)]
        must = [u"\u63a2\u6d4b\u53c2\u6570",        # 探测参数
                u"\u63a5\u89e6\u5f0f\u63a2\u6d4b",  # 接触式探测
                u"\u9ad8\u5ea6\u56fe"]              # 高度图
        missing = [m for m in must if not any(m in k for k in kids)]

        img = grab_window(main)
        shot = os.path.join(os.path.dirname(SHOT), "gui_level.png")
        img.save(shot)

        print("MISSING_CONTROLS=", missing)
        print("ERROR_DIALOGS=", error_dialogs())
        print("SCREENSHOT=", shot)
        ok = (not missing) and (not error_dialogs())
        print("RESULT=", "PASS" if ok else "FAIL")
        return ok
    finally:
        time.sleep(0.2)
        p.terminate()


# ---------------- main ----------------

CHECKS = {"smoke": check_smoke, "interact": check_interact, "config": check_config,
          "level": check_level}

if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    names = list(CHECKS) if mode == "all" else [mode]
    bad = []
    for name in names:
        if name not in CHECKS:
            print("unknown check:", name)
            sys.exit(2)
        print("=== %s ===" % name)
        try:
            if not CHECKS[name]():
                bad.append(name)
        except Exception as ex:
            print("EXCEPTION:", repr(ex))
            bad.append(name)
    print("=== GUI CHECK SUMMARY === total=%d failed=%d %s"
          % (len(names), len(bad), ("failed=" + ",".join(bad)) if bad else ""))
    sys.exit(1 if bad else 0)
