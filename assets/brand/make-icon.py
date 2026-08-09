"""Generates RimManager's icon and in-app app mark from the badge artwork.

Run:  python assets/brand/make-icon.py

The design source is `rimmanager-logo.svg`. This script is a hand-port of the
badge half of it, in the SVG's own 0-200 badge coordinates so the two stay
comparable line by line.

WHY A PORT AND NOT A RENDERER. Every pure-Python SVG rasteriser on Windows wants
a native cairo that is not installed (cairosvg and reportlab's renderPM both fail
here), and `Avalonia.Svg.Skia` would pull Svg.Skia, Svg.Model, ShimSkiaSharp,
ExCSS and Fizzler into the shipped app to draw one static tile. For an asset
whose largest use is 64px that is several megabytes and a new supply-chain
surface for nothing — this project pins transitive CVEs by hand, so every added
dependency is also an audit item.

WHY A BITMAP AND NOT AVALONIA GEOMETRY. A second hand-port into XAML would be a
second place for the artwork to drift, and it buys only resolution we never use.
A 256px master downsamples cleanly to the 34/44/64px marks even at 200% DPI.

Committed output:
  rimmanager.ico                          — the Win32 exe icon (16..256)
  rimmanager-256.png                      — reference render
  src/RimManager.App/Assets/app-mark.png  — the in-app mark and the window icon
"""

import math
from PIL import Image, ImageDraw

SIZES = [16, 32, 48, 64, 128, 256]
SUPER = 1024                      # drawn large, downsampled for antialiasing
S = SUPER / 200.0                 # badge coords (0-200) -> pixels
OX, OY = 60, 50                   # the badge's origin inside the full artboard


def p(x, y):
    """SVG artboard coordinate -> supersampled pixel."""
    return ((x - OX) * S, (y - OY) * S)


def circle(draw, cx, cy, r, **kw):
    x, y = p(cx, cy)
    rr = r * S
    draw.ellipse([x - rr, y - rr, x + rr, y + rr], **kw)


def rgba(hexstr, alpha=1.0):
    h = hexstr.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), round(alpha * 255))


def pod(draw, tx, ty, angle=25, shell="#C4CBD2"):
    """One drop pod: exhaust trail, flame, capsule, band — rotated then placed."""
    a = math.radians(angle)
    cos, sin = math.cos(a), math.sin(a)

    def q(lx, ly):
        # SVG applies translate() then rotate(), so the local point is rotated first.
        return p(tx + lx * cos - ly * sin, ty + lx * sin + ly * cos)

    # Exhaust trail: a round-capped line, drawn as a polygon plus two caps.
    x1, y1 = q(0, -32)
    x2, y2 = q(0, -78)
    draw.line([x1, y1, x2, y2], fill=rgba("#E07A3C", 0.28), width=round(3 * S))

    draw.polygon([q(-6, -14), q(6, -14), q(0, -34)], fill=rgba("#F0A94B"))
    draw.polygon([q(-3.5, -14), q(3.5, -14), q(0, -26)], fill=rgba("#FBE6A8"))

    # Capsule: a rounded rect, so it is drawn as a rotated polygon with the
    # corners cut — at icon sizes the 7px radius is sub-pixel anyway.
    draw.polygon(
        [q(-8, -7), q(-8, 5), q(-5, 12), q(5, 12), q(8, 5), q(8, -7), q(5, -14), q(-5, -14)],
        fill=rgba(shell))
    draw.polygon([q(-8, -3), q(8, -3), q(8, 2), q(-8, 2)], fill=rgba("#78838E"))


def render():
    img = Image.new("RGBA", (SUPER, SUPER), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    d.rectangle([0, 0, SUPER, SUPER], fill=rgba("#161E29"))

    # The planet: only its top arc reaches into the tile, which is the "rim".
    circle(d, 160, 470, 272, outline=rgba("#6FA8B8", 0.45), width=max(1, round(1.5 * S)))
    circle(d, 160, 470, 260, fill=rgba("#3C4A35"),
           outline=rgba("#93B36A"), width=max(1, round(2.5 * S)))

    d.polygon([p(84, 232), p(100, 216), p(116, 232)], fill=rgba("#55663F"))
    d.polygon([p(172, 226), p(192, 208), p(212, 226)], fill=rgba("#55663F"))

    for cx, cy, r, o in [(88, 82, 2, .75), (236, 72, 1.6, .55), (108, 112, 1.4, .45),
                         (234, 182, 1.8, .6), (76, 160, 1.3, .4)]:
        circle(d, cx, cy, r, fill=rgba("#E8DFC8", o))

    pod(d, 120, 152)
    pod(d, 162, 120, shell="#DDE3E8")
    pod(d, 204, 142)

    # The rounded-square silhouette, applied as a mask so the corners are cut
    # exactly where the SVG's clipPath cuts them.
    mask = Image.new("L", (SUPER, SUPER), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, SUPER - 1, SUPER - 1],
                                           radius=round(46 * S), fill=255)
    img.putalpha(mask)

    edge = Image.new("RGBA", (SUPER, SUPER), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rounded_rectangle(
        [0, 0, SUPER - 1, SUPER - 1], radius=round(46 * S),
        outline=rgba("#2F3C4C"), width=round(2 * S))
    img.alpha_composite(edge)
    return img


if __name__ == "__main__":
    import os

    master = render()
    small = master.resize((256, 256), Image.LANCZOS)
    small.save("assets/brand/rimmanager-256.png")
    master.save("assets/brand/rimmanager.ico", sizes=[(s, s) for s in SIZES])

    app_assets = os.path.join("src", "RimManager.App", "Assets")
    os.makedirs(app_assets, exist_ok=True)
    small.save(os.path.join(app_assets, "app-mark.png"))

    print("wrote rimmanager.ico", SIZES, "and", os.path.join(app_assets, "app-mark.png"))
