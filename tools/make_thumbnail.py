"""Draws the YouTube / itch.io thumbnail (1280x720) in the game's visual style.

    python tools/make_thumbnail.py            -> Builds/Trailer/BITSHIFT-thumbnail.png
"""
import random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 2                       # supersample factor for smooth edges
W, H = 1280 * S, 720 * S
FONTS = Path("C:/Windows/Fonts")
OUT = Path(__file__).resolve().parent.parent / "Builds" / "Trailer" / "BITSHIFT-thumbnail.png"

BG = (7, 10, 16)
FLOOR_A, FLOOR_B = (17, 25, 37), (20, 32, 48)
ACCENT = (57, 255, 176)
CYAN = (46, 197, 255)
WARN = (255, 209, 102)
ERR = (255, 77, 109)


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), size * S)


def draw(img):
    d = ImageDraw.Draw(img)
    d._image_ref = img
    return d


def rr(d, box, r, fill=None, outline=None, width=0):
    d.rounded_rectangle([v * S for v in box], radius=r * S, fill=fill, outline=outline, width=width * S)


def text(d, xy, s, f, fill, anchor="la", stroke=0, stroke_fill=None):
    if len(fill) == 4 and fill[3] < 255 and hasattr(d, "_image_ref"):
        layer = Image.new("RGBA", d._image_ref.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).text((xy[0] * S, xy[1] * S), s, font=f, fill=fill, anchor=anchor)
        d._image_ref.alpha_composite(layer)
        return
    d.text((xy[0] * S, xy[1] * S), s, font=f, fill=fill, anchor=anchor,
           stroke_width=stroke * S, stroke_fill=stroke_fill)


def block(img, x, y, cells, digits, color, cell=96):
    """A register block like the in-game ones: rounded body, dark inset, anchor cap, digits."""
    w = cells * cell
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    rr(gd, (x - 6, y - 6, x + w + 6, y + cell + 6), 30, fill=color + (150,))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(22 * S)))
    d = draw(img)
    rr(d, (x, y, x + w, y + cell), 26, fill=color)
    inner = tuple(int(c * 0.4 + b * 0.6) for c, b in zip(color, BG))
    rr(d, (x + 10, y + 10, x + w - 10, y + cell - 10), 18, fill=inner)
    rr(d, (x + 14, y + 22, x + 22, y + cell - 22), 3, fill=(255, 255, 255))      # anchor cap
    f = font("consolab.ttf", 58)
    for i, ch in enumerate(digits):
        lit = ch == "1"
        text(d, (x + 52 + i * 42, y + cell / 2), ch, f, (255, 255, 255) if lit else (255, 255, 255, 80), "mm")


def main():
    img = Image.new("RGBA", (W, H), BG + (255,))
    d = draw(img)

    # checkerboard floor, fading out toward the top
    cell = 80
    for gy in range(0, 720 // cell + 1):
        for gx in range(0, 1280 // cell + 1):
            c = FLOOR_A if (gx + gy) % 2 == 0 else FLOOR_B
            a = min(1.0, max(0.0, (gy * cell - 180) / 360))
            c = tuple(int(BG[i] + (c[i] - BG[i]) * a) for i in range(3))
            d.rectangle([gx * cell * S, gy * cell * S, (gx + 1) * cell * S - 1, (gy + 1) * cell * S - 1], fill=c)

    # faint binary rain
    rnd = random.Random(3)
    fb = font("consolab.ttf", 22)
    rain = Image.new("RGBA", img.size, (0, 0, 0, 0))
    rd = ImageDraw.Draw(rain)
    for _ in range(220):
        x, y = rnd.randrange(0, 1280), rnd.randrange(0, 720)
        rd.text((x * S, y * S), rnd.choice("01"), font=fb, fill=ACCENT + (rnd.randrange(14, 46),))
    img.alpha_composite(rain)
    d = draw(img)

    # logo with a soft green glow on "BIT"
    logo = font("ariblk.ttf", 168)
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    text(ImageDraw.Draw(glow), (640, 150), "BITSHIFT", logo, ACCENT + (120,), "mm")
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(26 * S)))
    d = draw(img)
    bit_w = d.textlength("BIT", font=logo) / S
    full_w = d.textlength("BITSHIFT", font=logo) / S
    left = 640 - full_w / 2
    text(d, (left, 150), "BIT", logo, ACCENT, "lm", 3, (0, 0, 0))
    text(d, (left + bit_w, 150), "SHIFT", logo, (245, 255, 250), "lm", 3, (0, 0, 0))

    # the hook: a 5-wide block, shifted left, comes out 2 wide
    by = 400
    block(img, 40, by, 5, "101", CYAN, cell=88)
    d = draw(img)
    text(d, (640, by + 48), "<< 1", font("ariblk.ttf", 76), ACCENT, "mm", 4, (0, 0, 0))
    block(img, 830, by, 2, "010", CYAN)

    # the lost bit, falling off the top
    fbit = font("consolab.ttf", 64)
    text(d, (1050, by - 70), "1", fbit, WARN, "mm")
    for i, (dx, dy, a) in enumerate([(1080, by - 120, 160), (1110, by - 160, 90)]):
        text(d, (dx, dy), "1", font("consolab.ttf", 46 - i * 10), WARN + (a,), "mm")

    # OVERFLOW! sticker, slightly rotated
    stk = Image.new("RGBA", (560 * S, 140 * S), (0, 0, 0, 0))
    sd = ImageDraw.Draw(stk)
    rr(sd, (10, 10, 550, 130), 18, fill=WARN, outline=(0, 0, 0), width=5)
    text(sd, (280, 72), "OVERFLOW!", font("ariblk.ttf", 72), (20, 16, 8), "mm")
    stk = stk.rotate(-6, resample=Image.BICUBIC, expand=True)
    img.alpha_composite(stk, (int(700 * S), int(538 * S)))

    # caption for the left block
    d = draw(img)
    text(d, (260, by - 34), "5 = 0b101", font("consolab.ttf", 34), (170, 190, 215), "mm")
    text(d, (926, by - 34), "2?!", font("ariblk.ttf", 40), ERR, "mm", 2, (0, 0, 0))

    # jam badge
    rr(d, (40, 640, 420, 696), 12, fill=(10, 16, 24), outline=ACCENT, width=3)
    text(d, (230, 668), "GITHUB GAME OFF 2026", font("bahnschrift.ttf", 28), ACCENT, "mm")

    # vignette
    vig = Image.new("L", (W, H), 0)
    ImageDraw.Draw(vig).ellipse([-W * 0.15, -H * 0.25, W * 1.15, H * 1.25], fill=255)
    vig = vig.filter(ImageFilter.GaussianBlur(120 * S))
    dark = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    img = Image.composite(img, dark, vig.point(lambda v: int(80 + v * 175 / 255)))

    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.convert("RGB").resize((1280, 720), Image.LANCZOS).save(OUT, optimize=True)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
