# -*- coding: utf-8 -*-
"""gen_core.py — AC6956C 手册生成框架：页面模板 / 组件 / SVG 绘图 DSL"""

import html as _html
import os

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# 页面注册表：fname -> (标题, 英文副标题, 图标)
PAGES = [
    ("index.html",     "芯片总览",   "Overview",      "🏛"),
    ("clock.html",     "时钟系统",   "CLK / PLL",     "⏱"),
    ("gpio.html",      "GPIO 与 IO 复用", "GPIO / IOMAP", "🔌"),
    ("irq.html",       "中断系统",   "Interrupts",    "⚡"),
    ("uart.html",      "UART 串口",  "UART ×3",       "📡"),
    ("spi.html",       "SPI 总线",   "SPI ×3",        "🔗"),
    ("iic.html",       "IIC 总线",   "I²C",           "🔗"),
    ("timer.html",     "定时器",     "TIMER ×4",      "⏲"),
    ("mcpwm.html",     "MCPWM",      "PWM ×8",        "📊"),
    ("adc.html",       "ADC 采样",   "SARADC",        "🎚"),
    ("audio.html",     "音频系统",   "AUDIO / ANA",   "🎵"),
    ("usb.html",       "USB 设备",   "USB FS",        "🔌"),
    ("sdio.html",      "SD / TF 卡", "SDMMC ×2",      "💾"),
    ("sfc.html",       "SPI Flash 控制器", "SFC",      "🔏"),
    ("power.html",     "电源管理",   "PMU / P33",     "🔋"),
    ("dma-sec.html",   "DMA 与安全加速器", "DMA/CRC/AES", "🚀"),
    ("misc.html",      "其他外设",   "WDT/RTC/...",   "📦"),
]


def esc(s):
    return _html.escape(str(s))


# ---------------------------------------------------------------- 页面骨架
def page(fname, crumb, title, lead, body):
    """生成一个完整 HTML 页面"""
    idx = [p[0] for p in PAGES].index(fname)
    prev_p = PAGES[idx - 1] if idx > 0 else None
    next_p = PAGES[idx + 1] if idx < len(PAGES) - 1 else None

    nav = "".join(
        f'<a href="{f}"{" class=active" if f == fname else ""}>{esc(t)}</a>'
        for f, t, e, i in PAGES)

    foot_prev = (f'<a href="{prev_p[0]}">← {esc(prev_p[1])}</a>' if prev_p else '<span></span>')
    foot_next = (f'<a href="{next_p[0]}">{esc(next_p[1])} →</a>' if next_p else '<span></span>')

    doc = f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{esc(title)} — AC6956C 参考手册</title>
<link rel="stylesheet" href="assets/style.css">
</head>
<body>
<nav class="topbar"><div class="topbar-inner">
  <a class="brand" href="index.html">
    <span class="logo">AC6956C<em>·BR23</em></span>
    <span class="sub">芯片参考手册</span>
  </a>
  <div class="navlinks">{nav}</div>
</div></nav>

<div class="wrap">
  <header class="hero">
    <div class="crumb">{esc(crumb)}</div>
    <h1>{esc(title)}</h1>
    <p>{lead}</p>
  </header>

  <div class="layout">
    <main class="main">
{body}
      <div class="footer">
        {foot_prev}
        <span class="src">依据 SDK 头文件逆向整理 · JieLi AC6956C (BR23) · 仅供开发参考</span>
        {foot_next}
      </div>
    </main>
    <aside class="toc"><div class="t">本页目录</div>{toc_of(body)}</aside>
  </div>
</div>
</body>
</html>"""
    path = os.path.join(OUT, fname)
    with open(path, "w", encoding="utf-8") as f:
        f.write(doc)
    print("write", fname)


import re
def toc_of(body):
    out = []
    for m in re.finditer(r'<h2 id="([^"]+)"[^>]*>(.*?)</h2>', body):
        out.append(f'<a href="#{m.group(1)}">{re.sub("<[^>]+>", "", m.group(2))}</a>')
    return "".join(out)


# ---------------------------------------------------------------- 组件
def sec(id_, title, inner):
    return f'<section class="card" id="{id_}">\n<h2 id="{id_}">{title}</h2>\n{inner}\n</section>\n'


def p(inner):
    return f"<p>{inner}</p>\n"


def ul(items):
    return "<ul>" + "".join(f"<li>{i}</li>" for i in items) + "</ul>\n"


def table(headers, rows, cls=""):
    h = "".join(f"<th>{c}</th>" for c in headers)
    b = ""
    for r in rows:
        b += "<tr>" + "".join(f"<td>{c}</td>" for c in r) + "</tr>\n"
    return (f'<div class="tbl-wrap"><table class="tbl {cls}">'
            f"<thead><tr>{h}</tr></thead><tbody>\n{b}</tbody></table></div>\n")


def rw(tag):
    """读写属性徽章"""
    m = {"RW": "rw", "RO": "ro", "WO": "wo"}
    return f'<span class="rw {m.get(tag, "rw")}">{tag}</span>'


def regtable(regs):
    """寄存器总表: (offset, name, rw, desc)"""
    rows = []
    for off, name, mode, desc in regs:
        rows.append((f'<code class="rg">0x{off:02X}</code>',
                     f'<code class="rg">{name}</code>', rw(mode), desc))
    return table(["偏移", "寄存器", "属性", "说明"], rows)


def bits(rows):
    """位段表: (bits, name, desc)"""
    rr = [(f'<span class="bits-chip">{b}</span>', f"<code class='rg'>{n}</code>", d)
          for b, n, d in rows]
    return table(["位域", "名称", "说明"], rr)


def code(txt, lang="c"):
    txt = esc(txt)
    return f"<pre><code>{txt}</code></pre>\n"


def callout(kind, text):
    return f'<div class="{kind}">{text}</div>\n'


def grid(cards, cols="g3"):
    inner = "".join(
        f'<{"a href=" + chr(34) + c[3] + chr(34) if len(c) > 3 else "div"} class="pcard">'
        f'<div class="ic">{c[0]}</div><div class="nm">{c[1]}</div>'
        f'<div class="ds">{c[2]}</div>'
        f'{"</a>" if len(c) > 3 else "</div>"}'
        for c in cards)
    return f'<div class="grid {cols}">{inner}</div>\n'


def kpis(items):
    inner = "".join(f'<div class="kpi"><div class="v">{v}</div>'
                    f'<div class="k">{k}</div></div>' for v, k in items)
    return f'<div class="grid g4">{inner}</div>\n'


def fig(svg, cap=""):
    c = f'<div class="figcap">{cap}</div>' if cap else ""
    return f'<div class="fig">{svg}{c}</div>\n'


# ---------------------------------------------------------------- SVG DSL
class Svg:
    """轻量 SVG 块图构造器"""

    PALETTE = {
        "cpu":   ("#2456d6", "#e8eefc"),
        "bus":   ("#8a94a6", "#eef1f7"),
        "peri":  ("#1e7f4f", "#e6f5ec"),
        "mem":   ("#b7791f", "#fdf3e3"),
        "ana":   ("#7c2d92", "#f4ebfa"),
        "dark":  ("#14264d", "#dbe4f5"),
    }

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.parts = []

    def box(self, x, y, w, h, lines, kind="peri", fs=13, bold_first=True, rx=9):
        stroke, fill = self.PALETTE[kind]
        self.parts.append(
            f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" '
            f'fill="{fill}" stroke="{stroke}" stroke-width="1.4"/>')
        n = len(lines)
        lh = fs + 4
        y0 = y + h / 2 - (n - 1) * lh / 2 + fs * 0.35
        for i, t in enumerate(lines):
            weight = "800" if i == 0 and bold_first and n > 1 else "500"
            size = fs if i == 0 and bold_first else fs - 1.5
            color = "#14264d" if i == 0 else "#4a5568"
            self.parts.append(
                f'<text x="{x + w / 2}" y="{y0 + i * lh}" text-anchor="middle" '
                f'font-family="Segoe UI,PingFang SC,Microsoft YaHei,sans-serif" '
                f'font-size="{size}" font-weight="{weight}" fill="{color}">{esc(t)}</text>')
        return self

    def arrow(self, x1, y1, x2, y2, label="", dashed=False, color="#5b6b8c"):
        dash = ' stroke-dasharray="5,4"' if dashed else ""
        self.parts.append(
            f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" '
            f'stroke-width="1.6" marker-end="url(#arw)"{dash}/>')
        if label:
            mx, my = (x1 + x2) / 2, (y1 + y2) / 2
            self.parts.append(
                f'<text x="{mx}" y="{my - 5}" text-anchor="middle" font-size="11" '
                f'fill="#5b6b8c" font-family="Segoe UI,sans-serif">{esc(label)}</text>')
        return self

    def line(self, x1, y1, x2, y2, color="#5b6b8c", dashed=False, width=1.6):
        dash = ' stroke-dasharray="5,4"' if dashed else ""
        self.parts.append(
            f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" '
            f'stroke-width="{width}"{dash}/>')
        return self

    def text(self, x, y, s, fs=11, color="#4a5568", anchor="middle", bold=False):
        w = '800' if bold else '500'
        self.parts.append(
            f'<text x="{x}" y="{y}" text-anchor="{anchor}" font-size="{fs}" '
            f'fill="{color}" font-weight="{w}" '
            f'font-family="Segoe UI,PingFang SC,Microsoft YaHei,sans-serif">{esc(s)}</text>')
        return self

    def render(self):
        return (f'<svg viewBox="0 0 {self.w} {self.h}" xmlns='
                f'"http://www.w3.org/2000/svg" role="img">'
                f'<defs><marker id="arw" markerWidth="9" markerHeight="7" '
                f'refX="8" refY="3.5" orient="auto"><polygon points="0 0, 9 3.5, 0 7" '
                f'fill="#5b6b8c"/></marker></defs>'
                f'{"".join(self.parts)}</svg>')
