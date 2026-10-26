"""Picture of the sewn polyp, drawn by ray marching a smooth 3D shape.

The shape is built from the same numbers as the pattern: a cuff the size of the
wrist, and six thumbs whose thickness matches the pattern pieces. The seams are
drawn as dark lines so the two sewing versions can be compared.
"""
import math
import numpy as np
import matplotlib.pyplot as plt

FABRIC = np.array([0.93, 0.49, 0.38])
TUBE = np.array([0.80, 0.81, 0.84])
SEAM_COLOR = np.array([0.35, 0.12, 0.08])


def _smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0, 1)
    return b * (1 - h) + a * h - k * h * (1 - h)


def _round_cone(p, a, b, r1, r2):
    """Distance to a cone with rounded ends (radius r1 at a, r2 at b)."""
    ba = b - a
    l2 = ba @ ba
    rr = r1 - r2
    a2 = l2 - rr * rr
    il2 = 1.0 / l2
    pa = p - a
    y = pa @ ba
    z = y - l2
    x = pa * l2 - y[:, None] * ba
    x2 = np.sum(x * x, axis=1)
    y2 = y * y * l2
    z2 = z * z * l2
    k = np.sign(rr) * rr * rr * x2
    side = (np.sqrt(x2 * a2 * il2) + y * rr) * il2 - r1
    bottom = np.sqrt(x2 + y2) * il2 - r1
    top = np.sqrt(x2 + z2) * il2 - r2
    return np.where(np.sign(z) * a2 * z2 > k, top, np.where(np.sign(y) * a2 * y2 < k, bottom, side))


def _cylinder(p, radius, z0, z1, round_=0.0):
    rho = np.hypot(p[:, 0], p[:, 1])
    zc, hh = (z0 + z1) / 2, (z1 - z0) / 2
    dx = rho - (radius - round_)
    dy = np.abs(p[:, 2] - zc) - (hh - round_)
    out = np.hypot(np.maximum(dx, 0), np.maximum(dy, 0))
    return out + np.minimum(np.maximum(dx, dy), 0) - round_


class Polyp:
    def __init__(self, cuff_radius, cuff_height, thumbs, r_base, r_top, thumb_length,
                 tube_radius, tilt_deg=12):
        self.R, self.H, self.n = cuff_radius, cuff_height, thumbs
        self.tube_r = tube_radius
        self.r_base, self.r_top = r_base, r_top
        tilt = math.radians(tilt_deg)
        rho = cuff_radius - r_base            # thumb base sits flush with the cuff wall
        self.thumbs = []
        for k in range(thumbs):
            ang = 2 * math.pi * (k + 0.5) / thumbs
            e = np.array([math.cos(ang), math.sin(ang), 0.0])          # outward
            t = np.array([-math.sin(ang), math.cos(ang), 0.0])         # sideways
            u = math.sin(tilt) * e + np.array([0, 0, math.cos(tilt)])  # thumb axis
            a = rho * e + np.array([0, 0, -0.35])
            b = a + u * (thumb_length + 0.35 - r_top)
            n_ab = e - (e @ u) * u                                     # A-version seam plane normal
            self.thumbs.append(dict(ang=ang, e=e, t=t, u=u, a=a, b=b, n_ab=n_ab / np.linalg.norm(n_ab)))

    def fabric(self, p):
        d = _cylinder(p, self.R, -self.H, 0.0, round_=0.35)
        for th in self.thumbs:
            d = _smin(d, _round_cone(p, th["a"], th["b"], self.r_base, self.r_top), 0.28)
        return d

    def tube(self, p):
        return _cylinder(p, self.tube_r, -self.H - 2.5, -self.H + 0.2)

    def sdf(self, p):
        return np.minimum(self.fabric(p), self.tube(p))

    def seam_distance(self, p, version):
        """Distance from each surface point to the nearest seam line."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        rho = np.hypot(x, y)
        phi = np.arctan2(y, x)
        step = 2 * math.pi / self.n
        # seams between neighbouring thumbs: down the cuff, and from crotch to centre
        phib = np.round(phi / step) * step
        d = np.where(z < 0.3, rho * np.abs(np.sin(phi - phib)), 9.0)
        # nearest thumb
        k = np.mod(np.floor(phi / step), self.n).astype(int)
        for i, th in enumerate(self.thumbs):
            m = k == i
            if not m.any():
                continue
            q = p[m]
            if version == "A":   # seam splits thumb into outer and inner halves
                along = (q - th["a"]) @ th["u"]
                ds = np.abs((q - th["a"]) @ th["n_ab"])
                radial = np.linalg.norm((q - th["a"]) - along[:, None] * th["u"], axis=1)
                ds = np.where((along > 0.3) & (radial < self.r_top + 0.03), ds, 9.0)
            else:                # seam splits thumb into left and right halves, centre to wrist
                ds = np.abs(q @ th["t"])
                ds = np.where(q @ th["e"] > 0, ds, 9.0)
            d[m] = np.minimum(d[m], ds)
        hem = np.abs(z - (-self.H + 0.6))
        return d, hem


def _normals(f, p, eps=2e-3):
    ex = np.array([eps, 0, 0]); ey = np.array([0, eps, 0]); ez = np.array([0, 0, eps])
    n = np.stack([f(p + ex) - f(p - ex), f(p + ey) - f(p - ey), f(p + ez) - f(p - ez)], axis=1)
    return n / (np.linalg.norm(n, axis=1, keepdims=True) + 1e-12)


def render(polyp, version, elev, azim, width=560, height=640, dist=22.0, target=(0, 0, 0.2),
           fov_deg=27):
    target = np.array(target, dtype=float)
    el, az = math.radians(elev), math.radians(azim)
    cam = target + dist * np.array([math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el)])
    fwd = target - cam; fwd /= np.linalg.norm(fwd)
    right = np.cross(fwd, [0, 0, 1]); right /= np.linalg.norm(right)
    up = np.cross(right, fwd)
    tan = math.tan(math.radians(fov_deg) / 2)
    js, is_ = np.meshgrid(np.arange(width), np.arange(height))
    sx = (2 * (js + 0.5) / width - 1) * tan * width / height
    sy = (1 - 2 * (is_ + 0.5) / height) * tan
    dirs = fwd + sx[..., None] * right + sy[..., None] * up
    dirs = (dirs / np.linalg.norm(dirs, axis=-1, keepdims=True)).reshape(-1, 3)

    t = np.full(len(dirs), dist - 8.0)
    hit = np.zeros(len(dirs), bool)
    alive = np.ones(len(dirs), bool)
    for _ in range(200):
        idx = np.nonzero(alive)[0]
        if len(idx) == 0:
            break
        p = cam + dirs[idx] * t[idx, None]
        d = polyp.sdf(p)
        t[idx] += d * 0.85
        done = d < 2e-3
        hit[idx[done]] = True
        alive[idx[done]] = False
        alive[idx[t[idx] > dist + 10]] = False

    img = np.ones((len(dirs), 3))
    idx = np.nonzero(hit)[0]
    p = cam + dirs[idx] * t[idx, None]
    n = _normals(polyp.sdf, p)
    is_tube = polyp.tube(p) < polyp.fabric(p)
    # lighting: key light, soft fill, ambient occlusion
    key = np.array([0.4, -0.6, 0.9]); key /= np.linalg.norm(key)
    key = key[0] * right * -1 + key[1] * fwd * -1 + key[2] * np.array([0, 0, 1.0])
    key /= np.linalg.norm(key)
    diff = np.clip(n @ key, 0, 1)
    fill = np.clip(n @ (-fwd), 0, 1)
    ao = np.ones(len(idx))
    for s in (0.08, 0.18, 0.32, 0.5):
        ao -= np.clip(s - polyp.sdf(p + n * s), 0, None) * (1.6 / (1 + 4 * s))
    ao = np.clip(ao, 0.25, 1)
    light = (0.38 + 0.55 * diff + 0.22 * fill) * ao
    base = np.where(is_tube[:, None], TUBE, FABRIC)
    col = base * light[:, None]
    seam, hem = polyp.seam_distance(p, version)
    w = 0.045
    s = np.clip(1 - seam / w, 0, 1) * (~is_tube)
    col = col * (1 - 0.8 * s[:, None]) + SEAM_COLOR * 0.8 * s[:, None] * light[:, None]
    hm = np.clip(1 - hem / 0.03, 0, 1) * (~is_tube) * 0.45
    col = col * (1 - hm[:, None])
    img[idx] = np.clip(col, 0, 1)
    return img.reshape(height, width, 3)


def save_preview(polyp, version, title, path):
    fig, axes = plt.subplots(1, 2, figsize=(10, 6.2))
    for ax, (el, az, lbl) in zip(axes, [(16, 35, "From the side"), (68, 35, "From above")]):
        ax.imshow(render(polyp, version, el, az))
        ax.set_title(lbl, fontsize=10)
        ax.axis("off")
    fig.suptitle(title, fontsize=12)
    fig.text(0.5, 0.03, "Dark lines are seams. Grey is the 5 in tube. Shape shown filled out "
             "(stuffed or with a little body); thin fabric left empty will droop more.",
             ha="center", fontsize=8.5)
    fig.savefig(path, dpi=110, bbox_inches="tight")
    plt.close(fig)
