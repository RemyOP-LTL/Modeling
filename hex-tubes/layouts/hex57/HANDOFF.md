# Handoff: Layout B (hex57) coral skin, Fusion version

Read this first, then `README.md` (how the script works) and `../../FUSION-CONTEXT.md` (the project background). If you are an AI assistant (Cursor, Claude Code), read all three before changing anything. Remy is new to git and to coding, so explain things in plain language and say what a step does before asking Remy to run it.

## Where things stand (2026-10-26)

A Python script lays a pattern of hexagons, pentagons and heptagons on a test tube and shows it in a 3D viewer that opens in a web browser. Fusion is not involved yet. Remy wants to see and spin the pattern first, and bring it into Fusion at the end.

**Look Remy wants:** like a slightly irregular geodesic dome wrapped around a tube. Mostly hexagons, scattered pentagons that never touch each other, and a heptagon for every pentagon.

**Test tube:** 16 in across, 8 ft long, centreline bent on a 20 ft radius. No taper. The pattern also runs 15 in past each end, shown in green and left out of every count, so the 8 ft has no cut-off edge. Ends will be reworked later when branches join.

**Current result (default settings):**

| | |
|---|---|
| Pentagons | about 23 % of the area (58 pentagons, none touching) |
| Pentagon size | about 5 in across, nearly regular (longest side at most 1.08 times the shortest) |
| Biggest round hole that leaves 0.5 in of plastic | about 3.75 in |
| Roundness | corner distance from each shape's centre varies by 14 % on average, 35 % at worst |
| Other shapes | longest side about 1.5 times the shortest on average |
| Flatness | about 0.27 in out of flat on average, 0.6 in at worst |

## What Remy has decided

- Roundness comes first: every corner of a shape about the same distance from its centre. Remy said "lets start with roundness".
- Pentagons should be regular and all the same size, with a round hole in the middle and about 0.5 in of plastic at the closest point. The 5 in hole from earlier needs pentagons 6.45 in across, which won't be round and flat on this tube, so the hole is currently about 3.75 in. **Remy has not yet chosen between a smaller hole and a bigger, bowed pentagon.**
- Pentagon coverage: Remy wanted about 35 % with a slider. The slider exists (about 10 to 24 %). **35 % is not reached.** More coverage needs bigger pentagons and less round neighbours.
- Cells should be largely flat. The pieces will eventually be unrolled into long connected strips of shapes and stitched at the ends. The plastic can flex and bend but not stretch, so edge lengths shared between neighbours must match closely. The script already gives every shared edge one length. Flat patterns are not wanted yet.
- No two pentagons may ever touch.
- Don't force exact flatness if it ruins the shapes. Tolerances for flattening can be forgiving as long as shared edge lengths match.
- The 3D view in the browser comes before Fusion.

## Open questions for Remy

1. Which to give up first: a little roundness, some flatness, or pentagon coverage? (My suggestion: accept slightly less round neighbours for about 35 % coverage and bigger holes, and get flatness from smaller cells.)
2. Hole size: about 3.75 in, or pentagons big enough for a 5 in hole?
3. Remy wrote "1." in the roundness message, which suggests a list. Ask whether more items are coming.

## Files in this folder

| File | What it is |
|---|---|
| `hex57_layout.py` | The script. `python3 hex57_layout.py` takes about 70 seconds. |
| `make_viewer.py`, `viewer_template.html` | Build `viewer.html` from the layouts. The main script runs this for you. |
| `viewer.html` | The 3D viewer. Open it in a web browser. It needs internet the first time (it loads the 3D library from a CDN). |
| `layout.json` | The main result as data: corners, shapes, measurements. Used for the later steps (holes, flat strips). |
| `coverage/layout_*.json` | One layout per step of the viewer's pentagon slider. |
| `preview_*.png` | Static pictures. |
| `README.md` | How the method works, in plain language, with a section per round of changes. |

## Settings that matter (top of `hex57_layout.py`)

| Setting | Now | What it does |
|---|---|---|
| `TUBE_RADIUS`, `TUBE_LENGTH`, `BEND_RADIUS` | 8, 96, 240 | The test tube, in inches. |
| `TUBE_EXTRA` | 15 | Extra pattern past each end. |
| `EDGE` | 3.0 | Side length of hexagons and heptagons. Smaller gives smaller, flatter cells and a smaller hole. |
| `PENT_SIDE_RATIO` | 1.15 | Pentagon side as a multiple of `EDGE`. Higher gives more coverage and bigger holes, but less round neighbours (1.3 reached about 26 %). |
| `HOLE_RIM` | 0.5 | Plastic left around the hole. `HOLE_DIAMETER` is worked out from it. |
| `PENTAGON_COVERAGE`, `COVERAGE_STEPS` | 0.25, 0.10 to 0.30 | The slider's steps. |
| `FLATTEN_MAX_OFFSET` | 0.4 | How far a corner may lift off the tube so cells get flatter. |
| `PENT_WEIGHT`, `SHAPE_WEIGHT`, `SPRING_WEIGHT`, `PLANE_WEIGHT` | 30, 8, 1, 0 | How strongly the corner solver pulls shapes towards regular polygons, edges towards their length, and cells towards flat. `PLANE_WEIGHT` above 0 flattens cells but makes shapes less round. |
| `SEED` | 7 | Change for a different random layout with the same character. |

## What I learned (so you don't repeat it)

- Roundness, flatness and pentagon coverage pull against each other on a curved tube. A regular polygon is flat, but its neighbours' planes meet it at an angle, so corners can't satisfy all three cells at once. Measure all three after any change.
- Letting pentagons be much bigger than their neighbours (ratio 1.7 or more, or pentagons 6.45 in across) gave high coverage but wild, stretched neighbours. Remy rejected that look.
- Flattening corners with a separate "flatten" pass (the old `FLATTEN_ROUNDS`, now 0) doubled the roundness error. Keep flatness inside the solver, with `FLATTEN_MAX_OFFSET` limiting the lift.
- The earlier coverage number counted the greyed extra length in the total, which made it read low. It now counts only shapes inside the 8 ft.
- Pentagon coverage tops out because each pentagon needs five non-pentagon neighbours and a matching heptagon somewhere. Pairs are added by rotating one link (`add_pair`) until the target is reached or none fit.

## Suggested next steps

1. Get Remy's answers to the open questions above, then tune `EDGE`, `PENT_SIDE_RATIO` and the weights.
2. Try to pack more pentagons without hurting roundness (a smarter search than adding random pairs).
3. Hole and cutout data for Fusion (every pentagon centre, normal and size is already in `layout.json`).
4. Bring a real branch from Fusion in as a mesh instead of the test tube.
5. Flat strips for the CNC (later).

## Picking this up on a Mac (Cursor)

This work is merged into `main` of `RemyOP-LTL/Modeling`. In a Terminal:

```
cd ~/Documents/Modeling
git checkout main
git pull
pip3 install numpy scipy matplotlib
cd hex-tubes/layouts/hex57
python3 hex57_layout.py
open viewer.html
```

(If `git checkout main` complains about unsaved changes, run `git status` and ask for help before going on.)

Then open the `~/Documents/Modeling` folder in Cursor and tell its assistant: "Read hex-tubes/layouts/hex57/HANDOFF.md first."

Save your own changes with `git add -A`, then `git commit -m "what changed"`, then `git push`. Commit after each change you're happy with and push at the end of a session.
