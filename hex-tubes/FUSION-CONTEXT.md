# Context: pentagon skin with holes, to try in Fusion 360

This file gives a new conversation the background on Remy's project. It covers the goal, the design decisions so far, the geometry, and what was learned building it in Onshape. Read all of it before proposing an approach. The starting point in Fusion may be different (see "Starting points"), so treat the Onshape method as one way to do it, not the only one.

## Who and what
- **Remy** is building a **20 ft animatronic coral**. The branches are smooth tube-like forms. Each branch gets a decorative skin made from **1 mm plastic sheet**: flat panels are cut on a **CNC router**, fold lines are scored rather than cut through, and the panels are folded and joined into the 3D shape.
- **Remy is new to** git, the command line and CAD scripting (FeatureScript, and now Fusion's API). Explain in plain language, define terms the first time, give one recommended path, and say what a step or script does before asking Remy to run it.

## What the skin should look like (Remy's decisions)
- **Pentagons with connecting shapes between them.** Pentagons are the main panels. They don't touch each other; the gaps between them are filled with "connecting shapes" (hexagons, heptagons, triangles, whatever the layout produces).
- **Organic, irregular look** is preferred over neat rows. Random placement and some size variation are good.
- **Pentagon size: 7 in measured edge to edge** (across the flats). That makes them 8.65 in corner to corner, with sides of about 5.09 in and an area of about 44.5 sq in.
- **Coverage: about 50 % of the surface is pentagons** (counting each pentagon's full outline, before holes).
- **Holes:**
  - **A 6 in round hole in the middle of every pentagon.** That leaves a 0.5 in rim at the edge middles.
  - **A 1.5 in round hole at every corner** where three or more panels meet. Corners are where folds fight each other, and a hole there means the plastic doesn't have to resolve them. With 7 in pentagons this leaves about 0.58 in between a corner hole and the pentagon's big hole.
  - **A cutout in each connecting shape with curved sides.** The plastic rim around it is **1 in wide at the corners and narrows to 0.5 in at the middle of each edge**. That thinner middle lets the plastic flex as the panels close up into the 3D shape. Corners of the cutout are rounded. Shapes too small for a cutout stay solid.
- **Don't force panels perfectly flat.** A slight bow is fine, since 1 mm plastic bends, especially in one direction like a cylinder. Forcing flatness in Onshape distorted the pattern badly.
- **Outputs wanted:**
  1. A 3D preview of the skin on the shape (a surface is fine; thickness isn't needed).
  2. Later: flat pieces for cutting, with SCORE (fold) and CUT lines, exported as **DXF** to fit **48 × 96 in sheets**, with seam allowances or tabs for joining. Remy parked this to focus on the 3D look first.

## Geometry facts and lessons (from the Onshape work)
- **The test tube is fairly small relative to the pentagons.** It is about 12.5 in across (circumference about 39 in) and about 6 ft long, tapering and curving. These figures are estimated from feature output; measure the real model. A pentagon 8.65 in across sits on a 12.5 in tube like a playing card on a can, so its corners end up about 0.9 in off the surface. Only about four pentagons fit around. Either accept a coarse look on thin branches, use smaller pentagons there, or vary size with branch thickness. This is worth raising with Remy early.
- **Coverage vs "pentagons never touch".** If pentagons can't touch, at most roughly a quarter to a third of the cells can be pentagons. To still reach 50 % of the area, each pentagon must be about 3 to 4 times the area of a connecting shape. Starting from a uniform grid and then stretching some cells that much crushes their neighbours. Better: lay the pattern out with pentagons at their real size from the start, then fill the gaps.
- **Every corner should be shared** by the neighbouring panels, with identical coordinates, so the panels line up exactly and can be joined into one surface.
- **Make holes after joining**, and make them in bulk: one cylinder per hole, one split with all the cylinders, one delete of the inside pieces. Only delete a piece if its area is about the hole's size, so a failed split never deletes a whole panel.
- **Don't trust "fill a surface across a non-flat outline"** when shapes are distorted. In Onshape that failed on dozens of faces, and then joining the faces failed too.

## Layouts tried in Onshape (for ideas)
1. **Cairo pentagon tiling** (every tile a pentagon, wrapped around the tube). Works cleanly, but it's all pentagons with no connecting shapes.
2. **Cairo grid with a gap.** Each Cairo pentagon is shrunk to a regular pentagon and the gaps become small 3- and 4-sided connecting shapes. The gap sets coverage, and 50 % is easy to reach, but pentagons sit in rows.
3. **Honeycomb with rotated edges** (the "frame skin", Remy's preferred look). Hexagons wrap the tube. Turning random edges a quarter turn creates scattered pentagon/heptagon pairs where pentagons never touch, and the vertices are then relaxed until the pentagons reach their size. It looks organic, but with 7 in pentagons and 50 % coverage the cells get too distorted (see below).
4. **"Soccer ball" style:** hexagons with scattered pentagon/heptagon pairs, pentagons evened out.

## Current Onshape status (2026-10-16), feature "Pentagon hole skin"
Built with layout 3, 7 in pentagons, 50 % coverage and 5 in connecting shapes:
- **Layout:** 8 around and 18 rows: 30 pentagons (51 %) and 114 connecting shapes.
- **Distortion:** pentagons average 0.87 in out of flat.
- **Failures:** 55 of 144 faces couldn't be built, so the faces couldn't be joined into one surface.
- **Holes:** all 30 pentagon holes and 265 of 272 corner holes were cut, but only 17 connecting shapes had room for a cutout.

**Conclusion:** the hole method works. The layout is the problem: the honeycomb approach can't carry big pentagons at 50 % on a tube this thin. Pick a new layout method.

## Starting points Remy may use in Fusion (generalize to these)
The input may not be a clean lofted tube. Possibilities:
- a T-spline or Form body (sculpted branch, possibly branching or forked);
- a mesh (STL or OBJ) of the coral;
- a solid or surface body made some other way;
- a flat design first (draw the pattern flat, then work out how it wraps).

So prefer a method that works on **any surface**, not only one with a neat "around × along" grid. Here is one tool-neutral pipeline to consider; it is a suggestion, not a requirement:
1. **Sample the surface.** Place the pentagon centres with roughly even spacing (Poisson-disk sampling, sized so that 50 % coverage comes out), making sure no two pentagons are closer than a pentagon width plus a minimum gap.
2. **Place the pentagons.** Put a 7 in regular pentagon on the tangent plane at each centre, turned to a random angle or aligned to the neighbours.
3. **Fill the gaps.** Fill the space between the pentagons with connecting polygons, for example by triangulating the gaps between pentagon corners and then merging triangles into 3- to 6-sided shapes. Every corner must be shared.
4. **Check before building.** Measure coverage, how far each panel is from flat, and panel sizes before making any CAD geometry.
5. **Build and cut.** Make the faces, join them, then cut the holes in bulk as described above.
6. **Flat pattern (later).** Unfold panels into strips or pieces that fit on 48 × 96 sheets, with SCORE and CUT layers in the DXF.

Generating the pattern as plain data (point and polygon lists) in a standalone Python script first, then building it in Fusion, makes the math easy to preview and debug. Check what Python libraries Fusion's built-in Python has before relying on any (for example numpy) inside a Fusion script.

## Fusion 360 notes (verify; not tested yet)
- Fusion scripts are Python, run from **Utilities → Add-Ins → Scripts and Add-Ins**.
- Fusion can use API keys or cloud access differently from Onshape; nothing from the Onshape setup carries over. Don't look for or reuse the Onshape API keys file.

## Reference files (in Remy's project folder, if the new thread can see it)
`/Users/remy/Documents/Modeling/hex-tubes/`:
- `pentagonHoleSkin.fs`: the Onshape version with holes. Useful functions: `curvedHole` (curved-side cutout), `roundHoles` (bulk round holes), `organicLayout` (honeycomb with rotated edges), `regularPentagon`, and the `flatPieces` flat-pattern code.
- `prototypes/*.py`: Python prototypes of the tilings and flat patterns.
- `pentagon-organic-preview.png`, `pentagon-irregular-preview.png`, `pentagon-gap-options.png`, `frame-skin-v*.png`: pictures of looks Remy has seen.
- `HANDOFF.md`: the Onshape project notes.
