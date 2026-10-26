"""Cloth simulation of version B: what the flat pieces actually make when sewn.

1. Each of the 12 flat pieces (6 as drawn, 6 mirrored) is filled with small triangles.
2. Matching seam edges are stitched by joining their points.
3. The fabric is placed roughly around the tube, then relaxed: every little triangle
   edge keeps its flat-pattern length (fabric doesn't stretch), a gentle pressure
   puffs it out as if lightly stuffed, and the tube pushes back from inside.
   There is no stiffness across seams, so creases and ridges form there on their own.

The shape that comes out is driven only by the pattern, not drawn by hand.
"""
import math
import numpy as np
import matplotlib.tri as mtri
from shapely.geometry import Polygon
from shapely import contains_xy

import make_pattern as P

H_STEP = 0.13      # mesh spacing in inches


def _resample(path, step):
    seg = np.linalg.norm(np.diff(path, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    n = max(2, int(math.ceil(s[-1] / step)) + 1)
    t = np.linspace(0, s[-1], n)
    return np.column_stack([np.interp(t, s, path[:, 0]), np.interp(t, s, path[:, 1])])


def flat_piece():
    """Mesh of one version B piece, in unbent coordinates (u, v), with its seam edges.
    Seam edges, all running in a fixed direction so mirrored pieces line up:
      centre: wrist, up the outside, over the tip, down the inside, to the centre point
      v_top:  centre point to crotch;  v_cuff: crotch down to the wrist;  hem: wrist edge"""
    H, W, T = P.CUFF_HEIGHT, P.W0, P.TRI_DEPTH
    th = P.thumb_outline(200)
    centre = _resample(np.vstack([[(-W / 2, -H)], th, [(W / 2, -T)]]), H_STEP)
    v_top = _resample(np.array([(W / 2, -T), (0, 0)]), H_STEP)
    v_cuff = _resample(np.array([(0, 0), (0, -H)]), H_STEP)
    hem = _resample(np.array([(0, -H), (-W / 2, -H)]), H_STEP)
    boundary = np.vstack([centre[:-1], v_top[:-1], v_cuff[:-1], hem[:-1]])
    nc, nt, nk = len(centre) - 1, len(v_top) - 1, len(v_cuff) - 1
    nb = len(boundary)
    idx = np.arange(nb + 1) % nb
    edges = dict(centre=idx[:nc + 1], v_top=idx[nc:nc + nt + 1],
                 v_cuff=idx[nc + nt:nc + nt + nk + 1], hem=idx[nc + nt + nk:])
    poly = Polygon(boundary)
    # interior points on a triangular grid, kept away from the edge
    xs = np.arange(-W, W + 1, H_STEP)
    ys = np.arange(-H - 0.5, P.THUMB_LENGTH + 0.5, H_STEP * math.sqrt(3) / 2)
    gx, gy = np.meshgrid(xs, ys)
    gx = gx + (np.arange(len(ys))[:, None] % 2) * H_STEP / 2
    g = np.column_stack([gx.ravel(), gy.ravel()])
    g = g[contains_xy(poly, g[:, 0], g[:, 1])]
    g = g[np.array([poly.exterior.distance(_pt(p)) for p in g]) > 0.55 * H_STEP]
    pts = np.vstack([boundary, g])
    tri = mtri.Triangulation(pts[:, 0], pts[:, 1]).triangles
    cen = pts[tri].mean(axis=1)
    keep = contains_xy(poly, cen[:, 0], cen[:, 1])
    tri = tri[keep]
    # make every triangle counter-clockwise in the flat
    a, b, c = pts[tri[:, 0]], pts[tri[:, 1]], pts[tri[:, 2]]
    cw = ((b - a)[:, 0] * (c - a)[:, 1] - (b - a)[:, 1] * (c - a)[:, 0]) < 0
    tri[cw] = tri[cw][:, [0, 2, 1]]
    return pts, tri, edges


def _pt(p):
    from shapely.geometry import Point
    return Point(p)


def _embed(uv, k, side):
    """Rough first guess at where each point of a piece sits in 3D."""
    n = P.THUMBS
    R = P.CIRC / (2 * math.pi)
    r0 = P.W0 / math.pi
    a = 2 * math.pi * (k + 0.5) / n
    e = np.array([math.cos(a), math.sin(a), 0.0])
    t = np.array([-math.sin(a), math.cos(a), 0.0])
    u, v = uv[:, 0], uv[:, 1]
    out = np.zeros((len(uv), 3))
    # cuff strip
    m = (v < 0) & (u <= 0)
    ang = a + side * (u[m] + P.W0 / 2) / (P.W0 / 2) * math.pi / n
    out[m] = np.column_stack([R * np.cos(ang), R * np.sin(ang), v[m]])
    # triangle on the top: affine map of crotch, inner base, centre point
    m = (v < 0) & (u > 0)
    crotch = R * np.array([math.cos(a + side * math.pi / n), math.sin(a + side * math.pi / n), 0])
    inner = (R - 2 * r0) * e
    A = np.array([[0, 0, 1], [P.W0 / 2, 0, 1], [P.W0 / 2, -P.TRI_DEPTH, 1]], float)
    B = np.vstack([crotch, inner, np.zeros(3)])
    M = np.linalg.solve(A, B)
    out[m] = np.column_stack([u[m], v[m], np.ones(m.sum())]) @ M
    # finger: half a tube around its axis
    m = v >= 0
    vv = v[m]
    hw = P.finger_half_width(vv)
    hw = np.maximum(hw, 1e-3)
    psi = np.pi * np.clip((u[m] + hw) / (2 * hw), 0, 1)
    rr = 2 * hw / math.pi
    # start the finger already curved the way the pattern intends, so the solver
    # settles near the intended shape instead of getting stuck straight
    grid = np.linspace(0, P.FINGER_LENGTH, 400)
    phi = P._bend_angle(grid)
    ds = grid[1] - grid[0]
    ce = np.concatenate([[0], np.cumsum(np.sin(phi[:-1]) * ds)])
    cz = np.concatenate([[0], np.cumsum(np.cos(phi[:-1]) * ds)])
    sv = np.minimum(vv, 0.9 * P.FINGER_LENGTH)
    ph = np.interp(sv, grid, phi)
    zh = np.array([0, 0, 1.0])
    c = (R - r0 + np.interp(sv, grid, ce))[:, None] * e + np.interp(sv, grid, cz)[:, None] * zh
    nrm = np.cos(ph)[:, None] * e - np.sin(ph)[:, None] * zh
    out[m] = c + rr[:, None] * (np.cos(psi)[:, None] * nrm + side * np.outer(np.sin(psi), t))
    return out


def build():
    uv, tri, edges = flat_piece()
    rest2d = P.bend(uv)                       # true flat shape (finger curved)
    n = P.THUMBS
    npts = len(uv)
    X, F, seam_pairs, flat_edges, bend_pairs = [], [], [], [], []
    hem_idx = []
    # flat edge lengths and "flatness" pairs, per piece (no stiffness across seams)
    e_all = np.vstack([tri[:, [0, 1]], tri[:, [1, 2]], tri[:, [2, 0]]])
    e_sorted = np.sort(e_all, axis=1)
    uniq, inv, cnt = np.unique(e_sorted, axis=0, return_inverse=True, return_counts=True)
    opp = np.concatenate([tri[:, 2], tri[:, 0], tri[:, 1]])
    order = np.argsort(inv)
    inv_s, opp_s = inv[order], opp[order]
    first = np.searchsorted(inv_s, np.arange(len(uniq)))
    inner_e = np.nonzero(cnt == 2)[0]
    bp = np.column_stack([opp_s[first[inner_e]], opp_s[first[inner_e] + 1]])
    for k in range(n):
        for side in (-1, 1):
            off = len(X) * npts
            X.append(_embed(uv * [1, 1], k, side))
            f = tri + off
            if side == 1:                      # mirrored piece: flip winding
                f = f[:, [0, 2, 1]]
            F.append(f)
            flat_edges.append(uniq + off)
            bend_pairs.append(bp + off)
            hem_idx.append(edges["hem"] + off)
    X = np.vstack(X)
    F = np.vstack(F)
    E = np.vstack(flat_edges)
    BP = np.vstack(bend_pairs)
    L = np.linalg.norm(rest2d[E[:, 0] % npts] - rest2d[E[:, 1] % npts], axis=1)
    LB = np.linalg.norm(rest2d[BP[:, 0] % npts] - rest2d[BP[:, 1] % npts], axis=1)
    # stitch seams: piece index = 2k + (side == 1)
    parent = np.arange(len(X))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def join(a, b):
        for i, j in zip(a, b):
            ri, rj = find(i), find(j)
            if ri != rj:
                parent[rj] = ri

    seam_vertices = []
    for k in range(n):
        Lp, Rp = 2 * k * npts, (2 * k + 1) * npts
        Ln = (2 * ((k + 1) % n)) * npts
        join(edges["centre"] + Lp, edges["centre"] + Rp)       # down the middle of a finger
        join(edges["v_top"] + Rp, edges["v_top"] + Ln)         # to the next finger
        join(edges["v_cuff"] + Rp, edges["v_cuff"] + Ln)
        for o in (Lp, Rp):
            seam_vertices += list(edges["centre"] + o) + list(edges["v_top"] + o) + list(edges["v_cuff"] + o)
    roots = np.array([find(i) for i in range(len(X))])
    uniq_roots, newid = np.unique(roots, return_inverse=True)
    Xm = np.zeros((len(uniq_roots), 3))
    np.add.at(Xm, newid, X)
    Xm /= np.bincount(newid)[:, None]
    F = newid[F]
    E = newid[E]
    BP = newid[BP]
    seam = np.zeros(len(Xm), bool)
    seam[newid[np.array(seam_vertices)]] = True
    hem = np.unique(newid[np.concatenate(hem_idx)])
    # drop duplicate edges created by stitching (keep one)
    Es = np.sort(E, axis=1)
    _, ui = np.unique(Es, axis=0, return_index=True)
    E, L = E[ui], L[ui]
    # outward orientation
    vol = np.einsum("ij,ij->i", Xm[F[:, 0]], np.cross(Xm[F[:, 1]], Xm[F[:, 2]])).sum()
    if vol < 0:
        F = F[:, [0, 2, 1]]
    tip_local = np.argmin(np.linalg.norm(uv - [0, P.THUMB_LENGTH], axis=1))
    tips = newid[[2 * k * npts + tip_local for k in range(n)]]
    centre = newid[np.argmin(np.linalg.norm(uv - [P.W0 / 2, -P.TRI_DEPTH], axis=1))]
    return dict(X=Xm, F=F, E=E, L=L, BP=BP, LB=LB, seam=seam, hem=hem, tips=tips, centre=centre)


def _scatter(n, idx, vals):
    out = np.zeros((n, 3))
    for d in range(3):
        out[:, d] = np.bincount(idx, weights=vals[:, d], minlength=n)
    return out


def relax(m, iters=4000, pressure=1.0, stiff=60.0, log=False, tie=None):
    """tie: if set, the centre of the top is tied down to this height (a cord to the tube end)."""
    """Find the resting shape: minimise (stretch energy + tube contact - pressure x volume)."""
    from scipy.optimize import minimize
    F, E, L, BP, LB = m["F"], m["E"], m["L"], m["BP"], m["LB"]
    N = len(m["X"])
    tube_r = P.TUBE_DIAMETER / 2
    hem = m["hem"]
    ref = np.array([0, 0, -P.CUFF_HEIGHT])
    kc = 0.02 * stiff          # fabric bunches up much more easily than it stretches
    kb = 0.02                  # slight body within each piece, none across seams
    kt = 400.0                 # tube contact
    kh = 400.0                 # hem stays at the bottom

    def f(x):
        X = x.reshape(-1, 3)
        g = np.zeros_like(X)
        d = X[E[:, 1]] - X[E[:, 0]]
        l = np.linalg.norm(d, axis=1) + 1e-12
        eps = l / L - 1
        k = np.where(eps > 0, stiff, kc)
        en = np.sum(k * L * eps ** 2)
        gl = (2 * k * eps)[:, None] * d / l[:, None]
        g -= _scatter(N, E[:, 0], gl)
        g += _scatter(N, E[:, 1], gl)
        d = X[BP[:, 1]] - X[BP[:, 0]]
        l = np.linalg.norm(d, axis=1) + 1e-12
        en += kb * np.sum((l - LB) ** 2)
        gl = (2 * kb * (l - LB))[:, None] * d / l[:, None]
        g -= _scatter(N, BP[:, 0], gl)
        g += _scatter(N, BP[:, 1], gl)
        a, b, c = X[F[:, 0]] - ref, X[F[:, 1]] - ref, X[F[:, 2]] - ref
        vol = np.einsum("ij,ij->", a, np.cross(b, c)) / 6
        en -= pressure * vol
        g -= pressure / 6 * (_scatter(N, F[:, 0], np.cross(b, c)) + _scatter(N, F[:, 1], np.cross(c, a))
                             + _scatter(N, F[:, 2], np.cross(a, b)))
        rho = np.hypot(X[:, 0], X[:, 1]) + 1e-12
        pen_side = np.clip(tube_r - rho, 0, None)
        pen_top = np.clip(-X[:, 2], 0, None)
        inside = (pen_side > 0) & (pen_top > 0)
        use_side = inside & (pen_side <= pen_top)
        use_top = inside & ~use_side
        en += kt * np.sum(pen_side[use_side] ** 2) + kt * np.sum(pen_top[use_top] ** 2)
        g[use_side, 0] -= 2 * kt * pen_side[use_side] * X[use_side, 0] / rho[use_side]
        g[use_side, 1] -= 2 * kt * pen_side[use_side] * X[use_side, 1] / rho[use_side]
        g[use_top, 2] -= 2 * kt * pen_top[use_top]
        dz = X[hem, 2] + P.CUFF_HEIGHT
        en += kh * np.sum(dz ** 2)
        g[hem, 2] += 2 * kh * dz
        if tie is not None:
            c = m["centre"]
            dc = X[c] - np.array([0, 0, tie])
            en += kh * np.sum(dc ** 2)
            g[c] += 2 * kh * dc
        return en, g.ravel()

    res = minimize(f, m["X"].ravel(), jac=True, method="L-BFGS-B",
                   options=dict(maxiter=iters, maxcor=20))
    X = res.x.reshape(-1, 3)
    if log:
        d = np.linalg.norm(X[E[:, 1]] - X[E[:, 0]], axis=1)
        print(res.message, res.nit, "stretch max %.3f mean %.4f" % ((d / L).max(), (d / L).mean()))
    return X


def render(m, X, path, title):
    import matplotlib.pyplot as plt
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection
    F = m["F"]
    seam = m["seam"]
    fabric = np.array([0.93, 0.49, 0.38])
    dark = np.array([0.35, 0.12, 0.08])
    tri = X[F]
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    seam_face = seam[F].sum(axis=1) >= 1
    # grey tube below the cuff
    tube_r = P.TUBE_DIAMETER / 2
    th = np.linspace(0, 2 * math.pi, 49)
    z0, z1 = -P.CUFF_HEIGHT - 2.5, -P.CUFF_HEIGHT + 0.05
    quads = []
    for i in range(48):
        a, b = th[i], th[i + 1]
        quads.append([(tube_r * math.cos(a), tube_r * math.sin(a), z0), (tube_r * math.cos(b), tube_r * math.sin(b), z0),
                      (tube_r * math.cos(b), tube_r * math.sin(b), z1), (tube_r * math.cos(a), tube_r * math.sin(a), z1)])
    quads = np.array(quads)
    qn = np.column_stack([np.cos((th[:-1] + th[1:]) / 2), np.sin((th[:-1] + th[1:]) / 2), np.zeros(48)])

    fig = plt.figure(figsize=(11, 6.4))
    for i, (el, az, lbl) in enumerate([(14, 35, "From the side"), (60, 35, "From above")]):
        ax = fig.add_subplot(1, 2, i + 1, projection="3d")
        ela, aza = math.radians(el), math.radians(az)
        view = np.array([math.cos(ela) * math.cos(aza), math.cos(ela) * math.sin(aza), math.sin(ela)])
        key = view * 0.6 + np.array([0, 0, 0.8]) + 0.4 * np.array([-math.sin(aza), math.cos(aza), 0])
        key /= np.linalg.norm(key)
        shade = 0.35 + 0.65 * np.clip(np.abs(n @ key), 0, 1) * (0.55 + 0.45 * np.clip(n @ view, 0, 1))
        cols = np.where(seam_face[:, None], 0.45 * dark + 0.55 * fabric, fabric) * shade[:, None]
        tshade = 0.45 + 0.5 * np.clip(qn @ key, 0, 1)
        tcols = np.array([0.80, 0.81, 0.84]) * tshade[:, None]
        polys = list(quads) + list(tri)
        allc = np.vstack([tcols, cols])
        allc = np.column_stack([np.clip(allc, 0, 1), np.ones(len(allc))])
        pc = Poly3DCollection(polys, facecolors=allc, edgecolors=allc, linewidths=0.25)
        ax.add_collection3d(pc)
        lim = 5.5
        ax.set_xlim(-lim, lim); ax.set_ylim(-lim, lim); ax.set_zlim(-P.CUFF_HEIGHT - 2.5, P.FINGER_LENGTH + 1.0)
        ax.set_box_aspect((2 * lim, 2 * lim, P.FINGER_LENGTH + 3.5 + P.CUFF_HEIGHT), zoom=1.25)
        ax.view_init(elev=el, azim=az)
        ax.set_axis_off()
        ax.set_title(lbl, fontsize=10)
    fig.suptitle(title, fontsize=12)
    fig.text(0.5, 0.04, "Simulated from the flat pattern pieces: dark bands are seams, grey is the 5 in tube. "
             "Fabric shown inflated; it can't stretch, so ridges and creases come from the pattern.",
             ha="center", fontsize=8.5)
    fig.savefig(path, dpi=115, bbox_inches="tight")
    plt.close(fig)


def make_preview(path, title, iters=6000):
    m = build()
    X = relax(m, iters=iters)
    render(m, X, path, title)
    return m, X
