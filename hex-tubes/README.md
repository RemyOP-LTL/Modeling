# Pentagon origami tube (current approach)

`pentagonOrigamiTube.fs` (v5) rebuilds a loft's side face as **one solid shell of pentagons** that follows the same path.

- **Layout: Organic (default).** This keeps a fixed number of pentagons around the tube. They stretch and shrink with the taper and with how the loft is parameterized, which gives the irregular look in `pentagon-organic-preview.png`. The Cairo pentagons are only equal-sided in the flat pattern. Once wrapped onto the tube they distort, which is why they look uneven.
- **Layout: Even sizing.** Bands with their own count around, joined by filler-triangle rings (`pentagon-flat-preview.png`).
- **Merged:** neighbouring pentagons share exact straight edges. The faces are knitted into one surface and thickened (default 1 mm).
- **Faces bow slightly:** on a curved tube, a pentagon's corners don't lie in one plane, so each face bows a little. That's about 2–3% of the tube radius at 12 around, and roughly a quarter of that at 24 around. 1 mm plastic flexes that much easily.
- **Perfectly flat options (both change the look):**
  - *Flattening passes* moves the vertices until every pentagon is flat. Neighbours tend to merge into long flat strips, and the tube's shape shifts.
  - *Split non-flat pentagons* turns each pentagon into 5 shallow triangles.
- **Output: Flat pattern (score + cut lines)**, for CNC scoring. Each row of pentagons is unrolled into one strip, cut open at the seam. Strips are labelled R1 to Rn, starting at one end of the tube. See `flat-pattern-preview.png`.
  - **SCORE lines:** fold lines between pentagons in the same strip.
  - **CUT lines:** the strip outlines.
  - **Where it appears:** as two sketches on the Top plane, beside the tube.
  - **Glue tab depth:** adds trapezoid tabs on each strip's top edges and on its seam end. The tab's base is scored. Default 0 means no tabs.
  - **Getting the DXF:** with *Print DXF to console* on, the feature prints a DXF in millimetres with layers SCORE, CUT and LABELS. Open the FeatureScript console in the Part Studio (the notices panel, Console tab). Copy everything between `DXF START` and `DXF END` into a text file and save it as `pentagon-strips.dxf`.
  - **Accuracy:** each pentagon is flattened onto its best-fit plane. On the sample, shared edges matched within about 0.1 mm.
  - **Layout:** works with the Organic layout only.
- **Untested in Onshape.** If it errors, send the message.

---



# Hex lattice tubes in Onshape

## Approaches, best fit first

1. **Custom FeatureScript (recommended).** Fully parametric and lives in your feature tree, so it updates when you change the loft. Cells are mapped onto the face's UV grid, so they follow any loft shape and taper. Starter: `hexLatticeTube.fs`.
2. **Existing public FeatureScripts.** In Onshape, open the Custom Features menu or search the FeatureScript section of forum.onshape.com for "honeycomb", "hex pattern", "voronoi" or "lattice". Most of these work on flat faces or cylinders rather than arbitrary lofts, so check before investing in one.
3. **Onshape-native workaround (Wrap + pattern).** Sketch a flat hex grid, then use Wrap to lay it onto the tube and cut with the wrapped faces. Wrap only works on cylinders and cones, so it suits straight or evenly tapered segments, not organic coral branches.
4. **Grasshopper (Rhino) or Blender.** Use these when you want things the starter can't do easily: hex size that varies with curvature, Voronoi cells, organic branching, or very high cell counts. Grasshopper with LunchBox or Weaverbird does hex panels on any surface in a few components. The catch is you lose live links to Onshape, and you round-trip via STEP or mesh.

## Installing the starter

1. In your Onshape document, click **+** (bottom left) and choose **Create Feature Studio**.
2. Keep the two header lines Onshape writes at the top. Delete everything else.
3. Paste everything from `hexLatticeTube.fs` below the `8<` line, then click **Commit**.
4. Go back to your Part Studio. The feature appears as **Hex lattice tube** in the toolbar (in other documents, add it via **Custom features**, the puzzle-piece icon).

## Using it

- **Tube side face:** pick the loft's outer side face.
- **Mode:**
  - *Cut holes in existing solid:* use this when your loft is already a solid tube (for example loft then Shell). Pick the solid too.
  - *Thicken surface into lattice:* use this when your loft is a surface. Set the wall thickness.
- **Cells around / Rows along:** grid density. Start small (around 8 × 10) because every cell is its own sketch and extrude, and regeneration slows down past a few hundred cells.
- **Strut width:** the material left between holes.
- **Cut depth:** make it a bit more than the wall thickness. It is centered on the picked face.
- **Swap around/along:** turn this on if the hexes run the wrong way, because which loft direction is "U" varies.
- **Closed tube:** leave this on for a closed loft so cells wrap across the seam.

## Known limits

- It expects the tube side to be **one face**. If your loft profile is split into several edges, the side becomes several faces. Use a single closed spline or circle per profile so the side is one face.
- Cell shape follows the loft's parameterization. Uneven profile spacing or a big taper stretches cells, so increase rows along to compensate.
- Holes are straight prisms along the normal at each cell's center. On tight curvature, strut width varies slightly through the wall thickness.

## Fabrication at 20 ft scale

- **Split it into segments** sized to your fabrication bed (3D printer, CNC, or laser). Add bolted flanges or sleeves at the joints, and line segment breaks up with a hex row.
- **For a metal frame,** use the cell centers or edges as a node-and-strut map instead of cutting a shell. That means welded rod or tube along the hex edges, with skin or mesh over it. The FeatureScript's grid math can be reused to output just the edges.
- **For an animatronic,** a lattice is light but flexible. Keep a solid spine or internal frame for the actuators and mount points, and treat the hex shell as cladding. Leave larger cells or removable panels where you need access to the mechanisms.
- **Check strut width** against your material and process (for example the minimum feature size for printing, or kerf for plasma or laser), and plan for wind and outdoor loads if it's installed outside.
