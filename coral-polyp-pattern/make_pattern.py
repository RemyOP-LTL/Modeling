"""Sewing pattern for a fabric coral polyp: six "glove thumbs" around a round cuff
that slides over the end of a 5 in tube.

Run:  python3 make_pattern.py
Makes: pattern.pdf (print at 100 %, "actual size"), pattern-preview.png,
       polyp-preview.png

All sizes are in inches. Change the numbers in SETTINGS and run again.
"""
import math
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages

# ---------------- SETTINGS ----------------
TUBE_DIAMETER = 5.0     # the tube the polyp slides over
EASE = 0.5              # extra room around the tube so it slides on (woven fabric)
THUMBS = 6              # number of thumbs
CUFF_HEIGHT = 3.0       # finished height of the round "wrist" part
THUMB_LENGTH = 4.0      # finished height of each thumb, from crotch to tip
THUMB_BULGE = 0.35      # how much wider the thumb gets above its base (total)
SEAM = 3 / 8            # seam allowance on every sewn edge
HEM = 1.0               # extra at the wrist edge, folded up for a hem
# ------------------------------------------

CIRC = math.pi * TUBE_DIAMETER + EASE        # finished cuff circumference
W0 = CIRC / THUMBS                           # finished width of one panel at the crotch
WM = W0 + THUMB_BULGE                        # widest part of the thumb
TIP_H = WM / 2 * 1.15                        # height of the rounded tip
BODY_H = THUMB_LENGTH - TIP_H                # straight-ish part below the tip


def thumb_outline(n=120):
    """Finished (sewing) line of the thumb, crotch to crotch over the tip.
    y = 0 at the crotch. Returns points from left crotch, up, over, down."""
    ys = np.linspace(0, BODY_H, n)
    half = W0 / 2 + (WM - W0) / 2 * np.sin(np.pi / 2 * ys / BODY_H)
    left = np.column_stack([-half, ys])
    t = np.linspace(math.pi, 0, n)
    tip = np.column_stack([WM / 2 * np.cos(t), BODY_H + TIP_H * np.sin(t)])
    right = np.column_stack([half[::-1], ys[::-1]])
    return np.vstack([left, tip[1:-1], right])


def outer_piece():
    """Thumb on top of a cuff rectangle. Closed sewing line."""
    th = thumb_outline()
    bottom = [(W0 / 2, -CUFF_HEIGHT), (-W0 / 2, -CUFF_HEIGHT)]
    return np.vstack([th, bottom, th[:1]])


def inner_piece():
    """Thumb on top of a triangle whose point meets the other five at the centre."""
    th = thumb_outline()
    apex_angle = 2 * math.pi / THUMBS
    depth = (W0 / 2) / math.tan(apex_angle / 2)
    return np.vstack([th, [(0, -depth)], th[:1]])


def offset(poly, d):
    """Grow a closed polygon outward by d (simple miter offset, good enough here)."""
    p = poly[:-1]
    n = len(p)
    area = 0.5 * np.sum(p[:, 0] * np.roll(p[:, 1], -1) - np.roll(p[:, 0], -1) * p[:, 1])
    sign = -1 if area > 0 else 1
    out = []
    for i in range(n):
        a, b, c = p[i - 1], p[i], p[(i + 1) % n]
        n1 = np.array([(b - a)[1], -(b - a)[0]]); n1 /= np.linalg.norm(n1) + 1e-12
        n2 = np.array([(c - b)[1], -(c - b)[0]]); n2 /= np.linalg.norm(n2) + 1e-12
        m = n1 + n2
        if np.linalg.norm(m) < 1e-9:
            m = n1
        m /= np.linalg.norm(m)
        cos = max(np.dot(m, n1), 0.35)
        out.append(b - sign * m * d / cos)
    out.append(out[0])
    return np.array(out)


def cut_line(piece, hem_bottom=False):
    cut = offset(piece, SEAM)
    if hem_bottom:  # drop the bottom edge further for the hem
        low = piece[:, 1].min()
        cut[cut[:, 1] < low - SEAM / 2, 1] -= (HEM - SEAM)
    return cut


def draw_piece(ax, piece, cut, title, lines, notches):
    ax.plot(cut[:, 0], cut[:, 1], "k-", lw=1.4)
    ax.plot(piece[:, 0], piece[:, 1], "k--", lw=0.7)
    # grain line
    ax.annotate("", xy=(0, BODY_H * 0.9), xytext=(0, BODY_H * 0.1 - 1.0),
                arrowprops=dict(arrowstyle="<->", lw=0.8))
    ax.text(0.08, BODY_H * 0.45 - 0.4, "grain", rotation=90, fontsize=7, va="center")
    for (x, y) in notches:  # small triangles on the cut line = match points
        ax.plot([x], [y], marker="v" if x == 0 else ("<" if x < 0 else ">"),
                ms=6, color="k")
    ax.text(0, BODY_H + TIP_H + SEAM + 0.25, title, ha="center", fontsize=12, weight="bold")
    ax.text(0, -0.25, "\n".join(lines), ha="center", va="top", fontsize=7.5)


def test_square(ax, x, y):
    ax.add_patch(plt.Rectangle((x, y), 1, 1, fill=False, lw=1))
    ax.text(x + 0.5, y + 0.5, '1 in\ntest\nsquare', ha="center", va="center", fontsize=6.5)


def page(pdf, piece, cut, title, lines, notches, footer):
    fig = plt.figure(figsize=(8.5, 11))
    ax = fig.add_axes([0, 0, 1, 1])
    # page coordinates in inches, centred on the piece
    cx = 0
    top = cut[:, 1].max() + 0.9
    ax.set_xlim(cx - 4.25, cx + 4.25)
    ax.set_ylim(top - 11, top)
    ax.set_aspect("equal")
    ax.axis("off")
    draw_piece(ax, piece, cut, title, lines, notches)
    test_square(ax, cx - 3.75, top - 1.6)
    ax.text(cx + 3.75, top - 0.6, "Print at 100 % / Actual size.\nCheck the square measures 1 in.",
            ha="right", va="top", fontsize=7)
    ax.text(cx, top - 10.6, footer, ha="center", fontsize=7)
    pdf.savefig(fig)
    return fig


def build_3d_preview(path):
    from mpl_toolkits.mplot3d import Axes3D  # noqa: F401
    R = CIRC / (2 * math.pi)
    fig = plt.figure(figsize=(7, 7))
    ax = fig.add_subplot(111, projection="3d")
    col_cuff, col_thumb = "#e8735a", "#f29a7e"
    # cuff
    th = np.linspace(0, 2 * math.pi, 60)
    z = np.linspace(-CUFF_HEIGHT, 0, 6)
    T, Z = np.meshgrid(th, z)
    ax.plot_surface(R * np.cos(T), R * np.sin(T), Z, color=col_cuff, alpha=0.95, lw=0)
    # tube (grey) sticking out below
    rt = TUBE_DIAMETER / 2
    z = np.linspace(-CUFF_HEIGHT - 4, -CUFF_HEIGHT + 0.5, 4)
    T, Z = np.meshgrid(th, z)
    ax.plot_surface(rt * np.cos(T), rt * np.sin(T), Z, color="#9a9a9a", alpha=0.6, lw=0)
    # thumbs: tube whose circumference follows the pattern (2 panels wide)
    s = np.linspace(0, THUMB_LENGTH, 40)
    half = np.where(s < BODY_H,
                    W0 / 2 + (WM - W0) / 2 * np.sin(np.pi / 2 * np.minimum(s, BODY_H) / BODY_H),
                    WM / 2 * np.sqrt(np.clip(1 - ((s - BODY_H) / TIP_H) ** 2, 0, 1)))
    r = 2 * half * 2 / (2 * math.pi)
    phi = np.linspace(0, 2 * math.pi, 30)
    S, P = np.meshgrid(s, phi)
    Rr = np.tile(r, (len(phi), 1))
    base_r = R * 0.62
    tilt = math.radians(22)
    for k in range(THUMBS):
        a = 2 * math.pi * (k + 0.5) / THUMBS
        axis = np.array([math.sin(tilt) * math.cos(a), math.sin(tilt) * math.sin(a), math.cos(tilt)])
        u = np.array([-math.sin(a), math.cos(a), 0])
        v = np.cross(axis, u)
        base = np.array([base_r * math.cos(a), base_r * math.sin(a), 0.15])
        X = base[0] + axis[0] * S + Rr * (np.cos(P) * u[0] + np.sin(P) * v[0])
        Y = base[1] + axis[1] * S + Rr * (np.cos(P) * u[1] + np.sin(P) * v[1])
        Zs = base[2] + axis[2] * S + Rr * (np.cos(P) * u[2] + np.sin(P) * v[2])
        ax.plot_surface(X, Y, Zs, color=col_thumb, lw=0, shade=True)
    # top "cap" between thumbs
    rr = np.linspace(0, R, 8)
    RR, T = np.meshgrid(rr, th)
    ax.plot_surface(RR * np.cos(T), RR * np.sin(T), 0.25 * (1 - (RR / R) ** 2),
                    color=col_cuff, lw=0)
    lim = 5
    ax.set_xlim(-lim, lim); ax.set_ylim(-lim, lim); ax.set_zlim(-CUFF_HEIGHT - 2.5, THUMB_LENGTH + 1)
    ax.set_box_aspect((1, 1, (THUMB_LENGTH + CUFF_HEIGHT + 3.5) / (2 * lim)))
    ax.view_init(elev=22, azim=30)
    ax.set_axis_off()
    ax.set_title(f'Finished polyp (approx.) on a {TUBE_DIAMETER:g}" tube (grey)', fontsize=11)
    fig.savefig(path, dpi=130, bbox_inches="tight")


def main():
    outer, inner = outer_piece(), inner_piece()
    outer_cut, inner_cut = cut_line(outer, hem_bottom=True), cut_line(inner)
    sa = '3/8"' if abs(SEAM - 0.375) < 1e-6 else f'{SEAM}"'
    foot = (f'Solid line = cut.  Dashed line = sew.  Seam allowance {sa} included.  '
            f'Cuff circumference {CIRC:.2f}" for a {TUBE_DIAMETER:g}" tube ({EASE:g}" ease).')
    with PdfPages("pattern.pdf") as pdf:
        page(pdf, outer, outer_cut, "A  OUTER PANEL",
             [f"Cut {THUMBS}", "", "THUMB above the notches", "", "CUFF below",
              "", f'{HEM:g}" hem allowance', "at bottom edge"],
             [(-W0 / 2 - SEAM, 0), (W0 / 2 + SEAM, 0)], foot)
        page(pdf, inner, inner_cut, "B  INNER PANEL",
             [f"Cut {THUMBS}", "", "point goes to the", "centre of the top"],
             [(-W0 / 2 - SEAM, 0), (W0 / 2 + SEAM, 0)], foot)
    # side-by-side overview image
    fig, axes = plt.subplots(1, 2, figsize=(8, 7))
    for ax, p, c, t in [(axes[0], outer, outer_cut, "A  Outer panel - cut 6"),
                        (axes[1], inner, inner_cut, "B  Inner panel - cut 6")]:
        ax.plot(c[:, 0], c[:, 1], "k-", lw=1.4)
        ax.plot(p[:, 0], p[:, 1], "k--", lw=0.7)
        ax.set_aspect("equal"); ax.set_title(t)
        ax.set_xlim(-2.5, 2.5); ax.set_ylim(-CUFF_HEIGHT - HEM - 0.6, THUMB_LENGTH + 0.8)
        ax.grid(alpha=0.3); ax.set_xticks(range(-2, 3)); ax.set_yticks(range(-4, 6))
        ax.set_xlabel("inches")
    fig.suptitle("Solid = cut line, dashed = sewing line (not to scale here; print the PDF)")
    fig.savefig("pattern-preview.png", dpi=120, bbox_inches="tight")
    build_3d_preview("polyp-preview.png")
    tri = (W0 / 2) / math.tan(math.pi / THUMBS)
    print(f"cuff circumference {CIRC:.3f}  panel width at crotch {W0:.3f}  widest {WM:.3f}")
    print(f"outer cut size {np.ptp(outer_cut[:,0]):.2f} x {np.ptp(outer_cut[:,1]):.2f}")
    print(f"inner cut size {np.ptp(inner_cut[:,0]):.2f} x {np.ptp(inner_cut[:,1]):.2f}  triangle depth {tri:.3f}")


if __name__ == "__main__":
    main()
