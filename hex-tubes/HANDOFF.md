# Handoff: pentagon "origami" tubes in Onshape

## Goal
Remy is building a 20 ft animatronic coral. The branches are lofted tubes modeled in Onshape. The aim is to turn each tube into an irregular faceted skin of pentagons, then make it from 1 mm plastic sheet. A CNC router scores the fold lines.

Two outputs are wanted:
1. A 3D surface preview in Onshape. Thickness is not needed for now.
2. A flat pattern of strips with SCORE and CUT lines, exported as DXF for the CNC.

## What Remy decided
- **Pentagon pattern:** Cairo pentagonal tiling.
- **Layout:** Organic, with a fixed count around the tube. Remy prefers the irregular look this gives, where pentagons stretch with the taper (`pentagon-organic-preview.png`). The "even sizing" layout exists but was rejected.
- **Flat faces:** don't force pentagons flat. Flattening passes default to 0, because forcing flatness turns the pentagons into long strips. The slight bow is fine, since 1 mm plastic flexes.
- **Thickness:** surface only for now. "Keep surface" is on by default.
- **CNC pattern:** one strip per row of pentagons around the tube, cut open at the seam. Strips are labelled R1…Rn. Optional glue tabs, default off.
- **File header:** the .fs file starts with Remy's header, `FeatureScript 3095;` plus the `common.fs` 3095.0 import. That lets Remy select all, delete and paste the whole file.

## Files
| File | What it is |
|---|---|
| `pentagonOrigamiTube.fs` | **Pentagon feature (v8).** Lives in Feature Studio 1. |
| `soccerSkin.fs` | **Soccer skin feature.** Lives in the "Soccer skin" Feature Studio. |
| `pentagonFrameSkin.fs` | **Pentagon frame skin.** Flat regular pentagons (set by diameter) with cut-out connecting shapes; organic or grid layout. Lives in the "Pentagon frame skin" Feature Studio. |
| `pentagonHoleSkin.fs` | **Pentagon hole skin (v1).** A copy of the frame skin v4 (made by the pentagon session, so the frame skin session's file is untouched) with: pentagon size measured edge to edge (7 in), 50 % coverage, a 6 in round hole in each pentagon, a 1.5 in round hole at every corner of 3+ faces, and connecting-shape cutouts with curved sides (1 in rim at corners, 0.5 in at edge middles). Lives in the "Pentagon hole skin" Feature Studio. Round holes are not on the flat pieces yet. |
| `README.md` | User-facing notes on the feature and its options. |
| `pentagon-organic-preview.png` | The look Remy wants. |
| `flat-pattern-preview.png` | What the flat pattern should look like. Rendered in Python from the same algorithm. |
| `pentagon-flat-preview.png` | The rejected even-sizing layout, for reference. |
| `hexLatticeTube.fs` | First, superseded attempt: hex holes or struts. Ignore it unless it's useful as reference. |
| `prototypes/*.py` | Python/numpy prototypes of the tiling, flattening, flat pattern and previews. They are the reference for the math. |

## Feature pipeline (`pentagonOrigamiTube.fs`)
1. **Inputs:** the user picks one tube side face (the loft's single side face). The feature auto-detects which UV direction is closed, meaning the direction that goes around the tube (`detectSwap`). A "Swap" checkbox inverts that guess.
2. **Layout:** Organic layout uses N around (must be even). M rows come from the length ÷ circumference ratio when auto rows is on. Tiling coords run x ∈ [0,N) around and y ∈ [0,M] along. y is clamped at the tube ends. Vertices are shared using string keys.
3. **Cairo pentagon per grid square (i, j):** grid points with an even coordinate sum are 4-valent. Odd points split into a diagonal "bar" of half-length t = (−2+√28)/12. See `cairoPentagon` and `barEnd`. This was verified in Python: the tiles have no gaps or overlaps, and all edges are equal in the flat pattern.
4. **Positions:** the UV points are evaluated on the loft with `evFaceTangentPlanes` in one batched call.
5. **Flattening:** optional, default off.
6. **Flat pattern** (`buildFlatPattern`): each row is unrolled pentagon by pentagon. Each pentagon goes into its own best-fit-plane coordinates, viewed from outside. It is then rigidly rotated onto the edge it shares with the previous pentagon. Each strip is rotated to run horizontal and stacked. The output is two sketches on the Top plane, plus an R12 DXF printed via `println` (in mm, with layers SCORE, CUT and LABELS).
7. **3D surface:** each pentagon is filled with `opPolyline` (a closed loop) followed by `opFillSurface`. If that fails, it falls back to three flat triangles, each built as sketch → thin `opExtrude` → `opExtractSurface` of the start cap. The faces are then joined with `opBoolean` UNION, using `recomputeMatches: true` and `eraseImprintedEdges: false`. Thickening only runs if "Keep surface" is off.
8. **Error reporting:** the whole body is wrapped in try/catch with a `stage` string, so errors read "Failed while <stage>: …". Faces that fail are skipped, and up to three are logged to the console.

## Status (2026-10-12)
- **The per-face parent Id fix works.** Run in Onshape with 12 around: all 792 pentagons built and joined into one surface. See `onshape-result-v6.png`.
- **v7** names the output surface "Skin - N faces".
- **v8** adds "Irregularity (%)" (default 60) and "Random seed" to the Organic layout: each corner gets a random nudge plus slow waves in tiling coords (`irregularOffset`). Pushed and compiles, but **not yet seen in Onshape**: "Pentagon origami tube 1" was found suppressed right after the push (not by the pentagon session). Flat check of the math: `pentagon-irregular-preview.png`.
- **Open oddity:** after the join, most faces report 6 to 9 edges instead of 5, so the union is splitting edges. The render looks fine. Cause not found.
- **Flat pattern output** (Output = "Flat pattern") has still not been run in Onshape. Remy has parked it to focus on the skin shape.
- **Second feature:** `soccerSkin.fs` (hexagons with pentagon/heptagon pairs, pentagons evened out) lives in its own Feature Studio, "Soccer skin". `soccer-pattern-options.png` shows the look Remy picked (option B).

## Working with Onshape directly
Claude Code on Remy's Mac can reach Onshape through the API. Keys are in `onshape-keys.env` at the repo root, which git ignores. Use `tools/onshape.sh` (run it with no arguments for usage); it never prints the keys.

| Thing | Id |
|---|---|
| Document "Pentagon skin" | `ef4e23f1db1545ca302d904d` |
| Workspace | `00b81bd11d173342d88b8eaf` |
| Part Studio 1 | `b35857b7883c52300e570341` |
| Feature Studio 1 (`pentagonOrigamiTube.fs`) | `162de2a1b289c2461c142705` |
| Feature Studio "Soccer skin" (`soccerSkin.fs`) | `9a42fdc18ebb8a37cc25c482` |
| Feature Studio "Pentagon frame skin" (`pentagonFrameSkin.fs`) | `651c48def67875bfee100a00` |
| Feature Studio "Pentagon hole skin" (`pentagonHoleSkin.fs`) | `fb40c94056db7e6ac3f642ec` |

- Pushing a `.fs` file to its Feature Studio rebuilds the Part Studio by itself. With about 800 faces the rebuild takes around two minutes, and API calls wait for it, so use a long timeout.
- The tube is "Sweep 1", a sheet part. The skins sit just inside it, so render the skin part on its own (`onshape.sh view`), not the whole Part Studio.
- `bodydetails` on the whole Part Studio lists solids only; ask for the part by id to see a surface's faces and edges.
- Remy's Onshape plan is Free. API calls may be capped per year, so don't poll in a loop.

## Onshape gotchas learned
- Ops under a parent Id must be contiguous in history. See the status section above.
- Checkbox UI defaults need `"Default" : true` in the annotation. The `defineFeature` defaults map doesn't set what the UI shows.
- `opFillSurface` requires `guideVertices`, as well as `edgesG0`, `edgesG1` and `edgesG2`. Pass `qNothing()` for any you don't use.
- Onshape shows only the last `reportFeatureWarning`, so combine messages into one.
- A loft can be parameterized with u running along the tube, so don't assume u goes around.
- Std library source mirror, for checking op signatures: `https://raw.githubusercontent.com/javawizard/onshape-std-library-mirror/master/<file>.fs`.
- The stack trace text in the Feature Studio error viewer is the most useful thing Remy can paste if the API can't show an error.

## Suggested next steps
1. Pentagon skin: whatever Remy asks for on the shape; find the cause of the split edges.
2. Soccer skin: get it building, then tune the count and evenness of the pentagons with Remy.
3. Later: flat pattern for the CNC, splitting long strips to fit the bed, branched coral.
