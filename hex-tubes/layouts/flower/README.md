# Layout C: pentagon "flower" clusters

One of three layout experiments for the coral skin. In this one, each seed on the surface becomes a **flower**: a centre pentagon with a ring of five pentagons around it, like the flat pentagon pattern in Remy's reference image. The space between flowers is filled with **connecting shapes** (fillers) of 4 to 8 sides. Every corner is shared exactly by the panels that meet there.

Two variants are built from the same layout:

| | **Gaps closed** | **Gaps open** |
|---|---|---|
| What happens to the thin gaps between ring pentagons | Zipped shut: the ring pentagons share edges, and the flower bends into a dome | Left open as diamond-shaped cutouts (white) |
| Picture | `preview-3d-closed.png` | `preview-3d-open.png` |

Also included: `preview-closeup.png` (one flower, both variants) and `preview-unrolled.png` (the skin cut along one line and laid flat).

## How to run it

```
cd hex-tubes/layouts/flower
python3 flower_layout.py
```

It needs Python 3 with **numpy**, **scipy** and **matplotlib** (`pip install numpy scipy matplotlib`). It runs on its own, outside Fusion, and takes about 15 seconds. It prints the metrics and rewrites `layout.json` and the four `preview-*.png` pictures in this folder.

## The method in plain language

1. **Test surface.** The script builds a stand-in for a branch: a tube 12.5 in across and 72 in long, gently curving, tapering to about 8 in across at the ends. It is stored as a **triangle mesh** (a surface made of many small triangles, like an STL file). Everything after this step only uses the mesh, so a real branch exported from Fusion as a mesh could be swapped in.
2. **Flower seeds.** Seed points are scattered with **Poisson-disk sampling**: random positions, but no two closer than a minimum distance. That gives "even but organic". Each flower gets its own size between `SIZE_MIN` and `SIZE_MAX`. Big and small sizes are tried alternately so that big flowers still find room.
3. **Flower shape.** From each seed, five more seed points are placed one pentagon-width away, at 72° steps. The script places them by walking along the surface, not through the air, so they follow the curve.
4. **Filler seeds.** The rest of the surface gets more Poisson-disk seeds. Their size blends smoothly between the sizes of nearby flowers.
5. **Growing the shapes.** Every point of the surface is claimed by its nearest seed, with big seeds counting as a little nearer. This is called a **Voronoi pattern**: each seed "spreads out" until it meets its neighbours. A spot where three regions meet becomes a **corner**, and that one corner is shared by all three panels.
6. **Clean-up.** Some flower panels come out with 6 or 7 sides, and some fillers with too many. The script fixes these with **edge flips**: an edge between two panels is swapped to run between the two panels at its ends. The two panels beside it each lose a side, and the two at its ends each gain one. After this, every flower panel has exactly 5 sides.
7. **Relaxing.** The corners are then nudged thousands of times until the panels settle. Pentagons try to keep their true cut size (plastic doesn't stretch). Fillers try to stay even. Every panel tries to be flat, and every corner tries to stay on the surface. These wishes fight each other, and the corners end up at the best compromise. The metrics show how far from perfect it ends up.
8. **Gaps open variant.** Before relaxing, the edge between each pair of neighbouring ring pentagons is "unzipped" into a thin diamond. The diamond's tip sits on a corner of the centre pentagon. This is the open star/diamond look from the reference image, 5 slits per flower.

**Why the closed version bumps.** Three pentagon corners make 3 × 108° = 324°, which is short of 360°, so a closed flower wants to be a dome, like the top of a soccer ball. A tube can't take a dome, so fillers with 7 or 8 sides form around each flower to cancel it out (more than 360° makes a saddle). The script doesn't place these; they come out of step 5 on their own. Leaving the gaps open removes most of the fight, so each flower can lie much closer to the tube.

## Results (default settings, random seed 11)

7 flowers, from 3.1 to 6.8 in across. That makes 42 flower pentagons plus 108 fillers: 150 panels and 300 corners.

| | Gaps closed | Gaps open |
|---|---|---|
| Panels by sides (excluding flower pentagons) | 4: 9, 5: 32, 6: 19, 7: 20, 8: 25, 9: 3 | same layout; fillers next to a cutout gain 2 notch edges, so many read as 9–10 |
| Size across, all panels (min / avg / max) | 1.4 / 4.5 / 8.0 in | 1.8 / 4.4 / 7.6 in |
| Size across, flower pentagons | 2.9 / 4.3 / 6.8 in | 3.0 / 4.3 / 6.8 in |
| Out of flat, per panel (max / avg) | 1.08 / 0.32 in | 1.05 / 0.30 in |
| **Bumpiness**: corner distance from the tube (max / avg) | **1.81 / 0.37 in** | **1.43 / 0.22 in** |
| Furthest flower-pentagon corner from the tube | about 0.85 in for 3 in flowers, 1.8 in for 7 in flowers | about 0.3 in for 3 in flowers, 1.0 in for 7 in flowers |
| Flower pentagons: edges within / angles within | 9 % / 21° | 5 % / 8° |
| Flower pentagons' share of the skin | 26 % | 26 % |
| Open area | none | 35 diamonds, 161 sq in = **6 %** of the skin |
| Diamond opening at the centre pentagon | – | 13° to 54°, average 32° (36° when the flower lies flat) |

"Size across" is the width of a circle with the same area. For a pentagon that is about 7 % more than its flat-to-flat width. The average flower came out at 4.3 in rather than 5 in, because on a tube this thin the big flowers run out of room first.

## `layout.json`

All lengths are in inches. The file has `parameters`, plus one entry per variant under `variants.closed` and `variants.open`. Each variant has:

- `vertices`: the corner positions `[x, y, z]`. Each corner is listed once and shared by every panel that uses it.
- `vertex_normals`: the surface direction at each corner (pointing outwards). Useful later for drilling corner holes square to the skin.
- `vertex_panel_count`: how many panels meet at each corner. The planned 1.5 in corner holes go where this is 3 or more.
- `polygons`: one entry per panel, with `vertices` (indices into the list above, counter-clockwise seen from outside), `sides`, and `kind`. `kind` is `pentagon` (a flower panel), `filler`, or `gap` (an open cutout, only in the open variant). Each panel also records its `flower` number and its `role` (`centre`, `ring`, `filler` or `gap`).
- `metrics`: the numbers printed by the script.

Holes aren't built yet. The pentagon centre holes, corner holes and filler cutouts can all be placed later from this data.

## Parameters you can tweak (top of `flower_layout.py`)

| Name | Default | What it does |
|---|---|---|
| `SIZE_MIN`, `SIZE_MAX` | 3, 7 | Range of flower sizes, in inches across |
| `FLOWER_SPACING` | 3.0 | Minimum distance between flower centres, as a multiple of their average size. Bigger means fewer flowers with more filler between them. |
| `FILLER_SIZE_SCALE` | 0.8 | Filler size compared with nearby flowers. Smaller means more, smaller fillers. |
| `FILLER_SIZE_JITTER` | 0.15 | Random ± variation of filler size |
| `FILLER_SPACING` | 1.0 | How tightly filler seeds pack |
| `KEEP_OUT` | 1.4 | Keeps filler seeds out of a flower's middle |
| `MAX_FILLER_SIDES` | 8 | Fillers with more sides than this are fixed with edge flips |
| `POWER_ALPHA` | 0.3 | How much big seeds push their borders outwards |
| `RANDOM_SEED` | 11 | Change it for a different random layout with the same rules |
| `W_*` | | How hard each wish pulls during relaxing. For example, raise `W_SURFACE` to hug the tube more at the cost of less regular pentagons. |
| `TUBE_LENGTH`, `TUBE_DIAMETER` | 72, 12.5 | Size of the test tube |

## Pros and cons

**Pros**
- It really does look "organic plus geometric". Crisp pentagon flowers are scattered unevenly, in different sizes, with soft irregular fillers between them.
- With gaps open, the pentagons stay close to true regular pentagons (edges within 5 %). The flowers hug the tube (0.3 to 1 in), and the diamonds already read as decorative cutouts.
- Every corner is shared, and the data is ready for holes. It works from a mesh, so it isn't tied to a neat loft.
- A closed flower is exactly a rounded cap: 6 pentagons, like a soccer ball's top. That is what a branch **tip** needs (see next steps).

**Cons**
- Flowers are big for this tube. A 7 in flower is about 20 in across, half the way around a 12.5 in tube. Closing its gaps lifts or sinks corners by up to 1.8 in, which is very visible.
- Fillers right around each flower have 7 or 8 sides and get large (dark blue in the pictures). That is geometry, not a bug: they cancel the flower's dome. But they look blobby next to the crisp flowers.
- Some fillers come out very small (down to about 1.4 in) where flips squeezed them. They would need merging before cutting.
- Panels are up to about 1 in out of flat, so a few would need to bend noticeably or be split.

## What I'd change next

1. **Use the open version, and size flowers to the branch.** Tie the flower size to the local branch thickness (for example 25–35 % of the circumference) rather than a fixed 3–7 in.
2. **Put a closed flower on each branch tip.** A rounded tip needs about 6 pentagons, which is one closed flower. Test it on a tube with one closed, rounded end.
3. **Tidy the fillers.** Merge fillers smaller than about 2 in into a neighbour. Then smooth sizes so the ring of 7/8-sided fillers around each flower is less blobby, for example by adding 5 filler seeds in the notches between petals (`NOTCH_RADIUS` > 0 tries this).
4. **Second ring (optional).** Add the next ring of 10 pentagons from the reference image, which brings back the five-pointed star gaps. This only makes sense on thicker branches.
5. **Then** holes and the Fusion build script that reads `layout.json`.
