# Coral polyp sewing pattern

A fabric coral polyp made of **six fingers** around a round "wrist" (cuff) that slides over the end of a **5 in tube**. It works like an **inflatable costume**: air pressure fills it out.

Each finger is two mirror-image halves, so the seam runs from the centre of the top, out over the fingertip, and down the cuff. There's one pattern piece, and you cut 12 of it (6 pairs). The fingers **lean outward just above the cuff, then curve back up to vertical** at the tips.

![Simulated polyp](polyp.png)

## Files

- **pattern.pdf**: the pattern piece at true size, drawn sideways on a **landscape** page. Print at **100 % / "Actual size"** and check the 1 in test square.
- **pattern-preview.png**: the piece as a quick picture, upright.
- **polyp.png**: the sewn polyp, simulated from the flat piece (see below).
- **five-fingers/**: the same design with `FINGERS = 5`, for comparison: its simulated picture and pattern. Each piece is 3.24 in wide at the crotch, not 2.70 in, so the fingers are fatter (about 2.1 in across). Cut 10.
- **previous-outward-curve/**: an earlier version, with 4 in fingers that were straight at the base and curved outward. Code in commit `e02c3d8`.
- **make_pattern.py**: draws the pattern. The settings are at the top. Run `python3 make_pattern.py` to remake everything, including the simulation (a few minutes). Add `--no-sim` to skip the simulation.
- **sim_polyp.py**: the cloth simulation behind `polyp.png`.

The scripts need `numpy`, `scipy`, `matplotlib` and `shapely` (`pip3 install numpy scipy matplotlib shapely`).

## Sizes and settings

| Setting | Value | Meaning |
|---|---|---|
| `TUBE_DIAMETER` | 5.0 in | the tube (15.71 in around) |
| `EASE` | 0.5 in | extra room in the cuff so it slides on (woven fabric). Use about -0.5 for stretchy fabric. |
| `FINGERS` | 6 | number of fingers |
| `CUFF_HEIGHT` | 3.0 in | finished height of the wrist |
| `FINGER_LENGTH` | 6.0 in | finger length on the flat piece, from crotch to tip |
| `FINGER_BULGE` | 0.35 in | how much wider the flat finger gets above its base |
| `SPLAY` | 45° | how far the fingers lean out just above the crotch |
| `TURN_LENGTH` | 1.5 in | the distance over which they turn outward. Above that, they curve back to vertical. |
| `SEAM` | 3/8 in | seam allowance, already added to every edge |
| `HEM` | 1.0 in | extra at the bottom of the cuff for the hem |

The cuff comes out 16.21 in around. At the crotch (where a finger starts), each piece is 2.70 in wide, because six fingers have to fill the cuff's circumference. A sewn finger is about 1.7 to 1.9 in across. The cut piece is about 5.0 × 10.1 in.

## How to sew it

Sew with the right (good) sides of the fabric facing each other, on the dashed line, 3/8 in in from the edge.

- **Cut 12.** Fold the fabric in half, right sides together, and cut 6. Each cut gives a mirror-image pair: the two halves of one finger.
- **Notches** (small triangles on the edges) mark the crotch. Snip no deeper than 1/4 in.
- **Slash to the dot.** The piece has a narrow V coming in from the cuff end. The seam allowances fill the point of the V, so the cut line stops short. Cut along the solid line up to the dot, and stop right at the dot. The dot is the crotch.

1. **Make the fingers.** Pin a pair together. Sew one continuous seam: from the bottom of the cuff strip, along the outer edge, around the tip, back along the inner edge, and down the straight edge of the triangle to its point. Clip into the seam allowance around the curves. Do this 6 times.
2. **Join the fingers.** The V edges of neighbouring halves are the seams between fingers. Sew the cuff edges from the dot to the bottom edge, then the triangle edges from the dot to the point. Go around until all 6 are joined.
3. **Check the middle.** All 12 triangle points meet at the centre of the top. Hand-stitch any small hole closed.
4. **Hem.** Fold the bottom edge up 1/4 in, then up 3/4 in, and stitch.
5. **Turn it right side out** through the wrist and push the fingers out.

## How the pattern shapes the fingers

- **Width.** The flat piece is half the finger's circumference, so a sewn finger is about (2 × flat width) / π across.
- **Curve.** A finger curves when one of its sewn edges is longer than the other. The seam runs in the same plane as the curve.
  - If the inner edge (the side facing the centre of the polyp) is longer, the finger turns outward.
  - If the outer edge is longer, it turns back in.
  - Near the crotch, the inner edge is made longer, so the finger turns out by `SPLAY` over `TURN_LENGTH`.
  - Above that, the outer edge is longer, so the finger eases back to vertical by the tip.
  - To bend the sewn finger by an angle *a*, the flat piece is bent by 2*a*/π. `bend()` in `make_pattern.py` does this along the finger, following `_bend_angle()`.
  - There's a limit. If the turn is too sharp for the finger's width, the outer edge of the flat piece folds over itself. A 45° turn over 1.5 in is safely inside that limit.

## How the 3D picture is made (sim_polyp.py)

This works from the actual pattern piece, not from a drawing of the finished shape:

1. It fills each of the 12 flat pieces with small triangles, about 1/8 in across.
2. It stitches matching seam edges together point by point.
3. It finds the resting shape by minimising energy (scipy L-BFGS):
   - fabric can't stretch, but bunches easily
   - the inside pressure fills it out, as in an inflatable
   - the tube pushes back from inside
   - the hem stays at the bottom of the tube
   - each piece has a little stiffness of its own, and there's none across seams, so ridges and curvature breaks form at the seams

What it leaves out: gravity, fabric thickness, seam-allowance bulk, the inflation air inlet, and the fingers pushing on each other.

**Finding: the inflated fingers can sit two ways, spread out or bunched up.**
- If the simulation starts from straight fingers, it gets stuck with them bunched upright.
- If it starts from the curve the pattern intends, it settles into the spread shape shown above.
- The spread shape holds more air (151 vs 149 cubic in), so inflation favours it.
- In real life, the fingers might need a nudge or a shake while inflating to pop into place.
- To make it reliable, you could add internal ties or a stiffener at the base of each finger.
- The simulation starts from the intended curve, and `polyp.png` shows that result.

**Other results:** the 6 in fingers stand about 5 in tall once inflated, and the tips reach about 3.9 in out from the centre.

## History

- First design: version A (outer + inner halves) and version B (left + right halves). Remy chose B, and A was removed. It's still in the git history.
- Next: B with fingers curving outward from vertical (`previous-outward-curve/`).
- A try at a narrow neck with a bulb near the tip was dropped at Remy's request.
- Now: fingers lean out at the base and curve back to vertical, and they're longer (6 in).
