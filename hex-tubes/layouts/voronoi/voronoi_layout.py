"""
Layout A: Voronoi mix skin for a coral branch.

What it does, in plain words:
  1. Builds a test surface: a gently curving tube about 12.5 in across and
     6 ft long, tapering at both ends, closed and rounded at the tip end.
     It is stored as a plain triangle mesh, so any other mesh (an STL/OBJ
     from Fusion) can be dropped in instead.
  2. Paints a smooth "size map" over the surface: some areas ask for small
     shapes (about 3 in), others for big ones (about 7 in).
  3. Scatters seed points evenly but randomly (Poisson-disk sampling): no two
     seeds closer than the size map allows.
  4. Evens the seeds out a little (Lloyd relaxation): each seed moves to the
     middle of its own patch, a few times.
  5. Every point of the surface belongs to its nearest seed. The patches that
     come out are Voronoi cells. Where three patches meet is a corner; every
     corner is stored once and shared by the cells around it.
  6. Writes layout.json (corners + polygon list) and preview images, and
     prints metrics.

Run it with:   python3 voronoi_layout.py
Needs:         numpy, scipy, matplotlib   (pip install numpy scipy matplotlib)
All lengths are in inches.
"""

import json
import math
import os
from collections import defaultdict

import numpy as np
from scipy.spatial import cKDTree
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components

# --------------------------------------------------------------------------
# PARAMETERS - change these
# --------------------------------------------------------------------------
SIZE_MIN = 3.0           # smallest shapes, inches across
SIZE_MAX = 7.0           # biggest shapes, inches across
SIZE_AVG = 5.0           # average shape, inches across (script tunes itself to hit it)
SIZE_WAVELENGTH = 22.0   # how far apart the "big" and "small" areas are (in)
LLOYD_ROUNDS = 4         # 0 = raw and wild, 4 = organic, 15+ = very regular
RANDOM_SEED = 7          # change for a different pattern with the same rules

# Test tube
TUBE_LENGTH = 72.0       # inches, including the rounded tip
TUBE_DIAMETER = 12.5
BASE_DIAMETER = 9.5      # open end (where the branch would meet the trunk)
TIP_DIAMETER = 6.0       # rounded closed tip
MESH_STEP = 0.2          # mesh resolution; smaller = more exact, slower

DEBUG = False
HERE = os.path.dirname(os.path.abspath(__file__))

# Colours by number of sides (hexagons calm, the odd ones stand out)
SIDE_COLOURS = {3: "#7a5cc4", 4: "#eda100", 5: "#eb6834", 6: "#c9d6e3",
                7: "#2a78d6", 8: "#e87ba4"}
SIDE_NAMES = {3: "triangles", 4: "quads", 5: "pentagons", 6: "hexagons",
              7: "heptagons", 8: "octagons"}


def side_colour(n):
    return SIDE_COLOURS.get(n, "#555555")


# --------------------------------------------------------------------------
# 1. Test surface (a triangle mesh)
# --------------------------------------------------------------------------
def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def make_test_tube():
    """Return vertices (n,3), triangles (m,3) and flat-view coords (n,2)."""
    R = TUBE_DIAMETER / 2
    r_base = BASE_DIAMETER / 2
    r_tip = TIP_DIAMETER / 2
    s_cap = TUBE_LENGTH - r_tip          # where the round cap starts

    # Radius profile along the length: taper at the base, gentle bulges,
    # taper towards the tip, then a half-sphere cap.
    s_dense = np.linspace(0, TUBE_LENGTH, 4000)
    rad = np.empty_like(s_dense)
    body = s_dense <= s_cap
    sb = s_dense[body]
    base_taper = r_base + (R - r_base) * smoothstep(0, 14, sb)
    to_tip = smoothstep(s_cap - 22, s_cap, sb)
    bulge = 1 + 0.05 * np.sin(2 * np.pi * sb / 28 + 0.8) * (1 - to_tip)
    rad[body] = base_taper * bulge * (1 - (1 - r_tip / R) * to_tip)
    sc = s_dense[~body] - s_cap
    rad[~body] = np.sqrt(np.clip(r_tip ** 2 - sc ** 2, 0, None))

    # Resample by meridian arc length so rings are evenly spaced on the skin.
    ds = np.hypot(np.diff(s_dense), np.diff(rad))
    arc = np.concatenate([[0], np.cumsum(ds)])
    n_rings = int(arc[-1] / MESH_STEP)
    a_r = np.linspace(0, arc[-1], n_rings + 1)
    s_r = np.interp(a_r, arc, s_dense)
    r_r = np.interp(a_r, arc, rad)

    # Centre line: a gentle sideways bend plus a slow wiggle.
    def centre(s):
        return np.stack([7.0 * (s / TUBE_LENGTH) ** 2 + 0.8 * np.sin(2 * np.pi * s / 45),
                         1.5 * np.sin(2 * np.pi * s / 70 + 0.5),
                         s], axis=-1)

    c_r = centre(s_r)
    tang = np.gradient(c_r, s_r, axis=0)
    tang /= np.linalg.norm(tang, axis=1)[:, None]
    # parallel-transport frame (no twisting)
    n0 = np.array([1.0, 0, 0])
    frames = []
    nrm = n0 - tang[0] * (n0 @ tang[0])
    nrm /= np.linalg.norm(nrm)
    for t in tang:
        nrm = nrm - t * (nrm @ t)
        nrm /= np.linalg.norm(nrm)
        frames.append((nrm, np.cross(t, nrm)))

    n_around = int(round(2 * np.pi * R / MESH_STEP))
    th = np.arange(n_around) * 2 * np.pi / n_around
    verts, flat = [], []
    last = len(s_r) - 1                   # last ring has radius 0 -> apex
    for j in range(last):
        nn, bb = frames[j]
        ring = c_r[j] + r_r[j] * (np.cos(th)[:, None] * nn + np.sin(th)[:, None] * bb)
        verts.append(ring)
        flat.append(np.stack([th, np.full_like(th, a_r[j]), np.full_like(th, r_r[j])], 1))
    verts.append(c_r[last][None])
    flat.append(np.array([[np.pi, a_r[last], 0.0]]))
    V = np.concatenate(verts)
    UV = np.concatenate(flat)

    tris = []
    for j in range(last - 1):
        a = j * n_around + np.arange(n_around)
        b = j * n_around + (np.arange(n_around) + 1) % n_around
        c, d = a + n_around, b + n_around
        tris.append(np.stack([a, b, d], 1))
        tris.append(np.stack([a, d, c], 1))
    apex = len(V) - 1
    a = (last - 1) * n_around + np.arange(n_around)
    b = (last - 1) * n_around + (np.arange(n_around) + 1) % n_around
    tris.append(np.stack([a, b, np.full(n_around, apex)], 1))
    F = np.concatenate(tris)
    # make triangles face outwards (check one against the centre line)
    p = V[F[0]]
    if np.cross(p[1] - p[0], p[2] - p[0]) @ (p[0] - c_r[0]) < 0:
        F = F[:, ::-1]
    # flat view coords per vertex: angle around, distance along, local radius
    flat_info = {"uv": UV, "ring_radius": r_r, "ring_arc": a_r}
    return V, F, flat_info


# --------------------------------------------------------------------------
# Mesh helpers (work on any triangle mesh)
# --------------------------------------------------------------------------
class Mesh:
    def __init__(self, V, F):
        self.V, self.F = V, F
        e1 = V[F[:, 1]] - V[F[:, 0]]
        e2 = V[F[:, 2]] - V[F[:, 0]]
        cr = np.cross(e1, e2)
        self.tri_area = 0.5 * np.linalg.norm(cr, axis=1)
        vn = np.zeros_like(V)
        for k in range(3):
            np.add.at(vn, F[:, k], cr)
        self.vnormal = vn / np.maximum(np.linalg.norm(vn, axis=1), 1e-12)[:, None]
        va = np.zeros(len(V))
        for k in range(3):
            np.add.at(va, F[:, k], self.tri_area / 3)
        self.vert_area = va
        self.tree = cKDTree(V)
        # unique edges and boundary edges
        E = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])
        key = np.sort(E, axis=1)
        uniq, inv, cnt = np.unique(key, axis=0, return_inverse=True, return_counts=True)
        self.edges = uniq
        self.edge_tris = defaultdict(list)          # edge index -> triangles
        tri_of = np.concatenate([np.arange(len(F))] * 3)
        for e, t in zip(inv.ravel().tolist(), tri_of.tolist()):
            self.edge_tris[e].append(t)
        self.boundary_edges = E[cnt[inv.ravel()] == 1]  # directed, as in faces
        self.edge_index = {tuple(k): i for i, k in enumerate(uniq)}

    def project(self, P):
        """Push points onto the surface (tangent plane of nearest vertex)."""
        _, i = self.tree.query(P)
        n = self.vnormal[i]
        return P - n * np.sum((P - self.V[i]) * n, axis=1)[:, None], i

    def random_points(self, n, rng):
        t = rng.choice(len(self.F), n, p=self.tri_area / self.tri_area.sum())
        r1, r2 = rng.random(n), rng.random(n)
        s = np.sqrt(r1)
        w = np.stack([1 - s, s * (1 - r2), s * r2], 1)
        return np.einsum("nk,nkd->nd", w, self.V[self.F[t]])


# --------------------------------------------------------------------------
# 2. Size map: a smooth random field from SIZE_MIN to SIZE_MAX
# --------------------------------------------------------------------------
def make_size_field(rng, bias=1.0):
    """Size wanted at each point. bias > 1 favours small shapes, < 1 big ones."""
    k = 10
    dirs = rng.normal(size=(k, 3))
    dirs /= np.linalg.norm(dirs, axis=1)[:, None]
    wav = SIZE_WAVELENGTH * rng.uniform(0.7, 1.4, k)
    phase = rng.uniform(0, 2 * np.pi, k)

    def raw(P):
        return np.sum(np.cos(P @ (dirs / wav[:, None] * 2 * np.pi).T + phase), axis=1) / math.sqrt(k / 2)

    def size(P):
        g = np.tanh(0.9 * raw(P))           # roughly -1..1, mean 0
        return SIZE_MIN + (SIZE_MAX - SIZE_MIN) * (0.5 * (g + 1)) ** bias
    return size


# --------------------------------------------------------------------------
# 3. Poisson-disk seeds with variable spacing
# --------------------------------------------------------------------------
def poisson_seeds(mesh, size, rng):
    # How many shapes the size map asks for: area / area of a hexagon that size
    d = size(mesh.V)
    target = int(round(np.sum(mesh.vert_area / (math.sqrt(3) / 2 * d ** 2))))
    cand = mesh.random_points(60 * target, rng)
    cd = size(cand)

    def throw(k):
        pts, rads = [], []
        cell = k * SIZE_MAX
        grid = defaultdict(list)
        for p, r in zip(cand, cd):
            g = tuple((p // cell).astype(int))
            ok = True
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    for dz in (-1, 0, 1):
                        for q in grid.get((g[0] + dx, g[1] + dy, g[2] + dz), ()):
                            if np.sum((pts[q] - p) ** 2) < (k * 0.5 * (r + rads[q])) ** 2:
                                ok = False
                                break
                        if not ok:
                            break
                    if not ok:
                        break
                if not ok:
                    break
            if ok:
                grid[g].append(len(pts))
                pts.append(p)
                rads.append(r)
        return np.array(pts)

    lo, hi = 0.5, 1.2                     # bisection on the spacing factor
    best = None
    for _ in range(12):
        k = 0.5 * (lo + hi)
        pts = throw(k)
        best = pts
        if abs(len(pts) - target) <= max(1, target // 100):
            break
        if len(pts) > target:
            lo = k
        else:
            hi = k
    return best, target


# --------------------------------------------------------------------------
# 4. Lloyd relaxation (weighted so small areas keep small cells)
# --------------------------------------------------------------------------
def lloyd(mesh, seeds, size, rounds):
    w = mesh.vert_area / size(mesh.V) ** 4   # density: cell size ~ size map
    for _ in range(rounds):
        _, lab = cKDTree(seeds).query(mesh.V)
        tot = np.bincount(lab, w, len(seeds))
        cen = np.stack([np.bincount(lab, w * mesh.V[:, k], len(seeds)) for k in range(3)], 1)
        keep = tot > 0
        seeds = seeds.copy()
        seeds[keep] = cen[keep] / tot[keep, None]
        seeds, _ = mesh.project(seeds)
    return seeds


# --------------------------------------------------------------------------
# 5. Voronoi cells on the mesh, with shared corners
# --------------------------------------------------------------------------
def label_mesh(mesh, seeds):
    """Give every mesh vertex the number of its nearest seed."""
    _, lab = cKDTree(seeds).query(mesh.V)
    # Clean-up: each cell must be one connected patch. Any stray bit cut off
    # from its own patch is handed to a neighbouring patch.
    E = mesh.edges
    n = len(mesh.V)
    same = lab[E[:, 0]] == lab[E[:, 1]]
    A = coo_matrix((np.ones(same.sum()), (E[same, 0], E[same, 1])), shape=(n, n))
    _, comp = connected_components(A, directed=False)
    comp_size = np.bincount(comp)
    biggest = {}
    for c in np.argsort(comp_size).tolist():          # last write = largest
        biggest[int(lab[np.argmax(comp == c)])] = c
    main_comp = np.array([biggest.get(int(l), -1) for l in range(len(seeds))])
    lab = lab.copy()
    lab[comp != main_comp[lab]] = -1
    while (lab < 0).any():
        a, b = E[:, 0], E[:, 1]
        fill = (lab[a] < 0) & (lab[b] >= 0)
        lab[a[fill]] = lab[b[fill]]
        fill = (lab[b] < 0) & (lab[a] >= 0)
        lab[b[fill]] = lab[a[fill]]
    return lab


def equidistant_point(seeds, trip, start, mesh):
    """Point on the surface equally far from the three seeds."""
    a, b, c = seeds[list(trip)]
    ab, ac = b - a, c - a
    n = np.cross(ab, ac)
    nn = n @ n
    cc = a + (np.cross(n, ab) * (ac @ ac) + np.cross(ac, n) * (ab @ ab)) / (2 * nn)
    n = n / math.sqrt(nn)
    q = start
    for _ in range(4):
        p = cc + n * ((q - cc) @ n)
        q = mesh.project(p[None])[0][0]
    return q


def build_cells(mesh, seeds, lab):
    F = mesh.F
    L = lab[F]
    # --- graph of "boundary" triangles: nodes are triangles with 2+ labels
    node_of_tri = {}
    corners = []           # list of dicts: pos, labels
    junction = (L[:, 0] != L[:, 1]) & (L[:, 1] != L[:, 2]) & (L[:, 0] != L[:, 2])

    # crossing edges: mesh edges whose ends lie in different cells
    E = mesh.edges
    cross = np.flatnonzero(lab[E[:, 0]] != lab[E[:, 1]])
    adj = defaultdict(list)     # triangle (or rim node) -> [(neighbour, pair)]
    rim_nodes = {}
    for e in cross.tolist():
        pair = tuple(sorted((int(lab[E[e, 0]]), int(lab[E[e, 1]]))))
        ts = mesh.edge_tris[e]
        if len(ts) == 2:
            adj[ts[0]].append((ts[1], pair))
            adj[ts[1]].append((ts[0], pair))
        else:   # on the open rim: a corner sits on this edge
            rn = ("rim", e)
            rim_nodes[rn] = pair
            adj[ts[0]].append((rn, pair))
            adj[rn].append((ts[0], pair))

    # merge junction triangles that touch and carry the same 3 labels
    jt = np.flatnonzero(junction).tolist()
    jset = set(jt)
    corner_of = {}
    for t in jt:
        if t in corner_of:
            continue
        trip = tuple(sorted(L[t]))
        group, stack = [t], [t]
        corner_of[t] = len(corners)
        while stack:
            u = stack.pop()
            for v, _ in adj[u]:
                if v in jset and v not in corner_of and tuple(sorted(L[v])) == trip:
                    corner_of[v] = len(corners)
                    group.append(v)
                    stack.append(v)
        start = mesh.V[F[group]].reshape(-1, 3).mean(0)
        corners.append({"pos": equidistant_point(seeds, trip, start, mesh),
                        "labels": set(trip), "rim": False})
    for rn, pair in rim_nodes.items():
        e = rn[1]
        u, w = mesh.V[E[e, 0]], mesh.V[E[e, 1]]
        sa, sb = seeds[pair[0]], seeds[pair[1]]
        # |x-sa|^2 = |x-sb|^2 along x = u + t (w-u)
        dvec = w - u
        t = ((sb @ sb - sa @ sa) - 2 * u @ (sb - sa)) / (2 * dvec @ (sb - sa))
        corner_of[rn] = len(corners)
        corners.append({"pos": u + np.clip(t, 0, 1) * dvec, "labels": set(pair), "rim": True})

    # --- walk chains of 2-label triangles between corners -> Voronoi edges
    vedges = {}            # (c1, c2, pair) set
    for start in corner_of:
        for nxt, pair in adj[start]:
            prev, cur = start, nxt
            while cur not in corner_of:
                options = [v for v, p in adj[cur] if v != prev]
                if not options:
                    break
                prev, cur = cur, options[0]
            if cur in corner_of:
                c1, c2 = corner_of[start], corner_of[cur]
                if c1 != c2:
                    vedges[(min(c1, c2), max(c1, c2), pair)] = True

    # --- rim chords: along each open boundary loop, join consecutive rim corners
    rim_chords = []
    if len(mesh.boundary_edges):
        nxt_v = {a: b for a, b in mesh.boundary_edges}
        seen = set()
        for v0 in nxt_v:
            if v0 in seen:
                continue
            loop, v = [], v0
            while v not in seen:
                seen.add(v)
                loop.append(v)
                v = nxt_v[v]
            hits = []      # (position in loop, corner)
            for i, v in enumerate(loop):
                w = loop[(i + 1) % len(loop)]
                ei = mesh.edge_index.get(tuple(sorted((v, w))))
                if ("rim", ei) in corner_of:
                    hits.append((i, corner_of[("rim", ei)], lab[w]))
            for k in range(len(hits)):
                _, c1, cell = hits[k]
                c2 = hits[(k + 1) % len(hits)][1]
                rim_chords.append((c1, c2, cell))

    # --- assemble each cell's corner loop
    cell_edges = defaultdict(list)
    for (c1, c2, pair) in vedges:
        for cell in pair:
            cell_edges[cell].append((c1, c2))
    for c1, c2, cell in rim_chords:
        cell_edges[cell].append((c1, c2))

    polygons, poly_seed, problems = [], [], 0
    for cell, eds in cell_edges.items():
        nb = defaultdict(list)
        for a, b in eds:
            nb[a].append(b)
            nb[b].append(a)
        if any(len(v) != 2 for v in nb.values()):
            if DEBUG:
                print("bad cell", cell, {k: v for k, v in nb.items()},
                      [corners[k]["labels"] for k in nb])
            problems += 1
            continue
        loop = [eds[0][0]]
        prev = None
        while True:
            cur = loop[-1]
            n2 = [x for x in nb[cur] if x != prev]
            prev = cur
            if not n2:
                break
            if n2[0] == loop[0]:
                break
            loop.append(n2[0])
        if len(loop) != len(nb):
            problems += 1
            continue
        # orient counter-clockwise seen from outside
        P = np.array([corners[c]["pos"] for c in loop])
        _, vi = mesh.tree.query(seeds[cell])
        if newell(P) @ mesh.vnormal[vi] < 0:
            loop = loop[::-1]
        polygons.append(loop)
        poly_seed.append(cell)
    return corners, polygons, poly_seed, problems


def newell(P):
    n = np.zeros(3)
    for i in range(len(P)):
        a, b = P[i], P[(i + 1) % len(P)]
        n += np.array([(a[1] - b[1]) * (a[2] + b[2]),
                       (a[2] - b[2]) * (a[0] + b[0]),
                       (a[0] - b[0]) * (a[1] + b[1])])
    return n


# --------------------------------------------------------------------------
# 6. Metrics
# --------------------------------------------------------------------------
def polygon_metrics(P):
    c = P.mean(0)
    _, _, vt = np.linalg.svd(P - c)
    nrm = vt[2]
    off = (P - c) @ nrm
    area = 0.5 * np.linalg.norm(newell(P))
    across = math.sqrt(area / (math.sqrt(3) / 2))  # width of equal-area hexagon
    return {"area": area, "across": across, "flat_dev": float(np.abs(off).max()),
            "centre": c}


# --------------------------------------------------------------------------
# 7. Pictures
# --------------------------------------------------------------------------
def render(V3, polys, sides, path_3d, path_close, mesh_centre):
    """3D pictures, drawn with a simple camera: rotate, flatten, paint back to front."""
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.patches import Patch
    from matplotlib.collections import PolyCollection
    from matplotlib.colors import to_rgb

    def draw(ax, turn, tilt, window=None):
        # camera: look along the tube's side; turn = spin around the tube (deg),
        # tilt = look down from above (deg). Screen x = along the tube (z).
        t, k = np.radians(turn), np.radians(tilt)
        view = np.array([np.cos(k) * np.cos(t), np.cos(k) * np.sin(t), np.sin(k)])
        right = np.array([0.0, 0.0, 1.0])               # along the tube
        up = np.cross(view, right)
        up /= np.linalg.norm(up)
        light = view + 0.6 * up
        light /= np.linalg.norm(light)
        items = []
        for P, n in zip(polys, sides):
            nn = newell(P)
            nn /= np.linalg.norm(nn)
            if nn @ view < 0:                         # back side of the tube
                continue
            if window is not None and abs(P.mean(0)[2] - window[0]) > window[1]:
                continue
            xy = np.stack([P @ right, P @ up], 1)
            sh = 0.62 + 0.38 * max(0.0, nn @ light)
            items.append((P.mean(0) @ view, xy, tuple(np.array(to_rgb(side_colour(n))) * sh)))
        items.sort(key=lambda it: it[0])               # far first
        ax.add_collection(PolyCollection([it[1] for it in items],
                                         facecolors=[it[2] for it in items],
                                         edgecolors="#222222", linewidths=0.6))
        ax.autoscale_view()
        ax.set_aspect("equal")
        ax.set_axis_off()

    counts = {n: sides.count(n) for n in sorted(set(sides))}
    legend = [Patch(facecolor=side_colour(n), edgecolor="#222222",
                    label=f"{n} sides ({SIDE_NAMES.get(n, '')}): {counts[n]}")
              for n in counts]

    fig, axes = plt.subplots(3, 1, figsize=(15, 9.5))
    views = [(-90, 10, "Front"), (90, 10, "Back (turned half way round)"),
             (0, 60, "Seen from above")]
    for ax, (turn, tilt, title) in zip(axes, views):
        draw(ax, turn, tilt)
        ax.set_title(title, fontsize=12)
    fig.legend(handles=legend, loc="lower center", ncol=6, frameon=False, fontsize=10)
    fig.suptitle("Layout A: Voronoi mix on the test tube (12.5 in across, 6 ft long). "
                 "Open base on the left, rounded tip on the right", fontsize=13)
    fig.tight_layout(rect=(0, 0.05, 1, 0.96))
    fig.savefig(path_3d, dpi=100)
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(11, 7))
    draw(ax, -80, 12, window=(mesh_centre[2], 15))
    ax.set_title("Close-up: about 30 in of the middle of the tube", fontsize=12)
    fig.legend(handles=legend, loc="lower center", ncol=3, frameon=False)
    fig.tight_layout(rect=(0, 0.08, 1, 1))
    fig.savefig(path_close, dpi=110)
    plt.close(fig)


def render_flat(flat_polys, sides, flat_info, path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.patches import Polygon, Patch

    arc, rad = flat_info["ring_arc"], flat_info["ring_radius"]
    half = np.pi * rad.max()
    fig, ax = plt.subplots(figsize=(8, 13))
    for Q, n in zip(flat_polys, sides):
        for turn in (-2 * np.pi, 0, 2 * np.pi):     # repeat across the seam
            xy = np.stack([(Q[:, 0] + turn) * Q[:, 2], Q[:, 1]], 1)
            if np.all(np.abs(xy[:, 0]) > np.pi * Q[:, 2] + 1e-6):
                continue
            ax.add_patch(Polygon(xy, closed=True, facecolor=side_colour(n),
                                 edgecolor="#222222", linewidth=0.6))
    # the seam: everything outside these lines is a repeat of the other side
    ax.fill_betweenx(arc, np.pi * rad, half + 8, color="white", alpha=0.75, zorder=3)
    ax.fill_betweenx(arc, -half - 8, -np.pi * rad, color="white", alpha=0.75, zorder=3)
    ax.plot(np.pi * rad, arc, color="#666", lw=0.9, ls="--", zorder=4)
    ax.plot(-np.pi * rad, arc, color="#666", lw=0.9, ls="--", zorder=4)
    ax.set_xlim(-half - 4, half + 4)
    ax.set_ylim(-1, arc[-1] + 1)
    ax.set_aspect("equal")
    ax.set_xlabel("around the tube (in); dashed lines = the cut, both are the same line")
    ax.set_ylabel("along the tube (in): open base at 0, rounded tip at the top")
    counts = {n: sides.count(n) for n in sorted(set(sides))}
    ax.legend(handles=[Patch(facecolor=side_colour(n), edgecolor="#222222",
                             label=f"{n} sides: {counts[n]}") for n in counts],
              loc="upper left", bbox_to_anchor=(1.01, 1), frameon=False)
    ax.set_title("Unrolled view: the skin cut along one side and laid flat\n"
                 "(the domed tip can't lie flat, so it is stretched sideways)", fontsize=11)
    fig.tight_layout()
    fig.savefig(path, dpi=110)
    plt.close(fig)


# --------------------------------------------------------------------------
def make_layout(mesh, bias):
    rng = np.random.default_rng(RANDOM_SEED)
    size = make_size_field(rng, bias)
    seeds, target = poisson_seeds(mesh, size, rng)
    seeds, _ = mesh.project(seeds)
    seeds = lloyd(mesh, seeds, size, LLOYD_ROUNDS)
    lab = label_mesh(mesh, seeds)
    corners, polygons, poly_seed, problems = build_cells(mesh, seeds, lab)
    across = [polygon_metrics(np.array([corners[c]["pos"] for c in p]))["across"]
              for p in polygons]
    return seeds, target, corners, polygons, poly_seed, problems, float(np.mean(across))


def main():
    V, F, flat_info = make_test_tube()
    mesh = Mesh(V, F)

    # Small shapes are more numerous, so the plain size map gives an average
    # below the middle of the range. Nudge the bias until the average matches.
    lo, hi = math.log(0.2), math.log(3.0)
    for step in range(8):
        bias = math.exp(0.5 * (lo + hi))
        res = make_layout(mesh, bias)
        print(f"  tuning sizes: bias {bias:.2f} -> average {res[-1]:.2f} in")
        if abs(res[-1] - SIZE_AVG) < 0.05:
            break
        if res[-1] < SIZE_AVG:
            hi = math.log(bias)
        else:
            lo = math.log(bias)
    seeds, target, corners, polygons, poly_seed, problems, _ = res

    # keep only corners used by polygons, renumber
    used = sorted({c for p in polygons for c in p})
    new = {c: i for i, c in enumerate(used)}
    verts = np.array([corners[c]["pos"] for c in used])
    rim = [corners[c]["rim"] for c in used]
    polys = [[new[c] for c in p] for p in polygons]

    # sanity check: every edge shared by exactly two shapes (one on the rim)
    edge_use = defaultdict(list)
    for pi, p in enumerate(polys):
        for k in range(len(p)):
            edge_use[tuple(sorted((p[k], p[(k + 1) % len(p)])))].append(pi)
    bad_edges = sum(1 for e, u in edge_use.items()
                    if len(u) > 2 or (len(u) == 1 and not (rim[e[0]] and rim[e[1]])))
    corner_polys = defaultdict(list)
    for pi, p in enumerate(polys):
        for c in p:
            corner_polys[c].append(pi)

    # metrics
    mets = [polygon_metrics(verts[p]) for p in polys]
    sides = [len(p) for p in polys]
    across = np.array([m["across"] for m in mets])
    dev = np.array([m["flat_dev"] for m in mets])
    counts = {n: sides.count(n) for n in sorted(set(sides))}

    print("Layout A: Voronoi mix")
    print(f"  seeds asked for by size map: {target}, placed: {len(seeds)}")
    print(f"  shapes: {len(polys)}   corners: {len(verts)}   edges: {len(edge_use)}")
    for n, c in counts.items():
        print(f"    {n} sides ({SIDE_NAMES.get(n, '?')}): {c}  ({100 * c / len(polys):.0f} %)")
    print(f"  size across (in): min {across.min():.2f}  average {across.mean():.2f}  "
          f"max {across.max():.2f}")
    print(f"  out of flat (in): max {dev.max():.2f}  average {dev.mean():.2f}")
    print(f"  check: cells that failed {problems}, edges not shared properly {bad_edges}")

    # flat (unrolled) coordinates: only meaningful for the test tube.
    # x = distance around from the far side (seam), y = distance along.
    uv = flat_info["uv"]
    _, vi = mesh.tree.query(verts)
    flat_polys = []
    for p in polys:
        th, along, r = uv[vi[p]].T
        th = th[0] + (th - th[0] + np.pi) % (2 * np.pi) - np.pi   # no seam jumps
        shift = 2 * np.pi * np.floor(th.mean() / (2 * np.pi))
        th = th - shift - np.pi                                    # now around -pi..pi
        flat_polys.append(np.stack([th, along, r], 1))
    out = {
        "description": "Layout A Voronoi mix. vertices = shared corners (inches); "
                       "polygons = lists of vertex indices, counter-clockwise seen from outside.",
        "units": "inch",
        "parameters": {"SIZE_MIN": SIZE_MIN, "SIZE_MAX": SIZE_MAX, "SIZE_AVG": SIZE_AVG, "size_bias": bias,
                       "SIZE_WAVELENGTH": SIZE_WAVELENGTH, "LLOYD_ROUNDS": LLOYD_ROUNDS,
                       "RANDOM_SEED": RANDOM_SEED, "TUBE_DIAMETER": TUBE_DIAMETER,
                       "TUBE_LENGTH": TUBE_LENGTH},
        "vertices": np.round(verts, 4).tolist(),
        "vertex_on_open_edge": rim,
        "vertex_polygons": [corner_polys[i] for i in range(len(verts))],
        "polygons": polys,
        "polygon_info": [{"sides": s, "seed": np.round(seeds[sd], 4).tolist(),
                          "area": round(m["area"], 3), "across": round(m["across"], 3),
                          "out_of_flat": round(m["flat_dev"], 3)}
                         for s, sd, m in zip(sides, poly_seed, mets)],
        "edges": [{"vertices": list(e), "polygons": u} for e, u in edge_use.items()],
        "flat_view": [np.round(np.stack([q[:, 0] * q[:, 2], q[:, 1]], 1), 3).tolist()
                      for q in flat_polys],
        "metrics": {"side_counts": {str(k): v for k, v in counts.items()},
                    "across_min": round(float(across.min()), 3),
                    "across_avg": round(float(across.mean()), 3),
                    "across_max": round(float(across.max()), 3),
                    "out_of_flat_max": round(float(dev.max()), 3),
                    "out_of_flat_avg": round(float(dev.mean()), 3)},
    }
    with open(os.path.join(HERE, "layout.json"), "w") as f:
        json.dump(out, f)

    centre = V[np.abs(V[:, 2] - TUBE_LENGTH * 0.5) < 1].mean(0)
    render(verts, [verts[p] for p in polys], sides,
           os.path.join(HERE, "preview-3d.png"),
           os.path.join(HERE, "preview-closeup.png"), centre)
    render_flat(flat_polys, sides, flat_info,
                os.path.join(HERE, "preview-flat.png"))
    print("  wrote layout.json, preview-3d.png, preview-closeup.png, preview-flat.png")


if __name__ == "__main__":
    main()
