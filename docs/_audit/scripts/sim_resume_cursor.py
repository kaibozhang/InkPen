# -*- coding: utf-8 -*-
"""维度 C 复核脚本：断点续写的「块内相对行号」vs「作业绝对行号」。

逐行移植：
  PenTracker.SetSentLines        src/App/Core/Grbl/PenTracker.cs:80-100
  PenTracker.CompletedPaths      src/App/Core/Grbl/PenTracker.cs:103-110
  MainForm.StartWriting 的送料    src/App/UI/MainForm.cs:1222-1239
  StreamController.Sent 语义      src/App/Core/Grbl/GrblCore.cs:459, 473, 523
  MainForm 进度回调               src/App/UI/MainForm.cs:383-390
"""


class Line(object):
    def __init__(self, down):
        self.Ispendown = down


def build_job(n_lines=1000, strokes=(0, 300, 600)):
    gcode = [Line(False)] * n_lines
    for s in strokes:
        gcode[s] = Line(True)
    paths = [type("P", (), {"FirstGCodeLine": s, "PageIndex": 0})() for s in strokes]
    return type("Job", (), {"GCode": gcode, "Paths": paths})()


class Pen(object):
    def __init__(self, job):
        self._job = job
        self._pathCursor = 0
        self._lastUpper = -1
        self.CurrentPath = -1
        self.LastSentLine = -1
        self.PenDown = False

    def set_sent_lines(self, sent):
        if self._job is None or len(self._job.Paths) == 0:
            self.CurrentPath = -1
            return
        upper = min(sent, len(self._job.GCode)) - 1
        if upper < 0:
            self.CurrentPath = -1
            self.PenDown = False
            return
        if upper < self._lastUpper:
            self._pathCursor = 0
        self._lastUpper = upper
        if self._pathCursor >= len(self._job.Paths):
            self._pathCursor = len(self._job.Paths) - 1
        while (self._pathCursor + 1 < len(self._job.Paths)
               and self._job.Paths[self._pathCursor + 1].FirstGCodeLine <= upper):
            self._pathCursor += 1
        self.CurrentPath = self._pathCursor if self._job.Paths[0].FirstGCodeLine <= upper else -1
        self.LastSentLine = upper
        self.PenDown = self._job.GCode[upper].Ispendown

    def completed(self):
        if self.CurrentPath < 0:
            return 0
        return self.CurrentPath if self.PenDown else self.CurrentPath + 1


def run(from_line):
    job = build_job()
    # MainForm.StartWriting: src = ToText(job, fromLine) -> 块只含 fromLine 之后的非空行
    chunk = [l for l in job.GCode[from_line:] if True]
    total_in_chunk = len(chunk)

    pen = Pen(job)
    # MainForm.cs:1236 先把游标摆到绝对行 fromLine（正确）
    pen.set_sent_lines(from_line)
    print("续写起点 fromLine=%d  ->  SetSentLines(%d): CurrentPath=%d, 已完成=%d"
          % (from_line, from_line, pen.CurrentPath, pen.completed()))

    # MainForm.cs:389 把 StreamController 的块内 Sent 直接当绝对行号喂进去
    for sent in (0, 1, 40, 120, 200, total_in_chunk):
        pen.set_sent_lines(sent)
        print("  块内 Sent=%-4d -> CurrentPath=%-3d 已完成=%-2d 进度条=%d/%d"
              % (sent, pen.CurrentPath, pen.completed(), sent, total_in_chunk))


if __name__ == "__main__":
    run(600)
    print()
    print("对照：若把 fromLine 偏移补上（SetSentLines(fromLine + sent)）")
    job = build_job()
    pen = Pen(job)
    for sent in (0, 200):
        pen.set_sent_lines(600 + sent)
        print("  Sent=%-4d -> CurrentPath=%d 已完成=%d" % (sent, pen.CurrentPath, pen.completed()))
