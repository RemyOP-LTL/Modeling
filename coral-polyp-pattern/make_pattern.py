"""Sewing pattern for a fabric coral polyp: six fingers around a round cuff that
slides over the end of a 5 in tube.

Each finger is two mirror-image halves, so the seam runs from the centre of the top,
out over the fingertip and down the cuff. One piece, cut 12 (6 pairs).

Run:  python3 make_pattern.py          (add --no-sim to skip the 3D simulation)
Makes: pattern.pdf (print at 100 %), pattern-preview.png, polyp.png

All sizes are in inches. Change the numbers in SETTINGS and run again.
"""
import math
import sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages
from scipy.interpolate import PchipInterpolator
from shapely.geometry import Polygon

# ---------------- SETTINGS ----------------
TUBE_DIAMETER = 5.0     # the tube the polyp slides over
EASE = 0.5              # extra room around the tube so it slides on (woven fabric)
FINGERS = 6             # number of fingers
CUFF_HEIGHT = 3.0       # finished height of the round "wrist" part
FINGER_LENGTH = 4.0     # length of a finger on the flat piece, crotch to tip
NECK_WIDTH = 1.9        # flat piece width at the narrowest part, low on the finger
BULB_WIDTH = 2.9        # flat piece width at the bulb near the end
SPLAY = 50              # degrees the fingers lean out low down; they curve back to vertical at the tip
SEAM = 3 / 8            # seam allowance on every sewn edge
HEM = 1.0               # extra at the wrist edge, folded up for a hem
# ------------------------------------------

THUMBS = FINGERS
THUMB_LENGTH = FINGER_LENGTH
CIRC = math.pi * TUBE_DIAMETER + EASE        # finished cuff circumference
W0 = CIRC / FINGERS                          # finished width of one piece at the crotch
TRI_DEPTH = (W0 / 2) / math.tan(math.pi / FINGERS)   # crotch line to centre of the top
TIP_START = 0.78                             # fraction of the length where the tip starts rounding off

_profile = PchipInterpolator(
    [0.0, 0.12, 0.30, 0.45, 0.70, 0.86, 1.0],
    [W0 / 2, (W0 / 2 + NECK_WIDTH / 2) / 2 + 0.05, NECK_WIDTH / 2, NECK_WIDTH / 2 * 1.04,
     BULB_WIDTH / 2, BULB_WIDTH / 2 * 0.96, BULB_WIDTH / 2 * 0.85])


def finger_half_width(s):
    """Half the flat width of the finger at distance s above the crotch."""
    s = np.asarray(s, float)
    u = np.clip(s / FINGER_LENGTH, 0, 1)
    h = _profile(u)
    t = np.clip((u - TIP_START) / (1 - TIP_START), 0, 1)
    return h * np.sqrt(np.clip(1 - t ** 2, 0, 1))


def thumb_outline(n=240):
    """Finished (sewing) line of the finger, left base corner, up, over the tip,
    down to the right base corner. y = 0 at the crotch level."""
    u = np.linspace(0, 1, n)
    s = FINGER_LENGTH * (1 - (1 - u) ** 1.6)          # closer points near the tip
    h = finger_half_width(s)
    left = np.column_stack([-h, s])
    right = np.column_stack([h[::-1], s[::-1]])
    return np.vstack([left, right[1:]])


def _bend_angle(s):
    """3D lean of the finger centre line away from vertical (radians), at distance s.
    Zero at the base, SPLAY halfway up, back to zero at the tip."""
    u = np.clip(np.asarray(s, float) / FINGER_LENGTH, 0, 1)
    return math.radians(SPLAY) * np.sin(np.pi * u)


def bend(pts):
    """Curve the finger part of the piece (y > 0). Where the inside edge (right, the side
    facing the centre of the polyp) is longer than the outside edge, the sewn finger
    turns outward; where it is shorter, the finger turns back up.
    A half-tube bent by angle a in 3D needs the flat piece bent by 2a/pi."""
    if SPLAY == 0:
        return pts
    grid = np.linspace(0, FINGER_LENGTH, 2001)
    phi = 2 / math.pi * _bend_angle(grid)
    ds = np.diff(grid)
    tx, ty = -np.sin(phi), np.cos(phi)
    cx = np.concatenate([[0], np.cumsum((tx[1:] + tx[:-1]) / 2 * ds)])
    cy = np.concatenate([[0], np.cumsum((ty[1:] + ty[:-1]) / 2 * ds)])
    x, y = pts[:, 0], pts[:, 1]
    yy = np.clip(y, 0, FINGER_LENGTH)
    p = np.interp(yy, grid, phi)
    bx = np.interp(yy, grid, cx) + x * np.cos(p)
    by = np.interp(yy, grid, cy) + x * np.sin(p)
    return np.where((y > 0)[:, None], np.column_stack([bx, by]), pts)


def close(*parts):
    p = np.vstack(parts)
    return np.vstack([p, p[:1]])


# The piece function returns the sewing line. `extra` lengthens the cuff for the hem.
def piece_b(extra=0.0):
    """Version B half thumb. Left of the middle: a strip of cuff. Right of the
    middle: half a triangle reaching the centre. The V between them starts at the crotch."""
    return close(bend(thumb_outline()),
                 [(W0 / 2, -TRI_DEPTH), (0, 0), (0, -CUFF_HEIGHT - extra), (-W0 / 2, -CUFF_HEIGHT - extra)])


def v_slash(cut):
    """Version B: the seam allowances fill the narrow V, so the cut line stops short of
    the crotch. Returns the slash line from the bottom of the cut V up to the crotch dot."""
    inside = cut[(cut[:, 0] > 0.05) & (cut[:, 0] < W0 / 2 - 0.05) &
                 (cut[:, 1] < -0.2) & (cut[:, 1] > -CUFF_HEIGHT)]
    tip = inside[np.argmax(inside[:, 1])]
    return np.array([tip, (0, 0)])


def cut_line(piece_fn, has_cuff):
    base = piece_fn(HEM - SEAM if has_cuff else 0.0)
    cut = Polygon(base).buffer(SEAM, join_style=2, mitre_limit=3.0)
    return np.array(cut.exterior.coords)


def text_block(ax, x, y, lines, size=7.5):
    ax.text(x, y, "\n".join(lines), ha="center", va="top", fontsize=size)


def draw_piece(ax, sew, cut, notches, grain_x=0.0, grain_y=None, slash=None):
    ax.plot(cut[:, 0], cut[:, 1], "k-", lw=1.4)
    ax.plot(sew[:, 0], sew[:, 1], "k--", lw=0.7)
    y0, y1 = grain_y
    ax.annotate("", xy=(grain_x, y1), xytext=(grain_x, y0), arrowprops=dict(arrowstyle="<->", lw=0.8))
    ax.text(grain_x + 0.08, (y0 + y1) / 2, "grain", rotation=90, fontsize=7, va="center")
    if slash is not None:
        ax.plot(slash[:, 0], slash[:, 1], "k-", lw=1.4)
        ax.plot([0], [0], "ko", ms=4)
        ax.text(0.12, -0.55, "slash\nto dot", fontsize=6.5, va="center")
    for (x, y, mk) in notches:   # small triangles on the cut line = match points
        ax.plot([x], [y], marker=mk, ms=6, color="k")


def page(pdf, sew, cut, notches, title, lines, footer, grain_x=0.0, grain_y=None, text_xy=(0, -0.25),
         slash=None):
    fig = plt.figure(figsize=(8.5, 11))
    ax = fig.add_axes([0, 0, 1, 1])
    top = cut[:, 1].max() + 0.9
    ax.set_xlim(-4.25, 4.25)        # axes cover the whole page, so 1 unit = 1 inch
    ax.set_ylim(top - 11, top)
    ax.set_aspect("equal")
    ax.axis("off")
    draw_piece(ax, sew, cut, notches, grain_x, grain_y, slash)
    ax.text(0, top - 0.5, title, ha="center", fontsize=12, weight="bold")
    text_block(ax, text_xy[0], text_xy[1], lines)
    ax.add_patch(plt.Rectangle((-3.75, top - 1.6), 1, 1, fill=False, lw=1))
    ax.text(-3.25, top - 1.1, "1 in\ntest\nsquare", ha="center", va="center", fontsize=6.5)
    ax.text(3.75, top - 0.6, "Print at 100 % / Actual size.\nCheck the square measures 1 in.",
            ha="right", va="top", fontsize=7)
    ax.text(0, top - 10.6, footer, ha="center", fontsize=7)
    pdf.savefig(fig)
    plt.close(fig)


def main():
    sa = '3/8"' if abs(SEAM - 0.375) < 1e-6 else f'{SEAM}"'
    foot = (f'Solid line = cut.  Dashed line = sew.  Seam allowance {sa} included.  '
            f'Cuff circumference {CIRC:.2f}" for a {TUBE_DIAMETER:g}" tube ({EASE:g}" ease).')
    side_notches = [(-W0 / 2 - SEAM, 0, "<"), (W0 / 2 + SEAM, 0, ">")]
    b, b_cut = piece_b(), cut_line(piece_b, True)
    slash = v_slash(b_cut)
    lines = [f"Cut {2 * FINGERS}", f"({FINGERS} pairs: fold fabric", "in two and cut 6)", "",
             "Left edge + tip + right edge", "+ triangle's straight edge", "= seam to its twin",
             "", "Both sides of the V", "= seams to the", "neighbouring finger"]
    with PdfPages("pattern.pdf") as pdf:
        page(pdf, b, b_cut, side_notches, "FINGER HALF", lines, foot, grain_x=-0.7,
             grain_y=(-2.6, 0.7), text_xy=tuple(bend(np.array([[0.0, 0.62 * FINGER_LENGTH]]))[0]),
             slash=slash)

    fig, ax = plt.subplots(figsize=(5, 7))
    ax.plot(b_cut[:, 0], b_cut[:, 1], "k-", lw=1.4)
    ax.plot(b[:, 0], b[:, 1], "k--", lw=0.7)
    ax.plot(slash[:, 0], slash[:, 1], "k-", lw=1.4)
    ax.plot([0], [0], "ko", ms=4)
    ax.set_aspect("equal")
    ax.set_title(f"Finger half - cut {2 * FINGERS}\nSolid = cut line, dashed = sewing line", fontsize=10)
    ax.grid(alpha=0.3)
    ax.set_xticks(range(-4, 4)); ax.set_yticks(range(-5, 7))
    ax.set_xlabel("inches")
    fig.savefig("pattern-preview.png", dpi=110, bbox_inches="tight")
    plt.close(fig)

    if "--no-sim" not in sys.argv:
        print("simulating the sewn polyp (takes a few minutes)...")
        from sim_polyp import make_preview
        make_preview("polyp.png", f"Simulated from the flat pattern (fingers lean out up to {SPLAY:g}\u00b0)")

    print(f"cuff circumference {CIRC:.3f}  piece width at crotch {W0:.3f}")
    print(f"cut size {np.ptp(b_cut[:, 0]):.2f} x {np.ptp(b_cut[:, 1]):.2f}")


if __name__ == "__main__":
    main()
