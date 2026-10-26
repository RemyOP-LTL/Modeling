# Coral polyp sewing pattern

A fabric coral polyp made of **six fingers** around a round "wrist" (cuff) that slides over the end of a **5 in tube**.

Remy chose **version B**. Each finger is two mirror-image halves, so the seam runs from the centre of the top, out over the fingertip, and down the cuff. There is one pattern piece, and you cut 12 of it (6 pairs). An earlier version A (outer + inner halves) was dropped. It's still in the git history.

> **Status (2026-10-26): finger shape change in progress, not finished.** See [Where things stand](#where-things-stand) before you continue.

## Files

- **pattern.pdf**: the pattern piece at true size, with the *current, unfinished* finger shape. Print at **100 % / "Actual size"** and check the 1 in test square.
- **pattern-preview.png**: the same piece as a quick picture.
- **polyp-work-in-progress.png**: a simulation of the current pattern. It shows the problems listed below.
- **previous-outward-curve/**: the last version Remy saw and liked (fingers straight up at the base, curving outward), with its pattern and simulated picture. The code for it is in commit `e02c3d8`.
- **make_pattern.py**: draws the pattern. Settings are at the top. Run `python3 make_pattern.py` to remake everything, including the simulation (a few minutes), or add `--no-sim` to skip the simulation.
- **sim_polyp.py**: the cloth simulation that turns the flat pieces into the 3D picture.

The scripts need `numpy`, `scipy`, `matplotlib` and `shapely` (`pip3 install numpy scipy matplotlib shapely`).

## Sizes and settings

| Setting | Value | Meaning |
|---|---|---|
| `TUBE_DIAMETER` | 5.0 in | the tube (15.71 in around) |
| `EASE` | 0.5 in | extra room in the cuff so it slides on (woven fabric). Use about -0.5 for stretchy fabric. |
| `FINGERS` | 6 | number of fingers |
| `CUFF_HEIGHT` | 3.0 in | finished height of the wrist |
| `FINGER_LENGTH` | 4.0 in | finger length on the flat piece. Sewn and puffed out, it comes out shorter (about 3 to 3.2 in). |
| `NECK_WIDTH` | 1.9 in | flat width at the narrow part low on the finger (about 1.2 in across when sewn) |
| `BULB_WIDTH` | 2.9 in | flat width at the bulb near the end (about 1.8 in across when sewn) |
| `SPLAY` | 50° | how far the fingers lean out partway up. They curve back to vertical at the tip. |
| `SEAM` | 3/8 in | seam allowance, already added to every edge |
| `HEM` | 1.0 in | extra at the bottom of the cuff for the hem |

The cuff comes out 16.21 in around. At the crotch (where a finger starts), each piece is 2.70 in wide, because six fingers have to fill the cuff's circumference.

## How to sew it

Sew with the right (good) sides of the fabric facing each other, on the dashed line, 3/8 in in from the edge.

- **Cut 12.** Fold the fabric in half, right sides together, and cut 6. Each cut gives a mirror-image pair: the left and right halves of one finger.
- **Notches** (small triangles on the edges) mark the crotch. Snip no deeper than 1/4 in.
- **Slash to the dot.** The piece has a narrow V coming up from the bottom. The seam allowances fill the top of the V, so the cut line stops short. Cut along the solid line up to the dot, and stop right at the dot. The dot is the crotch.

1. **Make the fingers.** Pin a pair together. Sew one continuous seam: from the bottom of the cuff strip, up the long side, over the tip, down the other side, and down the straight edge of the triangle to its point. Clip into the seam allowance around curves. Do this 6 times.
2. **Join the fingers.** The V edges of neighbouring halves are the seams between fingers. Sew the cuff edges from the dot down to the bottom edge, then the triangle edges from the dot to the point. Go around until all 6 are joined.
3. **Check the middle.** All 12 triangle points meet at the centre of the top. Hand-stitch any small hole closed.
4. **Hem.** Fold the bottom edge up 1/4 in, then up 3/4 in, and stitch.
5. **Turn it right side out** through the wrist and push the fingers out.

## How the pattern shapes the fingers

- **Width.** The flat piece is half the finger's circumference, so a sewn finger is about (2 × flat width) / π across.
- **Curve.** A finger curves when one sewn edge is longer than the other. The seam runs in the same plane as the curve. If the inside edge (facing the centre of the polyp) is longer, the finger turns outward. If the outside edge is longer, it turns back in. To bend the sewn finger by an angle *a*, the flat piece is bent by 2*a*/π. `bend()` in `make_pattern.py` does this along the finger, following the angle profile in `_bend_angle()`.

## How the 3D picture is made (sim_polyp.py)

This works from the actual pattern, not from a drawing of the finished shape:

1. It fills each of the 12 flat pieces with small triangles, about 1/8 in across.
2. It stitches matching seam edges together point by point.
3. It finds the resting shape by minimising energy (scipy L-BFGS). Fabric can't stretch but bunches easily, a gentle inside pressure puffs it out as if lightly stuffed, the tube pushes back from inside, and the hem stays at the bottom. Each piece has a little stiffness of its own, and there is none across seams, so ridges and curvature breaks form at the seams.

What it leaves out: gravity, fabric thickness, seam-allowance bulk, and the fingers pushing on each other (they can overlap a little).

## Where things stand

**Remy's latest requests (2026-10-26):**

1. Reverse the curve. The fingers should **arc out from the centre near the base and curve back toward vertical** at the tip. The previous version was the opposite: vertical at the base, curving outward.
2. Make the fingers **narrower near the bottom with a gentle bulb near the end**, but **not phallic**.

**What's in the code now:**

- `_bend_angle()`: the 3D lean is 0 at the base, peaks at `SPLAY` halfway up, and returns to 0 at the tip (a sine curve). A first try peaked a third of the way up, but the flat piece's outside edge folded over itself near the base, because the curve was too sharp for that width.
- `finger_half_width()`: a smooth profile through set points. It narrows from 2.70 in at the crotch to the neck at 30 % of the length, flares to the bulb at 70 %, and rounds off from 78 %.

**What the simulation shows** (`polyp-work-in-progress.png`):

- The fingers come out stubby, about 3 in tall.
- The fabric crumples at the "shoulders", where the 2.70 in crotch narrows to the 1.9 in neck.
- The fingers **don't lean out**. The tips sit about 1.9 in from the centre, while the finger bases are about 1.7 in from the centre. So the S-curve isn't taking effect. The likely reason is that a puffed-out fabric tube can take up the extra length on one side by wrinkling, instead of bending.

**Ideas for next steps:**

- Separate the two changes. Simulate the curve with no neck (`NECK_WIDTH` = 2.7), and the neck/bulb with no curve (`SPLAY` = 0), to see which one causes what. These runs were started but not finished.
- Get the outward lean at the base from the base shape rather than a smooth bend. For example, shorten the outside edge sharply right above the crotch, or change how the floor triangle meets the finger. Then curve back to vertical, as the earlier banana curve did successfully.
- Make the narrowing gentler, or start it higher, so the shoulders don't crumple.
- Make `FINGER_LENGTH` longer to make up for the shortening from puffing out.
- Keep the bulb organic: a long, gradual flare like a bud or club, with no sudden "neck then head" step.
- For quick tests, set `sim_polyp.H_STEP = 0.16` and use about 3000 iterations. The full settings (0.13 and 6000 iterations) take about 3 minutes.
