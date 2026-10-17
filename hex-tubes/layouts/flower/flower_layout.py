#!/usr/bin/env python3
"""
Layout C: pentagon "flower" clusters on a tube.

What it does, in order:
  1. Builds a test surface as a triangle mesh: a gently curving tube, about
     12.5 in across and 6 ft long, tapering at both ends.
  2. Scatters flower seeds evenly but organically (Poisson-disk sampling, which
     means "random, but no two seeds closer than a minimum distance").
     Each flower gets its own size.
  3. At each seed it places a flower: one centre pentagon plus a ring of five
     pentagons around it. Connecting shapes ("fillers") are seeded in the space
     left between the flowers.
  4. Every seed "grows" outwards until it meets its neighbours (a Voronoi
     pattern measured on the surface itself). Where three regions meet is a
     corner, so every corner is shared exactly by the panels around it.
  5. Small fixes so every flower panel really has 5 sides.
  6. Two variants are relaxed into shape on the surface:
       closed - the thin gaps between the ring pentagons are zipped shut, so
                the pentagons must bend and bump to cover the tube;
       open   - those gaps are left open as diamond-shaped cutouts.
  7. Prints metrics, writes layout.json and the preview images next to this file.

Works on any surface given as a triangle mesh (vertices, faces, normals). Only
the "unrolled" preview uses tube-specific coordinates.

Run:  python3 flower_layout.py
Needs: numpy, scipy, matplotlib (not Fusion's built-in Python).
"""
import json
import math
import os

import numpy as np
from scipy.spatial import cKDTree
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.collections import PolyCollection
from matplotlib.patches import Patch

# ----------------------------------------------------------------------------
# Parameters you can tweak (all lengths in inches)
# ----------------------------------------------------------------------------
# "Across" means the width of a circle with the same area as the shape.
# For a regular pentagon that is about 7 % more than its flat-to-flat width.
SIZE_MIN = 3.0            # smallest flower pentagon, across
SIZE_MAX = 7.0            # largest flower pentagon, across
FLOWER_SPACING = 3.0      # min distance between flower centres, x their average size
FILLER_SPACING = 1.0      # min distance between filler seeds, x their average size
FILLER_SIZE_SCALE = 0.8   # fillers' size relative to nearby flowers
FILLER_SIZE_JITTER = 0.15 # +/- random variation of filler size (0.15 = 15 %)
NOTCH_RADIUS = 0.0        # >0 adds 5 fillers in the flower's notches, at this many ring distances
KEEP_OUT = 1.4            # no other filler seeds within this many ring distances of a flower centre
MAX_FILLER_SIDES = 8      # fillers with more sides get an edge flipped to a neighbour
POWER_ALPHA = 0.3         # how strongly big seeds push their borders outwards
RANDOM_SEED = 11          # change for a different random layout

# Relaxation: how strongly each wish is weighted (relative to each other)
W_PENTAGON_EDGE = 3.0     # flower pentagon edges keep their cut length (plastic doesn't stretch)
W_PENTAGON = 1.0          # flower pentagons keep their shape (diagonals; bending is allowed)
W_FILLER_EDGE = 0.3       # fillers keep their edge lengths
W_FILLER_ANGLE = 0.1      # fillers keep roughly even angles
W_PLANAR = 0.5            # every panel tries to be flat
W_SURFACE = 0.3           # every corner tries to sit on the surface
W_GAP = 0.3               # open gaps stay diamond-shaped
ITERATIONS = 3000

# Test tube
TUBE_LENGTH = 72.0
TUBE_DIAMETER = 12.5
MESH_STEP = 0.25          # triangle size of the test mesh

OUT_DIR = os.path.dirname(os.path.abspath(__file__))
PHI = (1 + 5 ** 0.5) / 2


def regular_edge(across, n):
    """Edge length of a regular n-gon whose area equals a circle 'across' wide."""
    return across * math.sqrt(math.pi * math.tan(math.pi / n) / n)


# ----------------------------------------------------------------------------
# 1. Test surface (a triangle mesh)
# ----------------------------------------------------------------------------
class Surface:
    """A triangle mesh with the few queries the layout needs."""

    def __init__(self, V, F, boundary):
        self.V, self.F = V, F
        fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]])
        N = np.zeros_like(V)
        for k in range(3):
            np.add.at(N, F[:, k], fn)
        self.N = N / np.linalg.norm(N, axis=1, keepdims=True)
        self.tree = cKDTree(V)
        self.boundary = boundary
        self.btree = cKDTree(V[boundary]) if boundary.any() else None

    def project(self, P):
        """Closest point on the surface, its normal and the signed distance
        (positive = outside)."""
        P = np.atleast_2d(P)
        _, idx = self.tree.query(P)
        n = self.N[idx]
        d = np.einsum("ij,ij->i", P - self.V[idx], n)
        return P - d[:, None] * n, n, d, idx

    def walk(self, p, direction, dist, step=0.25):
        """Walk 'dist' along the surface from p, starting in 'direction'."""
        d = np.asarray(direction, float)
        travelled = 0.0
        while travelled < dist - 1e-9:
            h = min(step, dist - travelled)
            q, n, _, _ = self.project(p + h * d)
            q, n = q[0], n[0]
            new_d = q - p
            new_d -= (new_d @ n) * n
            if np.linalg.norm(new_d) > 1e-9:
                d = new_d / np.linalg.norm(new_d)
            p = q
            travelled += h
        return p


def smoothstep(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def make_test_tube():
    """Gently curving tube with tapered ends. Returns the Surface plus the
    centreline data the unrolled preview uses."""
    R0 = TUBE_DIAMETER / 2
    nt = int(round(TUBE_LENGTH / MESH_STEP)) + 1
    na = int(round(2 * math.pi * R0 / MESH_STEP))
    t = np.linspace(0, 1, nt)
    zscale = TUBE_LENGTH
    for _ in range(5):  # scale z so the centreline is TUBE_LENGTH long
        C = np.stack([7.0 * np.sin(1.1 * np.pi * t),
                      3.5 * np.sin(2 * np.pi * t + 0.5),
                      zscale * t], 1)
        L = np.linalg.norm(np.diff(C, axis=0), axis=1).sum()
        zscale *= TUBE_LENGTH / L
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(C, axis=0), axis=1))])
    T = np.gradient(C, axis=0)
    T /= np.linalg.norm(T, axis=1, keepdims=True)
    # parallel-transport frame (no twisting along the tube)
    Nf = np.zeros_like(C)
    n0 = np.cross(T[0], [0, 1, 0])
    Nf[0] = n0 / np.linalg.norm(n0)
    for i in range(1, nt):
        v = Nf[i - 1] - (Nf[i - 1] @ T[i]) * T[i]
        Nf[i] = v / np.linalg.norm(v)
    B = np.cross(T, Nf)
    r = R0 * (1 - 0.30 * smoothstep(1 - t / 0.22)
              - 0.38 * smoothstep(1 - (1 - t) / 0.28)
              + 0.02 * np.sin(9 * t))
    th = np.linspace(-math.pi, math.pi, na, endpoint=False)
    V = (C[:, None, :] + r[:, None, None] *
         (np.cos(th)[None, :, None] * Nf[:, None, :] +
          np.sin(th)[None, :, None] * B[:, None, :])).reshape(-1, 3)
    idx = np.arange(nt * na).reshape(nt, na)
    a, b = idx[:-1, :], idx[:-1, np.r_[1:na, 0]]
    c, d = idx[1:, np.r_[1:na, 0]], idx[1:, :]
    F = np.concatenate([np.stack([a, b, c], -1).reshape(-1, 3),
                        np.stack([a, c, d], -1).reshape(-1, 3)])
    # make faces point outwards
    f0 = F[0]
    fn = np.cross(V[f0[1]] - V[f0[0]], V[f0[2]] - V[f0[0]])
    if fn @ (V[f0].mean(0) - C[0]) < 0:
        F = F[:, ::-1]
    boundary = np.zeros(nt * na, bool)
    boundary[idx[0]] = True
    boundary[idx[-1]] = True
    tube = dict(C=C, T=T, N=Nf, B=B, s=s, r=r, ring_of_vertex=np.repeat(np.arange(nt), na))
    return Surface(V, F, boundary), tube


def unroll(points, tube, surf):
    """Flat coordinates (along, around) for points near the test tube."""
    _, _, _, idx = surf.project(points)
    i = tube["ring_of_vertex"][idx]
    rel = points - tube["C"][i]
    theta = np.arctan2(np.einsum("ij,ij->i", rel, tube["B"][i]),
                       np.einsum("ij,ij->i", rel, tube["N"][i]))
    along = tube["s"][i] + np.einsum("ij,ij->i", rel, tube["T"][i])
    return along, theta, tube["r"][i]


# ----------------------------------------------------------------------------
# 2-3. Seeds: flowers first, then fillers
# ----------------------------------------------------------------------------
def tangent_frame(n, rng):
    a = rng.normal(size=3)
    u = np.cross(n, a)
    u /= np.linalg.norm(u)
    return u, np.cross(n, u)


def place_seeds(surf, rng):
    V = surf.V
    bdist = surf.btree.query(V)[0]
    flowers = []
    sites, size, role, fid, ringk = [], [], [], [], []

    def add(p, D, r, f, k):
        sites.append(p); size.append(D); role.append(r); fid.append(f); ringk.append(k)

    # Sizes spread evenly over the range, tried big, small, big, small ... so
    # large flowers still find room and small ones fill in between.
    # Each size gets up to 2000 random tries to find a free spot.
    n = 10
    order = []
    for _ in range(4):
        sizes = SIZE_MIN + (SIZE_MAX - SIZE_MIN) * (np.arange(n) + rng.uniform(0, 1, n)) / n
        sizes = sorted(sizes, reverse=True)
        order += [sizes[i // 2] if i % 2 == 0 else sizes[-1 - i // 2] for i in range(n)]
    for D in order:
        ring = regular_edge(D, 5) / math.tan(math.pi / 5)  # centre to centre
        for vi in rng.choice(len(V), 2000):
            if bdist[vi] < 2.0 * ring:  # whole flower stays on the tube
                continue
            p = V[vi]
            if any(np.linalg.norm(p - f["p"]) < FLOWER_SPACING * (D + f["D"]) / 2
                   for f in flowers):
                continue
            break
        else:
            continue
        f = len(flowers)
        flowers.append(dict(p=p, D=D, ring=ring))
        u, w = tangent_frame(surf.N[vi], rng)
        phi0 = rng.uniform(0, 2 * math.pi)
        add(p, D, "centre", f, -1)
        for k in range(5):
            a = phi0 + 2 * math.pi * k / 5
            add(surf.walk(p, math.cos(a) * u + math.sin(a) * w, ring), D, "ring", f, k)
        for k in range(5 if NOTCH_RADIUS > 0 else 0):
            a = phi0 + 2 * math.pi * (k + 0.5) / 5
            add(surf.walk(p, math.cos(a) * u + math.sin(a) * w, NOTCH_RADIUS * ring),
                D * FILLER_SIZE_SCALE, "notch", f, k)

    # smooth size field for the fillers: blend of nearby flower sizes
    fp = np.array([f["p"] for f in flowers])
    fD = np.array([f["D"] for f in flowers])

    def size_at(p):
        d2 = ((fp - p) ** 2).sum(1)
        w = 1 / (d2 + 25.0)
        return FILLER_SIZE_SCALE * (w @ fD) / w.sum()

    S = np.array(sites)
    Dn = np.array(size)
    keep_out = [(f["p"], KEEP_OUT * f["ring"]) for f in flowers]
    for vi in rng.permutation(len(V)):
        p = V[vi]
        D = size_at(p) * rng.uniform(1 - FILLER_SIZE_JITTER, 1 + FILLER_SIZE_JITTER)
        if any(np.linalg.norm(p - c) < r for c, r in keep_out):
            continue
        d = np.linalg.norm(S - p, axis=1)
        if np.any(d < FILLER_SPACING * (D + Dn) / 2):
            continue
        S = np.vstack([S, p]); Dn = np.append(Dn, D)
        add(p, D, "filler", -1, -1)
    return flowers, np.array(sites), np.array(size), role, np.array(fid), np.array(ringk)


# ----------------------------------------------------------------------------
# 4. Grow regions from the seeds and read off the shared-corner pattern
# ----------------------------------------------------------------------------
def label_surface(surf, sites, size):
    """Each mesh vertex belongs to the seed that is 'closest', where big seeds
    count as a bit closer (a power diagram)."""
    tree = cKDTree(sites)
    d, idx = tree.query(surf.V, k=min(12, len(sites)))
    power = d ** 2 - POWER_ALPHA * (size[idx] / 2) ** 2
    return idx[np.arange(len(surf.V)), power.argmin(1)]


def extract_polygons(surf, lab, nsites):
    """Trace each region's border on the mesh. A triangle touching three
    regions is a corner; a mesh-boundary edge between two regions is a
    boundary corner. Corners are shared by construction."""
    V, F = surf.V, surf.F
    L = lab[F]
    mixed = np.where(~((L[:, 0] == L[:, 1]) & (L[:, 1] == L[:, 2])))[0]
    edge_tris = {}
    for t in mixed:
        f = F[t]
        for k in range(3):
            e = tuple(sorted((f[k], f[(k + 1) % 3])))
            edge_tris.setdefault(e, []).append(t)
    # an edge only listed once here may still have a non-mixed neighbour;
    # it's a true mesh-boundary edge only if both ends are boundary vertices
    links = {}  # (label, tri) -> [edge, edge]
    for t in mixed:
        f = F[t]
        for l in set(lab[f]):
            es = []
            for k in range(3):
                a, b = f[k], f[(k + 1) % 3]
                if (lab[a] == l) != (lab[b] == l):
                    es.append(tuple(sorted((a, b))))
            links[(l, t)] = es
    per_label = {}
    for (l, t) in links:
        per_label.setdefault(l, []).append(t)

    def other_tri(e, t):
        ts = [x for x in edge_tris.get(e, []) if x != t]
        return ts[0] if ts else None

    def walk(l, t0, e_out):
        seq, t, e = [], t0, e_out
        while True:
            nt_ = other_tri(e, t)
            if nt_ is None:
                seq.append(("b", e))
                return seq, False
            if nt_ == t0:
                return seq, True
            seq.append(("t", nt_))
            e1, e2 = links[(l, nt_)]
            e = e2 if e1 == e else e1
            t = nt_

    corner_key, corner_pos = {}, []

    def cid(item):
        if item not in corner_key:
            corner_key[item] = len(corner_pos)
            if item[0] == "t":
                corner_pos.append(V[F[item[1]]].mean(0))
            else:
                corner_pos.append(V[list(item[1])].mean(0))
        return corner_key[item]

    polys = {}
    for l, tris in per_label.items():
        unvisited = set(tris)
        best = []
        while unvisited:
            t0 = next(iter(unvisited))
            e1, e2 = links[(l, t0)]
            fwd, closed = walk(l, t0, e2)
            if closed:
                seq = [("t", t0)] + fwd
            else:
                back, _ = walk(l, t0, e1)
                seq = back[::-1] + [("t", t0)] + fwd
            for it in seq:
                if it[0] == "t":
                    unvisited.discard(it[1])
            if len(seq) > len(best):
                best = seq
        corners = [it for it in best
                   if it[0] == "b" or len(set(lab[F[it[1]]])) == 3]
        ids = [cid(c) for c in corners]
        if len(ids) >= 3:
            polys[l] = ids
    pos = np.array(corner_pos)
    # orient every polygon counter-clockwise seen from outside
    for l, ids in polys.items():
        P = pos[ids]
        n = newell(P)
        _, sn, _, _ = surf.project(P.mean(0))
        if n @ sn[0] < 0:
            polys[l] = ids[::-1]
    boundary_vertices = {i for k, i in corner_key.items() if k[0] == "b"}
    return polys, pos, boundary_vertices


def newell(P):
    n = np.zeros(3)
    for i in range(len(P)):
        a, b = P[i], P[(i + 1) % len(P)]
        n += np.array([(a[1] - b[1]) * (a[2] + b[2]),
                       (a[2] - b[2]) * (a[0] + b[0]),
                       (a[0] - b[0]) * (a[1] + b[1])])
    return n


def check_topology(polys, boundary_vertices):
    """Every edge used once in each direction; open edges only on the tube ends."""
    seen = {}
    problems = 0
    for f, ids in polys.items():
        for i in range(len(ids)):
            e = (ids[i], ids[(i + 1) % len(ids)])
            if e in seen:
                problems += 1
            seen[e] = f
    for (a, b) in seen:
        if (b, a) not in seen and not (a in boundary_vertices and b in boundary_vertices):
            problems += 1
    return problems


# ----------------------------------------------------------------------------
# 5. Edge flips so each flower panel has exactly 5 sides
# ----------------------------------------------------------------------------
def vertex_faces(polys):
    vf = {}
    for f, ids in polys.items():
        for v in ids:
            vf.setdefault(v, []).append(f)
    return vf


def flip_edge(polys, pos, a, b, vf, protected):
    """Swap which two panels an edge separates (a 'T1' move). The two panels
    on either side lose a side, the two at its ends gain one."""
    fa, fb = vf.get(a, []), vf.get(b, [])
    if len(fa) != 3 or len(fb) != 3:
        return False
    shared = set(fa) & set(fb)
    if len(shared) != 2:
        return False
    P, Bf = shared
    A = (set(fa) - shared).pop()
    C = (set(fb) - shared).pop()
    if A == C or A in protected or C in protected:
        return False
    if set(polys[A]) & set(polys[C]):
        return False
    if len(polys[P]) <= 4 or len(polys[Bf]) <= 4:
        return False
    cP = pos[polys[P]].mean(0)
    cB = pos[polys[Bf]].mean(0)
    origP = list(polys[P])
    polys[P] = [v for v in polys[P] if v != b]       # a stays as corner (A,C,P)
    polys[Bf] = [v for v in polys[Bf] if v != a]     # b stays as corner (A,C,B)
    for F, old in ((A, a), (C, b)):
        ids = polys[F]
        i = ids.index(old)
        prev = ids[i - 1]
        polys[F] = ids[:i] + ([a, b] if prev in origP else [b, a]) + ids[i + 1:]
    m = (pos[a] + pos[b]) / 2
    h = max(np.linalg.norm(pos[a] - pos[b]), 0.5) / 2
    u = cP - cB
    u /= np.linalg.norm(u)
    pos[a] = m + h * u
    pos[b] = m - h * u
    vf.clear()
    vf.update(vertex_faces(polys))
    return True


def fix_side_counts(polys, pos, pent_faces, rng):
    """Shrink or grow flower panels to 5 sides, and keep fillers between 4
    and MAX_FILLER_SIDES sides."""
    flips = 0
    for _ in range(10):
        changed = False
        vf = vertex_faces(polys)
        targets = [(f, 5, 5) for f in pent_faces if f in polys] + \
                  [(f, 4, MAX_FILLER_SIDES) for f in polys if f not in pent_faces]
        for f, lo, hi in targets:
            guard = 0
            while len(polys[f]) > hi and guard < 6:
                guard += 1
                ids = polys[f]
                edges = sorted(range(len(ids)), key=lambda i: np.linalg.norm(
                    pos[ids[i]] - pos[ids[(i + 1) % len(ids)]]))
                for i in edges:
                    a, b = ids[i], ids[(i + 1) % len(ids)]
                    other = (set(vf.get(a, [])) & set(vf.get(b, []))) - {f}
                    # the panel across the edge must be a filler, or a pentagon
                    # (of another flower) that also has too many sides
                    if not other or any(g in pent_faces and len(polys[g]) <= 5
                                        for g in other):
                        continue
                    if flip_edge(polys, pos, a, b, vf, pent_faces):
                        flips += 1; changed = True
                        break
                else:
                    break
            guard = 0
            while len(polys[f]) < lo and guard < 6:
                guard += 1
                ids = polys[f]
                done = False
                for v in ids:
                    others = [g for g in vf.get(v, []) if g != f]
                    if len(others) != 2 or set(others) & pent_faces:
                        continue
                    Y, Z = others
                    w = [x for x in set(polys[Y]) & set(polys[Z]) if x != v]
                    for x in w:
                        far = set(vf.get(x, [])) - {Y, Z}
                        if far & pent_faces:
                            continue
                        if flip_edge(polys, pos, v, x, vf, pent_faces | {f}):
                            flips += 1; changed = done = True
                            break
                    if done:
                        break
                if not done:
                    break
        if not changed:
            break
    return flips


# ----------------------------------------------------------------------------
# 6. Variants, relaxation
# ----------------------------------------------------------------------------
def open_gaps(polys, pos, kind, flower_of, ring_of):
    """Unzip the edge between each pair of neighbouring ring pentagons into a
    diamond-shaped gap. Returns the new polygon dict, positions, kinds, and
    the gap records (apex a, b1, b2, far corner c)."""
    polys = {f: list(v) for f, v in polys.items()}
    pos = [p for p in pos]
    kind = dict(kind)
    gaps = []
    flowers = {}
    for f in polys:
        if kind.get(f) == "pentagon":
            flowers.setdefault(flower_of[f], {})[ring_of[f]] = f
    next_id = max(polys) + 1
    for fl, members in flowers.items():
        if -1 not in members:
            continue
        P0 = members[-1]
        for k in range(5):
            Ra, Rb = members.get(k), members.get((k + 1) % 5)
            if Ra is None or Rb is None:
                continue
            vf = vertex_faces(polys)
            shared = set(polys[Ra]) & set(polys[Rb])
            a = [v for v in shared if v in polys[P0]]
            b = [v for v in shared if v not in polys[P0]]
            if len(a) != 1 or len(b) != 1 or len(vf[b[0]]) != 3:
                continue
            a, b = a[0], b[0]
            # L is the ring pentagon that walks a -> b
            def has_ab(F):
                ids = polys[F]
                i = ids.index(a)
                return ids[(i + 1) % len(ids)] == b
            L_, R_ = (Ra, Rb) if has_ab(Ra) else (Rb, Ra)
            X = (set(vf[b]) - {Ra, Rb}).pop()
            pa, pb = pos[a], pos[b]
            cL = np.mean([pos[v] for v in polys[L_]], 0)
            cR = np.mean([pos[v] for v in polys[R_]], 0)
            b1, b2, c = b, len(pos), len(pos) + 1
            pos.append(pb + 0.1 * (cR - pb))
            pos.append(pb + 0.1 * (pb - pa))
            pos[b1] = pb + 0.1 * (cL - pb)
            polys[R_] = [b2 if v == b else v for v in polys[R_]]
            ids = polys[X]
            i = ids.index(b)
            polys[X] = ids[:i] + [b1, c, b2] + ids[i + 1:]
            g = next_id
            next_id += 1
            polys[g] = [a, b2, c, b1]
            kind[g] = "gap"
            gaps.append((a, b1, b2, c))
    return polys, np.array(pos), kind, gaps


def relax(polys, pos, kind, size_of, gaps, surf, boundary_vertices):
    """Move corners until the panels are as close as possible to what they
    want to be (regular pentagons, even fillers, flat panels) while sitting on
    the surface. Each wish pulls with its weight; corners settle at the
    weighted compromise."""
    pos = pos.copy()
    gap_c = {g[3] for g in gaps}
    gap_edges = set()
    for f, ids in polys.items():
        if kind[f] == "gap":
            for i in range(len(ids)):
                gap_edges.add(frozenset((ids[i], ids[(i + 1) % len(ids)])))
    I, J, Lt, W = [], [], [], []
    for f, ids in polys.items():
        n = len(ids)
        if kind[f] == "pentagon":
            s = regular_edge(size_of[f], 5)
            for i in range(5):
                for j in range(i + 1, 5):
                    edge = (j - i) in (1, 4)
                    I.append(ids[i]); J.append(ids[j])
                    W.append(W_PENTAGON_EDGE if edge else W_PENTAGON)
                    Lt.append(s if edge else PHI * s)
        elif kind[f] == "filler":
            # fillers keep the area they grew to, so the skin neither shrinks nor stretches
            n_eff = max(3, n - 2 * sum(v in gap_c for v in ids))
            s = regular_edge(2 * math.sqrt(poly_area(pos[ids]) / math.pi), n_eff)
            for i in range(n):
                a, b, c = ids[i], ids[(i + 1) % n], ids[(i + 2) % n]
                if frozenset((a, b)) not in gap_edges:
                    I.append(a); J.append(b); Lt.append(s); W.append(W_FILLER_EDGE)
                if b not in gap_c and frozenset((a, b)) not in gap_edges \
                        and frozenset((b, c)) not in gap_edges:
                    I.append(a); J.append(c); W.append(W_FILLER_ANGLE)
                    Lt.append(2 * s * math.cos(math.pi / n_eff))
    I, J, Lt, W = map(np.array, (I, J, Lt, W))
    flat_faces = [ids for f, ids in polys.items() if kind[f] != "gap"]
    maxn = max(len(x) for x in flat_faces)
    pad = np.array([ids + [ids[0]] * (maxn - len(ids)) for ids in flat_faces])
    mask = np.array([[1.0] * len(ids) + [0.0] * (maxn - len(ids)) for ids in flat_faces])
    G = np.array(gaps, int).reshape(-1, 4)
    ws = np.full(len(pos), W_SURFACE)
    bv = np.array(sorted(boundary_vertices), int)
    bpts = surf.V[surf.boundary]
    for it in range(ITERATIONS):
        acc = np.zeros_like(pos)
        wsum = np.zeros(len(pos))
        d = pos[J] - pos[I]
        l = np.linalg.norm(d, axis=1)
        corr = ((l - Lt) / np.maximum(l, 1e-9) * 0.5 * W)[:, None] * d
        np.add.at(acc, I, corr); np.add.at(acc, J, -corr)
        np.add.at(wsum, I, W); np.add.at(wsum, J, W)
        # planarity
        Pp = pos[pad]
        cen = (Pp * mask[..., None]).sum(1) / mask.sum(1)[:, None]
        X = (Pp - cen[:, None]) * mask[..., None]
        cov = np.einsum("fni,fnj->fij", X, X)
        nrm = np.linalg.eigh(cov)[1][:, :, 0]
        off = np.einsum("fni,fi->fn", Pp - cen[:, None], nrm)
        pc = -(off * mask)[..., None] * nrm[:, None, :] * W_PLANAR
        np.add.at(acc, pad.ravel(), pc.reshape(-1, 3))
        np.add.at(wsum, pad.ravel(), (mask * W_PLANAR).ravel())
        # open gaps stay diamonds: c = b1 + b2 - a
        if len(G):
            target = pos[G[:, 1]] + pos[G[:, 2]] - pos[G[:, 0]]
            np.add.at(acc, G[:, 3], W_GAP * (target - pos[G[:, 3]]))
            np.add.at(wsum, G[:, 3], W_GAP)
        # stay on the surface
        proj, _, _, _ = surf.project(pos)
        acc += ws[:, None] * (proj - pos)
        wsum += ws
        # corners on the tube ends stay on the end rims
        if len(bv):
            _, k = surf.btree.query(pos[bv])
            acc[bv] += 2.0 * (bpts[k] - pos[bv])
            wsum[bv] += 2.0
        pos += acc / wsum[:, None]
    return pos


# ----------------------------------------------------------------------------
# 7. Metrics, export, pictures
# ----------------------------------------------------------------------------
def poly_area(P):
    return np.linalg.norm(newell(P)) / 2


def flatness(P):
    c = P.mean(0)
    n = np.linalg.eigh((P - c).T @ (P - c))[1][:, 0]
    return np.abs((P - c) @ n).max()


def metrics(polys, pos, kind, size_of, surf, boundary_vertices, flower_of=None):
    m = {}
    panels = {f: ids for f, ids in polys.items() if kind[f] != "gap"}
    sides = {}
    for f, ids in panels.items():
        key = f"{len(ids)}" + (" (flower)" if kind[f] == "pentagon" else "")
        sides[key] = sides.get(key, 0) + 1
    m["side_counts"] = dict(sorted(sides.items(), key=lambda kv: (int(kv[0].split()[0]), kv[0])))
    area = {f: poly_area(pos[ids]) for f, ids in polys.items()}
    across = np.array([2 * math.sqrt(area[f] / math.pi) for f in panels])
    pent_across = np.array([2 * math.sqrt(area[f] / math.pi) for f in panels if kind[f] == "pentagon"])
    m["across_all"] = [across.min(), across.mean(), across.max()]
    m["across_pentagons"] = [pent_across.min(), pent_across.mean(), pent_across.max()]
    fl = np.array([flatness(pos[ids]) for ids in panels.values()])
    m["flatness"] = [fl.max(), fl.mean()]
    inner = [v for v in range(len(pos)) if v not in boundary_vertices]
    _, _, d, _ = surf.project(pos[inner])
    m["surface_offset"] = [np.abs(d).max(), np.abs(d).mean(), d.min(), d.max()]
    errs, angs = [], []
    for f, ids in panels.items():
        if kind[f] != "pentagon":
            continue
        P = pos[ids]
        s = regular_edge(size_of[f], 5)
        e = np.linalg.norm(P - np.roll(P, -1, 0), axis=1)
        errs.append(np.abs(e / s - 1).max())
        for i in range(5):
            u, w = P[i - 1] - P[i], P[(i + 1) % 5] - P[i]
            angs.append(abs(math.degrees(math.acos(np.clip(
                u @ w / np.linalg.norm(u) / np.linalg.norm(w), -1, 1))) - 108))
    # bump per flower: furthest pentagon corner from the surface
    per = {}
    for f, ids in panels.items():
        if kind[f] == "pentagon":
            _, _, d, _ = surf.project(pos[ids])
            fl_ = flower_of[f]
            per[fl_] = (size_of[f], max(per.get(fl_, (0, 0))[1], np.abs(d).max()))
    m["flower_bumps"] = [[float(D), float(b)] for D, b in sorted(per.values())]
    m["pentagon_edge_error_max_pct"] = 100 * max(errs)
    m["pentagon_angle_error_max_deg"] = max(angs)
    total = sum(area.values())
    gap_area = sum(area[f] for f in polys if kind[f] == "gap")
    pent_area = sum(area[f] for f in panels if kind[f] == "pentagon")
    m["total_area_sqin"] = total
    m["open_area_sqin"] = gap_area
    m["open_area_pct"] = 100 * gap_area / total
    m["pentagon_coverage_pct"] = 100 * pent_area / total
    m["gap_count"] = sum(1 for f in polys if kind[f] == "gap")
    apex = []
    for f, ids in polys.items():
        if kind[f] == "gap":
            P = pos[ids]
            u, w = P[1] - P[0], P[3] - P[0]
            apex.append(math.degrees(math.acos(np.clip(
                u @ w / np.linalg.norm(u) / np.linalg.norm(w), -1, 1))))
    if apex:
        m["gap_apex_angle_deg"] = [min(apex), float(np.mean(apex)), max(apex)]
    return m


def print_metrics(name, m):
    print(f"\n=== Variant: {name} ===")
    print("Panels by number of sides:")
    for k, v in m["side_counts"].items():
        print(f"   {k:>12}-sided: {v}")
    a, p = m["across_all"], m["across_pentagons"]
    print(f"Size across, all panels:    min {a[0]:.2f}  avg {a[1]:.2f}  max {a[2]:.2f} in")
    print(f"Size across, flower pents:  min {p[0]:.2f}  avg {p[1]:.2f}  max {p[2]:.2f} in")
    print(f"Out of flat, per panel:     max {m['flatness'][0]:.3f}  avg {m['flatness'][1]:.3f} in")
    s = m["surface_offset"]
    print(f"Bumpiness (corner distance from the surface): max {s[0]:.3f}  avg {s[1]:.3f} in"
          f"  (range {s[2]:+.2f} to {s[3]:+.2f})")
    print(f"Pentagon regularity:        edges within {m['pentagon_edge_error_max_pct']:.1f} %,"
          f" angles within {m['pentagon_angle_error_max_deg']:.1f} deg")
    print(f"Flower pentagons cover {m['pentagon_coverage_pct']:.0f} % of the skin")
    print("Bump per flower (size across -> furthest pentagon corner from surface): " +
          ", ".join(f"{D:.1f} in -> {b:.2f}" for D, b in m["flower_bumps"]))
    if m["gap_count"]:
        g = m["gap_apex_angle_deg"]
        print(f"Open gaps: {m['gap_count']}, open area {m['open_area_sqin']:.0f} sq in"
              f" = {m['open_area_pct']:.1f} % of {m['total_area_sqin']:.0f} sq in")
        print(f"Gap opening angle at the centre pentagon: min {g[0]:.0f}  avg {g[1]:.0f}"
              f"  max {g[2]:.0f} deg (36 deg when the flower lies flat)")


# Colours by number of sides (validated categorical palette)
COLOURS = {3: "#e87ba4", 4: "#eda100", 5: "#f6b38f", 6: "#9ec5f4",
           7: "#1baf7a", 8: "#2a78d6"}
FLOWER_COLOUR = "#eb6834"
GAP_COLOUR = "#ffffff"


def colour_of(f, polys, kind):
    if kind[f] == "gap":
        return GAP_COLOUR
    if kind[f] == "pentagon":
        return FLOWER_COLOUR
    return COLOURS[min(max(len(polys[f]), 3), 8)]


def legend_handles(polys, kind):
    present = {min(max(len(polys[f]), 3), 8) for f in polys if kind[f] == "filler"}
    h = [Patch(facecolor=FLOWER_COLOUR, edgecolor="#333", label="flower pentagon")]
    names = {3: "triangle", 4: "4 sides", 5: "5 sides (filler)", 6: "6 sides",
             7: "7 sides", 8: "8+ sides"}
    for n in sorted(present):
        h.append(Patch(facecolor=COLOURS[n], edgecolor="#333", label=names[n]))
    if any(kind[f] == "gap" for f in polys):
        h.append(Patch(facecolor=GAP_COLOUR, edgecolor="#333", label="open gap (cutout)"))
    return h


def shade(hexcol, k):
    c = np.array(matplotlib.colors.to_rgb(hexcol))
    return tuple(np.clip(c * (0.5 + 0.5 * k), 0, 1))


def draw_view(ax, polys, Q, kind, keep=None):
    """Draw the skin seen from the -y side, in already-rotated coordinates Q
    (x right, z up, y away from the viewer). Simple painter's algorithm."""
    light = np.array([-0.35, -1.0, 0.55])
    light /= np.linalg.norm(light)
    items = []
    for f, ids in polys.items():
        P = Q[ids]
        if keep is not None and not keep(P):
            continue
        n = newell(P)
        n /= np.linalg.norm(n) + 1e-12
        depth = P[:, 1].mean()
        if n[1] > 0:  # facing away: the inside of the skin, seen through gaps
            if kind[f] != "gap":
                items.append((depth, P[:, [0, 2]], "#b5b5b5"))
            continue
        if kind[f] == "gap":  # a hole, drawn see-through white
            items.append((depth - 0.01, P[:, [0, 2]], (1, 1, 1, 0.85)))
            continue
        items.append((depth, P[:, [0, 2]], shade(colour_of(f, polys, kind), max(0, n @ light))))
    items.sort(key=lambda t: -t[0])
    ax.add_collection(PolyCollection([t[1] for t in items], facecolors=[t[2] for t in items],
                                     edgecolors="#2b2b2b", linewidths=0.4))
    ax.set_aspect("equal")
    ax.set_axis_off()


def rot_x(deg):
    c, s_ = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return np.array([[1, 0, 0], [0, c, -s_], [0, s_, c]])


def picture_3d(polys, pos, kind, title, path):
    """Three views of the whole tube, turned 0, 120 and 240 degrees about its
    length. The tube is laid horizontally for the picture."""
    fig, axes = plt.subplots(3, 1, figsize=(15, 10.5))
    disp = pos[:, [2, 0, 1]]
    disp = disp - disp.mean(0)
    for ax, turn in zip(axes, (0, 120, 240)):
        Q = disp @ (rot_x(turn) @ rot_x(-12).T).T
        draw_view(ax, polys, Q, kind)
        ax.set_xlim(Q[:, 0].min() - 1, Q[:, 0].max() + 1)
        ax.set_ylim(Q[:, 2].min() - 1, Q[:, 2].max() + 1)
        ax.set_title(f"turned {turn} degrees", fontsize=10)
    fig.legend(handles=legend_handles(polys, kind), loc="lower center", ncol=8, frameon=False)
    fig.suptitle(title, fontsize=13)
    fig.subplots_adjust(left=0.01, right=0.99, top=0.93, bottom=0.06, hspace=0.12)
    fig.savefig(path, dpi=110)
    plt.close(fig)


def picture_closeup(variants, centre, normal, axis, path, radius=14):
    """Look straight at one flower, tube running left to right."""
    y = -normal / np.linalg.norm(normal)
    x = axis - (axis @ y) * y
    x /= np.linalg.norm(x)
    z = np.cross(x, y)
    R = np.stack([x, y, z])
    fig, axes = plt.subplots(1, 2, figsize=(15, 5.6))
    for ax, (name, (polys, pos, kind)) in zip(axes, variants.items()):
        Q = (pos - centre) @ R.T
        draw_view(ax, polys, Q, kind,
                  keep=lambda P: np.linalg.norm(P.mean(0)[[0, 2]]) < radius * 1.3)
        ax.set_xlim(-radius, radius)
        ax.set_ylim(-0.6 * radius, 0.6 * radius)
        ax.set_title(f"gaps {name}", fontsize=12)
    polys, _, kind = list(variants.values())[1]
    fig.legend(handles=legend_handles(polys, kind), loc="lower center", ncol=8, frameon=False)
    fig.suptitle("Close-up of one flower, seen straight on", fontsize=13)
    fig.subplots_adjust(left=0.01, right=0.99, top=0.9, bottom=0.08, wspace=0.05)
    fig.savefig(path, dpi=110)
    plt.close(fig)


def picture_unrolled(variants, surf, tube, path):
    fig, axes = plt.subplots(2, 1, figsize=(15, 11))
    for ax, (name, (polys, pos, kind)) in zip(axes, variants.items()):
        along, theta, r = unroll(pos, tube, surf)
        faces, cols = [], []
        for f, ids in polys.items():
            th = theta[ids].copy()
            th = th[0] + (th - th[0] + math.pi) % (2 * math.pi) - math.pi
            if th.mean() > math.pi:
                th -= 2 * math.pi
            if th.mean() < -math.pi:
                th += 2 * math.pi
            for shift in (0, 2 * math.pi, -2 * math.pi):
                t2 = th + shift
                if t2.max() < -math.pi or t2.min() > math.pi:
                    continue
                faces.append(np.stack([along[ids], t2 * r[ids]], 1))
                cols.append(colour_of(f, polys, kind))
        pc = PolyCollection(faces, facecolors=cols, edgecolors="#2b2b2b", linewidths=0.4)
        ax.add_collection(pc)
        outline = np.concatenate([np.stack([tube["s"], math.pi * tube["r"]], 1),
                                  np.stack([tube["s"], -math.pi * tube["r"]], 1)[::-1]])
        clip = matplotlib.patches.Polygon(outline, closed=True, transform=ax.transData)
        pc.set_clip_path(clip)
        ax.plot(tube["s"], math.pi * tube["r"], color="#888", lw=0.8, ls="--")
        ax.plot(tube["s"], -math.pi * tube["r"], color="#888", lw=0.8, ls="--")
        ax.set_xlim(-1, TUBE_LENGTH + 1)
        ax.set_ylim(-math.pi * TUBE_DIAMETER / 2 - 1, math.pi * TUBE_DIAMETER / 2 + 1)
        ax.set_aspect("equal")
        ax.set_title(f"Unrolled skin, gaps {name} (cut along one line and laid flat;"
                     " dashed = tube outline)", fontsize=11)
        ax.set_xlabel("along the tube (in)")
        ax.set_ylabel("around the tube (in)")
        for sp in ("top", "right"):
            ax.spines[sp].set_visible(False)
    polys, _, kind = list(variants.values())[1]
    fig.legend(handles=legend_handles(polys, kind), loc="lower center", ncol=8, frameon=False)
    fig.subplots_adjust(left=0.06, right=0.98, top=0.95, bottom=0.08, hspace=0.25)
    fig.savefig(path, dpi=110)
    plt.close(fig)


def export(path, variants_data, params):
    out = {"units": "inch",
           "description": "Layout C: pentagon flower clusters. Each variant lists "
                          "vertices (shared corners) and polygons (lists of vertex "
                          "indices, counter-clockwise seen from outside).",
           "parameters": params, "variants": {}}
    for name, (polys, pos, kind, extra, surf, m) in variants_data.items():
        order = sorted(polys)
        _, normals, _, _ = surf.project(pos)
        vf = vertex_faces({f: polys[f] for f in order if kind[f] != "gap"})
        out["variants"][name] = {
            "vertices": np.round(pos, 4).tolist(),
            "vertex_normals": np.round(normals, 4).tolist(),
            "vertex_panel_count": [len(vf.get(v, [])) for v in range(len(pos))],
            "polygons": [dict(id=i, vertices=[int(v) for v in polys[f]],
                              sides=len(polys[f]), kind=kind[f], **extra.get(f, {}))
                         for i, f in enumerate(order)],
            "metrics": json.loads(json.dumps(m, default=float)),
        }
    with open(path, "w") as fh:
        json.dump(out, fh, indent=1)


# ----------------------------------------------------------------------------
def main():
    rng = np.random.default_rng(RANDOM_SEED)
    print("Building test tube mesh ...")
    surf, tube = make_test_tube()
    print(f"  {len(surf.V)} mesh vertices, {len(surf.F)} triangles")

    print("Placing seeds ...")
    flowers, sites, size, role, fid, ringk = place_seeds(surf, rng)
    print(f"  {len(flowers)} flowers, {len(sites)} seeds in all; flower sizes: "
          + ", ".join(f"{f['D']:.1f}" for f in flowers))

    print("Growing regions from the seeds ...")
    lab = label_surface(surf, sites, size)
    polys, pos, boundary_vertices = extract_polygons(surf, lab, len(sites))
    print(f"  {len(polys)} regions, topology problems: {check_topology(polys, boundary_vertices)}")

    pent_faces = {f for f in polys if role[f] in ("centre", "ring")}
    flips = fix_side_counts(polys, pos, pent_faces, rng)
    problems = check_topology(polys, boundary_vertices)
    bad = [f for f in pent_faces if f in polys and len(polys[f]) != 5]
    print(f"  {flips} edge flips; flower panels not 5-sided: {len(bad)}; "
          f"topology problems: {problems}")

    kind = {f: ("pentagon" if f in pent_faces and len(polys[f]) == 5 else "filler")
            for f in polys}
    size_of = {f: size[f] for f in polys}
    flower_of = {f: int(fid[f]) for f in polys}
    ring_of = {f: int(ringk[f]) for f in polys}
    extra = {f: {"flower": int(fid[f]) if fid[f] >= 0 else None, "role": role[f],
                 "target_across": round(float(size[f]), 3)} for f in polys}

    print("Relaxing the closed variant ...")
    pos_closed = relax(polys, pos, kind, size_of, [], surf, boundary_vertices)

    polys_o, pos_o, kind_o, gaps = open_gaps(polys, pos, kind, flower_of, ring_of)
    size_o = dict(size_of)
    extra_o = dict(extra)
    for f in polys_o:
        if kind_o[f] == "gap":
            size_o[f] = 0
            extra_o[f] = {"flower": None, "role": "gap"}
    print(f"Relaxing the open variant ({len(gaps)} gaps) ...")
    print(f"  topology problems: {check_topology(polys_o, boundary_vertices)}")
    pos_open = relax(polys_o, pos_o, kind_o, size_o, gaps, surf, boundary_vertices)

    m_closed = metrics(polys, pos_closed, kind, size_of, surf, boundary_vertices, flower_of)
    m_open = metrics(polys_o, pos_open, kind_o, size_o, surf, boundary_vertices, flower_of)
    print_metrics("gaps closed", m_closed)
    print_metrics("gaps open", m_open)

    params = {k: globals()[k] for k in (
        "SIZE_MIN", "SIZE_MAX", "FLOWER_SPACING", "FILLER_SPACING", "FILLER_SIZE_SCALE",
        "FILLER_SIZE_JITTER", "NOTCH_RADIUS", "KEEP_OUT", "MAX_FILLER_SIDES", "POWER_ALPHA", "RANDOM_SEED", "W_PENTAGON_EDGE", "W_PENTAGON",
        "W_FILLER_EDGE", "W_FILLER_ANGLE", "W_PLANAR", "W_SURFACE", "W_GAP", "ITERATIONS",
        "TUBE_LENGTH", "TUBE_DIAMETER")}
    export(os.path.join(OUT_DIR, "layout.json"), {
        "closed": (polys, pos_closed, kind, extra, surf, m_closed),
        "open": (polys_o, pos_open, kind_o, extra_o, surf, m_open)}, params)

    print("\nDrawing previews ...")
    picture_3d(polys, pos_closed, kind, "Flower layout, gaps closed",
               os.path.join(OUT_DIR, "preview-3d-closed.png"))
    picture_3d(polys_o, pos_open, kind_o, "Flower layout, gaps open (white = cutout)",
               os.path.join(OUT_DIR, "preview-3d-open.png"))
    # close-up on the flower closest to the average size
    avg = (SIZE_MIN + SIZE_MAX) / 2
    fbest = min(range(len(flowers)), key=lambda i: abs(flowers[i]["D"] - avg))
    cf = [f for f in polys if fid[f] == fbest and role[f] == "centre"][0]
    centre = pos_closed[polys[cf]].mean(0)
    _, n, _, vi = surf.project(centre)
    axis = tube["T"][tube["ring_of_vertex"][vi[0]]]
    picture_closeup({"closed": (polys, pos_closed, kind), "open": (polys_o, pos_open, kind_o)},
                    centre, n[0], axis, os.path.join(OUT_DIR, "preview-closeup.png"))
    picture_unrolled({"closed": (polys, pos_closed, kind), "open": (polys_o, pos_open, kind_o)},
                     surf, tube, os.path.join(OUT_DIR, "preview-unrolled.png"))
    print("Done. Wrote layout.json and preview-*.png to", OUT_DIR)


if __name__ == "__main__":
    main()
