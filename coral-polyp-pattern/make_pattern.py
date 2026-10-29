"""Sewing pattern for a fabric coral polyp: six fingers around a round cuff that
slides over the end of a 5 in tube.

Each finger is two mirror-image halves, so the seam runs from the centre of the top,
out over the fingertip and down the cuff. One piece, cut 12 (6 pairs).

Run:  python3 make_pattern.py          (add --no-sim to skip the 3D simulation)
Makes: pattern.pdf (print at 100 %), pattern-preview.png, polyp.png

All sizes are in inches. Change the numbers in SETTINGS and run again.
"""
import math
import os
import sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages
from shapely.geometry import Polygon

# ---------------- SETTINGS ----------------
TUBE_DIAMETER = 5.0     # the tube the polyp slides over
EASE = 0.5              # extra room around the tube so it slides on (woven fabric)
FINGERS = 5             # number of fingers
CUFF_HEIGHT = 3.0       # finished height of the round "wrist" part
FINGER_LENGTH = 8.5     # length of a finger on the flat piece, crotch to tip
BODY_WIDTH = 2.6        # flat width of the finger's middle (it starts wider at the crotch)
TIP_WIDTH = 3.59        # flat width of the end of the finger
TIP_LENGTH = 2.06       # length of the rounded end
TIP_POINT = 0.5         # end shape: 0.5 = round, 1.0 = pointed, in between = rounded point
TAPER = 1               # 1 = ignore the body/tip widths above: the finger starts at full crotch
                        #     width and tapers all the way to a sharp point (the default design).
                        # 0 = use BODY_WIDTH / TIP_WIDTH / TIP_LENGTH / TIP_POINT (club-shaped end)
TAPER_FULLNESS = 0.6    # with TAPER: 0 = straight cone sides, higher = fuller before the point
SPLAY = 45              # degrees the fingers lean out just above the crotch
TURN_LENGTH = 1.5       # distance above the crotch over which they turn outward;
                        # above that they curve gently back to vertical at the tip
SEAM = 3 / 8            # seam allowance on every sewn edge
HEM = 1.0               # extra at the wrist edge, folded up for a hem
# ------------------------------------------

# Any setting can be overridden from the command line, e.g.
#   POLYP="FINGER_LENGTH=8.5,TIP_POINT=1" python3 make_pattern.py --out pointy
for _kv in filter(None, os.environ.get("POLYP", "").split(",")):
    _k, _v = _kv.split("=")
    globals()[_k.strip()] = float(_v)
FINGERS = int(FINGERS)

THUMBS = FINGERS
THUMB_LENGTH = FINGER_LENGTH
CIRC = math.pi * TUBE_DIAMETER + EASE        # finished cuff circumference
W0 = CIRC / FINGERS                          # finished width of one piece at the crotch
TRI_DEPTH = (W0 / 2) / math.tan(math.pi / FINGERS)   # crotch line to centre of the top
BODY_H = FINGER_LENGTH - TIP_LENGTH          # where the rounded end starts
NARROW = 1.5                                 # crotch width eases to BODY_WIDTH over this length
WIDEN = 1.5                                  # body eases out to TIP_WIDTH over this length


def _ease(t):
    """0 to 1 smoothly as t goes 0 to 1."""
    t = np.clip(t, 0, 1)
    return (1 - np.cos(np.pi * t)) / 2


def finger_half_width(s):
    """Half the flat width of the finger at distance s above the crotch."""
    s = np.asarray(s, float)
    if TAPER:
        u = np.clip(s / FINGER_LENGTH, 0, 1)
        return W0 / 2 * (1 - u) * (1 + TAPER_FULLNESS * u)
    body = (W0 + (BODY_WIDTH - W0) * _ease(s / NARROW)
            + (TIP_WIDTH - BODY_WIDTH) * _ease((s - (BODY_H - WIDEN)) / WIDEN)) / 2
    t = np.clip((s - BODY_H) / TIP_LENGTH, 0, 1)
    tip = TIP_WIDTH / 2 * np.clip(1 - t ** 2, 0, 1) ** TIP_POINT
    return np.where(s < BODY_H, body, tip)


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
    Turns out from 0 to SPLAY over TURN_LENGTH, then eases back to 0 (vertical) at the tip."""
    s = np.asarray(s, float)
    th = math.radians(SPLAY)
    up = th * np.sin(np.pi / 2 * np.clip(s / TURN_LENGTH, 0, 1)) ** 2
    back = th * np.cos(np.pi / 2 * np.clip((s - TURN_LENGTH) / (FINGER_LENGTH - TURN_LENGTH), 0, 1)) ** 2
    return np.where(s < TURN_LENGTH, up, back)


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


def _rot(p):
    """Turn the piece a quarter turn so the finger points right (fits a landscape page)."""
    p = np.atleast_2d(np.asarray(p, float))
    return np.column_stack([p[:, 1], -p[:, 0]])


def page(pdf, sew, cut, notches, title, lines, footer, grain_x, grain_y, text_xy, slash):
    """Landscape pages, true size. The piece is drawn turned so the finger points right.
    If it is too long for one page it is split over several, with tape lines to join them."""
    rc, rs = _rot(cut), _rot(sew)
    x0, x1 = rc[:, 0].min() - 0.2, rc[:, 0].max() + 0.2
    cy = (rc[:, 1].min() + rc[:, 1].max()) / 2 - 0.3
    usable, overlap = 10.0, 1.0                # printable width per page, and shared strip
    n = max(1, math.ceil((x1 - x0 - overlap) / (usable - overlap)))
    if n == 1:
        lefts = [(x0 + x1) / 2 - usable / 2]
    else:
        lefts = [x0 + i * (usable - overlap) for i in range(n)]
    joins = [lefts[i + 1] + overlap / 2 for i in range(n - 1)]
    for i, L in enumerate(lefts):
        fig = plt.figure(figsize=(11, 8.5))
        ax = fig.add_axes([0, 0, 1, 1])
        left, right = L - 0.5, L + usable + 0.5
        ax.set_xlim(left, right)               # axes cover the whole page, so 1 unit = 1 inch
        ax.set_ylim(cy - 4.25, cy + 4.25)
        ax.set_aspect("equal")
        ax.axis("off")
        ax.plot(rc[:, 0], rc[:, 1], "k-", lw=1.4)
        ax.plot(rs[:, 0], rs[:, 1], "k--", lw=0.7)
        g = _rot([(grain_x, grain_y[0]), (grain_x, grain_y[1])])
        ax.annotate("", xy=g[1], xytext=g[0], arrowprops=dict(arrowstyle="<->", lw=0.8))
        ax.text(g[:, 0].mean(), g[0, 1] + 0.08, "grain", fontsize=7, ha="center")
        sl = _rot(slash)
        ax.plot(sl[:, 0], sl[:, 1], "k-", lw=1.4)
        ax.plot([0], [0], "ko", ms=4)
        t = _rot([(0.12, -0.55)])[0]
        ax.text(t[0], t[1], "slash to dot", fontsize=6.5, ha="center", va="top")
        marks = {"<": "^", ">": "v"}
        for (x, y, mk) in notches:             # small triangles on the cut line = match points
            p = _rot([(x, y)])[0]
            ax.plot([p[0]], [p[1]], marker=marks[mk], ms=6, color="k")
        t = _rot([text_xy])[0]
        ax.text(t[0], t[1], "\n".join(lines), ha="center", va="center", fontsize=7.5)
        top = cy + 4.25
        mid = (left + right) / 2
        label = title if n == 1 else f"{title}   (page {i + 1} of {n})"
        ax.text(mid, top - 0.55, label, ha="center", fontsize=12, weight="bold")
        piece = Polygon(rc).buffer(0)
        for xj in joins:                       # keep clear of tape lines and their notes
            piece = piece.union(Polygon([(xj - 2.2, cy - 5), (xj + 2.2, cy - 5), (xj + 2.2, cy + 5), (xj - 2.2, cy + 5)]))
        bottom = cy - 4.25
        for qx, qy in [(left + 0.5, top - 1.5), (left + 0.5, bottom + 0.8),
                       (right - 1.5, bottom + 0.8), (right - 1.5, top - 1.9)]:
            if not piece.buffer(0.2).intersects(Polygon([(qx, qy), (qx + 1, qy), (qx + 1, qy + 1), (qx, qy + 1)])):
                break
        ax.add_patch(plt.Rectangle((qx, qy), 1, 1, fill=False, lw=1))
        ax.text(qx + 0.5, qy + 0.5, "1 in\ntest\nsquare", ha="center", va="center", fontsize=6.5)
        ax.text(right - 0.5, top - 0.5, "Print at 100 % / Actual size, landscape.\nCheck the square measures 1 in.",
                ha="right", va="top", fontsize=7)
        ax.text(mid, cy - 4.25 + 0.4, footer, ha="center", fontsize=7)
        # tape lines: page i's right join matches page i+1's left join
        for j, xj in enumerate(joins):
            if j not in (i - 1, i):
                continue
            ax.plot([xj, xj], [cy - 3.6, cy + 3.2], color="0.4", lw=0.8, ls=(0, (6, 3)))
            for yy in (cy - 3.0, cy, cy + 2.6):
                ax.plot([xj], [yy], marker="D", ms=5, color="0.4")
            if j == i - 1:
                txt = f"TRIM page {i + 1} on this line,\nlay it over page {i}\nso lines and diamonds match"
                ax.text(xj + 0.1, cy - 3.6, txt, fontsize=6.5, va="bottom", ha="left", color="0.3")
            else:
                txt = f"Page {i + 2} lines up here"
                ax.text(xj - 0.1, cy - 3.6, txt, fontsize=6.5, va="bottom", ha="right", color="0.3")
        pdf.savefig(fig)
        plt.close(fig)


def main():
    out = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else "."
    name = sys.argv[sys.argv.index("--name") + 1] if "--name" in sys.argv else f"polyp-{FINGERS}-taper" if TAPER else f"polyp-{FINGERS}-club"
    os.makedirs(out, exist_ok=True)
    sa = '3/8"' if abs(SEAM - 0.375) < 1e-6 else f'{SEAM}"'
    foot = (f'Solid line = cut.  Dashed line = sew.  Seam allowance {sa} included.  '
            f'Cuff circumference {CIRC:.2f}" for a {TUBE_DIAMETER:g}" tube ({EASE:g}" ease).')
    side_notches = [(-W0 / 2 - SEAM, 0, "<"), (W0 / 2 + SEAM, 0, ">")]
    b, b_cut = piece_b(), cut_line(piece_b, True)
    slash = v_slash(b_cut)
    lines = [f"Cut {2 * FINGERS}", f"({FINGERS} pairs: fold fabric", f"in two and cut {FINGERS})", "",
             "Outer edge + tip + inner edge", "+ triangle's straight edge", "= seam to its twin",
             "", "Both sides of the V", "= seams to the", "neighbouring finger"]
    with PdfPages(os.path.join(out, f"{name}-pattern.pdf")) as pdf:
        page(pdf, b, b_cut, side_notches, f"FINGER HALF  -  {name}", lines, foot, grain_x=-0.7,
             grain_y=(-2.6, -0.3), text_xy=tuple(bend(np.array([[0.0, 0.4 * FINGER_LENGTH]]))[0]),
             slash=slash)

    fig, ax = plt.subplots(figsize=(5, 7))
    ax.plot(b_cut[:, 0], b_cut[:, 1], "k-", lw=1.4)
    ax.plot(b[:, 0], b[:, 1], "k--", lw=0.7)
    ax.plot(slash[:, 0], slash[:, 1], "k-", lw=1.4)
    ax.plot([0], [0], "ko", ms=4)
    ax.set_aspect("equal")
    ax.set_title(f"{name}: finger half - cut {2 * FINGERS}\nSolid = cut line, dashed = sewing line", fontsize=10)
    ax.grid(alpha=0.3)
    ax.set_xticks(range(-4, 4)); ax.set_yticks(range(-5, 11))
    ax.set_xlabel("inches")
    fig.savefig(os.path.join(out, f"{name}-piece.png"), dpi=110, bbox_inches="tight")
    plt.close(fig)

    if "--no-sim" not in sys.argv:
        print("simulating the sewn polyp (takes a few minutes)...")
        from sim_polyp import make_preview
        make_preview(os.path.join(out, f"{name}-3d.png"), f"Simulated from the flat pattern, inflated (fingers turn out {SPLAY:g}\u00b0, then back up)")

    print(f"cuff circumference {CIRC:.3f}  piece width at crotch {W0:.3f}")
    print(f"cut size {np.ptp(b_cut[:, 0]):.2f} x {np.ptp(b_cut[:, 1]):.2f}")


if __name__ == "__main__":
    main()
