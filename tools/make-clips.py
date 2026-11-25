#!/usr/bin/env python3
"""Make one of the docs' side-by-side clips: the host's screen on the left, a friend's on the right, as an animated
WebP.

The frames come from the map test's rec-start step (src/DarkwoodMP.MapTests/World/MapScenarioClips.cs): DIR/host and
DIR/c1 hold JPG files named MILLISECONDS-SEQ.jpg by the machine's clock, which both games share. Only the time both
recorded is used; for each tick of the clip the nearest frame of each game is taken, so the two sides show the same
moment. The file is made smaller (lower quality, then fewer frames a second) until it fits --max-kb.

Needs Pillow (python3 -m venv VENV && VENV/bin/pip install pillow). tools/run-test.sh never runs this.

    make-clips.py DIR OUT.webp [--fps 12] [--width 480] [--cut A-B ...] [--zoom Z] [--focus X,Y] [--quality 70]
                  [--max-kb 2048] [--preview]

--width is each side's width in pixels. --cut A-B keeps seconds A to B of the shared time (several cuts are joined).
--zoom Z keeps 1/Z of each screen's width and height (the game's camera is far out), around --focus X,Y (fractions of
the screen; the middle by default). --preview writes OUT.png instead, one pair a second, for choosing the cuts.
"""

import argparse
import bisect
import io
import os
import sys

from PIL import Image, ImageDraw, ImageFont

GAP = 4
NAMES = (("host", "Host"), ("c1", "Friend"))


def frames(folder):
    """[(ms, path)] in time order."""
    out = []
    for name in os.listdir(folder):
        if name.endswith(".jpg"):
            out.append((int(name.split("-")[0]), os.path.join(folder, name)))
    return sorted(out)


def nearest(side, ms):
    times = [t for t, _ in side]
    i = bisect.bisect_left(times, ms)
    best = min((j for j in (i - 1, i) if 0 <= j < len(side)), key=lambda j: abs(times[j] - ms))
    return side[best][1]


def font(size):
    for path in ("/usr/share/fonts/dejavu-sans-fonts/DejaVuSans-Bold.ttf",
                 "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default(size=size)


def caption(img, text, f):
    d = ImageDraw.Draw(img)
    box = d.textbbox((0, 0), text, font=f)
    w, h = box[2] - box[0], box[3] - box[1]
    d.rectangle((6, 6, 6 + w + 12, 6 + h + 10), fill=(0, 0, 0))
    d.text((12 - box[0], 11 - box[1]), text, font=f, fill=(235, 235, 235))


def pair(paths, width, f, zoom=1.0, focus=(0.5, 0.5)):
    sides = []
    for path, (_, label) in zip(paths, NAMES):
        im = Image.open(path).convert("RGB")
        if zoom > 1:
            # The game's camera follows its player, so the middle of the screen is where the action is (--focus moves
            # the kept part, e.g. to a window at one side).
            w, h = im.width / zoom, im.height / zoom
            x = min(max(focus[0] * im.width - w / 2, 0), im.width - w)
            y = min(max(focus[1] * im.height - h / 2, 0), im.height - h)
            im = im.crop((round(x), round(y), round(x + w), round(y + h)))
        im = im.resize((width, round(im.height * width / im.width / 2) * 2), Image.LANCZOS)
        caption(im, label, f)
        sides.append(im)
    h = max(s.height for s in sides)
    out = Image.new("RGB", (width * 2 + GAP, h), (20, 20, 20))
    out.paste(sides[0], (0, 0))
    out.paste(sides[1], (width + GAP, 0))
    return out


def ticks(start, end, fps, cuts):
    spans = [(start + a * 1000, start + b * 1000) for a, b in cuts] or [(start, end)]
    step = 1000.0 / fps
    out = []
    for a, b in spans:
        t = max(a, start)
        while t <= min(b, end):
            out.append(t)
            t += step
    return out


def encode(images, fps, quality):
    buf = io.BytesIO()
    images[0].save(buf, format="WEBP", save_all=True, append_images=images[1:], duration=round(1000 / fps), loop=0,
                   quality=quality, method=6)
    return buf.getvalue()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("dir")
    ap.add_argument("out")
    ap.add_argument("--fps", type=float, default=12)
    ap.add_argument("--width", type=int, default=480)
    ap.add_argument("--cut", action="append", default=[])
    ap.add_argument("--zoom", type=float, default=1.0)
    ap.add_argument("--focus", default="0.5,0.5")
    ap.add_argument("--quality", type=int, default=70)
    ap.add_argument("--max-kb", type=int, default=2048)
    ap.add_argument("--preview", action="store_true")
    a = ap.parse_args()

    sides = [frames(os.path.join(a.dir, folder)) for folder, _ in NAMES]
    if not all(sides):
        sys.exit(f"no frames in {a.dir}/host or {a.dir}/c1")
    start = max(s[0][0] for s in sides)
    end = min(s[-1][0] for s in sides)
    if end <= start:
        sys.exit("the two recordings don't overlap")
    focus = tuple(float(x) for x in a.focus.split(","))
    cuts = [tuple(float(x) for x in c.split("-")) for c in a.cut]
    f = font(max(12, a.width // 32))
    print(f"shared time {(end - start) / 1000:.1f} s; host {len(sides[0])} frames, friend {len(sides[1])}")

    if a.preview:
        shown = [pair([nearest(s, t) for s in sides], 320, f, a.zoom, focus) for t in ticks(start, end, 1, cuts)]
        cols = 3
        rows = (len(shown) + cols - 1) // cols
        w, h = shown[0].size
        sheet = Image.new("RGB", (cols * w, rows * (h + 20)), (0, 0, 0))
        d = ImageDraw.Draw(sheet)
        for i, im in enumerate(shown):
            x, y = (i % cols) * w, (i // cols) * (h + 20)
            sheet.paste(im, (x, y + 20))
            d.text((x + 4, y + 3), f"{i} s", fill=(255, 255, 0))
        path = os.path.splitext(a.out)[0] + ".png"
        sheet.save(path)
        print(f"preview {path}: one pair a second")
        return

    fps, quality = a.fps, a.quality
    while True:
        images = [pair([nearest(s, t) for s in sides], a.width, f, a.zoom, focus) for t in ticks(start, end, fps, cuts)]
        data = encode(images, fps, quality)
        kb = len(data) // 1024
        print(f"{len(images)} frames, {fps:g} fps, quality {quality}: {kb} KB")
        if kb <= a.max_kb:
            break
        if quality > 40:
            quality -= 10
        elif fps > 8:
            fps -= 2
            quality = a.quality
        else:
            sys.exit(f"still {kb} KB at {fps:g} fps, quality {quality}: cut it shorter (--cut)")
    with open(a.out, "wb") as out:
        out.write(data)
    print(f"wrote {a.out}")


if __name__ == "__main__":
    main()
