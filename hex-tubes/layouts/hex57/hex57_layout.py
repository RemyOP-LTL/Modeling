#!/usr/bin/env python3
"""
Layout B: hexagons with scattered pentagon/heptagon pairs, with size variety.

Runs outside Fusion (plain Python 3 with numpy, scipy and matplotlib).

    python3 hex57_layout.py

writes, next to this file:
    layout.json            corner coordinates + polygon list (inches)
    preview_3d.png         the skin on the test tube from three angles
    preview_closeup.png    a close-up
    preview_unrolled.png   the tube cut along one side and laid flat

How it works, in short (README.md has the long version):
  1. Build a test tube (about 12.5 in across, 6 ft long, gently bent, tapered ends)
     and sample it densely. Everything after this step only uses that dense sample
     (points + normals), which is what a Fusion mesh or T-spline would give us.
  2. Make a "size map": a smooth, blotchy map over the surface that says how big
     the shapes should be at each spot (3 to 7 in by default).
  3. Lay a honeycomb on the tube. Internally we store the honeycomb's "seed"
     points (one per shape) joined into triangles; each shape is the area around
     its seed. A seed with 6 neighbours is a hexagon, 5 a pentagon, 7 a heptagon.
  4. Adapt the seeds to the size map: split stretched links (adds a shape),
     merge squashed ones (removes a shape), swap links so every seed has 5-7
     neighbours, and let the seeds settle like springs. Where sizes change,
     pentagon/heptagon pairs appear on their own.
  5. Scatter a few extra pairs on purpose (one "edge rotation" each).
  6. Turn seeds into shapes: one shared corner per triangle. Relax the corners
     so each shape is close to a regular polygon.
  7. Measure, export, draw.
"""
import json
import math
import os
from collections import defaultdict

import numpy as np
from scipy.spatial import cKDTree
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.collections import PolyCollection
from matplotlib.patches import Patch
from mpl_toolkits.mplot3d.art3d import Poly3DCollection

HERE = os.path.dirname(os.path.abspath(__file__))

# ============================================================================
# Parameters you can tweak (all lengths in inches)
# ============================================================================
SEED = 7                    # change for a different random layout

# Shape size, measured "across" = diameter of a circle with the same area.
# (For a regular hexagon that is 5 % more than its flat-to-flat width.)
SIZE_MIN = 3.0
SIZE_MAX = 7.0
SIZE_MEAN = 5.0             # target average across all shapes
SIZE_PATCH = 12.0            # size of the patches of similar-sized shapes; bigger = smoother grading
SIZE_FOLLOWS_RADIUS = 0.7   # 0 = ignore how thin the tube is; 1 = shrink shapes in proportion
REFERENCE_RADIUS = 6.25     # tube radius at which shapes get their full size

EXTRA_PAIRS = 4             # pentagon/heptagon pairs added on purpose, on top of the ones size grading makes
REMESH_ROUNDS = 14          # rounds of split / merge / swap / settle
CORNER_RELAX_ROUNDS = 40    # rounds nudging each shape towards a regular polygon
CORNER_RELAX_STRENGTH = 0.5
SIZE_PULL = 0.5             # 0..1: how strongly each shape is pulled to the size map's size (heptagons
                            # otherwise come out large and pentagons small)
FLATTEN_ROUNDS = 20         # rounds letting corners lift off the surface so panels get flatter (0 = off)
FLATTEN_MAX_OFFSET = 0.15   # how far a corner may leave the surface while flattening

# Test tube
TUBE_LENGTH = 72.0
TUBE_RADIUS = 6.25          # 12.5 in across in the middle
BASE_TAPER = 0.75           # radius at the base end, as a fraction of TUBE_RADIUS
TIP_TAPER = 0.5             # radius at the tip end
TAPER_LENGTH = 0.18         # fraction of the length over which each end tapers
BEND = 6.0                  # how far the centreline bows sideways

# Colours by number of sides
COLOURS = {5: "#eb6834", 6: "#e4ddd0", 7: "#2a78d6", "end": "#a9d9c5", "other": "#e87ba4"}

HEX_EDGE_TO_ACROSS = 2 * math.sqrt(math.sqrt(3) / (2 * math.pi))   # 1.050: seed spacing -> shape across


# ============================================================================
# 1. Test surface
# ============================================================================
def smoothstep(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


class TestTube:
    """A bent, tapered tube. u in [0,1) goes around, t in [0,1] goes along.

    TUBE-SPECIFIC: this class is the only place that knows about "around x along".
    The layout itself only uses dense_sample() and rim(); the unrolled preview and
    the starting honeycomb also use point() and params().
    """

    def __init__(self):
        n = 2001
        t = np.linspace(0, 1, n)
        z = TUBE_LENGTH * t
        for _ in range(3):   # stretch z so the centreline is TUBE_LENGTH long
            c = np.stack([BEND * np.sin(np.pi * t) + 0.25 * BEND * np.sin(3 * np.pi * t),
                          0.5 * BEND * np.sin(2 * np.pi * t + 0.3), z], 1)
            length = np.linalg.norm(np.diff(c, axis=0), axis=1).sum()
            z = z * TUBE_LENGTH / length
        tang = np.gradient(c, axis=0)
        tang /= np.linalg.norm(tang, axis=1)[:, None]
        # Frame carried along the curve without twisting (parallel transport).
        nrm = np.zeros_like(c)
        v = np.cross(tang[0], [0.0, 1.0, 0.0])
        for i in range(n):
            v = v - (v @ tang[i]) * tang[i]
            v /= np.linalg.norm(v)
            nrm[i] = v
        self.t, self.c, self.N, self.B = t, c, nrm, np.cross(tang, nrm)
        self.s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(c, axis=0), axis=1))])

    def radius(self, t):
        f = 1 - (1 - BASE_TAPER) * smoothstep((TAPER_LENGTH - t) / TAPER_LENGTH) \
              - (1 - TIP_TAPER) * smoothstep((t - 1 + TAPER_LENGTH) / TAPER_LENGTH)
        return TUBE_RADIUS * f * (1 + 0.04 * np.sin(2 * np.pi * 1.3 * t + 1))

    def _interp(self, arr, t):
        return np.stack([np.interp(t, self.t, arr[:, k]) for k in range(3)], -1)

    def point(self, u, t):
        u, t = np.broadcast_arrays(np.asarray(u, float), np.asarray(t, float))
        a = 2 * np.pi * u
        r = self.radius(t)[..., None]
        return self._interp(self.c, t) + r * (np.cos(a)[..., None] * self._interp(self.N, t)
                                              + np.sin(a)[..., None] * self._interp(self.B, t))

    def dense_sample(self, nu=256, nt=720):
        u, t = np.meshgrid(np.arange(nu) / nu, np.linspace(0, 1, nt))
        P = self.point(u, t)
        du = self.point(u + 1e-4, t) - self.point(u - 1e-4, t)
        dt = self.point(u, np.clip(t + 1e-4, 0, 1)) - self.point(u, np.clip(t - 1e-4, 0, 1))
        n = np.cross(du, dt)
        n /= np.linalg.norm(n, axis=-1)[..., None]
        outward = P - self._interp(self.c, t)
        n *= np.sign(np.sum(n * outward, axis=-1))[..., None]
        return P.reshape(-1, 3), n.reshape(-1, 3), np.stack([u, t], -1).reshape(-1, 2)

    def rim(self, end, n=1440):
        return self.point(np.arange(n) / n, np.full(n, float(end)))

    def params(self, X, sample_uv, sample_tree):
        """(u, t) of points on the surface: nearest sample, then Gauss-Newton."""
        _, i = sample_tree.query(X)
        u, t = sample_uv[i, 0].copy(), sample_uv[i, 1].copy()
        h = 1e-5
        for _ in range(6):
            r = self.point(u, t) - X
            ju = (self.point(u + h, t) - self.point(u - h, t)) / (2 * h)
            jt = (self.point(u, t + h) - self.point(u, t - h)) / (2 * h)
            a, b, d = (ju * ju).sum(1), (ju * jt).sum(1), (jt * jt).sum(1)
            gu, gt = (ju * r).sum(1), (jt * r).sum(1)
            det = a * d - b * b
            u -= (d * gu - b * gt) / det
            t = np.clip(t - (a * gt - b * gu) / det, 0, 1)
        return u % 1.0, t


# ============================================================================
# 2. Generic surface helpers (work on any densely sampled surface or mesh)
# ============================================================================
class Surface:
    def __init__(self, P, N, rims):
        self.P, self.N = P, N
        self.tree = cKDTree(P)
        self.rims = rims                        # closed polylines along the open edges
        self.rim_trees = [cKDTree(r) for r in rims]
        self.local_radius = self._local_radius()

    def project(self, X):
        """Closest point on the surface and the normal there."""
        X = np.atleast_2d(X)
        for _ in range(2):
            d, i = self.tree.query(X, k=4)
            w = 1 / (d + 1e-6)
            n = (self.N[i] * w[..., None]).sum(1)
            n /= np.linalg.norm(n, axis=1)[:, None]
            p0 = self.P[i[:, 0]]
            X = X - ((X - p0) * n).sum(1)[:, None] * n
        return X, n

    def normal(self, X):
        return self.project(X)[1]

    def project_rim(self, X, which):
        X = np.atleast_2d(X)
        R = self.rims[which]
        _, i = self.rim_trees[which].query(X)
        best = R[i].copy()
        for j in (i - 1, i + 1):   # refine onto the two neighbouring segments
            a, b = R[i], R[j % len(R)]
            ab = b - a
            s = np.clip(((X - a) * ab).sum(1) / (ab * ab).sum(1), 0, 1)
            q = a + s[:, None] * ab
            closer = np.linalg.norm(q - X, axis=1) < np.linalg.norm(best - X, axis=1)
            best[closer] = q[closer]
        return best

    def _local_radius(self):
        """Smallest bending radius at each sample, from how fast the normal turns."""
        d, i = self.tree.query(self.P, k=13)
        diff = self.P[i[:, 1:]] - self.P[:, None]
        k = 2 * np.abs((diff * self.N[:, None]).sum(-1)) / np.maximum((diff ** 2).sum(-1), 1e-9)
        kmax = k.max(1)
        _, j = self.tree.query(self.P, k=60)
        return 1 / np.maximum(np.median(kmax[j], axis=1), 1e-6)


class SizeField:
    """How big shapes should be at each point: smooth blotches mapped to [SIZE_MIN, SIZE_MAX]."""

    def __init__(self, surf, area, rng):
        n_blobs = max(8, int(1.5 * area / SIZE_PATCH ** 2))
        self.centres = surf.P[rng.choice(len(surf.P), n_blobs)]
        self.amps = rng.normal(size=n_blobs)
        self.surf = surf
        sample = surf.P[rng.choice(len(surf.P), 20000)]
        self.sorted_noise = np.sort(self._noise(sample))
        # Skew the mapping so the average over shapes (not over area) is SIZE_MEAN.
        # Small shapes are more numerous per square inch, so area-average alone runs low.
        r = np.linspace(0, 1, 2001)

        def shape_mean(g):
            h = SIZE_MIN + (SIZE_MAX - SIZE_MIN) * r ** g
            return (h ** -1).mean() / (h ** -2).mean()
        lo, hi = 0.02, 50.0
        for _ in range(60):
            mid = math.sqrt(lo * hi)
            lo, hi = (mid, hi) if shape_mean(mid) > SIZE_MEAN else (lo, mid)
        self.gamma = mid

    def _noise(self, X):
        d2 = ((X[:, None, :] - self.centres[None]) ** 2).sum(-1)
        return (self.amps * np.exp(-d2 / (2 * (0.5 * SIZE_PATCH) ** 2))).sum(1)

    def across(self, X):
        X = np.atleast_2d(X)
        rank = np.interp(self._noise(X), self.sorted_noise, np.linspace(0, 1, len(self.sorted_noise)))
        h = SIZE_MIN + (SIZE_MAX - SIZE_MIN) * rank ** self.gamma
        _, i = self.surf.tree.query(X)
        factor = np.minimum(1.0, self.surf.local_radius[i] / REFERENCE_RADIUS) ** SIZE_FOLLOWS_RADIUS
        return np.clip(h * factor, SIZE_MIN, SIZE_MAX)

    def spacing(self, X):
        """Target distance between neighbouring seeds."""
        return self.across(X) / HEX_EDGE_TO_ACROSS


# ============================================================================
# 3. Seed triangulation (the "dual" of the shape pattern)
# ============================================================================
class SeedMesh:
    """Seeds (one per shape) joined into triangles, wound anticlockwise seen from outside.
    rim[v] is -1 for a free seed, or 0 / 1 for a seed fixed on that open end."""

    def __init__(self, V, T, rim, surf):
        self.V = [np.asarray(v, float) for v in V]
        self.T = [list(t) for t in T]
        self.rim = list(rim)
        self.surf = surf
        self.Nv = list(surf.normal(np.array(self.V)))
        self.rebuild()

    def rebuild(self):
        self.edge_tris = defaultdict(list)
        self.vtris = defaultdict(list)
        for ti, t in enumerate(self.T):
            if t is None:
                continue
            for k in range(3):
                a, b = t[k], t[(k + 1) % 3]
                self.edge_tris[(min(a, b), max(a, b))].append(ti)
                self.vtris[a].append(ti)
        self.nbrs = defaultdict(set)
        for a, b in self.edge_tris:
            self.nbrs[a].add(b)
            self.nbrs[b].add(a)

    def valence(self, v):
        return len(self.nbrs[v])

    def ideal(self, v):
        return 4 if self.rim[v] >= 0 else 6

    def add_vertex(self, p, rim):
        self.V.append(p)
        self.rim.append(rim)
        self.Nv.append(self.surf.normal(p)[0])
        return len(self.V) - 1

    def tri_ok(self, a, b, c, pos=None, min_angle=0.0):
        pos = pos or {}
        P = [pos.get(x, self.V[x]) for x in (a, b, c)]
        n = np.cross(P[1] - P[0], P[2] - P[0])
        ref = self.Nv[a] + self.Nv[b] + self.Nv[c]
        if n @ ref <= 0.3 * np.linalg.norm(n) * np.linalg.norm(ref):
            return False
        if min_angle > 0:
            for k in range(3):
                e1, e2 = P[(k + 1) % 3] - P[k], P[(k + 2) % 3] - P[k]
                cosang = e1 @ e2 / (np.linalg.norm(e1) * np.linalg.norm(e2))
                if math.degrees(math.acos(np.clip(cosang, -1, 1))) < min_angle:
                    return False
        return True

    def oriented(self, ti, a, b):
        """Rotate triangle ti so that it reads (p, q, r) with {p,q} = {a,b}."""
        t = self.T[ti]
        for k in range(3):
            p, q, r = t[k], t[(k + 1) % 3], t[(k + 2) % 3]
            if {p, q} == {a, b}:
                return p, q, r
        raise ValueError("edge not in triangle")

    # ---- the three topology operations ----
    def split(self, e):
        a, b = e
        tris = self.edge_tris[e]
        mid = (self.V[a] + self.V[b]) / 2
        if len(tris) == 1 and self.rim[a] >= 0 and self.rim[a] == self.rim[b]:
            m = self.add_vertex(self.surf.project_rim(mid, self.rim[a])[0], self.rim[a])
        else:
            m = self.add_vertex(self.surf.project(mid)[0][0], -1)
        for ti in tris:
            p, q, r = self.oriented(ti, a, b)
            self.T[ti] = [p, m, r]
            self.T.append([m, q, r])

    def try_collapse(self, e, size):
        a, b = e
        tris = self.edge_tris[e]
        boundary_edge = len(tris) == 1
        if self.rim[a] >= 0 and self.rim[b] >= 0:
            if not boundary_edge:
                return False
            keep, drop = a, b
            newpos = self.surf.project_rim((self.V[a] + self.V[b]) / 2, self.rim[a])[0]
        elif self.rim[a] >= 0 or self.rim[b] >= 0:
            keep, drop = (a, b) if self.rim[a] >= 0 else (b, a)
            newpos = self.V[keep]
        else:
            keep, drop = a, b
            newpos = self.surf.project((self.V[a] + self.V[b]) / 2)[0][0]
        opposite = {self.oriented(ti, a, b)[2] for ti in tris}
        if self.nbrs[a] & self.nbrs[b] != opposite:
            return False                                   # would pinch the surface
        limit = 1.33 * size.spacing(newpos)[0]
        pos = {keep: newpos, drop: newpos}
        new_tris = {}
        for ti in set(self.vtris[a]) | set(self.vtris[b]):
            if ti in tris:
                continue
            t = [keep if x == drop else x for x in self.T[ti]]
            if not self.tri_ok(*t, pos=pos):
                return False
            for x in t:
                if x != keep and np.linalg.norm(self.V[x] - newpos) > limit:
                    return False
            new_tris[ti] = t
        for ti in tris:
            self.T[ti] = None
        for ti, t in new_tris.items():
            self.T[ti] = t
        self.V[keep] = newpos
        self.Nv[keep] = self.surf.normal(newpos)[0]
        return True

    def flip_info(self, e):
        """Return (p, q, c, d) for an interior edge p-q with opposite seeds c, d, or None."""
        tris = self.edge_tris[e]
        if len(tris) != 2:
            return None
        p, q, c = self.oriented(tris[0], *e)
        d = self.oriented(tris[1], *e)[2]
        if c == d or d in self.nbrs[c]:
            return None
        return p, q, c, d, tris

    def flip(self, info):
        p, q, c, d, tris = info
        self.T[tris[0]] = [c, p, d]
        self.T[tris[1]] = [d, q, c]

    def compact(self):
        used = sorted({x for t in self.T if t is not None for x in t})
        remap = {v: i for i, v in enumerate(used)}
        self.V = [self.V[v] for v in used]
        self.Nv = [self.Nv[v] for v in used]
        self.rim = [self.rim[v] for v in used]
        self.T = [[remap[x] for x in t] for t in self.T if t is not None]
        self.rebuild()


def start_honeycomb(tube, surf, size):
    """TUBE-SPECIFIC: a regular honeycomb wrapped on the tube (seeds on a triangular grid)."""
    mean_spacing = SIZE_MEAN / HEX_EDGE_TO_ACROSS
    around = max(5, round(2 * math.pi * TUBE_RADIUS / mean_spacing))
    rows = max(2, round(TUBE_LENGTH / (mean_spacing * math.sqrt(3) / 2)))
    V, rim = [], []
    for j in range(rows + 1):
        u = (np.arange(around) + 0.5 * (j % 2)) / around
        V.extend(tube.point(u, np.full(around, j / rows)))
        rim.extend([0 if j == 0 else 1 if j == rows else -1] * around)
    idx = lambda i, j: j * around + i % around
    T = []
    for j in range(rows):
        for i in range(around):
            if j % 2 == 0:
                T += [[idx(i, j), idx(i + 1, j), idx(i, j + 1)], [idx(i, j + 1), idx(i + 1, j), idx(i + 1, j + 1)]]
            else:
                T += [[idx(i, j), idx(i + 1, j), idx(i + 1, j + 1)], [idx(i, j + 1), idx(i, j), idx(i + 1, j + 1)]]
    mesh = SeedMesh(V, T, rim, surf)
    for k, t in enumerate(mesh.T):               # wind every triangle anticlockwise from outside
        if not mesh.tri_ok(*t):
            mesh.T[k] = [t[0], t[2], t[1]]
    mesh.rebuild()
    return mesh


# ============================================================================
# 4. Adapting the seeds to the size map
# ============================================================================
def edge_arrays(mesh):
    E = np.array(list(mesh.edge_tris.keys()))
    V = np.array(mesh.V)
    L = np.linalg.norm(V[E[:, 0]] - V[E[:, 1]], axis=1)
    return E, V, L


def split_long(mesh, size):
    for _ in range(20):
        E, V, L = edge_arrays(mesh)
        ratio = L / size.spacing((V[E[:, 0]] + V[E[:, 1]]) / 2)
        touched, n = set(), 0
        for k in np.argsort(-ratio):
            if ratio[k] < 4 / 3:
                break
            e = tuple(E[k])
            tris = mesh.edge_tris[e]
            if touched & set(tris):
                continue
            touched |= set(tris)
            mesh.split(e)
            n += 1
        mesh.rebuild()
        if n == 0:
            return


def collapse_short(mesh, size):
    for _ in range(20):
        E, V, L = edge_arrays(mesh)
        ratio = L / size.spacing((V[E[:, 0]] + V[E[:, 1]]) / 2)
        touched, n = set(), 0
        for k in np.argsort(ratio):
            if ratio[k] > 0.8:
                break
            a, b = E[k]
            region = {a, b} | mesh.nbrs[a] | mesh.nbrs[b]
            if touched & region:
                continue
            if mesh.try_collapse((a, b), size):
                touched |= region
                n += 1
        mesh.rebuild()
        if n == 0:
            return


VALENCE_COST = [0.0, 1.0, 12.0, 40.0]   # 6 neighbours ideal; 5 or 7 fine; 4 or 8 bad
TOUCH_PENALTY_55 = 4.0                   # two pentagons sharing an edge
TOUCH_PENALTY_77 = 2.0                   # two heptagons sharing an edge


def pattern_energy(mesh, verts, val):
    """Low when every seed has its ideal neighbour count and pentagons never touch."""
    e = 0.0
    seen = set()
    for v in verts:
        e += VALENCE_COST[min(abs(val(v) - mesh.ideal(v)), 3)]
        for w in mesh.nbrs[v]:
            key = (min(v, w), max(v, w))
            if key in seen or mesh.rim[v] >= 0 or mesh.rim[w] >= 0:
                continue
            seen.add(key)
            if val(v) == 5 and val(w) == 5:
                e += TOUCH_PENALTY_55
            elif val(v) == 7 and val(w) == 7:
                e += TOUCH_PENALTY_77
    return e


def improve_valence(mesh, passes=10, min_angle=12.0):
    for _ in range(passes):
        n = 0
        touched = set()
        for e in list(mesh.edge_tris.keys()):
            if e[0] in touched or e[1] in touched:
                continue          # its triangles may have just changed
            info = mesh.flip_info(e)
            if info is None:
                continue
            p, q, c, d, _ = info
            region = {p, q, c, d} | mesh.nbrs[p] | mesh.nbrs[q] | mesh.nbrs[c] | mesh.nbrs[d]
            if touched & region:
                continue
            base = {v: mesh.valence(v) for v in region}
            after = dict(base)
            after[p] -= 1; after[q] -= 1; after[c] += 1; after[d] += 1
            # Neighbour sets change only for the edge itself; close enough for the penalty terms.
            delta = pattern_energy(mesh, region, after.get) - pattern_energy(mesh, region, base.get)
            if delta >= -1e-9:
                continue
            # Accept thin triangles when removing a 4- or 8-sided shape; settling fixes them.
            extreme = any(abs(base[v] - mesh.ideal(v)) >= 2 for v in (p, q, c, d))
            angle = 2.0 if extreme else min_angle
            if not (mesh.tri_ok(c, p, d, min_angle=angle) and mesh.tri_ok(d, q, c, min_angle=angle)):
                continue
            mesh.flip(info)
            touched |= region
            n += 1
        mesh.rebuild()
        if n == 0:
            return


def settle(mesh, size, rounds, step=0.3):
    """Seeds pull and push along their links like springs, staying on the surface."""
    E = np.array(list(mesh.edge_tris.keys()))
    T = np.array([t for t in mesh.T if t is not None])
    rim = np.array(mesh.rim)
    deg = np.maximum(np.bincount(E.ravel(), minlength=len(mesh.V)), 1)[:, None]
    for _ in range(rounds):
        V = np.array(mesh.V)
        N = np.array(mesh.Nv)
        d = V[E[:, 1]] - V[E[:, 0]]
        L = np.linalg.norm(d, axis=1)
        target = size.spacing((V[E[:, 0]] + V[E[:, 1]]) / 2)
        f = ((L - target) / L)[:, None] * d
        F = np.zeros_like(V)
        np.add.at(F, E[:, 0], f)
        np.add.at(F, E[:, 1], -f)
        move = step * F / deg
        move -= (move * N).sum(1)[:, None] * N
        new = V + move
        free = rim < 0
        new[free], nn = mesh.surf.project(new[free])
        for r in (0, 1):
            on = rim == r
            if on.any():
                new[on] = mesh.surf.project_rim(new[on], r)
        # Undo the move for any seed whose triangles would fold over.
        for _ in range(5):
            P0, P1, P2 = new[T[:, 0]], new[T[:, 1]], new[T[:, 2]]
            n = np.cross(P1 - P0, P2 - P0)
            ref = N[T].sum(1)
            bad = (n * ref).sum(1) <= 0.3 * np.linalg.norm(n, axis=1) * np.linalg.norm(ref, axis=1)
            if not bad.any():
                break
            undo = np.unique(T[bad])
            new[undo] = V[undo]
        mesh.V = list(new)
        Nnew = N.copy()
        Nnew[free] = nn
        mesh.Nv = list(Nnew)


def scatter_pairs(mesh, count, rng):
    """Rotate random links a quarter turn: two hexagons -> pentagons, two -> heptagons.
    Only where the whole neighbourhood (two rings) is plain hexagons, so pairs never touch."""
    made = 0
    edges = list(mesh.edge_tris.keys())
    rng.shuffle(edges)
    for clearance in (2, 1, 0):
        for e in edges:
            if made >= count:
                return made
            info = mesh.flip_info(e)
            if info is None:
                continue
            p, q, c, d, _ = info
            ring = {p, q, c, d}
            for _ in range(clearance):
                ring = ring | set().union(*(mesh.nbrs[v] for v in ring))
            if any(mesh.rim[v] >= 0 or mesh.valence(v) != 6 for v in ring):
                continue
            # Never let the new pentagons (p, q) touch an existing pentagon.
            if any(mesh.rim[w] < 0 and mesh.valence(w) == 5 for w in mesh.nbrs[p] | mesh.nbrs[q]):
                continue
            if not (mesh.tri_ok(c, p, d, min_angle=20) and mesh.tri_ok(d, q, c, min_angle=20)):
                continue
            mesh.flip(info)
            mesh.rebuild()
            made += 1
    return made


# ============================================================================
# 5. From seeds to shapes (corners + polygons)
# ============================================================================
def build_cells(mesh):
    """One corner per triangle (at its centre); one polygon per seed.
    At the open ends, the cut shapes also get corners on the rim."""
    surf = mesh.surf
    T = np.array(mesh.T)
    V = np.array(mesh.V)
    corners = list(surf.project(V[T].mean(1))[0])
    fixed = [False] * len(corners)
    rim_corner = {}

    def rim_mid(a, b):
        key = (min(a, b), max(a, b))
        if key not in rim_corner:
            rim_corner[key] = len(corners)
            corners.append(surf.project_rim((V[a] + V[b]) / 2, mesh.rim[a])[0])
            fixed.append(True)
        return rim_corner[key]

    polys, is_end, seed_of = [], [], []
    for v in range(len(V)):
        fan = {}
        for ti in mesh.vtris[v]:
            t = mesh.T[ti]
            k = t.index(v)
            fan[t[(k + 1) % 3]] = (t[(k + 2) % 3], ti)
        if mesh.rim[v] < 0:
            start = next(iter(fan))
        else:
            ends = set(y for y, _ in fan.values())
            start = next(x for x in fan if x not in ends)
        x, poly = start, []
        while True:
            y, ti = fan[x]
            poly.append(ti)
            x = y
            if x == start or x not in fan:
                break
        if mesh.rim[v] >= 0:
            m1, m2 = rim_mid(v, start), rim_mid(v, x)
            corners.append(V[v])
            fixed.append(True)
            poly = [m1] + poly + [m2, len(corners) - 1]
        polys.append(poly)
        is_end.append(mesh.rim[v] >= 0)
        seed_of.append(v)
    return np.array(corners), np.array(fixed), polys, np.array(is_end)


def newell(P):
    n = np.zeros(3)
    for i in range(len(P)):
        a, b = P[i], P[(i + 1) % len(P)]
        n += np.cross(a, b)
    return n


def plane_fit(P):
    o = P.mean(0)
    _, _, vt = np.linalg.svd(P - o)
    n = vt[2]
    if n @ newell(P) < 0:
        n = -n
    return o, n


def regularise_corners(C, fixed, polys, is_end, surf, size, rounds, alpha):
    """Pull each shape towards a regular polygon (of a size between its own and the
    size map's), sharing the pull at every corner between the shapes that meet there."""
    for _ in range(rounds):
        acc = np.zeros_like(C)
        cnt = np.zeros(len(C))
        for poly, end in zip(polys, is_end):
            if end:
                continue
            P = C[poly]
            k = len(poly)
            o = P.mean(0)
            n = newell(P)
            n /= np.linalg.norm(n)
            e1 = P[0] - o
            e1 -= (e1 @ n) * n
            e1 /= np.linalg.norm(e1)
            e2 = np.cross(n, e1)
            z = (P - o) @ e1 + 1j * ((P - o) @ e2)
            steps = np.exp(2j * np.pi * np.arange(k) / k)
            rot = (z / steps).sum()
            rot /= abs(rot)
            want = size.across(o)[0]
            r_want = want * math.sqrt(math.pi / (2 * k * math.sin(2 * math.pi / k)))
            r = (1 - SIZE_PULL) * np.abs(z).mean() + SIZE_PULL * r_want
            w = r * rot * steps
            target = o + np.outer(w.real, e1) + np.outer(w.imag, e2)
            np.add.at(acc, poly, target)
            np.add.at(cnt, poly, 1)
        move = (cnt > 0) & ~fixed
        new = C.copy()
        new[move] = (1 - alpha) * C[move] + alpha * acc[move] / cnt[move][:, None]
        new[move] = surf.project(new[move])[0]
        C = new
    return C


def flatten_corners(C, fixed, polys, surf, rounds, max_offset):
    """Let corners lift slightly off the surface so each panel gets closer to flat."""
    if rounds <= 0:
        return C
    on_surface = C.copy()
    for _ in range(rounds):
        acc = np.zeros_like(C)
        cnt = np.zeros(len(C))
        for poly in polys:
            P = C[poly]
            o, n = plane_fit(P)
            np.add.at(acc, poly, P - np.outer((P - o) @ n, n))
            np.add.at(cnt, poly, 1)
        move = ~fixed
        C = C.copy()
        C[move] = 0.5 * C[move] + 0.5 * acc[move] / cnt[move][:, None]
        off = C - on_surface
        dist = np.linalg.norm(off, axis=1)
        too_far = dist > max_offset
        C[too_far] = on_surface[too_far] + off[too_far] * (max_offset / dist[too_far])[:, None]
    return C


# ============================================================================
# 6. Metrics
# ============================================================================
def measure(C, polys, is_end, surf):
    info = []
    for poly, end in zip(polys, is_end):
        P = C[poly]
        o, n = plane_fit(P)
        area = np.linalg.norm(newell(P)) / 2
        # convex? (every turn the same way in the panel's own plane)
        e = np.roll(P, -1, 0) - P
        turns = np.cross(e, np.roll(e, -1, 0)) @ n
        info.append(dict(sides=len(poly), end=bool(end), area=area,
                         across=2 * math.sqrt(area / math.pi),
                         flatness=float(np.abs((P - o) @ n).max()),
                         min_edge=float(np.linalg.norm(e, axis=1).min()),
                         convex=bool((turns > 0).all()), centre=o, normal=n))
    inner = [d for d in info if not d["end"]]
    counts = defaultdict(int)
    for d in inner:
        counts[d["sides"]] += 1
    across = np.array([d["across"] for d in inner])
    flat = np.array([d["flatness"] for d in info])
    # pentagons touching? (two pentagons sharing an edge)
    edge_owner = defaultdict(list)
    for fi, poly in enumerate(polys):
        for k in range(len(poly)):
            a, b = poly[k], poly[(k + 1) % len(poly)]
            edge_owner[(min(a, b), max(a, b))].append(fi)
    touching = sum(1 for fs in edge_owner.values()
                   if len(fs) == 2 and all(info[f]["sides"] == 5 and not info[f]["end"] for f in fs))
    off = np.linalg.norm(surf.project(C)[0] - C, axis=1)
    m = dict(sides={int(k): v for k, v in sorted(counts.items())},
             interior_shapes=len(inner), end_shapes=len(info) - len(inner),
             across_min=float(across.min()), across_mean=float(across.mean()), across_max=float(across.max()),
             flatness_max=float(flat.max()), flatness_mean=float(flat.mean()),
             min_edge=float(min(d["min_edge"] for d in inner)),
             min_edge_end_shapes=float(min(d["min_edge"] for d in info if d["end"])),
             non_convex=sum(1 for d in inner if not d["convex"]),
             pentagons_touching=touching,
             corner_off_surface_max=float(off.max()))
    return info, m


# ============================================================================
# 7. Pictures
# ============================================================================
def face_colour(d):
    if d["end"]:
        return COLOURS["end"]
    return COLOURS.get(d["sides"], COLOURS["other"])


def legend_handles(metrics):
    s = metrics["sides"]
    h = [Patch(facecolor=COLOURS[5], edgecolor="#333", label=f"pentagon ({s.get(5, 0)})"),
         Patch(facecolor=COLOURS[6], edgecolor="#333", label=f"hexagon ({s.get(6, 0)})"),
         Patch(facecolor=COLOURS[7], edgecolor="#333", label=f"heptagon ({s.get(7, 0)})"),
         Patch(facecolor=COLOURS["end"], edgecolor="#333", label=f"cut at tube end ({metrics['end_shapes']})")]
    other = sum(v for k, v in s.items() if k not in (5, 6, 7))
    if other:
        h.append(Patch(facecolor=COLOURS["other"], edgecolor="#333", label=f"other ({other})"))
    return h


def shaded(hexcol, shade):
    rgb = np.array(matplotlib.colors.to_rgb(hexcol))
    return tuple(np.clip(rgb * shade, 0, 1))


def draw_3d(ax, C, polys, info, elev, azim, lo, hi, zoom=1.0):
    """Draw the panels that face the viewer, inside the box lo..hi, with simple shading."""
    view = np.array([math.cos(math.radians(elev)) * math.cos(math.radians(azim)),
                     math.cos(math.radians(elev)) * math.sin(math.radians(azim)),
                     math.sin(math.radians(elev))])
    light = view + np.array([0.3, -0.4, 0.6])
    light /= np.linalg.norm(light)
    faces, cols = [], []
    for poly, d in zip(polys, info):
        if d["normal"] @ view <= 0:      # skip the far side of the tube
            continue
        P = C[poly]
        if np.any(P.max(0) < lo) or np.any(P.min(0) > hi):
            continue
        faces.append(P)
        cols.append(shaded(face_colour(d), 0.55 + 0.45 * max(0.0, d["normal"] @ light)))
    coll = Poly3DCollection(faces, facecolors=cols, edgecolors="#2b2b2b", linewidths=0.6)
    coll.set_clip_on(False)   # with zoom > 1, matplotlib would otherwise crop the ends
    ax.add_collection3d(coll)
    ax.set_xlim(lo[0], hi[0])
    ax.set_ylim(lo[1], hi[1])
    ax.set_zlim(lo[2], hi[2])
    ax.set_box_aspect(tuple(hi - lo), zoom=zoom)
    ax.set_proj_type("ortho")
    ax.view_init(elev, azim)
    ax.set_axis_off()


def tube_axes_view(C):
    """Rotate so the tube's long direction is z (makes nicer 3D framing)."""
    o = C.mean(0)
    _, _, vt = np.linalg.svd(C - o)
    R = np.array([vt[1], vt[2], vt[0]])   # long axis -> z
    if np.linalg.det(R) < 0:
        R[0] = -R[0]
    return (C - o) @ R.T, R


def render(C, polys, info, metrics, tube, surf, sample_uv, out):
    Cv, R = tube_axes_view(C)
    info_v = [dict(d, normal=R @ d["normal"]) for d in info]
    # Lay the tube horizontally across the picture: long axis = z, view from the side.
    Cv = Cv[:, [2, 0, 1]]
    info_v = [dict(d, normal=d["normal"][[2, 0, 1]]) for d in info_v]
    lo, hi = Cv.min(0) - 0.5, Cv.max(0) + 0.5
    # Make the box tall enough in every direction that a tilted view isn't clipped.
    pad = 0.5 * (hi[0] - lo[0]) - 0.5 * (hi[1:] - lo[1:])
    lo[1:] -= pad * 0.25
    hi[1:] += pad * 0.25

    fig = plt.figure(figsize=(16, 13))
    for k, (elev, azim, title) in enumerate([(5, -90, "side"), (30, -70, "three-quarter, from above"),
                                            (-30, -110, "three-quarter, from below")]):
        ax = fig.add_axes([0, 0.68 - 0.31 * k, 1, 0.29], projection="3d")
        draw_3d(ax, Cv, polys, info_v, elev, azim, lo, hi, zoom=1.9)
        ax.set_title(title, fontsize=12, color="#444", y=0.92)
    fig.legend(handles=legend_handles(metrics), loc="lower center", ncol=5, frameon=False, fontsize=11)
    fig.suptitle("Layout B: hexagons with pentagon/heptagon pairs on the 12.5 in x 6 ft test tube",
                 fontsize=14)
    fig.savefig(os.path.join(out, "preview_3d.png"), dpi=110)
    plt.close(fig)

    # Close-up of the middle of the tube, where size grading is visible.
    fig = plt.figure(figsize=(10, 9))
    ax = fig.add_axes([0, 0.06, 1, 0.88], projection="3d")
    mid = 0.5 * (lo + hi)
    box = np.array([15.0, 15.0, 12.0])
    draw_3d(ax, Cv, polys, info_v, 15, -80, mid - box, mid + box, zoom=1.5)
    ax.set_title("Close-up, middle of the tube (about 30 in long)", fontsize=13)
    fig.legend(handles=legend_handles(metrics), loc="lower center", ncol=3, frameon=False, fontsize=11)
    fig.savefig(os.path.join(out, "preview_closeup.png"), dpi=110)
    plt.close(fig)

    # Unrolled: x = distance around (at the local radius), y = distance along.
    # TUBE-SPECIFIC: uses the tube's (u, t) to cut it open along one line.
    u, t = tube.params(C, sample_uv, surf.tree)
    circ = 2 * np.pi * tube.radius(t)
    along = np.interp(t, tube.t, tube.s)
    fig, ax = plt.subplots(figsize=(9, 15))
    polys2d, cols = [], []
    for poly, d in zip(polys, info):
        uu = u[poly].copy()
        if uu.max() - uu.min() > 0.5:
            uu[uu < 0.5] += 1
        for shift in ((0, -1) if uu.max() > 1 else (0,)):
            x = (uu + shift - 0.5) * circ[poly]
            polys2d.append(np.stack([x, along[poly]], 1))
            cols.append(face_colour(d))
    ts = np.linspace(0, 1, 200)
    for sign in (-1, 1):
        ax.plot(sign * np.pi * tube.radius(ts), np.interp(ts, tube.t, tube.s), "--", color="#666", lw=1)
    ax.add_collection(PolyCollection(polys2d, facecolors=cols, edgecolors="#2b2b2b", linewidths=0.6))
    ax.set_xlim(-0.5 * circ.max() - 1, 0.5 * circ.max() + 1)
    ax.set_ylim(along.min() - 1, along.max() + 1)
    ax.set_aspect("equal")
    ax.set_xlabel("around the tube (in), cut open along one side")
    ax.set_ylabel("along the tube (in)")
    ax.set_title("Unrolled view (each row drawn at its own circumference;\n"
                 "dashed = cut line, shapes crossing it are drawn on both sides)")
    ax.legend(handles=legend_handles(metrics), loc="upper center", bbox_to_anchor=(0.5, -0.05),
              ncol=2, frameon=False)
    for side in ("top", "right"):
        ax.spines[side].set_visible(False)
    fig.tight_layout()
    fig.savefig(os.path.join(out, "preview_unrolled.png"), dpi=110)
    plt.close(fig)
    return u, along, circ


# ============================================================================
# Main
# ============================================================================
def main():
    rng = np.random.default_rng(SEED)
    tube = TestTube()
    P, N, uv = tube.dense_sample()
    surf = Surface(P, N, [tube.rim(0), tube.rim(1)])
    area = float(np.mean(2 * np.pi * tube.radius(np.linspace(0, 1, 500))) * TUBE_LENGTH)
    size = SizeField(surf, area, rng)

    mesh = start_honeycomb(tube, surf, size)
    print(f"start: {len(mesh.V)} seeds in a plain honeycomb")
    for r in range(REMESH_ROUNDS):
        split_long(mesh, size)
        collapse_short(mesh, size)
        improve_valence(mesh)
        settle(mesh, size, 8)
    mesh.compact()
    for _ in range(6):     # polish: fewer, better-spread pentagon/heptagon pairs
        improve_valence(mesh, passes=20, min_angle=8)
        settle(mesh, size, 15)
    made = scatter_pairs(mesh, EXTRA_PAIRS, rng)
    settle(mesh, size, 40, step=0.2)
    print(f"adapted: {len(mesh.V)} seeds, {made} extra pentagon/heptagon pairs scattered")

    C, fixed, polys, is_end = build_cells(mesh)
    C = regularise_corners(C, fixed, polys, is_end, surf, size, CORNER_RELAX_ROUNDS, CORNER_RELAX_STRENGTH)
    _, m_surface = measure(C, polys, is_end, surf)
    C = flatten_corners(C, fixed, polys, surf, FLATTEN_ROUNDS, FLATTEN_MAX_OFFSET)
    info, m = measure(C, polys, is_end, surf)
    m["flatness_on_surface_max"] = m_surface["flatness_max"]
    m["flatness_on_surface_mean"] = m_surface["flatness_mean"]

    u, along, circ = render(C, polys, info, m, tube, surf, uv, HERE)

    corner_cells = defaultdict(list)
    for fi, poly in enumerate(polys):
        for c in poly:
            corner_cells[c].append(fi)
    layout = dict(
        units="inch",
        description="Layout B (hex57): hexagons with scattered pentagon/heptagon pairs on a test tube. "
                    "Polygons list corner indices anticlockwise as seen from outside the tube. "
                    "Every corner is shared exactly by the shapes that meet there.",
        params=dict(SEED=SEED, SIZE_MIN=SIZE_MIN, SIZE_MAX=SIZE_MAX, SIZE_MEAN=SIZE_MEAN,
                    SIZE_PATCH=SIZE_PATCH, SIZE_FOLLOWS_RADIUS=SIZE_FOLLOWS_RADIUS,
                    EXTRA_PAIRS=EXTRA_PAIRS, FLATTEN_ROUNDS=FLATTEN_ROUNDS,
                    FLATTEN_MAX_OFFSET=FLATTEN_MAX_OFFSET, TUBE_LENGTH=TUBE_LENGTH,
                    TUBE_RADIUS=TUBE_RADIUS),
        vertices=np.round(C, 4).tolist(),
        vertex_on_rim=[bool(f) for f in fixed],
        vertex_unrolled=np.round(np.stack([(u - 0.5) * circ, along], 1), 4).tolist(),
        vertex_shapes=[corner_cells[c] for c in range(len(C))],
        polygons=[[int(x) for x in p] for p in polys],
        polygon_info=[dict(sides=d["sides"], end=d["end"], area=round(d["area"], 3),
                           across=round(d["across"], 3), flatness=round(d["flatness"], 4),
                           centre=np.round(d["centre"], 4).tolist(),
                           normal=np.round(d["normal"], 4).tolist()) for d in info],
        metrics=m,
    )
    with open(os.path.join(HERE, "layout.json"), "w") as f:
        json.dump(layout, f, indent=1)

    s = m["sides"]
    print()
    print("Shapes (not counting the ones cut by the tube ends):")
    for k in sorted(s):
        name = {4: "4 sides", 5: "pentagons", 6: "hexagons", 7: "heptagons", 8: "8 sides"}.get(k, f"{k} sides")
        print(f"  {name:10s} {s[k]:4d}")
    print(f"  cut at the ends: {m['end_shapes']}")
    print(f"Size across (in):   min {m['across_min']:.2f}   average {m['across_mean']:.2f}   max {m['across_max']:.2f}")
    print(f"Out of flat (in):   max {m['flatness_max']:.3f}   average {m['flatness_mean']:.3f}"
          f"   (corners on the surface: max {m['flatness_on_surface_max']:.3f}, average {m['flatness_on_surface_mean']:.3f})")
    print(f"Corners lifted off the surface by up to {m['corner_off_surface_max']:.3f} in")
    print(f"Shortest edge: {m['min_edge']:.2f} in (cut end shapes: {m['min_edge_end_shapes']:.2f} in)   non-convex shapes: {m['non_convex']}"
          f"   pentagons touching: {m['pentagons_touching']}")
    print(f"Wrote layout.json and preview_*.png to {HERE}")


if __name__ == "__main__":
    main()
