# InkPen 字库报告

| 字库 | 字数 | 说明 |
|---|---:|---|
| `local.zip` | 8604 | 本机私有/不分发 |
| `mmah.zip` | 9574 | 随软件分发 |
| `fallback.zip` | 11442 | 随软件分发 |
| `latin.zip` | 114 | 随软件分发 |
| `latin_script.zip` | 114 | 随软件分发 |
| `pinyin.zip` | 45 | 随软件分发 |

**并集覆盖**：21666 个码点

## 方向契约（InkPen Glyph v1）

坐标系固定为 **1024 网格、原点左下、Y 向上**。所有来源在入库前必须对齐到该约定，
否则会整包上下颠倒——表现是「每个字都转了 180°」，而数据结构、笔画数、坐标范围全部正常，
不看几何方向查不出来。

| 字库 | 原始坐标 | 入库变换 |
|---|---|---|
| `mmah.zip` | hanzi-writer medians，Y 向上 | `y + 124` 平移到网格 |
| `fallback.zip` | Hershey 图像坐标，Y 向下 | `(1 - y) * GRID` 镜像 |
| `latin*.zip` | Hershey 数据，Y 向上 | 缩放居中 |
| `local.zip` | `.gfont`，Y 向下（原始范围 `-600..199`） | `GRID - (y*scale + offY)` 镜像（**0.1.1 修复，此前漏了**） |

回归保护：`tests/InkPen.Tests/GlyphOrientationTests.cs` 用垂直重心相关性
（`local` vs `mmah` 须正相关）+ 「三」上横短于下横 + 「P」圆环在上三个判据锁定方向。

## 样本回退链

| 字符 | 命中字库 | 笔画数 | 点数 |
|---|---|---:|---:|
| 写 | local.zip | 2 | 49 |
| 字 | local.zip | 2 | 48 |
| 机 | local.zip | 4 | 51 |
| 工 | local.zip | 1 | 21 |
| 作 | local.zip | 2 | 46 |
| 台 | local.zip | 2 | 41 |
| 一 | local.zip | 1 | 5 |
| 二 | local.zip | 1 | 17 |
| 三 | local.zip | 1 | 22 |
| 四 | local.zip | 2 | 41 |
| 五 | local.zip | 2 | 33 |
| 六 | local.zip | 2 | 28 |
| 七 | local.zip | 1 | 23 |
| 八 | local.zip | 2 | 16 |
| 九 | local.zip | 1 | 34 |
| 十 | local.zip | 2 | 13 |
| 你 | local.zip | 2 | 57 |
| 好 | local.zip | 1 | 58 |
| 世 | local.zip | 2 | 42 |
| 界 | local.zip | 2 | 60 |
| H | local.zip | 2 | 19 |
| e | local.zip | 1 | 30 |
| l | local.zip | 2 | 7 |
| l | local.zip | 2 | 7 |
| o | local.zip | 1 | 27 |
| 1 | local.zip | 2 | 7 |
| 2 | local.zip | 1 | 20 |
| 3 | local.zip | 1 | 23 |
| ， | local.zip | 1 | 14 |
| 。 | local.zip | 1 | 16 |
| ！ | local.zip | 1 | 15 |
| ？ | local.zip | 1 | 28 |
| ā | local.zip | 2 | 40 |
| á | local.zip | 2 | 32 |
| ǎ | local.zip | 2 | 41 |
| à | local.zip | 3 | 31 |

样本缺字：无

## 常用字抽检

500 / 500 = 100.0%

## 坐标系抽检

| 字库 | 越界字形数 | 空笔画字形数 |
|---|---:|---:|
| `local.zip` | 0 | 0 |
| `mmah.zip` | 0 | 0 |
| `fallback.zip` | 607 | 0 |
| `latin.zip` | 16 | 0 |
| `latin_script.zip` | 23 | 0 |
| `pinyin.zip` | 0 | 0 |

坐标系：1024 网格、Y 向上、原点左下；越界允许 1 单位浮点误差。
