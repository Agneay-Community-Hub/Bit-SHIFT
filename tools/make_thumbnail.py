"""Draws promo art in the game's visual style.

    python tools/make_thumbnail.py
        -> Builds/Trailer/BITSHIFT-thumbnail.png   1280x720  (YouTube thumbnail)
        -> Builds/itch/BITSHIFT-cover.png          630x500   (itch.io cover image)
"""
import random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 2                       # supersample factor for smooth edges
FONTS = Path("C:/Windows/Fonts")
ROOT = Path(__file__).resolve().parent.parent
THUMB_OUT = ROOT / "Builds" / "Trailer" / "BITSHIFT-thumbnail.png"
COVER_OUT = ROOT / "Builds" / "itch" / "BITSHIFT-cover.png"

BG = (7, 10, 16)
FLOOR_A, FLOOR_B = (17, 25, 37), (20, 32, 48)
ACCENT = (57, 255, 176)
CYAN = (46, 197, 255)
WARN = (255, 209, 102)
ERR = (255, 77, 109)


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), int(size * S))


def draw(img):
    d = ImageDraw.Draw(img)
    d._image_ref = img
    return d


def rr(d, box, r, fill=None, outline=None, width=0):
    d.rounded_rectangle([v * S for v in box], radius=r * S, fill=fill, outline=outline, width=int(width * S))


def text(d, xy, s, f, fill, anchor="la", stroke=0, stroke_fill=None):
    if len(fill) == 4 and fill[3] < 255 and hasattr(d, "_image_ref"):
        layer = Image.new("RGBA", d._image_ref.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).text((xy[0] * S, xy[1] * S), s, font=f, fill=fill, anchor=anchor)
        d._image_ref.alpha_composite(layer)
        return
    d.text((xy[0] * S, xy[1] * S), s, font=f, fill=fill, anchor=anchor,
           stroke_width=int(stroke * S), stroke_fill=stroke_fill)


def block(img, x, y, cells, digits, color, cell=96):
    """A register block like the in-game ones: rounded body, dark inset, anchor cap, digits."""
    k = cell / 96
    w = cells * cell
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    rr(ImageDraw.Draw(glow), (x - 6 * k, y - 6 * k, x + w + 6 * k, y + cell + 6 * k), 30 * k, fill=color + (150,))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(22 * k * S)))
    d = draw(img)
    rr(d, (x, y, x + w, y + cell), 26 * k, fill=color)
    inner = tuple(int(c * 0.4 + b * 0.6) for c, b in zip(color, BG))
    rr(d, (x + 10 * k, y + 10 * k, x + w - 10 * k, y + cell - 10 * k), 18 * k, fill=inner)
    rr(d, (x + 14 * k, y + 22 * k, x + 22 * k, y + cell - 22 * k), 3 * k, fill=(255, 255, 255))  # anchor cap
    f = font("consolab.ttf", 58 * k)
    for i, ch in enumerate(digits):
        lit = ch == "1"
        text(d, (x + (52 + i * 42) * k, y + cell / 2), ch, f, (255, 255, 255) if lit else (255, 255, 255, 80), "mm")


def background(w, h, cell, fade_top, fade_len, rain_count, rain_size, seed=3):
    img = Image.new("RGBA", (w * S, h * S), BG + (255,))
    d = draw(img)
    for gy in range(0, h // cell + 1):
        for gx in range(0, w // cell + 1):
            c = FLOOR_A if (gx + gy) % 2 == 0 else FLOOR_B
            a = min(1.0, max(0.0, (gy * cell - fade_top) / fade_len))
            c = tuple(int(BG[i] + (c[i] - BG[i]) * a) for i in range(3))
            d.rectangle([gx * cell * S, gy * cell * S, (gx + 1) * cell * S - 1, (gy + 1) * cell * S - 1], fill=c)
    rnd = random.Random(seed)
    fb = font("consolab.ttf", rain_size)
    rain = Image.new("RGBA", img.size, (0, 0, 0, 0))
    rd = ImageDraw.Draw(rain)
    for _ in range(rain_count):
        x, y = rnd.randrange(0, w), rnd.randrange(0, h)
        rd.text((x * S, y * S), rnd.choice("01"), font=fb, fill=ACCENT + (rnd.randrange(14, 46),))
    img.alpha_composite(rain)
    return img


def logo(img, cx, cy, size):
    f = font("ariblk.ttf", size)
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    text(ImageDraw.Draw(glow), (cx, cy), "BITSHIFT", f, ACCENT + (120,), "mm")
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(size * 0.155 * S)))
    d = draw(img)
    bit_w = d.textlength("BIT", font=f) / S
    left = cx - d.textlength("BITSHIFT", font=f) / S / 2
    text(d, (left, cy), "BIT", f, ACCENT, "lm", size / 56, (0, 0, 0))
    text(d, (left + bit_w, cy), "SHIFT", f, (245, 255, 250), "lm", size / 56, (0, 0, 0))


def sticker(img, x, y, scale):
    stk = Image.new("RGBA", (int(560 * scale * S), int(140 * scale * S)), (0, 0, 0, 0))
    sd = ImageDraw.Draw(stk)
    rr(sd, (10 * scale, 10 * scale, 550 * scale, 130 * scale), 18 * scale, fill=WARN, outline=(0, 0, 0), width=5 * scale)
    text(sd, (280 * scale, 72 * scale), "OVERFLOW!", font("ariblk.ttf", 72 * scale), (20, 16, 8), "mm")
    stk = stk.rotate(-6, resample=Image.BICUBIC, expand=True)
    img.alpha_composite(stk, (int(x * S), int(y * S)))


def lost_bits(d, x, y, scale):
    text(d, (x, y), "1", font("consolab.ttf", 64 * scale), WARN, "mm")
    for i, (dx, dy, a) in enumerate([(30, -50, 160), (60, -90, 90)]):
        text(d, (x + dx * scale, y + dy * scale), "1", font("consolab.ttf", (46 - i * 10) * scale), WARN + (a,), "mm")


def badge(d, x, y, w, h, size):
    rr(d, (x, y, x + w, y + h), 12, fill=(10, 16, 24), outline=ACCENT, width=3)
    text(d, (x + w / 2, y + h / 2), "GITHUB GAME OFF 2026", font("bahnschrift.ttf", size), ACCENT, "mm")


def finish(img, w, h, out):
    W, H = w * S, h * S
    vig = Image.new("L", (W, H), 0)
    ImageDraw.Draw(vig).ellipse([-W * 0.15, -H * 0.25, W * 1.15, H * 1.25], fill=255)
    vig = vig.filter(ImageFilter.GaussianBlur(min(w, h) / 6 * S))
    dark = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    img = Image.composite(img, dark, vig.point(lambda v: int(80 + v * 175 / 255)))
    out.parent.mkdir(parents=True, exist_ok=True)
    img.convert("RGB").resize((w, h), Image.LANCZOS).save(out, optimize=True)
    print("wrote", out)


def thumbnail():
    img = background(1280, 720, 80, 180, 360, 220, 22)
    logo(img, 640, 150, 168)
    by = 400
    block(img, 40, by, 5, "101", CYAN, cell=88)
    d = draw(img)
    text(d, (640, by + 48), "<< 1", font("ariblk.ttf", 76), ACCENT, "mm", 4, (0, 0, 0))
    block(img, 830, by, 2, "010", CYAN)
    d = draw(img)
    lost_bits(d, 1050, by - 70, 1)
    sticker(img, 700, 538, 1)
    d = draw(img)
    text(d, (260, by - 34), "5 = 0b101", font("consolab.ttf", 34), (170, 190, 215), "mm")
    text(d, (926, by - 34), "2?!", font("ariblk.ttf", 40), ERR, "mm", 2, (0, 0, 0))
    badge(d, 40, 640, 380, 56, 28)
    finish(img, 1280, 720, THUMB_OUT)


def cover():
    """630x500: logo on top, the 101 -> 010 hook stacked vertically so it reads at small size."""
    img = background(630, 500, 50, 110, 220, 120, 16, seed=5)
    logo(img, 315, 66, 92)
    d = draw(img)
    text(d, (315, 120), "a puzzler about running out of bits", font("bahnschrift.ttf", 21), (200, 215, 235), "mm")

    # 5 = 0b101 (wide block)
    block(img, 85, 172, 5, "101", CYAN, cell=58)
    d = draw(img)
    text(d, (87, 163), "5 = 0b101", font("consolab.ttf", 19), (170, 190, 215), "ls")
    # << 1
    text(d, (315, 264), "<< 1", font("ariblk.ttf", 46), ACCENT, "mm", 3, (0, 0, 0))
    # 2 = 0b010 (now shorter!)
    block(img, 257, 296, 2, "010", CYAN, cell=58)
    d = draw(img)
    text(d, (396, 326), "2?!", font("ariblk.ttf", 30), ERR, "lm", 2, (0, 0, 0))
    lost_bits(d, 478, 292, 0.6)
    sticker(img, 168, 368, 0.5)
    d = draw(img)
    badge(d, 185, 462, 260, 28, 16)
    finish(img, 630, 500, COVER_OUT)


if __name__ == "__main__":
    thumbnail()
    cover()
