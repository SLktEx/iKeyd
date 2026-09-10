#!/usr/bin/env python3
"""Render exact README diagrams. Requires Python 3, Pillow and fontconfig.

Debian fonts: fonts-urw-base35 and fonts-dejavu-core.
Run from any directory: python tools/render_readme_art.py
"""

from functools import lru_cache
from html import escape
from pathlib import Path
import re
import subprocess

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs/assets/brand"
SOURCE = ROOT / "docs/examples/readme-demo.ikeyd"
BG, SURFACE, FG, MUTED, LINE, CYAN = (
    "#0A0A0A", "#151515", "#F2F2F2", "#999999", "#363636", "#2ED3D0"
)
SCALE = 2
FONT_NAMES = {"sans": "Nimbus Sans", "bold": "Nimbus Sans:style=Bold", "mono": "DejaVu Sans Mono"}
FONT_PATHS = {k: subprocess.check_output(["fc-match", "-f", "%{file}", v], text=True) for k, v in FONT_NAMES.items()}
SOURCE_TEXT = SOURCE.read_text()


def block(name):
    return re.search(r"keymap " + name + r" \{.*?\n\}", SOURCE_TEXT, re.S).group()


TAP_LINE = next(s.strip() for s in block("S").splitlines() if "LT(" in s)
COMBO_LINE = next(s.strip() for s in block("S").splitlines() if "combo " in s)
NUM_BLOCK = block("NUM")
HERO_CODE = "keymap S {\n    " + TAP_LINE + "\n}\n\n" + NUM_BLOCK

# This is a fixed teaching example, not a general keymap visualizer. Stop if
# its behavior changes so that regenerated pictures cannot silently disagree.
if TAP_LINE != "A = LT(NUM, Z)" or COMBO_LINE != 'combo Q + W = "escape"':
    raise ValueError("The example changed; update the artwork's teaching sequence.")
if dict(re.findall(r'(Q|W|E) = "([123])"', NUM_BLOCK)) != {"Q": "1", "W": "2", "E": "3"}:
    raise ValueError("The NUM mappings no longer match the illustrated key labels.")


@lru_cache(maxsize=128)
def font(size, face="sans"):
    return ImageFont.truetype(FONT_PATHS[face], round(size * SCALE))


class Canvas:
    def __init__(self, width, height, title):
        self.w, self.h = width, height
        self.im = Image.new("RGB", (width * SCALE, height * SCALE), BG)
        self.d = ImageDraw.Draw(self.im)
        self.svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img">',
                    f"<title>{escape(title)}</title>", f'<rect width="{width}" height="{height}" fill="{BG}"/>']

    def rect(self, x, y, w, h, fill=SURFACE, stroke=None, radius=0):
        box = tuple(round(v * SCALE) for v in (x, y, x + w, y + h))
        self.d.rounded_rectangle(box, radius=radius * SCALE, fill=fill, outline=stroke, width=SCALE)
        self.svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{radius}" fill="{fill or "none"}" stroke="{stroke or "none"}"/>')

    def line(self, x1, y1, x2, y2, color=LINE, width=1):
        self.d.line(tuple(round(v * SCALE) for v in (x1, y1, x2, y2)), fill=color, width=width * SCALE)
        self.svg.append(f'<path d="M{x1} {y1} L{x2} {y2}" fill="none" stroke="{color}" stroke-width="{width}"/>')

    def text(self, x, y, value, size=28, color=FG, face="sans", anchor="start"):
        f = font(size, face)
        width = self.d.textlength(value, font=f) / SCALE
        px = x - (width / 2 if anchor == "middle" else width if anchor == "end" else 0)
        # Bounds are checked on every visible text item, including GIF frames.
        box = self.d.textbbox((px * SCALE, y * SCALE), value, font=f, anchor="lt")
        if box[0] < 0 or box[2] > self.w * SCALE or box[3] > self.h * SCALE:
            raise ValueError(f"Text outside canvas: {value!r} {box}")
        self.d.text((px * SCALE, y * SCALE), value, font=f, fill=color, anchor="lt")
        family = "DejaVu Sans Mono, monospace" if face == "mono" else "Nimbus Sans, Arial, sans-serif"
        weight = "700" if face == "bold" else "400"
        self.svg.append(f'<text x="{x}" y="{y + size * .78}" fill="{color}" font-family="{family}" font-size="{size}" font-weight="{weight}" text-anchor="{anchor}" xml:space="preserve">{escape(value)}</text>')
        return width

    def code(self, x, y, value, size=28, leading=39):
        for i, line in enumerate(value.splitlines()):
            cx = x
            for tok in re.split(r'(\bkeymap\b|\bcombo\b|\bLT\b|"[^"]*")', line):
                color = CYAN if tok in ("keymap", "combo", "LT") else FG
                cx += self.text(cx, y + i * leading, tok, size, color, "mono")

    def arrow(self, x1, y, x2):
        self.line(x1, y, x2, y, MUTED, 2)
        self.line(x2 - 10, y - 8, x2, y, MUTED, 2)
        self.line(x2 - 10, y + 8, x2, y, MUTED, 2)

    def key(self, x, y, label, size=100, pressed=False, physical=None, dim=False):
        self.rect(x, y, size, size, CYAN if pressed else SURFACE, CYAN if pressed else LINE, 7)
        fg = BG if pressed else MUTED if dim else FG
        if physical:
            self.text(x + 14, y + 12, physical, max(16, size * .12), fg, "mono")
        self.text(x + size / 2, y + size * (.36 if physical else .31), label,
                  size * (.32 if len(label) > 2 else .40), fg, "mono", "middle")
        if pressed:
            self.line(x + 15, y + size - 12, x + size - 15, y + size - 12, BG, 2)

    def footer(self, label):
        self.line(88, self.h - 78, self.w - 88, self.h - 78)
        self.text(88, self.h - 48, "iKeyd", 25, FG, "bold")
        self.text(self.w - 88, self.h - 47, label, 19, MUTED, "mono", "end")

    def raster(self, width=None):
        w = width or self.w
        return self.im.resize((w, round(self.h * w / self.w)), Image.Resampling.LANCZOS)

    def save(self, name):
        self.raster().save(OUT / f"{name}.png", optimize=True)
        (OUT / f"{name}.svg").write_text("\n".join(self.svg + ["</svg>\n"]))


def keyboard(c, x, y, size, num=False, pressed=()):
    gap = round(size * .09)
    for row, keys in enumerate(("QWE", "ASD")):
        for col, physical in enumerate(keys):
            label = str(col + 1) if num and row == 0 else physical
            c.key(x + col * (size + gap) + row * size * .24, y + row * (size + gap), label,
                  size, physical in pressed, physical if num and row == 0 else None,
                  dim=num and physical in "SD")


def hero():
    c = Canvas(1600, 900, "iKeyd: real DSL and the number layer it defines")
    c.text(88, 65, "KEYBOARD BEHAVIOR IN CODE", 21, MUTED, "mono")
    c.text(88, 129, "Your keyboard,", 72, FG, "bold")
    c.text(88, 214, "defined in code.", 72, FG, "bold")
    c.code(88, 344, HERO_CODE, 29, 39)
    c.text(895, 159, "HOLD A", 23, CYAN, "mono")
    c.text(895, 198, "Number layer", 43, FG, "sans")
    keyboard(c, 877, 300, 162, num=True, pressed=("A",))
    c.text(895, 705, "Tap A: Z key", 25)
    c.text(895, 747, "Hold A: NUM layer", 25)
    c.footer("Example: docs/examples/readme-demo.ikeyd")
    c.save("readme-hero")


def features():
    c = Canvas(1600, 1000, "Tap A for Z; hold A and press Q for 1; combine Q and W for Escape")
    c.text(88, 65, "THREE WAYS TO USE THE SAME KEYS", 21, MUTED, "mono")
    c.text(88, 127, "Tap. Hold. Combine.", 67, FG, "bold")
    rows = [(264, "Tap", ["A"], "Z", "Press and release A.", "Sends the Z key."),
            (478, "Hold", ["A", "Q"], "1", "Hold A, then press Q.", "Q uses the NUM mapping."),
            (692, "Combine", ["Q", "W"], "Esc", "Press Q + W together.", "The pair sends Escape.")]
    for y, title, inputs, output, description, result in rows:
        c.line(88, y - 29, 1512, y - 29)
        c.text(88, y + 1, title, 39, FG, "bold")
        c.text(88, y + 64, description, 25, MUTED)
        for i, k in enumerate(inputs):
            c.key(560 + i * 154, y, k, 112, pressed=title != "Tap")
            if i:
                c.text(690, y + 40, "+", 28, MUTED, "mono")
        c.arrow(908, y + 56, 1000)
        c.key(1052, y, output, 112)
        c.text(1200, y + 42, "OUTPUT", 20, MUTED, "mono")
        c.text(1052, y + 144, result, 23, MUTED)
    c.footer("Example configuration / not the default keymap")
    c.save("readme-features")


def pipeline():
    c = Canvas(1600, 680, "Current Windows build: .ikeyd source, generated C# profiles, iKeyd.exe")
    c.text(88, 65, "THE WINDOWS BUILD", 21, MUTED, "mono")
    c.text(88, 126, "From source to executable.", 65, FG, "bold")
    for x, title in [(88, "Write"), (640, "Compile"), (1270, "Run")]:
        c.text(x, 280, title, 36, FG, "bold")
    c.text(88, 362, "config/", 28, MUTED, "mono")
    c.text(88, 411, "hotkeySKG.ikeyd", 30, FG, "mono")
    c.arrow(468, 385, 581)
    c.text(640, 362, "GeneratedProfile.g.cs", 24, FG, "mono")
    c.text(640, 411, "GeneratedMouseProfile.g.cs", 24, FG, "mono")
    c.arrow(1080, 385, 1205)
    c.text(1270, 372, "iKeyd.exe", 33, FG, "mono")
    c.text(88, 514, "Readable configuration", 25, MUTED)
    c.text(640, 514, "Profiles generated during the build", 25, MUTED)
    c.text(1270, 500, "Compiled", 25, MUTED)
    c.text(1270, 535, "profile included", 25, MUTED)
    c.footer("Current .NET / Windows build")
    c.save("readme-dsl")


def animation_frame(phase, num=False, pressed=(), output=None, description=""):
    c = Canvas(1200, 700, "Illustrated layer-tap example")
    c.text(64, 48, "TAP / HOLD", 20, MUTED, "mono")
    c.text(64, 92, "One A key, two behaviors.", 49, FG, "bold")
    c.code(64, 173, TAP_LINE, 27)
    c.line(64, 225, 1136, 225)
    c.text(64, 260, phase, 27, FG, "bold")
    keyboard(c, 64, 320, 114, num, pressed)
    c.text(635, 266, "OUTPUT", 19, MUTED, "mono")
    if output:
        c.key(635, 320, output, 124)
    else:
        c.line(647, 385, 687, 385, MUTED, 2)
    c.text(827, 335, "ACTIVE LAYER", 18, MUTED, "mono")
    c.text(827, 384, "NUM" if num else "S", 42, CYAN if num else FG, "mono")
    c.text(635, 495, description, 24, MUTED)
    c.line(64, 614, 1136, 614)
    c.text(64, 649, "iKeyd", 23, FG, "bold")
    c.text(1136, 650, "Illustration / timing slowed for clarity", 17, MUTED, "mono", "end")
    return c


def animation():
    # Semantic stages, not a recording or a latency measurement. Long delays
    # intentionally make each key-down, resolution and cleanup readable.
    stages = [
        ("Tap A", False, (), None, "Press and release A.", 950),
        ("Tap A", False, ("A",), None, "Waiting for release.", 400),
        ("Tap A", False, (), "Z", "Release sends the Z key.", 1350),
        ("Hold A", False, (), None, "Keep A pressed.", 750),
        ("Hold A", False, ("A",), None, "Waiting for hold resolution.", 650),
        ("Hold A", True, ("A",), None, "NUM is active while A is held.", 1250),
        ("Press Q while holding A", True, ("A", "Q"), "1", "Q now sends 1.", 1250),
        ("Release Q", True, ("A",), "1", "A still holds NUM.", 850),
        ("Release A", False, (), None, "Back to S. No Z is sent.", 1350),
    ]
    frames, durations = [], []
    for phase, num, pressed, output, desc, ms in stages:
        frames.append(animation_frame(phase, num, pressed, output, desc).raster())
        durations.append(ms)
    # A common palette prevents label and border colors flickering between frames.
    strip = Image.new("RGB", (1200, 700 * len(frames)))
    for i, f in enumerate(frames):
        strip.paste(f, (0, i * 700))
    palette = strip.quantize(colors=128)
    indexed = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    indexed[0].save(OUT / "readme-tap-hold.gif", save_all=True, append_images=indexed[1:],
                    duration=durations, loop=0, disposal=1, optimize=True)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    hero()
    features()
    pipeline()
    animation()
    for path in sorted(OUT.glob("readme-*")):
        print(f"{path.relative_to(ROOT)}  {path.stat().st_size:,} bytes")
