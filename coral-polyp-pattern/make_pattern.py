"""Sewing pattern for a fabric coral polyp: six "glove thumbs" around a round cuff
that slides over the end of a 5 in tube. Two ways to sew it:

  Version A: each thumb has an OUTER and an INNER half (seam runs up the
             sides of the thumb). Pieces A1 and A2, cut 6 of each.
  Version B: each thumb has a LEFT and a RIGHT half (seam runs from the centre
             of the top, out over the tip of the thumb and down the cuff).
             One piece, cut 12 (6 pairs).

Run:  python3 make_pattern.py
Makes: pattern-version-a.pdf, pattern-version-b.pdf (print at 100 %),
       pattern-preview.png, polyp-version-a.png, polyp-version-b.png

All sizes are in inches. Change the numbers in SETTINGS and run again.
"""
import math
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages
from shapely.geometry import Polygon

from render_polyp import Polyp, save_preview

# ---------------- SETTINGS ----------------
TUBE_DIAMETER = 5.0     # the tube the polyp slides over
EASE = 0.5              # extra room around the tube so it slides on (woven fabric)
THUMBS = 6              # number of thumbs
CUFF_HEIGHT = 3.0       # finished height of the round "wrist" part
THUMB_LENGTH = 4.0      # finished height of each thumb, from crotch to tip
THUMB_BULGE = 0.35      # how much wider the thumb gets above its base (total)
SEAM = 3 / 8            # seam allowance on every sewn edge
HEM = 1.0               # extra at the wrist edge, folded up for a hem
SPLAY = 45              # version B: degrees each finger tip leans out from straight up
# ------------------------------------------

CIRC = math.pi * TUBE_DIAMETER + EASE        # finished cuff circumference
W0 = CIRC / THUMBS                           # finished width of one thumb piece at the crotch
WM = W0 + THUMB_BULGE                        # widest part of a thumb piece
TIP_H = WM / 2 * 1.15                        # height of the rounded tip
BODY_H = THUMB_LENGTH - TIP_H                # straight-ish part below the tip
TRI_DEPTH = (W0 / 2) / math.tan(math.pi / THUMBS)   # crotch line to centre of the top


def thumb_outline(n=120):
    """Finished (sewing) line of a thumb piece, left base corner, up, over the tip,
    down to the right base corner. y = 0 at the crotch level."""
    ys = np.linspace(0, BODY_H, n)
    half = W0 / 2 + (WM - W0) / 2 * np.sin(np.pi / 2 * ys / BODY_H)
    left = np.column_stack([-half, ys])
    t = np.linspace(math.pi, 0, n)
    tip = np.column_stack([WM / 2 * np.cos(t), BODY_H + TIP_H * np.sin(t)])
    right = np.column_stack([half[::-1], ys[::-1]])
    return np.vstack([left, tip[1:-1], right])


def bend(pts):
    """Version B: curve the finger part of the piece (y > 0) like a banana. The inside
    edge (right, x > 0, the side facing the centre of the polyp) comes out longer than
    the outside edge (left), so when sewn the finger curves outward by SPLAY degrees
    over its straight part."""
    if SPLAY <= 0:
        return pts
    kappa = math.radians(SPLAY) / BODY_H     # bend of the finger's centre line in 3D
    rho = math.pi / (2 * kappa)              # matching bend radius of the flat piece
    x, y = pts[:, 0], pts[:, 1]
    phi = np.clip(y, 0, BODY_H) / rho
    extra = np.clip(y - BODY_H, 0, None)     # the rounded tip stays straight
    r = rho + x
    bx = -rho + r * np.cos(phi) - extra * np.sin(phi)
    by = r * np.sin(phi) + extra * np.cos(phi)
    return np.where((y > 0)[:, None], np.column_stack([bx, by]), pts)


def close(*parts):
    p = np.vstack(parts)
    return np.vstack([p, p[:1]])


# Each piece function returns the sewing line. `extra` lengthens the cuff for the hem.
def piece_a1(extra=0.0):
    """Version A outer half: thumb on a strip of cuff."""
    return close(thumb_outline(), [(W0 / 2, -CUFF_HEIGHT - extra), (-W0 / 2, -CUFF_HEIGHT - extra)])


def piece_a2(extra=0.0):
    """Version A inner half: thumb on a triangle that reaches the centre of the top."""
    return close(thumb_outline(), [(0, -TRI_DEPTH)])


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
    y0, y1 = grain_y or (BODY_H * 0.1 - 1.0, BODY_H * 0.9)
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
    ax.text(0, BODY_H + TIP_H + SEAM + 0.25, title, ha="center", fontsize=12, weight="bold")
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

    a1, a1_cut = piece_a1(), cut_line(piece_a1, True)
    a2, a2_cut = piece_a2(), cut_line(piece_a2, False)
    b, b_cut = piece_b(), cut_line(piece_b, True)

    with PdfPages("pattern-version-a.pdf") as pdf:
        page(pdf, a1, a1_cut, side_notches, "A1  OUTER HALF  (version A)",
             [f"Cut {THUMBS}", "", "THUMB above the notches", "CUFF below",
              "", f'{HEM:g}" hem allowance', "at bottom edge"], foot)
        page(pdf, a2, a2_cut, side_notches, "A2  INNER HALF  (version A)",
             [f"Cut {THUMBS}", "", "point goes to the", "centre of the top"], foot)
    with PdfPages("pattern-version-b.pdf") as pdf:
        page(pdf, b, b_cut, side_notches, "B  THUMB HALF  (version B)",
             [f"Cut {2 * THUMBS}", f"({THUMBS} pairs: fold fabric", "in two and cut 6)", "",
              "Left edge + tip + right edge", "+ triangle's straight edge", "= seam to its twin",
              "", "Both sides of the V", "= seams to the", "neighbouring thumb"],
             foot, grain_x=-0.7, grain_y=(-2.6, 0.7), text_xy=tuple(bend(np.array([[0.0, 2.5]]))[0]), slash=v_slash(b_cut))

    # overview image of all pieces
    fig, axes = plt.subplots(1, 3, figsize=(11, 6.5))
    for ax, sew, cut, t in [(axes[0], a1, a1_cut, f"A1 outer half - cut {THUMBS}"),
                            (axes[1], a2, a2_cut, f"A2 inner half - cut {THUMBS}"),
                            (axes[2], b, b_cut, f"B thumb half - cut {2 * THUMBS}")]:
        ax.plot(cut[:, 0], cut[:, 1], "k-", lw=1.4)
        ax.plot(sew[:, 0], sew[:, 1], "k--", lw=0.7)
        if sew is b:
            sl = v_slash(cut)
            ax.plot(sl[:, 0], sl[:, 1], "k-", lw=1.4)
            ax.plot([0], [0], "ko", ms=4)
        ax.set_aspect("equal"); ax.set_title(t, fontsize=10)
        ax.set_xlim(-3.5, 2.5); ax.set_ylim(-CUFF_HEIGHT - HEM - 0.6, THUMB_LENGTH + 0.8)
        ax.grid(alpha=0.3); ax.set_xticks(range(-3, 3)); ax.set_yticks(range(-4, 6))
        ax.set_xlabel("inches")
    axes[0].text(-2.4, -CUFF_HEIGHT - HEM - 0.45, "Version A", fontsize=11, weight="bold")
    axes[2].text(-2.4, -CUFF_HEIGHT - HEM - 0.45, "Version B", fontsize=11, weight="bold")
    fig.suptitle("Solid = cut line, dashed = sewing line. Print the PDFs for true size.")
    fig.savefig("pattern-preview.png", dpi=110, bbox_inches="tight")
    plt.close(fig)

    polyp = Polyp(cuff_radius=CIRC / (2 * math.pi), cuff_height=CUFF_HEIGHT, thumbs=THUMBS,
                  r_base=W0 / math.pi, r_top=WM / math.pi, thumb_length=THUMB_LENGTH,
                  tube_radius=TUBE_DIAMETER / 2)
    save_preview(polyp, "A", "Version A: thumbs in outer + inner halves", "polyp-version-a.png")
    print("simulating version B (takes a few minutes)...")
    from sim_polyp import make_preview
    make_preview("polyp-version-b.png",
                 f"Version B, simulated from the flat pieces (fingers spread {SPLAY:g}\u00b0)")

    print(f"cuff circumference {CIRC:.3f}  piece width at crotch {W0:.3f}  widest {WM:.3f}")
    for name, c in [("A1", a1_cut), ("A2", a2_cut), ("B", b_cut)]:
        print(f"{name} cut size {np.ptp(c[:, 0]):.2f} x {np.ptp(c[:, 1]):.2f}")


if __name__ == "__main__":
    main()
