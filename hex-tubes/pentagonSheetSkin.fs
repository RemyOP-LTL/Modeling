FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");

// Pentagon sheet skin — FeatureScript for Onshape   (v1)
//
// The pentagon frame skin, redesigned to be cut from one flat plastic sheet and
// rolled into a tube.
//
// The pattern is designed on the flat sheet itself: the tube unrolled into a
// rectangle as wide as the tube is round and as long as the tube is long. A
// honeycomb covers it, wrapping around from the right edge to the left. Random
// hexagon edges are turned a quarter turn, which makes pentagon / heptagon pairs
// (pentagons never touch). Each pentagon is then made an exact regular pentagon of
// the chosen size, and each connecting shape gets a hole with rounded corners.
//
// Seam: every face goes whole to one side of the sheet, so the two long edges are
// matching zigzags along pattern edges that lock together when the sheet is rolled.
//
// Outputs:
//   - Sketches on the Top plane beside the tube: the cut pattern, and optionally
//     score lines; a 48 x 96 in stock outline for reference.
//   - A DXF printed to the FeatureScript console (mm; layers CUT and SCORE).
//   - A 1 mm thick flat sheet part, pentagons coloured.
//   - A 3D preview: the flat pattern mapped onto the tube (fraction around, distance
//     along), with holes cut and pentagons coloured.
//   - The sheet size, and on bent tubes how much longer / shorter the tube's surface
//     is than the sheet (what the frames would have to absorb).
// Tapered tubes use the local circumference for each row of the sheet. That is an
// approximation (a true cone unrolls into a curved fan).
//
// INSTALL: in a Feature Studio, select all, delete, paste this whole file, Commit.
//
// ------------------------------------------------------------------- 8< ----

export enum SheetPreview
{
    annotation { "Name" : "Wrapped surface" }
    SURFACE,
    annotation { "Name" : "Outlines on the tube (fast)" }
    WIRES,
    annotation { "Name" : "None" }
    NONE
}

const CONNECTOR_BOUNDS = {
            (meter) : [0.005, 0.127, 5],
            (centimeter) : 12.7,
            (millimeter) : 127,
            (inch) : 5,
            (foot) : 0.4167,
            (yard) : 0.1389
        } as LengthBoundSpec;

const DIAMETER_BOUNDS = {
            (meter) : [0.005, 0.127, 5],
            (centimeter) : 12.7,
            (millimeter) : 127,
            (inch) : 5,
            (foot) : 0.4167,
            (yard) : 0.1389
        } as LengthBoundSpec;

const OFFSET_BOUNDS = {
            (meter) : [0.0005, 0.0127, 1],
            (centimeter) : 1.27,
            (millimeter) : 12.7,
            (inch) : 0.5,
            (foot) : 0.0833,
            (yard) : 0.0278
        } as LengthBoundSpec;

const RADIUS_BOUNDS = {
            (meter) : [0, 0.0127, 1],
            (centimeter) : 1.27,
            (millimeter) : 12.7,
            (inch) : 0.5,
            (foot) : 0.0833,
            (yard) : 0.0278
        } as LengthBoundSpec;

const PERCENT_BOUNDS = { (unitless) : [0, 35, 50] } as RealBoundSpec;
const VARIATION_BOUNDS = { (unitless) : [0, 50, 60] } as RealBoundSpec;
const SEED_BOUNDS = { (unitless) : [1, 1, 100000] } as IntegerBoundSpec;
const PASS_BOUNDS = { (unitless) : [0, 100, 1000] } as IntegerBoundSpec;

// Honeycomb with hexagons 1 unit wide, points up: row spacing and corner heights.
const ROW_PITCH = 0.8660254037844386;   // sqrt(3) / 2
const HEX_HALF = 0.5773502691896258;    // 1 / sqrt(3)
const HEX_QUARTER = 0.2886751345948129;
// Diagonal / side of a regular pentagon.
const GOLDEN = 1.618033988749895;
const INCH = 0.0254;
// Cutouts whose corners would need to be sharper than this are left out.
const MIN_HOLE_RADIUS = 0.0025;
// Plastic sheet stock and thickness.
const STOCK_SHORT = 48 * 0.0254;
const STOCK_LONG = 96 * 0.0254;
const SHEET_THICKNESS = 0.001;
// Rows of the circumference table (taper), and spacing of points on 3D curves.
const CIRC_ROWS = 20;
const CURVE_STEP = 0.006;

annotation { "Feature Type Name" : "Pentagon sheet skin",
             "Feature Type Description" : "Pentagon frame pattern designed on one flat sheet that rolls into the tube: cut pattern, DXF and a wrapped preview." }
export const pentagonSheetSkin = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Tube side face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        annotation { "Name" : "Pentagon diameter (circle through its corners)" }
        isLength(definition.pentagonDiameter, DIAMETER_BOUNDS);

        annotation { "Name" : "Connecting shape size" }
        isLength(definition.connectorSize, CONNECTOR_BOUNDS);

        annotation { "Name" : "Pentagons (% of faces)" }
        isReal(definition.pentagonPercent, PERCENT_BOUNDS);

        annotation { "Name" : "Size variation of connecting shapes (%)" }
        isReal(definition.sizeVariation, VARIATION_BOUNDS);

        annotation { "Name" : "Random seed" }
        isInteger(definition.seed, SEED_BOUNDS);

        annotation { "Name" : "Evening passes" }
        isInteger(definition.passes, PASS_BOUNDS);

        annotation { "Name" : "Cut out connecting shapes", "Default" : true }
        definition.cutOut is boolean;

        if (definition.cutOut)
        {
            annotation { "Name" : "Cutout offset from edge" }
            isLength(definition.cutOffset, OFFSET_BOUNDS);

            annotation { "Name" : "Cutout corner radius" }
            isLength(definition.cutRadius, RADIUS_BOUNDS);
        }

        annotation { "Name" : "Score lines on pentagon and connector edges", "Default" : false }
        definition.scoreLines is boolean;

        annotation { "Name" : "Flat sheet part (1 mm, beside the tube)", "Default" : true }
        definition.flatPart is boolean;

        annotation { "Name" : "3D preview" }
        definition.preview is SheetPreview;

        annotation { "Name" : "Color pentagons", "Default" : true }
        definition.colorPentagons is boolean;

        annotation { "Name" : "Print DXF to the console", "Default" : true }
        definition.printDxf is boolean;

        annotation { "Name" : "Swap around/along (if auto-detect gets it wrong)" }
        definition.swapUV is boolean;
    }
    {
        var stage = "reading the tube";
        try
        {
        const face = definition.face;
        const swap = detectSwap(context, face) != definition.swapUV;
        const along = alongTable(context, face, swap);
        const lens = along.lens;
        const L = lens[size(lens) - 1];
        const ctab = circumferenceTable(context, face, swap, lens);
        var cMin = 1e9;
        var cMax = 0;
        for (var c in ctab)
        {
            cMin = min(cMin, c);
            cMax = max(cMax, c);
        }

        // ---- 1. The pattern on the flat sheet ----
        stage = "laying out the pattern";
        const layout = sheetLayout(lens, ctab, definition);
        const faces = layout.faces;
        const nf = size(faces);
        var isPentagon = makeArray(nf, false);
        for (var fi in layout.pentagons)
            isPentagon[fi] = true;

        // Place every face whole: unwrapped around its first corner, then shifted by
        // whole turns so its middle is on the sheet. copies[fi][k] is that shift.
        stage = "placing faces on the sheet";
        var placed = makeArray(nf, []);
        var copies = makeArray(nf, []);
        for (var fi = 0; fi < nf; fi += 1)
        {
            const where = placeFace(faces[fi], layout.s, layout.y, ctab, L);
            placed[fi] = where.points;
            copies[fi] = where.copies;
        }

        // ---- 2. Edges: interior (score), seam and tube ends (cut) ----
        var edgeUses = {};
        for (var fi = 0; fi < nf; fi += 1)
        {
            const f = faces[fi];
            for (var k = 0; k < size(f); k += 1)
            {
                const k2 = (k + 1) % size(f);
                const key = "" ~ min(f[k], f[k2]) ~ "_" ~ max(f[k], f[k2]);
                const use = { "face" : fi, "k" : k, "k2" : k2, "low" : f[k] < f[k2] ? copies[fi][k] : copies[fi][k2],
                        "high" : f[k] < f[k2] ? copies[fi][k2] : copies[fi][k] };
                edgeUses[key] = append(edgeUses[key] == undefined ? [] : edgeUses[key], use);
            }
        }
        var outline = [];
        var seamEdges = [];
        var scoreSegs = [];
        for (var entry in edgeUses)
        {
            const uses = entry.value;
            const u0 = uses[0];
            const seg0 = [placed[u0.face][u0.k], placed[u0.face][u0.k2]];
            if (size(uses) == 1)
            {
                outline = append(outline, seg0);   // tube end
                continue;
            }
            const u1 = uses[1];
            if (u0.low == u1.low && u0.high == u1.high)
            {
                scoreSegs = append(scoreSegs, seg0);
                continue;
            }
            // Seam: the two faces sit on opposite long edges of the sheet.
            outline = append(outline, seg0);
            outline = append(outline, [placed[u1.face][u1.k], placed[u1.face][u1.k2]]);
            seamEdges = append(seamEdges, seg0);
        }

        // ---- 3. Cutouts ----
        stage = "shaping the cutouts";
        var holes = [];
        var holeLines = [];
        var holeArcs = [];
        var tooSmall = 0;
        var smallestRadius = 1e9;
        var largestRadius = 0;
        var connectorCount = 0;
        for (var fi = 0; fi < nf; fi += 1)
        {
            if (isPentagon[fi])
                continue;
            connectorCount += 1;
            if (!definition.cutOut)
                continue;
            var loop = placed[fi];
            if (signedArea2(loop) < 0)
                loop = reverse(loop);
            const hole = holeShape2d(loop, definition.cutOffset / meter, definition.cutRadius / meter);
            if (!hole.fits)
            {
                tooSmall += 1;
                continue;
            }
            const pieces = holePieces(hole);
            holes = append(holes, { "pieces" : pieces, "inside" : hole.inside });
            for (var piece in pieces)
            {
                if (piece.isArc)
                    holeArcs = append(holeArcs, piece);
                else
                    holeLines = append(holeLines, [piece.start, piece.end]);
            }
            smallestRadius = min(smallestRadius, hole.radius);
            largestRadius = max(largestRadius, hole.radius);
        }

        // ---- 4. Sheet size ----
        var minX = 1e9;
        var maxX = -1e9;
        var minY = 1e9;
        var maxY = -1e9;
        for (var seg in outline)
        {
            for (var p in seg)
            {
                minX = min(minX, p[0]);
                maxX = max(maxX, p[0]);
                minY = min(minY, p[1]);
                maxY = max(maxY, p[1]);
            }
        }
        const sheetW = maxX - minX;
        const sheetL = maxY - minY;
        const fits = (sheetW <= STOCK_SHORT && sheetL <= STOCK_LONG) || (sheetW <= STOCK_LONG && sheetL <= STOCK_SHORT);

        // ---- 5. Sketches on the Top plane, beside the tube ----
        stage = "drawing the cut pattern";
        const tubeBox = evBox3d(context, { "topology" : face, "tight" : false });
        const margin = 0.5 * INCH;
        const stockX = tubeBox.maxCorner[0] / meter + 6 * INCH;
        const stockY = tubeBox.minCorner[1] / meter;
        const shift = [stockX + margin - minX, stockY + margin - minY];
        const top = plane(vector(0, 0, 0) * meter, vector(0, 0, 1), vector(1, 0, 0));

        const cutSketch = newSketchOnPlane(context, id + "cutPattern", { "sketchPlane" : top });
        var n = 0;
        for (var seg in concatenateArrays([outline, holeLines]))
        {
            skLineSegment(cutSketch, "l" ~ n, { "start" : vec2(add2(seg[0], shift)), "end" : vec2(add2(seg[1], shift)) });
            n += 1;
        }
        for (var arc in holeArcs)
        {
            skArc(cutSketch, "a" ~ n, { "start" : vec2(add2(arc.start, shift)), "mid" : vec2(add2(arc.mid, shift)), "end" : vec2(add2(arc.end, shift)) });
            n += 1;
        }
        skSolve(cutSketch);

        if (definition.scoreLines && size(scoreSegs) > 0)
        {
            const scoreSketch = newSketchOnPlane(context, id + "scoreLines", { "sketchPlane" : top });
            for (var k = 0; k < size(scoreSegs); k += 1)
                skLineSegment(scoreSketch, "s" ~ k, { "start" : vec2(add2(scoreSegs[k][0], shift)), "end" : vec2(add2(scoreSegs[k][1], shift)) });
            skSolve(scoreSketch);
        }

        // Stock outline, turned to match the sheet's long side.
        const stockW = sheetL >= sheetW ? STOCK_SHORT : STOCK_LONG;
        const stockH = sheetL >= sheetW ? STOCK_LONG : STOCK_SHORT;
        const stockSketch = newSketchOnPlane(context, id + "stock", { "sketchPlane" : top });
        skRectangle(stockSketch, "stock", { "firstCorner" : vector(stockX, stockY) * meter, "secondCorner" : vector(stockX + stockW, stockY + stockH) * meter });
        skSolve(stockSketch);

        // ---- 6. Flat sheet part ----
        var note = "";
        if (definition.flatPart)
        {
            stage = "making the flat sheet part";
            const regions = qSketchRegion(id + "cutPattern", true);
            if (size(evaluateQuery(context, regions)) == 0)
                throw "the cut pattern has no closed outline";
            opExtrude(context, id + "flatSheet", {
                        "entities" : regions,
                        "direction" : vector(0, 0, -1),
                        "endBound" : BoundingType.BLIND,
                        "endDepth" : SHEET_THICKNESS * meter
                    });
            const flatBody = qCreatedBy(id + "flatSheet", EntityType.BODY);
            setProperty(context, { "entities" : flatBody, "propertyType" : PropertyType.NAME,
                        "value" : "Flat sheet " ~ roundToPrecision(sheetW / INCH, 1) ~ " x " ~ roundToPrecision(sheetL / INCH, 1) ~ " in" });
            if (definition.colorPentagons && size(layout.pentagons) > 0)
            {
                const pentSketch = newSketchOnPlane(context, id + "pentagonOutlines", { "sketchPlane" : top });
                var centres = [];
                var m = 0;
                for (var fi in layout.pentagons)
                {
                    const corners = placed[fi];
                    for (var k = 0; k < 5; k += 1)
                    {
                        skLineSegment(pentSketch, "p" ~ m, { "start" : vec2(add2(corners[k], shift)), "end" : vec2(add2(corners[(k + 1) % 5], shift)) });
                        m += 1;
                    }
                    centres = append(centres, add2(centroid2(corners), shift));
                }
                skSolve(pentSketch);
                const colored = try silent(colorFlatPentagons(context, id, flatBody, centres));
                if (colored != true)
                    note = note ~ "Could not colour pentagons on the flat sheet. ";
                try silent(opDeleteBodies(context, id + "pentagonOutlinesGone", { "entities" : qCreatedBy(id + "pentagonOutlines", EntityType.BODY) }));
            }
        }

        // ---- 7. 3D preview on the tube ----
        if (definition.preview != SheetPreview.NONE)
        {
            stage = "mapping the pattern onto the tube";
            // Curves to map: hole outlines (one smooth closed curve each), pentagon
            // edges and seam edges (one curve per straight edge).
            var curves = [];
            for (var hole in holes)
                curves = append(curves, { "points" : sampleHole(hole.pieces), "closed" : true });
            for (var fi in layout.pentagons)
            {
                for (var k = 0; k < 5; k += 1)
                    curves = append(curves, { "points" : sampleSegment(placed[fi][k], placed[fi][(k + 1) % 5]), "closed" : false });
            }
            for (var seg in seamEdges)
                curves = append(curves, { "points" : sampleSegment(seg[0], seg[1]), "closed" : false });
            // Plus a point inside each hole and in each pentagon, to find their faces.
            var flatPoints = [];
            for (var crv in curves)
                flatPoints = concatenateArrays([flatPoints, crv.points]);
            for (var hole in holes)
                flatPoints = append(flatPoints, hole.inside);
            for (var fi in layout.pentagons)
                flatPoints = append(flatPoints, centroid2(placed[fi]));
            var params = [];
            for (var p in flatPoints)
                params = append(params, tubeParam(p, lens, ctab, swap));
            const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });

            stage = "drawing curves on the tube";
            var at = 0;
            var curveEdges = [];
            for (var ci = 0; ci < size(curves); ci += 1)
            {
                var pts = [];
                for (var k = 0; k < size(curves[ci].points); k += 1)
                    pts = append(pts, planes[at + k].origin);
                at += size(curves[ci].points);
                if (curves[ci].closed)
                    pts = append(pts, pts[0]);
                opFitSpline(context, id + ("curve" ~ ci), { "points" : pts });
                curveEdges = append(curveEdges, qCreatedBy(id + ("curve" ~ ci), EntityType.EDGE));
            }
            var holePoints = [];
            for (var hi = 0; hi < size(holes); hi += 1)
                holePoints = append(holePoints, planes[at + hi].origin);
            at += size(holes);
            var pentagonPoints = [];
            for (var k = 0; k < size(layout.pentagons); k += 1)
                pentagonPoints = append(pentagonPoints, planes[at + k].origin);

            if (definition.preview == SheetPreview.SURFACE)
            {
                stage = "cutting the wrapped sheet";
                opExtractSurface(context, id + "wrapped", { "faces" : face });
                const wrappedBody = qCreatedBy(id + "wrapped", EntityType.BODY);
                opSplitFace(context, id + "wrappedSplit", {
                            "faceTargets" : qOwnedByBody(wrappedBody, EntityType.FACE),
                            "edgeTools" : qUnion(curveEdges)
                        });
                try silent(opDeleteBodies(context, id + "curvesGone", { "entities" : qUnion(curveEdges) }));
                const wrappedFaces = qOwnedByBody(wrappedBody, EntityType.FACE);
                var holeFaces = [];
                for (var p in holePoints)
                    holeFaces = append(holeFaces, qContainsPoint(wrappedFaces, p));
                var cutCount = 0;
                if (size(holeFaces) > 0)
                {
                    cutCount = size(evaluateQuery(context, qUnion(holeFaces)));
                    opDeleteFace(context, id + "wrappedHoles", {
                                "deleteFaces" : qUnion(holeFaces),
                                "includeFillet" : false,
                                "capVoid" : false,
                                "leaveOpen" : true
                            });
                }
                if (cutCount != size(holes))
                    note = note ~ "Preview: " ~ cutCount ~ " of " ~ size(holes) ~ " holes cut. ";
                if (definition.colorPentagons)
                {
                    var pentagonFaces = [];
                    for (var p in pentagonPoints)
                        pentagonFaces = append(pentagonFaces, qContainsPoint(qOwnedByBody(wrappedBody, EntityType.FACE), p));
                    if (size(pentagonFaces) > 0)
                        setProperty(context, { "entities" : qUnion(pentagonFaces), "propertyType" : PropertyType.APPEARANCE, "value" : color(0.93, 0.42, 0.25) });
                }
                setProperty(context, { "entities" : wrappedBody, "propertyType" : PropertyType.NAME, "value" : "Wrapped sheet preview" });
            }
        }

        // ---- 8. DXF, with the sheet's corner at 0, 0 ----
        if (definition.printDxf)
        {
            const origin = [-minX, -minY];
            println("----- DXF START (copy the lines below into pentagon-sheet.dxf) -----");
            println("0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES");
            for (var seg in concatenateArrays([outline, holeLines]))
                println(dxfLine("CUT", add2(seg[0], origin), add2(seg[1], origin)));
            for (var arc in holeArcs)
                println(dxfArc("CUT", arc, origin));
            if (definition.scoreLines)
            {
                for (var seg in scoreSegs)
                    println(dxfLine("SCORE", add2(seg[0], origin), add2(seg[1], origin)));
            }
            println("0\nENDSEC\n0\nEOF");
            println("----- DXF END -----");
        }

        // ---- 9. Report ----
        var report = "Sheet " ~ roundToPrecision(sheetW / INCH, 1) ~ " x " ~ roundToPrecision(sheetL / INCH, 1) ~ " in: " ~
            (fits ? "fits" : "does NOT fit") ~ " a 48 x 96 in sheet. " ~ layout.note ~
            size(layout.pentagons) ~ " pentagons, " ~ connectorCount ~ " connecting shapes";
        if (definition.cutOut)
        {
            report = report ~ ", " ~ size(holes) ~ " cutouts";
            if (size(holes) > 0)
                report = report ~ " (corner radius " ~ roundToPrecision(smallestRadius / INCH, 2) ~ " to " ~ roundToPrecision(largestRadius / INCH, 2) ~ " in)";
            if (tooSmall > 0)
                report = report ~ ", " ~ tooSmall ~ " too small for one";
        }
        report = report ~ ". ";
        const longer = along.lineMax / L - 1;
        const shorter = 1 - along.lineMin / L;
        if (max(longer, shorter) > 0.005)
        {
            report = report ~ "Bend: the tube surface is up to " ~ roundToPrecision(longer * 100, 1) ~ "% longer (+" ~
                roundToPrecision((along.lineMax - L) / INCH, 1) ~ " in) on the outside and " ~ roundToPrecision(shorter * 100, 1) ~
                "% shorter (-" ~ roundToPrecision((L - along.lineMin) / INCH, 1) ~ " in) on the inside than the sheet; about +/-" ~
                roundToPrecision(max(longer, shorter) * definition.connectorSize / inch, 2) ~ " in per connecting shape. ";
        }
        else
            report = report ~ "No bend: the sheet wraps exactly. ";
        if (cMax / cMin > 1.01)
            report = report ~ "Taper: sheet width " ~ roundToPrecision(cMin / INCH, 1) ~ " to " ~ roundToPrecision(cMax / INCH, 1) ~ " in. ";
        report = report ~ note;
        reportFeatureInfo(context, id, report);
        }
        catch (error)
        {
            throw regenError("Failed while " ~ stage ~ ": " ~ toString(error));
        }
    }, { "cutOut" : true, "cutOffset" : 0.5 * inch, "cutRadius" : 0.5 * inch, "scoreLines" : false, "flatPart" : true,
          "preview" : SheetPreview.SURFACE, "colorPentagons" : true, "printDxf" : true, "swapUV" : false,
          "pentagonDiameter" : 5 * inch, "connectorSize" : 5 * inch, "pentagonPercent" : 35, "sizeVariation" : 50,
          "seed" : 1, "passes" : 100 });

// ----------------------------------------------------------------- layout ----

// The pattern on the flat sheet. Vertices are kept as s (fraction of the way
// around, 0 to 1, wrapping) and y (distance along the tube); on the sheet a vertex
// sits at x = s * circumference(y).
// Returns { faces (vertex lists), pentagons (face indices), s, y, note }.
function sheetLayout(lens is array, ctab is array, definition is map) returns map
{
    const L = lens[size(lens) - 1];
    const diameter = definition.pentagonDiameter / meter;
    const targetSide = diameter * 0.5877852522924731;

    // ---- 1. Count around from the connecting shape size; rows to match ----
    const C = circumferenceAt(ctab, L, L / 2);
    const N = max(3, round(C / (definition.connectorSize / meter)));
    const M = max(2, round(L / (ROW_PITCH * C / N)));
    const yMax = M * ROW_PITCH;

    // ---- 2. Honeycomb in tiling coordinates: x in [0, N) around, y in [0, yMax] along ----
    var keys = {};
    var tx = [];
    var ty = [];
    var faces = [];
    var endFace = [];
    for (var j = 0; j <= M; j += 1)
    {
        for (var i = 0; i < N; i += 1)
        {
            const cx = i + (j % 2 == 1 ? 0.5 : 0);
            const cy = j * ROW_PITCH;
            var raw = [[cx, cy - HEX_HALF], [cx + 0.5, cy - HEX_QUARTER], [cx + 0.5, cy + HEX_QUARTER],
                       [cx, cy + HEX_HALF], [cx - 0.5, cy + HEX_QUARTER], [cx - 0.5, cy - HEX_QUARTER]];
            // The first and last rows are cut off at the tube ends.
            for (var k = 0; k < 6; k += 1)
                raw[k][1] = min(max(raw[k][1], 0), yMax);
            var f = [];
            for (var k = 0; k < 6; k += 1)
            {
                const ry = raw[k][1];
                const onEnd = ry == 0 || ry == yMax;
                // Skip a corner that ends up in the middle of the cut edge.
                if (onEnd && raw[(k + 5) % 6][1] == ry && raw[(k + 1) % 6][1] == ry)
                    continue;
                var x = raw[k][0] - floor(raw[k][0] / N) * N;
                if (abs(x - N) < 1e-9)
                    x = 0;
                const key = "" ~ round(x * 1000) ~ "_" ~ round(ry * 1000);
                if (keys[key] == undefined)
                {
                    keys[key] = size(tx);
                    tx = append(tx, x);
                    ty = append(ty, ry);
                }
                f = append(f, keys[key]);
            }
            faces = append(faces, f);
            endFace = append(endFace, j == 0 || j == M);
        }
    }
    const nv = size(tx);
    const nf = size(faces);

    // ---- 3. Pentagon / heptagon pairs: rotate random edges a quarter turn ----
    var vertexFaces = makeArray(nv, []);
    for (var fi = 0; fi < nf; fi += 1)
    {
        for (var vtx in faces[fi])
            vertexFaces[vtx] = append(vertexFaces[vtx], fi);
    }
    var edges = [];
    for (var f in faces)
    {
        for (var k = 0; k < size(f); k += 1)
        {
            const a = f[k];
            const b = f[(k + 1) % size(f)];
            if (a < b)
                edges = append(edges, [a, b]);
        }
    }
    var blocked = endFace;
    var pentagonFlag = makeArray(nf, false);
    var pentagons = [];
    const want = round(definition.pentagonPercent / 100 * nf / 2);
    var rng = definition.seed * 7919 + 13;
    var made = 0;
    // First try to keep pairs at least one hexagon apart, so pentagons have room to
    // even out. If that can't fit as many as asked for, pack the rest in wherever
    // four whole hexagons are left.
    const spacedAttempts = 60 * want + 100;
    for (var attempt = 0; attempt < 5 * spacedAttempts && made < want; attempt += 1)
    {
        const spaced = attempt < spacedAttempts;
        rng = (rng * 16807) % 2147483647;
        const e = edges[floor(rng / 2147483647 * size(edges))];
        const a = e[0];
        const b = e[1];
        const shared = listInBoth(vertexFaces[a], vertexFaces[b]);
        const endA = listNotIn(vertexFaces[a], shared);
        const endB = listNotIn(vertexFaces[b], shared);
        if (size(shared) != 2 || size(endA) != 1 || size(endB) != 1)
            continue;
        const four = [shared[0], shared[1], endA[0], endB[0]];
        var usable = true;
        for (var fi in four)
        {
            if ((spaced ? blocked[fi] : endFace[fi]) || size(faces[fi]) != 6)
                usable = false;
        }
        // Two pentagons must never touch: the two faces about to become pentagons
        // may not share a corner with an existing one.
        for (var vtx in concatenateArrays([faces[shared[0]], faces[shared[1]]]))
        {
            if (vtx == a || vtx == b)
                continue;
            for (var g in vertexFaces[vtx])
            {
                if (pentagonFlag[g])
                    usable = false;
            }
        }
        if (!usable)
            continue;

        for (var fi in four)
        {
            for (var vtx in faces[fi])
            {
                for (var g in vertexFaces[vtx])
                    blocked[g] = true;
            }
        }

        // The two faces beside the edge lose a corner (pentagons); the two at its
        // ends gain one (heptagons).
        const side1 = faces[shared[0]];
        const side2 = faces[shared[1]];
        faces[shared[0]] = listWithout(side1, b);
        faces[shared[1]] = listWithout(side2, a);
        var g3 = faces[endA[0]];
        var at = listIndex(g3, a);
        g3 = listInsert(g3, listHas(side2, g3[(at + 1) % size(g3)]) ? at + 1 : at, b);
        faces[endA[0]] = g3;
        var g4 = faces[endB[0]];
        at = listIndex(g4, b);
        g4 = listInsert(g4, listHas(side1, g4[(at + 1) % size(g4)]) ? at + 1 : at, a);
        faces[endB[0]] = g4;

        // Turn the edge a quarter turn about its middle; a goes to the side1 side.
        const bx = tx[a] + wrapDiff(tx[b] - tx[a], N);
        const mx = (tx[a] + bx) / 2;
        const my = (ty[a] + ty[b]) / 2;
        var px = -(ty[b] - ty[a]) / 2;
        var py = (bx - tx[a]) / 2;
        var other = side1[0];
        for (var vtx in side1)
        {
            if (vtx != a && vtx != b)
                other = vtx;
        }
        const ox = tx[a] + wrapDiff(tx[other] - tx[a], N);
        if (px * (ox - mx) + py * (ty[other] - my) < 0)
        {
            px = -px;
            py = -py;
        }
        tx[a] = wrapX(mx + px, N);
        ty[a] = my + py;
        tx[b] = wrapX(mx - px, N);
        ty[b] = my - py;

        // a left side2 and joined the far end face; b the other way round.
        vertexFaces[a] = [shared[0], endA[0], endB[0]];
        vertexFaces[b] = [shared[1], endA[0], endB[0]];

        pentagonFlag[shared[0]] = true;
        pentagonFlag[shared[1]] = true;
        pentagons = append(pentagons, shared[0]);
        pentagons = append(pentagons, shared[1]);
        made += 1;
    }

    // ---- 4. Neighbours, and which vertices may move ----
    var neighbours = makeArray(nv, []);
    for (var f in faces)
    {
        for (var k = 0; k < size(f); k += 1)
        {
            const a = f[k];
            const b = f[(k + 1) % size(f)];
            if (!listHas(neighbours[a], b))
                neighbours[a] = append(neighbours[a], b);
            if (!listHas(neighbours[b], a))
                neighbours[b] = append(neighbours[b], a);
        }
    }
    var fixed = makeArray(nv, false);
    for (var vtx = 0; vtx < nv; vtx += 1)
        fixed[vtx] = ty[vtx] == 0 || ty[vtx] == yMax;
    var inPentagon = makeArray(nv, false);
    var pentagonsAt = makeArray(nv, 0);
    var isPentagon = makeArray(nf, false);
    for (var fi in pentagons)
    {
        isPentagon[fi] = true;
        for (var vtx in faces[fi])
        {
            inPentagon[vtx] = true;
            pentagonsAt[vtx] += 1;
        }
    }
    // Every connecting shape (not a pentagon, not cut by a tube end) gets a random size.
    const variation = definition.sizeVariation / 100;
    var faceScale = makeArray(nf, 1);
    var resizable = makeArray(nf, false);
    for (var fi = 0; fi < nf; fi += 1)
    {
        rng = (rng * 16807) % 2147483647;
        faceScale[fi] = 1 + variation * (2 * rng / 2147483647 - 1);
        resizable[fi] = variation > 0 && !isPentagon[fi] && !endFace[fi];
    }

    // Smooth out the rotated edges in tiling coordinates first.
    for (var pass = 0; pass < 15 && made > 0; pass += 1)
    {
        var nx = tx;
        var ny = ty;
        for (var vtx = 0; vtx < nv; vtx += 1)
        {
            if (fixed[vtx])
                continue;
            var sx = 0;
            var sy = 0;
            for (var w in neighbours[vtx])
            {
                sx += wrapDiff(tx[w] - tx[vtx], N);
                sy += ty[w] - ty[vtx];
            }
            nx[vtx] = tx[vtx] + sx / size(neighbours[vtx]);
            ny[vtx] = ty[vtx] + sy / size(neighbours[vtx]);
        }
        tx = nx;
        ty = ny;
    }

    // ---- 5. Evening, on the flat sheet ----
    var sv = makeArray(nv, 0);
    var yv = makeArray(nv, 0);
    for (var vtx = 0; vtx < nv; vtx += 1)
    {
        sv[vtx] = wrapX(tx[vtx] / N, 1);
        yv[vtx] = fixed[vtx] ? (ty[vtx] == 0 ? 0 : L) : ty[vtx] / yMax * L;
    }
    for (var pass = 0; pass < definition.passes; pass += 1)
    {
        var cv = makeArray(nv, 0);
        for (var vtx = 0; vtx < nv; vtx += 1)
            cv[vtx] = circumferenceAt(ctab, L, yv[vtx]);

        // Pull every vertex toward the middle of its neighbours. Pentagon corners
        // feel this only at the start; after that the pentagon shape decides.
        var move = makeArray(nv, [0, 0]);
        const pentagonPull = pass < 0.4 * definition.passes ? 0.15 : 0;
        for (var vtx = 0; vtx < nv; vtx += 1)
        {
            if (fixed[vtx])
                continue;
            var mean = [0, 0];
            for (var w in neighbours[vtx])
                mean = add2(mean, sheetRel(sv, yv, cv, vtx, w));
            const pull = inPentagon[vtx] ? pentagonPull : (variation > 0 ? 0.3 : 0.5);
            move[vtx] = scale2(mean, pull / size(neighbours[vtx]));
        }

        // Connecting shapes: grow or shrink each one about its middle toward its random size.
        if (variation > 0)
        {
            var centre = makeArray(nf, [0, 0]);
            var radius = makeArray(nf, 0);
            var radiusSum = 0;
            var radiusCount = 0;
            for (var fi = 0; fi < nf; fi += 1)
            {
                if (!resizable[fi])
                    continue;
                const f = faces[fi];
                var c = [0, 0];
                for (var vtx in f)
                    c = add2(c, scale2(sheetRel(sv, yv, cv, f[0], vtx), 1 / size(f)));
                for (var vtx in f)
                    radius[fi] += length2(sub2(sheetRel(sv, yv, cv, f[0], vtx), c)) / size(f);
                centre[fi] = c;
                radiusSum += radius[fi];
                radiusCount += 1;
            }
            for (var fi = 0; fi < nf; fi += 1)
            {
                if (!resizable[fi])
                    continue;
                const grow = 0.5 * (faceScale[fi] * radiusSum / radiusCount - radius[fi]);
                const f = faces[fi];
                for (var vtx in f)
                {
                    if (fixed[vtx] || inPentagon[vtx])
                        continue;
                    const out = sub2(sheetRel(sv, yv, cv, f[0], vtx), centre[fi]);
                    // Each corner belongs to 3 faces, so share the correction.
                    move[vtx] = add2(move[vtx], scale2(out, grow / length2(out) / 3));
                }
            }
        }

        // All pentagons aim for the side of the chosen diameter: springs on their
        // 5 sides and 5 diagonals.
        for (var fi in pentagons)
        {
            const f = faces[fi];
            for (var k = 0; k < 5; k += 1)
            {
                for (var skip = 1; skip <= 2; skip += 1)
                {
                    const a = f[k];
                    const b = f[(k + skip) % 5];
                    const d = sheetRel(sv, yv, cv, a, b);
                    const len = length2(d);
                    const target = skip == 1 ? targetSide : GOLDEN * targetSide;
                    // Each corner sits on 4 springs per pentagon it belongs to, so share the correction.
                    const fix = scale2(d, 0.5 * (len - target) / len / 4);
                    move[a] = add2(move[a], scale2(fix, 1 / pentagonsAt[a]));
                    move[b] = sub2(move[b], scale2(fix, 1 / pentagonsAt[b]));
                }
            }
        }

        for (var vtx = 0; vtx < nv; vtx += 1)
        {
            if (fixed[vtx])
                continue;
            sv[vtx] = wrapX(sv[vtx] + move[vtx][0] / cv[vtx], 1);
            yv[vtx] = min(max(yv[vtx] + move[vtx][1], 0), L);
        }
    }

    // ---- 6. Exactly regular pentagons. Pentagons never share a corner, so each
    //         corner is moved by at most one of them. ----
    for (var fi in pentagons)
    {
        const f = faces[fi];
        const where = placeFace(f, sv, yv, ctab, L);
        const five = regularPentagon2d(where.points, diameter / 2);
        for (var k = 0; k < 5; k += 1)
        {
            yv[f[k]] = min(max(five[k][1], 0), L);
            sv[f[k]] = wrapX(five[k][0] / circumferenceAt(ctab, L, yv[f[k]]), 1);
        }
    }

    const fitNote = made < want ? " (asked for " ~ 2 * want ~ " pentagons, only this many fit)" : "";
    return { "faces" : faces, "pentagons" : pentagons, "s" : sv, "y" : yv,
            "note" : N ~ " around, " ~ (M + 1) ~ " rows" ~ fitNote ~ ": " };
}

// Where vertex b sits on the sheet relative to vertex a, going the short way around.
function sheetRel(sv is array, yv is array, cv is array, a is number, b is number) returns array
{
    return [(sv[a] + wrapDiff(sv[b] - sv[a], 1)) * cv[b] - sv[a] * cv[a], yv[b] - yv[a]];
}

// A face's corners on the sheet: unwrapped around its first corner, then shifted by
// whole turns so its middle has 0 <= s < 1. Each corner is computed from its own
// s and whole-turn shift only, so faces sharing a corner get exactly the same point.
// Returns { points, copies (the whole-turn shift of each corner) }.
function placeFace(f is array, sv is array, yv is array, ctab is array, L is number) returns map
{
    const s0 = sv[f[0]];
    var turns = [];
    var mean = 0;
    for (var vtx in f)
    {
        const t = round(s0 + wrapDiff(sv[vtx] - s0, 1) - sv[vtx]);
        turns = append(turns, t);
        mean += (sv[vtx] + t) / size(f);
    }
    const shift = -floor(mean);
    var points = [];
    var copies = [];
    for (var k = 0; k < size(f); k += 1)
    {
        const vtx = f[k];
        const copy = turns[k] + shift;
        points = append(points, [(sv[vtx] + copy) * circumferenceAt(ctab, L, yv[vtx]), yv[vtx]]);
        copies = append(copies, copy);
    }
    return { "points" : points, "copies" : copies };
}

// An exact regular pentagon of circumradius `reach`, centred on the given corners
// and turned to match them as closely as possible.
function regularPentagon2d(pts is array, reach is number) returns array
{
    const c = centroid2(pts);
    const turn = signedArea2(pts) > 0 ? 1 : -1;
    var sumCross = 0;
    var sumDot = 0;
    for (var k = 0; k < 5; k += 1)
    {
        const rel = sub2(pts[k], c);
        const a = turn * k * 0.4 * PI * radian;
        sumCross += cos(a) * rel[1] - sin(a) * rel[0];
        sumDot += cos(a) * rel[0] + sin(a) * rel[1];
    }
    const twist = atan2(sumCross, sumDot);
    var five = [];
    for (var k = 0; k < 5; k += 1)
    {
        const a = twist + turn * k * 0.4 * PI * radian;
        five = append(five, [c[0] + reach * cos(a), c[1] + reach * sin(a)]);
    }
    return five;
}

// ---------------------------------------------------------------- cutouts ----

// The cutout for a connecting shape (corners counter-clockwise): its outline moved
// in by `offset`, with corners rounded by `radius` (shrunk to fit).
// Returns { fits, points (corners of the hole before rounding), halfTan, radius, inside }.
function holeShape2d(q is array, offset is number, radius is number) returns map
{
    const count = size(q);
    // Move every edge inward and intersect neighbours.
    var linePoint = [];
    var lineDir = [];
    for (var k = 0; k < count; k += 1)
    {
        const d = sub2(q[(k + 1) % count], q[k]);
        const len = length2(d);
        linePoint = append(linePoint, [q[k][0] - d[1] / len * offset, q[k][1] + d[0] / len * offset]);
        lineDir = append(lineDir, d);
    }
    var pts = [];
    for (var k = 0; k < count; k += 1)
    {
        const p1 = linePoint[(k + count - 1) % count];
        const d1 = lineDir[(k + count - 1) % count];
        const p2 = linePoint[k];
        const d2 = lineDir[k];
        const den = d1[0] * d2[1] - d1[1] * d2[0];
        if (abs(den) < 1e-12)
            return { "fits" : false };
        const t = ((p2[0] - p1[0]) * d2[1] - (p2[1] - p1[1]) * d2[0]) / den;
        pts = append(pts, [p1[0] + d1[0] * t, p1[1] + d1[1] * t]);
    }
    // If an edge shrank to nothing or flipped, the shape is too small for a hole.
    var edgeLength = [];
    for (var k = 0; k < count; k += 1)
    {
        const e = sub2(pts[(k + 1) % count], pts[k]);
        const kept = (e[0] * lineDir[k][0] + e[1] * lineDir[k][1]) / length2(lineDir[k]);
        if (kept < 0.002)
            return { "fits" : false };
        edgeLength = append(edgeLength, kept);
    }
    // Largest corner radius that leaves a little straight on every edge.
    var halfTan = [];
    for (var k = 0; k < count; k += 1)
    {
        const v1 = unit2(pts[(k + count - 1) % count], pts[k]);
        const v2 = unit2(pts[(k + 1) % count], pts[k]);
        const c = min(max(v1[0] * v2[0] + v1[1] * v2[1], -0.999999), 0.999999);
        halfTan = append(halfTan, sqrt((1 - c) / (1 + c)));
    }
    var fitRadius = radius;
    for (var k = 0; k < count; k += 1)
        fitRadius = min(fitRadius, 0.9 * edgeLength[k] / (1 / halfTan[k] + 1 / halfTan[(k + 1) % count]));
    if (fitRadius < min(radius, MIN_HOLE_RADIUS))
        return { "fits" : false };
    return { "fits" : true, "points" : pts, "halfTan" : halfTan, "radius" : fitRadius, "inside" : centroid2(pts) };
}

// The hole outline as pieces in order around it: for each corner an arc (or nothing
// if the radius is tiny), then the straight to the next corner.
// Arc pieces: { isArc, start, mid, end, centre, radius }; line pieces: { isArc, start, end }.
function holePieces(hole is map) returns array
{
    const pts = hole.points;
    const count = size(pts);
    const r = hole.radius;
    var pieces = [];
    if (r < 0.0005)
    {
        for (var k = 0; k < count; k += 1)
            pieces = append(pieces, { "isArc" : false, "start" : pts[k], "end" : pts[(k + 1) % count] });
        return pieces;
    }
    var arcs = [];
    for (var k = 0; k < count; k += 1)
    {
        const v1 = unit2(pts[(k + count - 1) % count], pts[k]);
        const v2 = unit2(pts[(k + 1) % count], pts[k]);
        const reachAlong = r / hole.halfTan[k];
        const bis = normalize2(add2(v1, v2));
        const sinHalf = hole.halfTan[k] / sqrt(1 + hole.halfTan[k] * hole.halfTan[k]);
        arcs = append(arcs, {
                    "isArc" : true,
                    "start" : add2(pts[k], scale2(v1, reachAlong)),
                    "mid" : add2(pts[k], scale2(bis, r / sinHalf - r)),
                    "end" : add2(pts[k], scale2(v2, reachAlong)),
                    "centre" : add2(pts[k], scale2(bis, r / sinHalf)),
                    "radius" : r
                });
    }
    for (var k = 0; k < count; k += 1)
    {
        pieces = append(pieces, arcs[k]);
        pieces = append(pieces, { "isArc" : false, "start" : arcs[k].end, "end" : arcs[(k + 1) % count].start });
    }
    return pieces;
}

// Points around a hole outline, a few mm apart, for a smooth closed curve.
function sampleHole(pieces is array) returns array
{
    var out = [];
    for (var piece in pieces)
    {
        var pts = [];
        if (piece.isArc)
        {
            const a0 = angle2(sub2(piece.start, piece.centre));
            var sweep = angle2(sub2(piece.end, piece.centre)) - a0;
            while (sweep > PI)
                sweep -= 2 * PI;
            while (sweep < -PI)
                sweep += 2 * PI;
            const steps = max(2, ceil(abs(sweep) * piece.radius / CURVE_STEP));
            for (var i = 0; i < steps; i += 1)
            {
                const a = (a0 + sweep * i / steps) * radian;
                pts = append(pts, [piece.centre[0] + piece.radius * cos(a), piece.centre[1] + piece.radius * sin(a)]);
            }
        }
        else
        {
            const steps = max(1, ceil(length2(sub2(piece.end, piece.start)) / CURVE_STEP));
            for (var i = 0; i < steps; i += 1)
                pts = append(pts, add2(piece.start, scale2(sub2(piece.end, piece.start), i / steps)));
        }
        out = concatenateArrays([out, pts]);
    }
    return out;
}

// Points along a straight edge of the sheet, ends included.
function sampleSegment(a is array, b is array) returns array
{
    const steps = max(2, ceil(length2(sub2(b, a)) / CURVE_STEP));
    var pts = [];
    for (var i = 0; i <= steps; i += 1)
        pts = append(pts, add2(a, scale2(sub2(b, a), i / steps)));
    return pts;
}

// Splits the flat sheet's top face along the pentagon outlines and colours the pentagons.
// Splitting with the sketch edges directly does nothing, so push the pentagons through
// the face as a thin plug and split with its side walls.
function colorFlatPentagons(context is Context, id is Id, flatBody is Query, centres is array) returns boolean
{
    opExtrude(context, id + "flatPlug", {
                "entities" : qSketchRegion(id + "pentagonOutlines"),
                "direction" : vector(0, 0, 1),
                "endBound" : BoundingType.BLIND,
                "endDepth" : 0.002 * meter,
                "startBound" : BoundingType.BLIND,
                "startDepth" : 0.002 * meter
            });
    opSplitFace(context, id + "flatSplit", {
                "faceTargets" : qCapEntity(id + "flatSheet", CapType.START, EntityType.FACE),
                "faceTools" : qNonCapEntity(id + "flatPlug", EntityType.FACE)
            });
    opDeleteBodies(context, id + "flatPlugGone", { "entities" : qCreatedBy(id + "flatPlug", EntityType.BODY) });
    var found = [];
    for (var c in centres)
        found = append(found, qContainsPoint(qOwnedByBody(flatBody, EntityType.FACE), vector(c[0], c[1], 0) * meter));
    setProperty(context, { "entities" : qUnion(found), "propertyType" : PropertyType.APPEARANCE, "value" : color(0.93, 0.42, 0.25) });
    return true;
}

// ------------------------------------------------------------------ tube ----

// Surface parameter of a point on the sheet: x / circumference around, y along.
function tubeParam(p is array, lens is array, ctab is array, swap is boolean) returns Vector
{
    const L = lens[size(lens) - 1];
    const y = min(max(p[1], 0), L);
    return uvParam(p[0] / circumferenceAt(ctab, L, y), vOf(lens, y), swap);
}

function uvParam(u is number, v is number, swap is boolean) returns Vector
{
    const w = u - floor(u);
    return swap ? vector(v, w) : vector(w, v);
}

// True when the face's v direction (not u) is the closed one around the tube.
function detectSwap(context is Context, face is Query) returns boolean
{
    const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : [vector(0, 0.5), vector(1, 0.5), vector(0.5, 0), vector(0.5, 1)] });
    const uClosed = norm(pl[0].origin - pl[1].origin) < 1e-6 * meter;
    const vClosed = norm(pl[2].origin - pl[3].origin) < 1e-6 * meter;
    return vClosed && !uClosed;
}

// Length along the tube (meters). lens[k] is the length at v = k/100, averaged
// over 24 lines around; lineMin / lineMax are the shortest and longest lines (on a
// bend: the inside and outside).
function alongTable(context is Context, face is Query, swap is boolean) returns map
{
    const K = 100;
    const lines = 24;
    var params = [];
    for (var k = 0; k <= K; k += 1)
        for (var q = 0; q < lines; q += 1)
            params = append(params, uvParam(q / lines, k / K, swap));
    const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
    var lens = [0];
    var lineTotal = makeArray(lines, 0);
    for (var k = 1; k <= K; k += 1)
    {
        var step = 0;
        for (var q = 0; q < lines; q += 1)
        {
            const d = norm(pl[k * lines + q].origin - pl[(k - 1) * lines + q].origin) / meter;
            step += d;
            lineTotal[q] += d;
        }
        lens = append(lens, lens[k - 1] + step / lines);
    }
    var lineMin = 1e9;
    var lineMax = 0;
    for (var t in lineTotal)
    {
        lineMin = min(lineMin, t);
        lineMax = max(lineMax, t);
    }
    return { "lens" : lens, "lineMin" : lineMin, "lineMax" : lineMax };
}

// Parameter v at arc length l, by interpolating the length table.
function vOf(lens is array, l is number) returns number
{
    const K = size(lens) - 1;
    if (l <= 0)
        return 0;
    for (var k = 1; k <= K; k += 1)
    {
        if (lens[k] >= l)
        {
            const span = lens[k] - lens[k - 1];
            const f = span > 0 ? (l - lens[k - 1]) / span : 0;
            return (k - 1 + f) / K;
        }
    }
    return 1;
}

// Circumference (meters) at CIRC_ROWS + 1 evenly spaced distances along the tube.
function circumferenceTable(context is Context, face is Query, swap is boolean, lens is array) returns array
{
    const L = lens[size(lens) - 1];
    const S = 64;
    var params = [];
    for (var r = 0; r <= CIRC_ROWS; r += 1)
    {
        const v = vOf(lens, r / CIRC_ROWS * L);
        for (var k = 0; k <= S; k += 1)
            params = append(params, uvParam(k / S, v, swap));
    }
    const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
    // A polygon through points on a circle is a little short of it; correct for that.
    const polygonFix = (PI / S) / sin(PI / S * radian);
    var table = [];
    for (var r = 0; r <= CIRC_ROWS; r += 1)
    {
        var total = 0;
        for (var k = 0; k < S; k += 1)
            total += norm(pl[r * (S + 1) + k + 1].origin - pl[r * (S + 1) + k].origin) / meter;
        table = append(table, total * polygonFix);
    }
    return table;
}

function circumferenceAt(ctab is array, L is number, y is number) returns number
{
    const t = min(max(y / L, 0), 1) * CIRC_ROWS;
    const k = min(floor(t), CIRC_ROWS - 1);
    const f = t - k;
    return ctab[k] * (1 - f) + ctab[k + 1] * f;
}

// -------------------------------------------------------------------- DXF ----

function dxfLine(layer is string, a is array, b is array) returns string
{
    return "0\nLINE\n8\n" ~ layer ~ "\n10\n" ~ mm(a[0]) ~ "\n20\n" ~ mm(a[1]) ~ "\n30\n0\n11\n" ~
        mm(b[0]) ~ "\n21\n" ~ mm(b[1]) ~ "\n31\n0";
}

// DXF arcs run counter-clockwise from the start angle to the end angle.
function dxfArc(layer is string, arc is map, origin is array) returns string
{
    var a0 = angle2(sub2(arc.start, arc.centre));
    var a1 = angle2(sub2(arc.end, arc.centre));
    var sweep = a1 - a0;
    while (sweep > PI)
        sweep -= 2 * PI;
    while (sweep < -PI)
        sweep += 2 * PI;
    if (sweep < 0)
    {
        const t = a0;
        a0 = a1;
        a1 = t;
    }
    const c = add2(arc.centre, origin);
    return "0\nARC\n8\n" ~ layer ~ "\n10\n" ~ mm(c[0]) ~ "\n20\n" ~ mm(c[1]) ~ "\n30\n0\n40\n" ~ mm(arc.radius) ~
        "\n50\n" ~ roundToPrecision(a0 * 180 / PI, 4) ~ "\n51\n" ~ roundToPrecision(a1 * 180 / PI, 4);
}

function mm(x is number) returns string
{
    return "" ~ (round(x * 1e6) / 1000);
}

// ---------------------------------------------------------------- helpers ----

function vec2(p is array) returns Vector
{
    return vector(p[0], p[1]) * meter;
}

function add2(a is array, b is array) returns array
{
    return [a[0] + b[0], a[1] + b[1]];
}

function sub2(a is array, b is array) returns array
{
    return [a[0] - b[0], a[1] - b[1]];
}

function scale2(a is array, s is number) returns array
{
    return [a[0] * s, a[1] * s];
}

function length2(a is array) returns number
{
    return sqrt(a[0] * a[0] + a[1] * a[1]);
}

function normalize2(a is array) returns array
{
    return scale2(a, 1 / length2(a));
}

// Angle of a 2D direction, in radians as a plain number.
function angle2(a is array) returns number
{
    return atan2(a[1], a[0]) / radian;
}

// Unit vector in 2D from `from` toward `to`.
function unit2(to is array, from is array) returns array
{
    return normalize2(sub2(to, from));
}

function centroid2(pts is array) returns array
{
    var c = [0, 0];
    for (var p in pts)
        c = [c[0] + p[0] / size(pts), c[1] + p[1] / size(pts)];
    return c;
}

// Twice the signed area: positive when the corners run counter-clockwise.
function signedArea2(pts is array) returns number
{
    var area = 0;
    for (var k = 0; k < size(pts); k += 1)
    {
        const a = pts[k];
        const b = pts[(k + 1) % size(pts)];
        area += a[0] * b[1] - b[0] * a[1];
    }
    return area;
}

// Shortest way around from one x to another.
function wrapDiff(d is number, period is number) returns number
{
    return d - round(d / period) * period;
}

function wrapX(x is number, period is number) returns number
{
    return x - floor(x / period) * period;
}

function listHas(list is array, value) returns boolean
{
    for (var item in list)
    {
        if (item == value)
            return true;
    }
    return false;
}

function listIndex(list is array, value) returns number
{
    for (var k = 0; k < size(list); k += 1)
    {
        if (list[k] == value)
            return k;
    }
    return -1;
}

function listInBoth(a is array, b is array) returns array
{
    var out = [];
    for (var item in a)
    {
        if (listHas(b, item))
            out = append(out, item);
    }
    return out;
}

function listNotIn(a is array, b is array) returns array
{
    var out = [];
    for (var item in a)
    {
        if (!listHas(b, item))
            out = append(out, item);
    }
    return out;
}

function listWithout(list is array, value) returns array
{
    var out = [];
    for (var item in list)
    {
        if (item != value)
            out = append(out, item);
    }
    return out;
}

function listInsert(list is array, index is number, value) returns array
{
    var out = [];
    for (var k = 0; k < size(list); k += 1)
    {
        if (k == index)
            out = append(out, value);
        out = append(out, list[k]);
    }
    if (index >= size(list))
        out = append(out, value);
    return out;
}
