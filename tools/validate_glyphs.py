"""字库覆盖率 / 坐标系 / 笔画数抽检，输出 docs/glyph_report.md。"""
import io
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from glyph_format import GRID, read_font_zip, strokes_bbox  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
G = ROOT / "data" / "glyphs"
DOCS = ROOT / "docs"

FILES = ["local.zip", "mmah.zip", "fallback.zip", "latin.zip", "latin_script.zip", "pinyin.zip"]
SAMPLE = "写字机工作台一二三四五六七八九十你好世界Hello 123，。！？āáǎà"

# 常用 3500 字表的近似抽检集（GB2312 一级汉字首尾 + 常见字）
PROBE = "的一是在不了有和人这中大为上个国我以要他时来用们生到作地于出就分对成会可主发年动同工也能下过子说产种面而方后多定行学法所民得经十三之进着等部度家电力里如水化高自二理起小物现实加量都两体制机当使点从业本去把性好应开它合还因由其些然前外天政四日那社义事平形相全表间样与关各重新线内数正心反你明看原又么利比或但质气第向道命此变条只没结解问意建月公无系军很情者最立代想已通并提直题党程展五果料象员革位入常文总次品式活设及管特件长求老头基资边流路级少图山统接知较将组见计别她手角期根论运农指几九区强放决西被干做必战先回则任取据处队南给色光门即保治北造百规热领七海口东导器压志世金增争济阶油思术极交受联什认六共权收证改清己美再采转更单风切打白教速花带安场身车例真务具万每目至达走积示议声报斗完类八离华名确才科张信马节话米整空元况今集温传土许步群广石记需段研界拉林律叫且究观越织装影算低持音众书布复容儿须际商非验连断深难近矿千周委素技备半办青省列习响约支般史感劳便团往酸历市克何除消构府称太准精值号率族维划选标写存候毛亲快效斯院查江型眼王按格养易置派层片始却专状育厂京识适属圆包火住调满县局照参红细引听该铁价严龙飞"


def main():
    fonts = {}
    for f in FILES:
        p = G / f
        if not p.is_file():
            continue
        fonts[f] = read_font_zip(p)

    union = set()
    for d in fonts.values():
        union |= set(d.keys())

    lines = ["# InkPen 字库报告", "",
             "| 字库 | 字数 | 说明 |", "|---|---:|---|"]
    for f, d in fonts.items():
        lines.append("| `%s` | %d | %s |" % (f, len(d), "本机私有/不分发" if f == "local.zip" else "随软件分发"))
    lines += ["", "**并集覆盖**：%d 个码点" % len(union), ""]

    # 样本回退链
    chain = ["local.zip", "mmah.zip", "fallback.zip", "latin.zip", "pinyin.zip"]
    lines += ["## 样本回退链", "", "| 字符 | 命中字库 | 笔画数 | 点数 |", "|---|---|---:|---:|"]
    miss = []
    for ch in SAMPLE:
        if ch.isspace():
            continue
        cp = ord(ch)
        hit = next((f for f in chain if f in fonts and cp in fonts[f]), None)
        if hit is None:
            miss.append(ch)
            lines.append("| %s | **缺字** | - | - |" % ch)
            continue
        s, adv = fonts[hit][cp]
        lines.append("| %s | %s | %d | %d |" % (ch, hit, len(s), sum(len(x) for x in s)))
    lines += ["", "样本缺字：%s" % ("、".join(miss) if miss else "无"), ""]

    # 常用字覆盖
    covered = sum(1 for ch in PROBE if any(ord(ch) in fonts.get(f, {}) for f in chain))
    lines += ["## 常用字抽检", "", "%d / %d = %.1f%%" % (covered, len(PROBE), 100.0 * covered / max(1, len(PROBE))), ""]

    # 坐标系抽检
    lines += ["## 坐标系抽检", "", "| 字库 | 越界字形数 | 空笔画字形数 |", "|---|---:|---:|"]
    for f, d in fonts.items():
        bad, empty = 0, 0
        for cp, (s, adv) in d.items():
            if not s or all(len(x) < 2 for x in s):
                empty += 1
            x0, y0, x1, y1 = strokes_bbox(s)
            if x0 < -1 or y0 < -1 or x1 > GRID + 1 or y1 > GRID + 1:
                bad += 1
        lines.append("| `%s` | %d | %d |" % (f, bad, empty))
    lines += ["", "坐标系：1024 网格、Y 向上、原点左下；越界允许 1 单位浮点误差。", ""]

    DOCS.mkdir(parents=True, exist_ok=True)
    (DOCS / "glyph_report.md").write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines[:14]))
    print("... full report ->", DOCS / "glyph_report.md")
    print("COVERAGE", len(union), "SAMPLE_MISS", len(miss), "PROBE", covered, "/", len(PROBE))


if __name__ == "__main__":
    main()
