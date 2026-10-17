FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");

// Pentagon frame skin — FeatureScript for Onshape   (v4: fewer pieces, seam allowances, closed darts)
//
// Wraps a tube in flat, regular pentagon panels set apart from each other. The
// gaps between them are filled with small connecting shapes, which can have their
// middles cut out so they become open frames.
//
// Two layouts:
//   Organic (default): a honeycomb wraps the tube and random hexagon edges are
//     turned a quarter turn, which makes pentagon / heptagon pairs scattered at
//     random (pentagons never touch). Vertices slide on the tube until every
//     pentagon is even and the chosen size, and each connecting shape gets a random
//     size. Count around comes from the connecting shape size.
//   Grid: a Cairo pentagon grid, so pentagons sit in rows with small 3- and 4-sided
//     connecting shapes in the gaps. Count around comes from pentagon size + gap.
// In both, every pentagon is then an exactly flat regular pentagon of the chosen
// diameter, so its corners sit slightly off the tube.
//
// Cutouts: each connecting shape gets a hole set in from its edges, with rounded
// corners. The radius shrinks to fit; shapes too small are left solid.
// Everything is knitted into one surface named "Skin - N faces".
//
// "Wireframe preview" only draws the outlines. It is much faster.
//
// INSTALL: in a Feature Studio, select all, delete, paste this whole file, Commit.
//
// ------------------------------------------------------------------- 8< ----

export enum FrameOutput
{
    annotation { "Name" : "Surface" }
    SURFACE,
    annotation { "Name" : "Wireframe preview (fast)" }
    WIRES,
    annotation { "Name" : "Surface and flat pieces for cutting (organic layout)" }
    FLAT
}

export enum FrameLayout
{
    annotation { "Name" : "Organic (random pentagons among connecting shapes)" }
    ORGANIC,
    annotation { "Name" : "Grid (Cairo pattern, pentagons in rows)" }
    GRID
}

const CONNECTOR_BOUNDS = {
            (meter) : [0.005, 0.127, 5],
            (centimeter) : 12.7,
            (millimeter) : 127,
            (inch) : 5,
            (foot) : 0.4167,
            (yard) : 0.1389
        } as LengthBoundSpec;

const PERCENT_BOUNDS = { (unitless) : [0, 15, 50] } as RealBoundSpec;
const VARIATION_BOUNDS = { (unitless) : [0, 25, 60] } as RealBoundSpec;
const SEED_BOUNDS = { (unitless) : [1, 1, 100000] } as IntegerBoundSpec;
const PASS_BOUNDS = { (unitless) : [0, 40, 500] } as IntegerBoundSpec;
const PIECE_BOUNDS = { (unitless) : [1, 60, 500] } as IntegerBoundSpec;
const DART_BOUNDS = { (unitless) : [0, 5, 30] } as RealBoundSpec;
const ALLOWANCE_BOUNDS = {
            (meter) : [0, 0.00635, 0.1],
            (centimeter) : 0.635,
            (millimeter) : 6.35,
            (inch) : 0.25,
            (foot) : 0.0208,
            (yard) : 0.0069
        } as LengthBoundSpec;

const DIAMETER_BOUNDS = {
            (meter) : [0.005, 0.127, 5],
            (centimeter) : 12.7,
            (millimeter) : 127,
            (inch) : 5,
            (foot) : 0.4167,
            (yard) : 0.1389
        } as LengthBoundSpec;

const GAP_BOUNDS = {
            (meter) : [0, 0.0508, 5],
            (centimeter) : 5.08,
            (millimeter) : 50.8,
            (inch) : 2,
            (foot) : 0.1667,
            (yard) : 0.0556
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

// Honeycomb with hexagons 1 unit wide, points up: row spacing and corner heights.
const ROW_PITCH = 0.8660254037844386;   // sqrt(3) / 2
const HEX_HALF = 0.5773502691896258;    // 1 / sqrt(3)
const HEX_QUARTER = 0.2886751345948129;
// Diagonal / side of a regular pentagon.
const GOLDEN = 1.618033988749895;
// Step used to measure how the surface moves when a parameter changes.
const PARAM_STEP = 1e-4;

// Cairo bar offset that makes every grid pentagon edge equal.
const CAIRO_T = 0.2742918851774318;     // (-2 + sqrt(28)) / 12
// Cell size that gives a wanted gap: cell = (gap + GAP_PER_DIAMETER * diameter) / GAP_PER_CELL.
// From treating the grid pentagon as regular: its edge is 0.776 cells, and the gap
// between facing edges is twice the difference of the two inradii.
const GAP_PER_CELL = 1.068;
const GAP_PER_DIAMETER = 0.809;
const INCH = 0.0254;
// Plastic sheet the flat pieces are packed onto, and the spacing on it.
const SHEET_LENGTH = 96 * 0.0254;
const SHEET_WIDTH = 48 * 0.0254;
const SHEET_MARGIN = 0.5 * 0.0254;
const PIECE_GAP = 0.5 * 0.0254;
// Cutouts whose corners would need to be sharper than this are left out.
const MIN_HOLE_RADIUS = 0.0025;

annotation { "Feature Type Name" : "Pentagon frame skin",
             "Feature Type Description" : "Wraps a tube in flat regular pentagons joined by small connecting shapes, optionally cut out into frames." }
export const pentagonFrameSkin = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Tube side face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        annotation { "Name" : "Layout" }
        definition.layout is FrameLayout;

        annotation { "Name" : "Pentagon diameter (circle through its corners)" }
        isLength(definition.pentagonDiameter, DIAMETER_BOUNDS);

        if (definition.layout == FrameLayout.ORGANIC)
        {
            annotation { "Name" : "Connecting shape size" }
            isLength(definition.connectorSize, CONNECTOR_BOUNDS);

            annotation { "Name" : "Pentagon coverage (% of surface)" }
            isReal(definition.pentagonPercent, PERCENT_BOUNDS);

            annotation { "Name" : "Size variation of connecting shapes (%)" }
            isReal(definition.sizeVariation, VARIATION_BOUNDS);

            annotation { "Name" : "Random seed" }
            isInteger(definition.seed, SEED_BOUNDS);

            annotation { "Name" : "Evening passes" }
            isInteger(definition.passes, PASS_BOUNDS);

            annotation { "Name" : "Force pentagons exactly flat" }
            definition.flatPentagons is boolean;

            annotation { "Name" : "Most panels in one piece" }
            isInteger(definition.panelsPerPiece, PIECE_BOUNDS);

            annotation { "Name" : "Seam allowance" }
            isLength(definition.seamAllowance, ALLOWANCE_BOUNDS);

            annotation { "Name" : "Close darts narrower than (degrees)" }
            isReal(definition.dartClose, DART_BOUNDS);
        }
        else
        {
            annotation { "Name" : "Gap between pentagons (approximate)" }
            isLength(definition.gap, GAP_BOUNDS);
        }

        annotation { "Name" : "Cut out connecting shapes", "Default" : true }
        definition.cutOut is boolean;

        if (definition.cutOut)
        {
            annotation { "Name" : "Cutout offset from edge" }
            isLength(definition.cutOffset, OFFSET_BOUNDS);

            annotation { "Name" : "Cutout corner radius" }
            isLength(definition.cutRadius, RADIUS_BOUNDS);
        }

        annotation { "Name" : "Color pentagons", "Default" : true }
        definition.colorPentagons is boolean;

        annotation { "Name" : "Output" }
        definition.output is FrameOutput;

        if (definition.output == FrameOutput.FLAT)
        {
            annotation { "Name" : "Print DXF to console", "Default" : true }
            definition.printDxf is boolean;
        }

        annotation { "Name" : "Swap around/along (if auto-detect gets it wrong)" }
        definition.swapUV is boolean;
    }
    {
        var stage = "reading inputs";
        try
        {
        const face = definition.face;
        const swap = detectSwap(context, face) != definition.swapUV;
        // ---- 1. Where the pentagons and connecting shapes go ----
        stage = "laying out pentagons";
        const organic = definition.layout == FrameLayout.ORGANIC;
        var layout = {};
        if (organic)
            layout = organicLayout(context, face, swap, definition);
        else
        {
            const grid = gridLayout(context, face, swap, definition);
            var flags = makeArray(size(grid.pentagons) + size(grid.connectors), false);
            for (var k = 0; k < size(grid.pentagons); k += 1)
                flags[k] = true;
            layout = { "loops" : concatenateArrays([grid.pentagons, grid.connectors]), "isPentagon" : flags, "note" : grid.note };
        }
        const loops = layout.loops;
        const isPentagon = layout.isPentagon;
        const nFaces = size(loops);

        // How far faces are from flat: the largest distance of a corner from the face's best-fit plane.
        var pentFlat = 0;
        var pentCount = 0;
        var connFlat = 0;
        var connWorst = 0;
        var connCount = 0;
        for (var fi = 0; fi < nFaces; fi += 1)
        {
            const off = outOfFlat(loops[fi]);
            if (isPentagon[fi])
            {
                pentFlat += off;
                pentCount += 1;
            }
            else
            {
                connFlat += off;
                connWorst = max(connWorst, off);
                connCount += 1;
            }
        }
        var note = layout.note ~ "Out of flat on average: pentagons " ~ roundToPrecision(pentFlat / max(pentCount, 1) / INCH, 2) ~
            " in, connecting shapes " ~ roundToPrecision(connFlat / max(connCount, 1) / INCH, 2) ~ " in (worst " ~
            roundToPrecision(connWorst / INCH, 2) ~ " in). ";

        // ---- 2. Pieces that unfold flat (organic layout only) ----
        var pieces = undefined;
        if (organic)
        {
            stage = "dividing into pieces";
            pieces = makePieces(layout.P, layout.faces, isPentagon, definition);
        }

        // ---- 3. Flat pieces for cutting ----
        if (definition.output == FrameOutput.FLAT)
        {
            if (!organic)
                throw regenError("Flat pieces need the Organic layout.", ["output"]);
            stage = "laying out the flat pieces";
            note = note ~ flatPieces(context, id + "flat", layout.P, layout.faces, isPentagon, pieces, definition);
        }
        else if (pieces != undefined)
            note = note ~ size(pieces.pieces) ~ " pieces. ";

        // ---- 4. Wireframe preview ----
        if (definition.output == FrameOutput.WIRES)
        {
            stage = "drawing the wireframe";
            for (var fi = 0; fi < nFaces; fi += 1)
                opPolyline(context, id + ("wire" ~ fi), { "points" : closedLoop(loops[fi]) });
            reportFeatureInfo(context, id, note);
            return;
        }

        // ---- 5. One surface per face ----
        // Onshape requires all operations under one parent Id to be contiguous in
        // history, so every face gets its own parent.
        stage = "building faces";
        var surfs = [];
        var failed = 0;
        for (var fi = 0; fi < nFaces; fi += 1)
        {
            const faceId = id + ("face" ~ fi);
            const filled = try silent(fillLoop(context, faceId + "wire", faceId + "surf", loops[fi]));
            if (filled != true)
                failed += 1;
            try silent(opDeleteBodies(context, faceId + "cleanup", { "entities" : qCreatedBy(faceId + "wire", EntityType.BODY) }));
            surfs = append(surfs, qCreatedBy(faceId + "surf", EntityType.BODY));
        }

        // ---- 6. Knit into one surface, named with its face count ----
        stage = "knitting faces";
        const sheetBodies = qBodyType(qUnion(surfs), BodyType.SHEET);
        if (size(evaluateQuery(context, sheetBodies)) == 0)
            throw regenError("No faces were created.");
        const skinName = "Skin - " ~ (nFaces - failed) ~ " faces";
        setProperty(context, { "entities" : sheetBodies, "propertyType" : PropertyType.NAME, "value" : skinName });
        if (failed > 0)
            note = note ~ failed ~ " faces could not be built. ";
        var knitNote = "";
        try
        {
            // Same options Onshape's own surface features use to join adjacent surfaces.
            opBoolean(context, id + "knit", {
                        "tools" : sheetBodies,
                        "operationType" : BooleanOperationType.UNION,
                        "recomputeMatches" : true,
                        "eraseImprintedEdges" : false
                    });
        }
        catch (knitError)
        {
            knitNote = "Faces could not be joined into one surface (" ~ toString(knitError) ~ "); they are left separate. ";
        }
        const knitted = qBodyType(qUnion(append(surfs, qCreatedBy(id + "knit", EntityType.BODY))), BodyType.SHEET);
        setProperty(context, { "entities" : knitted, "propertyType" : PropertyType.NAME, "value" : skinName });
        if (knitNote == "" && size(evaluateQuery(context, knitted)) > 1)
            knitNote = "Faces did not all join into one surface. ";

        // Colours: pentagons orange; connecting shapes tinted by piece, so the pieces show.
        if (pieces != undefined)
        {
            const palette = [color(0.78, 0.85, 0.94), color(0.80, 0.90, 0.78), color(0.88, 0.82, 0.94),
                             color(0.94, 0.89, 0.74), color(0.84, 0.84, 0.84), color(0.76, 0.91, 0.90)];
            var byColour = makeArray(size(palette), []);
            for (var fi = 0; fi < nFaces; fi += 1)
            {
                if (isPentagon[fi] && definition.colorPentagons)
                    continue;
                const c = pieces.colour[pieces.pieceOf[fi]];
                byColour[c] = append(byColour[c], qCreatedBy(id + ("face" ~ fi), EntityType.FACE));
            }
            for (var c = 0; c < size(palette); c += 1)
            {
                if (size(byColour[c]) > 0)
                    setProperty(context, { "entities" : qUnion(byColour[c]), "propertyType" : PropertyType.APPEARANCE, "value" : palette[c] });
            }
        }
        if (definition.colorPentagons)
        {
            var pentagonFaces = [];
            for (var fi = 0; fi < nFaces; fi += 1)
            {
                if (isPentagon[fi])
                    pentagonFaces = append(pentagonFaces, qCreatedBy(id + ("face" ~ fi), EntityType.FACE));
            }
            setProperty(context, { "entities" : qUnion(pentagonFaces), "propertyType" : PropertyType.APPEARANCE, "value" : color(0.93, 0.42, 0.25) });
        }

        // ---- 7. Cutouts, after joining, so the holes don't get in the way of it ----
        stage = "cutting out connecting shapes";
        var cut = 0;
        var roomFor = 0;
        var firstHoleError = "";
        var smallestRadius = 1e9;
        var largestRadius = 0;
        for (var fi = 0; fi < nFaces && definition.cutOut; fi += 1)
        {
            if (isPentagon[fi])
                continue;
            const hole = holeShape(loops[fi], definition.cutOffset / meter, definition.cutRadius / meter);
            if (!hole.fits)
                continue;
            roomFor += 1;
            const holeId = id + ("hole" ~ fi);
            var holed = false;
            try
            {
                holed = cutHole(context, holeId, qCreatedBy(id + ("face" ~ fi) + "surf", EntityType.FACE), hole);
            }
            catch (holeError)
            {
                if (firstHoleError == "")
                    firstHoleError = toString(holeError);
            }
            // Remove the helper sketch and plug.
            try silent(opDeleteBodies(context, holeId + "cleanup", { "entities" : qCreatedBy(holeId, EntityType.BODY) }));
            if (holed == true)
            {
                cut += 1;
                smallestRadius = min(smallestRadius, hole.radius);
                largestRadius = max(largestRadius, hole.radius);
            }
        }
        if (definition.cutOut)
        {
            note = note ~ "Cutouts in " ~ cut ~ " of " ~ connCount ~ " connecting shapes";
            if (cut > 0)
                note = note ~ ", corner radius " ~ roundToPrecision(smallestRadius / INCH, 2) ~ " to " ~ roundToPrecision(largestRadius / INCH, 2) ~ " in";
            note = note ~ " (" ~ (connCount - roomFor) ~ " were too small for one). ";
            if (firstHoleError != "")
                note = note ~ "Some cutouts failed: " ~ firstHoleError ~ ". ";
        }

        if (knitNote != "" || failed > 0)
            reportFeatureWarning(context, id, note ~ knitNote);
        else
            reportFeatureInfo(context, id, note);
        }
        catch (error)
        {
            throw regenError("Failed while " ~ stage ~ ": " ~ toString(error));
        }
    }, { "cutOut" : true, "cutOffset" : 0.5 * inch, "cutRadius" : 0.5 * inch, "colorPentagons" : true, "swapUV" : false,
          "connectorSize" : 5 * inch, "pentagonPercent" : 15, "sizeVariation" : 25, "seed" : 1, "passes" : 40, "gap" : 2 * inch,
          "flatPentagons" : false, "panelsPerPiece" : 60, "printDxf" : true,
          "seamAllowance" : 0.25 * inch, "dartClose" : 5 });

// ---------------------------------------------------------------- layouts ----

// Organic layout: a honeycomb wraps the tube; random hexagon edges are turned a
// quarter turn, making pentagon / heptagon pairs (pentagons never touch); vertices
// then slide on the tube until every pentagon is even and the chosen size, and each
// connecting shape has a random size. Finally each pentagon is replaced by an exactly
// flat regular one, so its corners leave the tube slightly.
function organicLayout(context is Context, face is Query, swap is boolean, definition is map) returns map
{
    const diameter = definition.pentagonDiameter / meter;
    // Side of a regular pentagon from the circle through its corners.
    const targetSide = diameter * 0.5877852522924731;

    // ---- 1. Count around from the connecting shape size; rows to match ----
    const lens = lengthTable(context, face, swap);
    const L = lens[size(lens) - 1];
    const C = circumference(context, face, swap, 0.5);
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
                const y = raw[k][1];
                const onEnd = y == 0 || y == yMax;
                // Skip a corner that ends up in the middle of the cut edge.
                if (onEnd && raw[(k + 5) % 6][1] == y && raw[(k + 1) % 6][1] == y)
                    continue;
                var x = raw[k][0] - floor(raw[k][0] / N) * N;
                if (abs(x - N) < 1e-9)
                    x = 0;
                const key = "" ~ round(x * 1000) ~ "_" ~ round(y * 1000);
                if (keys[key] == undefined)
                {
                    keys[key] = size(tx);
                    tx = append(tx, x);
                    ty = append(ty, y);
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
    // Pentagon count from the share of the tube's surface they should cover.
    const pentagonArea = 0.5944103227 * diameter * diameter;   // (5 / 8) * sin(72 degrees) * d^2
    const want = round(definition.pentagonPercent / 100 * C * L / pentagonArea / 2);
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

    // Smooth out the rotated edges in the flat pattern first.
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

    // ---- 5. Evening: slide vertices on the tube until pentagons are regular ----
    var U = makeArray(nv, 0);
    var V = makeArray(nv, 0);
    for (var vtx = 0; vtx < nv; vtx += 1)
    {
        U[vtx] = tx[vtx] / N;
        V[vtx] = ty[vtx] / yMax;
    }
    for (var pass = 0; pass < definition.passes; pass += 1)
    {
        // Position of every vertex, and of a small step in each parameter.
        var params = makeArray(3 * nv, vector(0, 0));
        var vStep = makeArray(nv, PARAM_STEP);
        for (var vtx = 0; vtx < nv; vtx += 1)
        {
            if (V[vtx] > 0.5)
                vStep[vtx] = -PARAM_STEP;
            params[3 * vtx] = uvParam(U[vtx], V[vtx], swap);
            params[3 * vtx + 1] = uvParam(U[vtx] + PARAM_STEP, V[vtx], swap);
            params[3 * vtx + 2] = uvParam(U[vtx], V[vtx] + vStep[vtx], swap);
        }
        const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
        var X = makeArray(nv, [0, 0, 0]);
        for (var vtx = 0; vtx < nv; vtx += 1)
            X[vtx] = meters(pl[3 * vtx].origin);

        // Pull every vertex toward the middle of its neighbours. Pentagon corners
        // feel this only at the start; after that the pentagon shape decides.
        var move = makeArray(nv, [0, 0, 0]);
        const pentagonPull = pass < 0.4 * definition.passes ? 0.15 : 0;
        for (var vtx = 0; vtx < nv; vtx += 1)
        {
            if (fixed[vtx])
                continue;
            var mean = [0, 0, 0];
            for (var w in neighbours[vtx])
                mean = add3(mean, X[w]);
            const pull = inPentagon[vtx] ? pentagonPull : (variation > 0 ? 0.3 : 0.5);
            move[vtx] = scale3(sub3(scale3(mean, 1 / size(neighbours[vtx])), X[vtx]), pull);
        }

        // Connecting shapes: grow or shrink each one about its middle toward its random size.
        if (variation > 0)
        {
            var centre = makeArray(nf, [0, 0, 0]);
            var radius = makeArray(nf, 0);
            var radiusSum = 0;
            var radiusCount = 0;
            for (var fi = 0; fi < nf; fi += 1)
            {
                if (!resizable[fi])
                    continue;
                const f = faces[fi];
                var c = [0, 0, 0];
                for (var vtx in f)
                    c = add3(c, scale3(X[vtx], 1 / size(f)));
                for (var vtx in f)
                    radius[fi] += length3(sub3(X[vtx], c)) / size(f);
                centre[fi] = c;
                radiusSum += radius[fi];
                radiusCount += 1;
            }
            for (var fi = 0; fi < nf; fi += 1)
            {
                if (!resizable[fi])
                    continue;
                const grow = 0.5 * (faceScale[fi] * radiusSum / radiusCount - radius[fi]);
                for (var vtx in faces[fi])
                {
                    if (fixed[vtx] || inPentagon[vtx])
                        continue;
                    const out = sub3(X[vtx], centre[fi]);
                    // Each corner belongs to 3 faces, so share the correction.
                    move[vtx] = add3(move[vtx], scale3(out, grow / length3(out) / 3));
                }
            }
        }

        // All pentagons aim for the side of the chosen diameter.
        const springs = pentagonMoves(X, faces, pentagons, pentagonsAt, targetSide);
        for (var vtx = 0; vtx < nv; vtx += 1)
            move[vtx] = add3(move[vtx], springs[vtx]);

        // Turn each 3D move into a slide along the surface.
        for (var vtx = 0; vtx < nv; vtx += 1)
        {
            if (fixed[vtx])
                continue;
            const du = scale3(sub3(meters(pl[3 * vtx + 1].origin), X[vtx]), 1 / PARAM_STEP);
            const dv = scale3(sub3(meters(pl[3 * vtx + 2].origin), X[vtx]), 1 / vStep[vtx]);
            const g11 = dot3(du, du);
            const g12 = dot3(du, dv);
            const g22 = dot3(dv, dv);
            const r1 = dot3(du, move[vtx]);
            const r2 = dot3(dv, move[vtx]);
            const det = g11 * g22 - g12 * g12;
            if (det < 1e-18)
                continue;
            U[vtx] = U[vtx] + (g22 * r1 - g12 * r2) / det;
            V[vtx] = min(max(V[vtx] + (g11 * r2 - g12 * r1) / det, 0), 1);
        }
    }

    // ---- 6. Final positions, and how even the pentagons came out ----
    var finalParams = makeArray(nv, vector(0, 0));
    for (var vtx = 0; vtx < nv; vtx += 1)
        finalParams[vtx] = uvParam(U[vtx], V[vtx], swap);
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : finalParams });
    var P = makeArray(nv, [0, 0, 0]);
    for (var vtx = 0; vtx < nv; vtx += 1)
        P[vtx] = meters(planes[vtx].origin);

    // ---- 7. Optionally, exactly flat regular pentagons. Pentagons never share a
    //         corner, so each corner is moved by at most one of them. ----
    if (definition.flatPentagons)
    {
        for (var fi in pentagons)
        {
            const five = regularPentagon(P, faces[fi], diameter / 2);
            for (var k = 0; k < 5; k += 1)
                P[faces[fi][k]] = five[k];
        }
    }
    var loops = [];
    for (var fi = 0; fi < nf; fi += 1)
    {
        var loop = [];
        for (var vtx in faces[fi])
            loop = append(loop, P[vtx]);
        loops = append(loops, loop);
    }
    const fitNote = made < want ? " (asked for " ~ 2 * want ~ ", only this many fit)" : "";
    return { "loops" : loops, "isPentagon" : isPentagon, "faces" : faces, "P" : P, "vertexCount" : nv,
            "note" : N ~ " around, " ~ (M + 1) ~ " rows: " ~ size(pentagons) ~ " pentagons covering " ~
                roundToPrecision(100 * size(pentagons) * pentagonArea / (C * L), 0) ~ "% of the surface" ~ fitNote ~ " and " ~ (nf - size(pentagons)) ~ " connecting shapes. " };
}

// A flat regular pentagon of circumradius `reach`, in the best-fit plane of face f's
// corners, centred on them and turned to match them as closely as possible.
function regularPentagon(P is array, f is array, reach is number) returns array
{
    var normal = [0, 0, 0];
    var centre = [0, 0, 0];
    for (var k = 0; k < 5; k += 1)
    {
        const a = P[f[k]];
        const b = P[f[(k + 1) % 5]];
        normal = add3(normal, [(a[1] - b[1]) * (a[2] + b[2]), (a[2] - b[2]) * (a[0] + b[0]), (a[0] - b[0]) * (a[1] + b[1])]);
        centre = add3(centre, scale3(a, 0.2));
    }
    normal = scale3(normal, 1 / length3(normal));
    var e1 = sub3(P[f[0]], centre);
    e1 = sub3(e1, scale3(normal, dot3(e1, normal)));
    e1 = scale3(e1, 1 / length3(e1));
    const e2 = cross3(normal, e1);
    // With the Newell normal the corners run counter-clockwise in (e1, e2).
    var sumCross = 0;
    var sumDot = 0;
    for (var k = 0; k < 5; k += 1)
    {
        const rel = sub3(P[f[k]], centre);
        const x = dot3(rel, e1);
        const y = dot3(rel, e2);
        const a = k * 0.4 * PI * radian;
        sumCross += cos(a) * y - sin(a) * x;
        sumDot += cos(a) * x + sin(a) * y;
    }
    const twist = atan2(sumCross, sumDot);
    var five = [];
    for (var k = 0; k < 5; k += 1)
    {
        const a = twist + k * 0.4 * PI * radian;
        five = append(five, add3(centre, add3(scale3(e1, reach * cos(a)), scale3(e2, reach * sin(a)))));
    }
    return five;
}

// Grid layout: a Cairo pentagon grid wraps the tube, and each grid pentagon is
// replaced by a flat regular one of the chosen diameter. Returns the pentagon and
// connecting-shape outlines, and a note for the feature.
function gridLayout(context is Context, face is Query, swap is boolean, definition is map) returns map
{
    const diameter = definition.pentagonDiameter / meter;
    const reach = diameter / 2;
    // ---- 1. Count around and rows from the pentagon size and gap ----
    const lens = lengthTable(context, face, swap);
    const L = lens[size(lens) - 1];
    const C = circumference(context, face, swap, 0.5);
    const wantCell = (definition.gap / meter + GAP_PER_DIAMETER * diameter) / GAP_PER_CELL;
    const N = max(4, 2 * round(C / wantCell / 2));
    const cell = C / N;
    const M = max(1, round(L / cell));

    // ---- 2. Cairo grid in tiling coordinates: x in [0, N) around, y in [0, M] along ----
    var keys = {};
    var tx = [];
    var ty = [];
    var faces = [];
    for (var j = 0; j < M; j += 1)
    {
        for (var i = 0; i < N; i += 1)
        {
            var f = [];
            for (var p in cairoPentagon(i, j))
            {
                var x = p[0] - floor(p[0] / N) * N;
                if (abs(x - N) < 1e-9)
                    x = 0;
                const key = "" ~ round(x * 1e6) ~ "_" ~ round(p[1] * 1e6);
                if (keys[key] == undefined)
                {
                    keys[key] = size(tx);
                    tx = append(tx, x);
                    ty = append(ty, p[1]);
                }
                f = append(f, keys[key]);
            }
            faces = append(faces, f);
        }
    }
    const nv = size(tx);
    const nf = size(faces);

    // ---- 3. Grid corners and cell centres on the tube ----
    var params = makeArray(nv + nf, vector(0, 0));
    for (var vtx = 0; vtx < nv; vtx += 1)
        params[vtx] = uvParam(tx[vtx] / N, vOf(lens, min(max(ty[vtx], 0), M) / M * L), swap);
    for (var fi = 0; fi < nf; fi += 1)
        params[nv + fi] = uvParam((fi % N + 0.5) / N, vOf(lens, (floor(fi / N) + 0.5) / M * L), swap);
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
    var X = makeArray(nv, [0, 0, 0]);
    for (var vtx = 0; vtx < nv; vtx += 1)
        X[vtx] = meters(planes[vtx].origin);

    // ---- 4. A flat regular pentagon in every cell ----
    // A flat panel touching the tube at its middle has corners outside it. Sink it
    // by half of that, so the panel straddles the tube's surface.
    const tubeRadius = C / (2 * PI);
    const sink = (sqrt(tubeRadius * tubeRadius + diameter * diameter / 8) - tubeRadius) / 2;
    var corners = makeArray(nf, []);
    for (var fi = 0; fi < nf; fi += 1)
    {
        const f = faces[fi];
        const tangent = planes[nv + fi];
        var centre = meters(tangent.origin);
        var normal = [tangent.normal[0], tangent.normal[1], tangent.normal[2]];
        // Point the normal out of the tube: the grid corners lie on the inner side.
        var middle = [0, 0, 0];
        for (var vtx in f)
            middle = add3(middle, scale3(X[vtx], 0.2));
        if (dot3(normal, sub3(middle, centre)) > 0)
            normal = scale3(normal, -1);
        centre = sub3(centre, scale3(normal, sink));
        const e1 = [tangent.x[0], tangent.x[1], tangent.x[2]];
        const e2 = cross3(normal, e1);

        // Turn the regular pentagon to match the grid pentagon as closely as possible.
        var flat = [];
        for (var vtx in f)
        {
            const rel = sub3(X[vtx], centre);
            flat = append(flat, [dot3(rel, e1), dot3(rel, e2)]);
        }
        var area = 0;
        for (var k = 0; k < 5; k += 1)
            area += flat[k][0] * flat[(k + 1) % 5][1] - flat[(k + 1) % 5][0] * flat[k][1];
        const turn = area > 0 ? 1 : -1;
        var sumCross = 0;
        var sumDot = 0;
        for (var k = 0; k < 5; k += 1)
        {
            const a = turn * k * 0.4 * PI * radian;
            sumCross += cos(a) * flat[k][1] - sin(a) * flat[k][0];
            sumDot += cos(a) * flat[k][0] + sin(a) * flat[k][1];
        }
        const twist = atan2(sumCross, sumDot);
        var five = [];
        for (var k = 0; k < 5; k += 1)
        {
            const a = twist + turn * k * 0.4 * PI * radian;
            five = append(five, add3(centre, add3(scale3(e1, reach * cos(a)), scale3(e2, reach * sin(a)))));
        }
        corners[fi] = five;
    }

    // ---- 5. Connecting shapes ----
    var connectors = [];
    // 5a. A 4-sided piece between each pair of facing pentagon edges.
    var edgeSides = {};
    for (var fi = 0; fi < nf; fi += 1)
    {
        const f = faces[fi];
        for (var k = 0; k < 5; k += 1)
        {
            const a = f[k];
            const b = f[(k + 1) % 5];
            const key = "" ~ min(a, b) ~ "_" ~ max(a, b);
            const side = { "from" : a, "pFrom" : corners[fi][k], "pTo" : corners[fi][(k + 1) % 5] };
            edgeSides[key] = append(edgeSides[key] == undefined ? [] : edgeSides[key], side);
        }
    }
    var gapSum = 0;
    var gapCount = 0;
    for (var entry in edgeSides)
    {
        const sides = entry.value;
        if (size(sides) != 2)
            continue;   // an edge at a tube end
        const s1 = sides[0];
        const s2 = sides[1];
        // Walk s1 forward, then s2 the opposite way along the same grid edge.
        const sameWay = s1.from == s2.from;
        connectors = append(connectors, [s1.pFrom, s1.pTo, sameWay ? s2.pTo : s2.pFrom, sameWay ? s2.pFrom : s2.pTo]);
        gapSum += length3(sub3(scale3(add3(s1.pFrom, s1.pTo), 0.5), scale3(add3(s2.pFrom, s2.pTo), 0.5)));
        gapCount += 1;
    }
    // 5b. A 3- or 4-sided piece where pentagon corners meet.
    var around = makeArray(nv, []);
    for (var fi = 0; fi < nf; fi += 1)
    {
        for (var k = 0; k < 5; k += 1)
            around[faces[fi][k]] = append(around[faces[fi][k]], corners[fi][k]);
    }
    for (var vtx = 0; vtx < nv; vtx += 1)
    {
        const onGrid = abs(tx[vtx] - round(tx[vtx])) < 1e-6 && abs(ty[vtx] - round(ty[vtx])) < 1e-6;
        if (size(around[vtx]) != (onGrid ? 4 : 3))
            continue;   // at a tube end
        const tangent = planes[vtx];
        const normal = [tangent.normal[0], tangent.normal[1], tangent.normal[2]];
        const e1 = [tangent.x[0], tangent.x[1], tangent.x[2]];
        const e2 = cross3(normal, e1);
        var ring = [];
        for (var pt in around[vtx])
        {
            const rel = sub3(pt, X[vtx]);
            ring = append(ring, { "angle" : atan2(dot3(rel, e2), dot3(rel, e1)) / radian, "point" : pt });
        }
        ring = sort(ring, function(a, b)
            {
                return a.angle - b.angle;
            });
        var loop = [];
        for (var item in ring)
            loop = append(loop, item.point);
        connectors = append(connectors, loop);
    }
    const nc = size(connectors);

    const layoutNote = N ~ " around, " ~ M ~ " rows: " ~ nf ~ " pentagons and " ~ nc ~ " connecting shapes. Gaps average " ~
        roundToPrecision(gapSum / max(gapCount, 1) / INCH, 2) ~ " in. ";
    return { "pentagons" : corners, "connectors" : connectors, "note" : layoutNote };
}

// ----------------------------------------------------------------- pieces ----

// Splits the faces into as few pieces as possible that each unfold flat onto a
// sheet. A piece grows outward from a seed face, joining each new face along a
// single fold edge, so the folds form a tree and the piece can always be laid flat.
// A face is only added if, laid flat, it stays clear of the rest of the piece (room
// for seam allowances) and the piece still fits on a sheet. Returns:
//   pieceOf[face], pieces[{ faces, hinges[{ parent, child, a, b }], place{face: 2D corners} }],
//   colour[piece] (0-5, neighbouring pieces differ where possible), edgeFaces{edge key: faces}.
function makePieces(P is array, faces is array, isPentagon is array, definition is map) returns map
{
    const nf = size(faces);
    const maxSize = definition.panelsPerPiece;
    const clearance = 2 * definition.seamAllowance / meter;
    var edgeFaces = {};
    for (var fi = 0; fi < nf; fi += 1)
    {
        const f = faces[fi];
        for (var k = 0; k < size(f); k += 1)
        {
            const key = pieceEdgeKey(f[k], f[(k + 1) % size(f)]);
            edgeFaces[key] = append(edgeFaces[key] == undefined ? [] : edgeFaces[key], fi);
        }
    }
    var pieceOf = makeArray(nf, -1);
    var pieces = [];
    for (var seed = 0; seed < nf; seed += 1)
    {
        if (pieceOf[seed] != -1)
            continue;
        const p = size(pieces);
        var members = [seed];
        var hinges = [];
        var place = {};
        place[seed] = flattenFace(P, faces[seed]);
        var points = place[seed];
        pieceOf[seed] = p;
        var head = 0;
        while (head < size(members) && size(members) < maxSize)
        {
            const fi = members[head];
            head += 1;
            const f = faces[fi];
            for (var k = 0; k < size(f); k += 1)
            {
                const a = f[k];
                const b = f[(k + 1) % size(f)];
                for (var g in edgeFaces[pieceEdgeKey(a, b)])
                {
                    if (g == fi || pieceOf[g] != -1 || size(members) >= maxSize)
                        continue;
                    // Lay g flat against its fold edge.
                    const child = flattenFace(P, faces[g]);
                    const ca = child[listIndex(faces[g], a)];
                    const cb = child[listIndex(faces[g], b)];
                    const pa = place[fi][listIndex(f, a)];
                    const pb = place[fi][listIndex(f, b)];
                    const turn = atan2(pb[1] - pa[1], pb[0] - pa[0]) - atan2(cb[1] - ca[1], cb[0] - ca[0]);
                    var moved = [];
                    for (var pt in child)
                        moved = append(moved, add2(pa, rotate2(sub2(pt, ca), turn)));
                    // It must not run into the rest of the piece.
                    var clear = true;
                    for (var h in members)
                    {
                        if (h == fi)
                            continue;
                        if (size(listInBoth(faces[h], faces[g])) > 0)
                        {
                            // Neighbours meet at a corner. A thin overlap wedge there is fine
                            // (it gets closed like a dart); only a real overlap counts.
                            if (convexOverlap(shrink2(place[h], 0.9), shrink2(moved, 0.9), 0))
                                clear = false;
                        }
                        else if (convexOverlap(place[h], moved, clearance))
                            clear = false;
                        if (!clear)
                            break;
                    }
                    // And the piece must still fit on a sheet, with room for seam allowances.
                    if (clear && !fitsSheet(concatenateArrays([points, moved]), clearance))
                        clear = false;
                    if (!clear)
                        continue;
                    pieceOf[g] = p;
                    members = append(members, g);
                    hinges = append(hinges, { "parent" : fi, "child" : g, "a" : a, "b" : b });
                    place[g] = moved;
                    points = concatenateArrays([points, moved]);
                }
            }
        }
        pieces = append(pieces, { "faces" : members, "hinges" : hinges, "place" : place });
    }
    // Colour pieces so that neighbours differ where possible.
    var touching = makeArray(size(pieces), []);
    for (var entry in edgeFaces)
    {
        if (size(entry.value) != 2)
            continue;
        const p1 = pieceOf[entry.value[0]];
        const p2 = pieceOf[entry.value[1]];
        if (p1 != p2)
        {
            touching[p1] = append(touching[p1], p2);
            touching[p2] = append(touching[p2], p1);
        }
    }
    var colour = makeArray(size(pieces), -1);
    for (var p = 0; p < size(pieces); p += 1)
    {
        var used = makeArray(6, false);
        for (var q in touching[p])
        {
            if (colour[q] >= 0)
                used[colour[q]] = true;
        }
        colour[p] = p % 6;
        for (var c = 5; c >= 0; c -= 1)
        {
            if (!used[c])
                colour[p] = c;
        }
    }
    return { "pieceOf" : pieceOf, "pieces" : pieces, "colour" : colour, "edgeFaces" : edgeFaces };
}

function pieceEdgeKey(a is number, b is number) returns string
{
    return "" ~ min(a, b) ~ "_" ~ max(a, b);
}

// True when two convex polygons overlap, or come within `gap` of each other.
function convexOverlap(A is array, B is array, gap is number) returns boolean
{
    for (var poly in [A, B])
    {
        for (var k = 0; k < size(poly); k += 1)
        {
            const e = sub2(poly[(k + 1) % size(poly)], poly[k]);
            const len = sqrt(e[0] * e[0] + e[1] * e[1]);
            if (len < 1e-12)
                continue;
            const axis = [-e[1] / len, e[0] / len];
            var aLo = 1e9;
            var aHi = -1e9;
            var bLo = 1e9;
            var bHi = -1e9;
            for (var p in A)
            {
                aLo = min(aLo, p[0] * axis[0] + p[1] * axis[1]);
                aHi = max(aHi, p[0] * axis[0] + p[1] * axis[1]);
            }
            for (var p in B)
            {
                bLo = min(bLo, p[0] * axis[0] + p[1] * axis[1]);
                bHi = max(bHi, p[0] * axis[0] + p[1] * axis[1]);
            }
            if (bLo - aHi >= gap || aLo - bHi >= gap)
                return false;   // separated along this axis
        }
    }
    return true;
}

// A polygon scaled about its centre.
function shrink2(poly is array, factor is number) returns array
{
    const c = centroid2(poly);
    var out = [];
    for (var p in poly)
        out = append(out, add2(c, [(p[0] - c[0]) * factor, (p[1] - c[1]) * factor]));
    return out;
}

// True when the points, turned to their long direction and padded by `pad`, fit on a sheet.
function fitsSheet(points is array, pad is number) returns boolean
{
    const bounds = alignedBox(points);
    return bounds.w + 2 * pad <= SHEET_LENGTH - 2 * SHEET_MARGIN && bounds.h + 2 * pad <= SHEET_WIDTH - 2 * SHEET_MARGIN;
}

// Size of the bounds around the points once turned so their long direction runs along x.
function alignedBox(points is array) returns map
{
    const mean = centroid2(points);
    var sxx = 0;
    var syy = 0;
    var sxy = 0;
    for (var p in points)
    {
        const d = sub2(p, mean);
        sxx += d[0] * d[0];
        syy += d[1] * d[1];
        sxy += d[0] * d[1];
    }
    const spin = -0.5 * atan2(2 * sxy, sxx - syy);
    var lo = [1e9, 1e9];
    var hi = [-1e9, -1e9];
    for (var p in points)
    {
        const r = rotate2(sub2(p, mean), spin);
        lo = [min(lo[0], r[0]), min(lo[1], r[1])];
        hi = [max(hi[0], r[0]), max(hi[1], r[1])];
    }
    return { "w" : hi[0] - lo[0], "h" : hi[1] - lo[1], "spin" : spin, "mean" : mean };
}

// A face laid flat with every edge its true 3D length: the face is split into a fan
// of triangles from its first corner, and the triangles are unfolded into one plane.
// A face that is slightly cupped in 3D comes out as a flat shape the sheet bends into.
// Corners run counter-clockwise.
function flattenFace(P is array, f is array) returns array
{
    const count = size(f);
    var out = [[0, 0], [length3(sub3(P[f[1]], P[f[0]])), 0]];
    for (var k = 2; k < count; k += 1)
    {
        const d0 = length3(sub3(P[f[k]], P[f[0]]));
        const d1 = length3(sub3(P[f[k]], P[f[k - 1]]));
        const base = out[k - 1];
        const L = sqrt(base[0] * base[0] + base[1] * base[1]);
        const u = [base[0] / L, base[1] / L];
        const along = (d0 * d0 - d1 * d1 + L * L) / (2 * L);
        const up = sqrt(max(d0 * d0 - along * along, 0));
        out = append(out, [u[0] * along - u[1] * up, u[1] * along + u[0] * up]);
    }
    return out;
}

// Union-find over corner copies.
function rootOf(parent is array, k is number) returns number
{
    var r = k;
    while (parent[r] != r)
        r = parent[r];
    return r;
}

// Lays every piece out flat, closes narrow darts, adds seam allowances, packs the
// pieces onto 48 x 96 in sheets beside the tube, and draws them as sketches on the
// Top plane: CUT (outlines, seam allowances and cutouts), SCORE (folds between faces),
// SEAM (where each seam allowance folds) and the sheet outlines. Optionally prints the
// same as a DXF (mm). Returns a note for the feature.
function flatPieces(context is Context, id is Id, P is array, faces is array, isPentagon is array, pieces is map, definition is map) returns string
{
    const offset = definition.cutOffset / meter;
    const radius = definition.cutRadius / meter;
    const allowance = definition.seamAllowance / meter;
    const dartLimit = definition.dartClose * degree;
    var laid = [];
    var dartsClosed = 0;
    var worstStrain = 0;
    var tabs = 0;
    for (var pi = 0; pi < size(pieces.pieces); pi += 1)
    {
        const piece = pieces.pieces[pi];
        // One entry per face corner; corners joined by folds (or closed darts) share a root.
        var slot = {};
        var parent = [];
        var pos = [];
        var inPiece = {};
        for (var fi in piece.faces)
        {
            inPiece[fi] = true;
            const f = faces[fi];
            for (var k = 0; k < size(f); k += 1)
            {
                slot[fi ~ "_" ~ f[k]] = size(parent);
                parent = append(parent, size(parent));
                pos = append(pos, piece.place[fi][k]);
            }
        }
        var joined = {};
        for (var h in piece.hinges)
        {
            for (var v in [h.a, h.b])
            {
                const r1 = rootOf(parent, slot[h.parent ~ "_" ~ v]);
                const r2 = rootOf(parent, slot[h.child ~ "_" ~ v]);
                if (r1 != r2)
                    parent[r2] = r1;
            }
            joined[pieceEdgeKey(h.a, h.b)] = "fold";
        }
        // Darts: an edge whose two faces are both in this piece but not folded together.
        // If its copies meet at one end and open by no more than the limit, pull the
        // other ends together.
        var closedAny = false;
        for (var pass = 0; pass < 3; pass += 1)
        {
            for (var entry in pieces.edgeFaces)
            {
                const two = entry.value;
                if (size(two) != 2 || inPiece[two[0]] != true || inPiece[two[1]] != true || joined[entry.key] != undefined)
                    continue;
                const f = faces[two[0]];
                var ends = [];
                for (var k = 0; k < size(f); k += 1)
                {
                    if (pieceEdgeKey(f[k], f[(k + 1) % size(f)]) == entry.key)
                        ends = [f[k], f[(k + 1) % size(f)]];
                }
                for (var e = 0; e < 2; e += 1)
                {
                    const apex = ends[e];
                    const tip = ends[1 - e];
                    if (rootOf(parent, slot[two[0] ~ "_" ~ apex]) != rootOf(parent, slot[two[1] ~ "_" ~ apex]))
                        continue;
                    const o = pos[rootOf(parent, slot[two[0] ~ "_" ~ apex])];
                    const t1 = pos[slot[two[0] ~ "_" ~ tip]];
                    const t2 = pos[slot[two[1] ~ "_" ~ tip]];
                    const d1 = sub2(t1, o);
                    const d2 = sub2(t2, o);
                    const opening = abs(atan2(d1[0] * d2[1] - d1[1] * d2[0], d1[0] * d2[0] + d1[1] * d2[1]));
                    if (opening <= dartLimit)
                    {
                        const r1 = rootOf(parent, slot[two[0] ~ "_" ~ tip]);
                        const r2 = rootOf(parent, slot[two[1] ~ "_" ~ tip]);
                        if (r1 != r2)
                        {
                            pos[r1] = scale2(add2(pos[r1], pos[r2]), 0.5);
                            parent[r2] = r1;
                        }
                        joined[entry.key] = "dart";
                        dartsClosed += 1;
                        closedAny = true;
                    }
                    break;
                }
            }
        }
        // Gather each corner at its root's position, then, if darts were closed, ease
        // the piece so every edge (and each face's fan diagonals) is close to its true length.
        var at = makeArray(size(parent), [0, 0]);
        var count = makeArray(size(parent), 0);
        for (var k = 0; k < size(parent); k += 1)
        {
            const r = rootOf(parent, k);
            at[r] = add2(at[r], pos[k]);
            count[r] += 1;
        }
        for (var k = 0; k < size(parent); k += 1)
        {
            if (count[k] > 0)
                at[k] = scale2(at[k], 1 / count[k]);
        }
        var springs = [];
        for (var fi in piece.faces)
        {
            const f = faces[fi];
            for (var k = 0; k < size(f); k += 1)
            {
                const next = (k + 1) % size(f);
                springs = append(springs, [rootOf(parent, slot[fi ~ "_" ~ f[k]]), rootOf(parent, slot[fi ~ "_" ~ f[next]]), length3(sub3(P[f[next]], P[f[k]]))]);
                if (k >= 2)
                    springs = append(springs, [rootOf(parent, slot[fi ~ "_" ~ f[0]]), rootOf(parent, slot[fi ~ "_" ~ f[k]]), length3(sub3(P[f[k]], P[f[0]]))]);
            }
        }
        for (var it = 0; it < 200 && closedAny; it += 1)
        {
            var push = makeArray(size(parent), [0, 0]);
            var hits = makeArray(size(parent), 0);
            for (var s in springs)
            {
                const d = sub2(at[s[1]], at[s[0]]);
                const len = sqrt(d[0] * d[0] + d[1] * d[1]);
                const fix = scale2(d, 0.5 * (len - s[2]) / len);
                push[s[0]] = add2(push[s[0]], fix);
                push[s[1]] = sub2(push[s[1]], fix);
                hits[s[0]] += 1;
                hits[s[1]] += 1;
            }
            for (var k = 0; k < size(parent); k += 1)
            {
                if (hits[k] > 0)
                    at[k] = add2(at[k], scale2(push[k], 1 / hits[k]));
            }
        }
        for (var s in springs)
        {
            const d = sub2(at[s[1]], at[s[0]]);
            worstStrain = max(worstStrain, abs(sqrt(d[0] * d[0] + d[1] * d[1]) - s[2]) / s[2]);
        }
        var place = {};
        for (var fi in piece.faces)
        {
            var loop = [];
            for (var v in faces[fi])
                loop = append(loop, at[rootOf(parent, slot[fi ~ "_" ~ v])]);
            place[fi] = loop;
        }

        // Edges: folds and closed darts are scored once. Other edges are cut; those that
        // join another piece (or this one) get a seam allowance outside them.
        var cutSegs = [];
        var scoreSegs = [];
        var seamSegs = [];
        var outline = [];
        var scored = {};
        for (var fi in piece.faces)
        {
            const f = faces[fi];
            const flat = place[fi];
            for (var k = 0; k < size(f); k += 1)
            {
                const key = pieceEdgeKey(f[k], f[(k + 1) % size(f)]);
                const a = flat[k];
                const b = flat[(k + 1) % size(f)];
                if (joined[key] != undefined)
                {
                    if (scored[key] != true)
                    {
                        scoreSegs = append(scoreSegs, { "kind" : "line", "pts" : [a, b] });
                        scored[key] = true;
                    }
                }
                else
                    outline = append(outline, { "a" : a, "b" : b, "joins" : size(pieces.edgeFaces[key]) == 2 });
            }
            if (definition.cutOut && !isPentagon[fi])
            {
                var flat3 = [];
                for (var pt in flat)
                    flat3 = append(flat3, [pt[0], pt[1], 0]);
                const hole = holeShape(flat3, offset, radius);
                if (hole.fits)
                {
                    const yAxis = cross3(hole.normal, hole.xAxis);
                    for (var seg in holeSegments(hole))
                    {
                        var pts = [];
                        for (var pt in seg.pts)
                        {
                            const w = add3(hole.origin, add3(scale3(hole.xAxis, pt[0]), scale3(yAxis, pt[1])));
                            pts = append(pts, [w[0], w[1]]);
                        }
                        cutSegs = append(cutSegs, { "kind" : seg.kind, "pts" : pts });
                    }
                }
            }
        }
        // Seam allowances: a strip `allowance` wide outside each joining edge. Its ends
        // are cut back where the outline turns inward, so neighbouring strips don't
        // collide; in tight corners the strip gets shorter or is left off.
        for (var e in outline)
        {
            if (!e.joins || allowance <= 0)
            {
                cutSegs = append(cutSegs, { "kind" : "line", "pts" : [e.a, e.b] });
                continue;
            }
            const along = sub2(e.b, e.a);
            const len = sqrt(along[0] * along[0] + along[1] * along[1]);
            const t = scale2(along, 1 / len);
            const n = [t[1], -t[0]];   // faces lie to the left, so outside is to the right
            var before = undefined;
            var after = undefined;
            for (var o in outline)
            {
                if (dist2(o.b, e.a) < 1e-6 && dist2(o.a, e.b) > 1e-6)
                    before = o.a;
                if (dist2(o.a, e.b) < 1e-6 && dist2(o.b, e.a) > 1e-6)
                    after = o.b;
            }
            const backA = before == undefined ? 0 : cutBack(t, unit2(before, e.a), allowance, false);
            const backB = after == undefined ? 0 : cutBack(scale2(t, -1), unit2(after, e.b), allowance, true);
            if (len - backA - backB < 2 * allowance + 0.002)
            {
                cutSegs = append(cutSegs, { "kind" : "line", "pts" : [e.a, e.b] });
                continue;
            }
            const A = add2(e.a, scale2(t, backA));
            const B = sub2(e.b, scale2(t, backB));
            const A2 = add2(A, add2(scale2(n, allowance), scale2(t, allowance)));
            const B2 = add2(B, sub2(scale2(n, allowance), scale2(t, allowance)));
            if (backA > 1e-6)
                cutSegs = append(cutSegs, { "kind" : "line", "pts" : [e.a, A] });
            cutSegs = concatenateArrays([cutSegs, [{ "kind" : "line", "pts" : [A, A2] }, { "kind" : "line", "pts" : [A2, B2] }, { "kind" : "line", "pts" : [B2, B] }]]);
            if (backB > 1e-6)
                cutSegs = append(cutSegs, { "kind" : "line", "pts" : [B, e.b] });
            seamSegs = append(seamSegs, { "kind" : "line", "pts" : [A, B] });
            tabs += 1;
        }

        // Turn the piece so its long direction runs along the sheet, and move it to the origin.
        var outlinePoints = [];
        for (var seg in cutSegs)
            outlinePoints = concatenateArrays([outlinePoints, seg.pts]);
        const bounds = alignedBox(outlinePoints);
        cutSegs = turnSegments(cutSegs, bounds.mean, bounds.spin);
        scoreSegs = turnSegments(scoreSegs, bounds.mean, bounds.spin);
        seamSegs = turnSegments(seamSegs, bounds.mean, bounds.spin);
        var lo = [1e9, 1e9];
        var hi = [-1e9, -1e9];
        for (var seg in cutSegs)
        {
            for (var pt in seg.pts)
            {
                lo = [min(lo[0], pt[0]), min(lo[1], pt[1])];
                hi = [max(hi[0], pt[0]), max(hi[1], pt[1])];
            }
        }
        const label = add2(bounds.mean, rotate2(sub2(centroid2(place[piece.faces[0]]), bounds.mean), bounds.spin));
        const shift = [-lo[0], -lo[1]];
        laid = append(laid, { "cut" : moveSegments(cutSegs, shift), "score" : moveSegments(scoreSegs, shift), "seam" : moveSegments(seamSegs, shift),
                    "label" : add2(label, shift), "w" : hi[0] - lo[0], "h" : hi[1] - lo[1], "n" : pi + 1 });
    }

    // Pack: tallest pieces first, in rows along each sheet; a new sheet when one is full.
    var order = [];
    for (var k = 0; k < size(laid); k += 1)
        order = append(order, { "k" : k, "h" : laid[k].h });
    order = sort(order, function(a, b)
        {
            return b.h - a.h;
        });
    // Sheets sit side by side on the Top plane, clear of the tube.
    const baseX = 0.5;
    var sheet = 0;
    var x = SHEET_MARGIN;
    var y = SHEET_MARGIN;
    var rowHeight = 0;
    var tooBig = 0;
    var allCut = [];
    var allScore = [];
    var allSeam = [];
    var labels = [];
    for (var item in order)
    {
        const piece = laid[item.k];
        if (piece.w > SHEET_LENGTH - 2 * SHEET_MARGIN || piece.h > SHEET_WIDTH - 2 * SHEET_MARGIN)
            tooBig += 1;
        if (x + piece.w > SHEET_LENGTH - SHEET_MARGIN && x > SHEET_MARGIN)
        {
            x = SHEET_MARGIN;
            y += rowHeight + PIECE_GAP;
            rowHeight = 0;
        }
        if (y + piece.h > SHEET_WIDTH - SHEET_MARGIN && y > SHEET_MARGIN)
        {
            sheet += 1;
            x = SHEET_MARGIN;
            y = SHEET_MARGIN;
            rowHeight = 0;
        }
        const at = [baseX + sheet * (SHEET_LENGTH + 0.25) + x, y];
        allCut = concatenateArrays([allCut, moveSegments(piece.cut, at)]);
        allScore = concatenateArrays([allScore, moveSegments(piece.score, at)]);
        allSeam = concatenateArrays([allSeam, moveSegments(piece.seam, at)]);
        labels = append(labels, { "text" : "P" ~ piece.n, "at" : add2(piece.label, at) });
        x += piece.w + PIECE_GAP;
        rowHeight = max(rowHeight, piece.h);
    }
    const sheetCount = sheet + 1;
    var sheetSegs = [];
    for (var k = 0; k < sheetCount; k += 1)
    {
        const x0 = baseX + k * (SHEET_LENGTH + 0.25);
        const c = [[x0, 0], [x0 + SHEET_LENGTH, 0], [x0 + SHEET_LENGTH, SHEET_WIDTH], [x0, SHEET_WIDTH]];
        for (var e = 0; e < 4; e += 1)
            sheetSegs = append(sheetSegs, { "kind" : "line", "pts" : [c[e], c[(e + 1) % 4]] });
    }

    const top = plane(vector(0, 0, 0) * meter, vector(0, 0, 1), vector(1, 0, 0));
    for (var layer in [["cut", allCut], ["score", allScore], ["seam", allSeam], ["sheets", sheetSegs]])
    {
        if (size(layer[1]) == 0)
            continue;
        const sk = newSketchOnPlane(context, id + layer[0], { "sketchPlane" : top });
        drawSegments(sk, "s", layer[1]);
        skSolve(sk);
    }
    if (definition.printDxf)
        printPiecesDxf([["CUT", allCut], ["SCORE", allScore], ["SEAM", allSeam], ["SHEET", sheetSegs]], labels);

    var note = size(laid) ~ " pieces on " ~ sheetCount ~ " sheets of 48 x 96 in";
    if (tooBig > 0)
        note = note ~ " (" ~ tooBig ~ " pieces are bigger than a sheet)";
    note = note ~ ". " ~ tabs ~ " seam allowances. " ~ dartsClosed ~ " darts closed; edges stretched or squeezed by up to " ~
        roundToPrecision(worstStrain * 100, 1) ~ "% to do it. Labels P1 to P" ~ size(laid) ~ " are in the DXF. ";
    if (definition.printDxf)
        note = note ~ "DXF printed to the FeatureScript console. ";
    return note;
}

// How far back from a corner a seam allowance must start so it doesn't run into the
// neighbouring edge's allowance. `out` points along this edge away from the corner,
// `other` along the neighbouring outline edge. `atEnd` is true at the edge's far end,
// where the outside lies to the left of `out` instead of the right.
function cutBack(out is array, other is array, allowance is number, atEnd is boolean) returns number
{
    // Opening angle on the outside of the piece between the two edges.
    const crossing = out[0] * other[1] - out[1] * other[0];
    const ccw = atan2(crossing, out[0] * other[0] + out[1] * other[1]) / radian;
    var turn = ccw < 0 ? ccw + 2 * PI : ccw;
    const opening = atEnd ? turn : 2 * PI - turn;
    if (opening >= PI - 1e-9)
        return 0;
    return allowance / tan(opening / 2 * radian);
}

function scale2(a is array, s is number) returns array
{
    return [a[0] * s, a[1] * s];
}

function dist2(a is array, b is array) returns number
{
    return sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));
}

function turnSegments(segs is array, about is array, angle) returns array
{
    var out = [];
    for (var seg in segs)
    {
        var pts = [];
        for (var pt in seg.pts)
            pts = append(pts, add2(about, rotate2(sub2(pt, about), angle)));
        out = append(out, { "kind" : seg.kind, "pts" : pts });
    }
    return out;
}

function moveSegments(segs is array, shift is array) returns array
{
    var out = [];
    for (var seg in segs)
    {
        var pts = [];
        for (var pt in seg.pts)
            pts = append(pts, add2(pt, shift));
        out = append(out, { "kind" : seg.kind, "pts" : pts });
    }
    return out;
}

function add2(a is array, b is array) returns array
{
    return [a[0] + b[0], a[1] + b[1]];
}

function sub2(a is array, b is array) returns array
{
    return [a[0] - b[0], a[1] - b[1]];
}

function rotate2(p is array, angle) returns array
{
    const c = cos(angle);
    const s = sin(angle);
    return [p[0] * c - p[1] * s, p[0] * s + p[1] * c];
}

function centroid2(pts is array) returns array
{
    var c = [0, 0];
    for (var p in pts)
        c = [c[0] + p[0] / size(pts), c[1] + p[1] / size(pts)];
    return c;
}

// Minimal ASCII DXF (R12) in millimeters: the given layers plus LABELS.
// Copy everything between the markers into a .dxf file.
function printPiecesDxf(layers is array, labels is array)
{
    println("----- DXF START (copy the lines below into coral-pieces.dxf) -----");
    println("0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES");
    for (var layer in layers)
    {
        for (var seg in layer[1])
            println(dxfEntity(layer[0], seg));
    }
    for (var l in labels)
        println("0\nTEXT\n8\nLABELS\n10\n" ~ dxfMm(l.at[0]) ~ "\n20\n" ~ dxfMm(l.at[1]) ~ "\n30\n0\n40\n12\n1\n" ~ l.text);
    println("0\nENDSEC\n0\nEOF");
    println("----- DXF END -----");
}

function dxfEntity(layer is string, seg is map) returns string
{
    const p = seg.pts;
    if (seg.kind == "line")
        return "0\nLINE\n8\n" ~ layer ~ "\n10\n" ~ dxfMm(p[0][0]) ~ "\n20\n" ~ dxfMm(p[0][1]) ~ "\n30\n0\n11\n" ~
            dxfMm(p[1][0]) ~ "\n21\n" ~ dxfMm(p[1][1]) ~ "\n31\n0";
    // DXF arcs run counter-clockwise from the start angle to the end angle.
    const c = p[3];
    const r = sqrt((p[0][0] - c[0]) ^ 2 + (p[0][1] - c[1]) ^ 2);
    var a1 = atan2(p[0][1] - c[1], p[0][0] - c[0]) / degree;
    var a2 = atan2(p[2][1] - c[1], p[2][0] - c[0]) / degree;
    const ccw = (p[0][0] - c[0]) * (p[2][1] - c[1]) - (p[0][1] - c[1]) * (p[2][0] - c[0]) > 0;
    if (!ccw)
    {
        const t = a1;
        a1 = a2;
        a2 = t;
    }
    return "0\nARC\n8\n" ~ layer ~ "\n10\n" ~ dxfMm(c[0]) ~ "\n20\n" ~ dxfMm(c[1]) ~ "\n30\n0\n40\n" ~ dxfMm(r) ~
        "\n50\n" ~ roundToPrecision(a1, 4) ~ "\n51\n" ~ roundToPrecision(a2, 4);
}

function dxfMm(x is number) returns string
{
    return "" ~ roundToPrecision(x * 1000, 3);
}

// ---------------------------------------------------------------- cutouts ----

// The cutout for a connecting shape: its outline moved in by `offset`, with corners
// rounded by `radius` (shrunk to fit). Works in the shape's best-fit plane.
// Returns { fits, origin, normal, xAxis, points (2D corners of the hole), radius }.
function holeShape(loop is array, offset is number, radius is number) returns map
{
    const count = size(loop);
    var origin = [0, 0, 0];
    var normal = [0, 0, 0];
    for (var k = 0; k < count; k += 1)
    {
        const a = loop[k];
        const b = loop[(k + 1) % count];
        normal = add3(normal, [(a[1] - b[1]) * (a[2] + b[2]), (a[2] - b[2]) * (a[0] + b[0]), (a[0] - b[0]) * (a[1] + b[1])]);
        origin = add3(origin, scale3(a, 1 / count));
    }
    if (length3(normal) < 1e-12)
        return { "fits" : false };
    normal = scale3(normal, 1 / length3(normal));
    var xAxis = sub3(loop[0], origin);
    xAxis = sub3(xAxis, scale3(normal, dot3(xAxis, normal)));
    xAxis = scale3(xAxis, 1 / length3(xAxis));
    const yAxis = cross3(normal, xAxis);
    // The Newell normal makes the outline run counter-clockwise in (xAxis, yAxis).
    var q = [];
    for (var pt in loop)
        q = append(q, [dot3(sub3(pt, origin), xAxis), dot3(sub3(pt, origin), yAxis)]);

    // Move every edge inward and intersect neighbours.
    var linePoint = [];
    var lineDir = [];
    for (var k = 0; k < count; k += 1)
    {
        const d = [q[(k + 1) % count][0] - q[k][0], q[(k + 1) % count][1] - q[k][1]];
        const len = sqrt(d[0] * d[0] + d[1] * d[1]);
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
        const e = [pts[(k + 1) % count][0] - pts[k][0], pts[(k + 1) % count][1] - pts[k][1]];
        const along = (e[0] * lineDir[k][0] + e[1] * lineDir[k][1]) / sqrt(lineDir[k][0] * lineDir[k][0] + lineDir[k][1] * lineDir[k][1]);
        if (along < 0.002)
            return { "fits" : false };
        edgeLength = append(edgeLength, along);
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
    // A point inside the hole (it is convex, so the average of its corners is).
    var inside = [0, 0];
    for (var p in pts)
        inside = [inside[0] + p[0] / count, inside[1] + p[1] / count];
    // How far the plug must reach either side of the plane to get through a cupped face.
    var span = 0;
    for (var pt in loop)
        span = max(span, length3(sub3(pt, origin)));
    return { "span" : span, "inside" : add3(origin, add3(scale3(xAxis, inside[0]), scale3(yAxis, inside[1]))), "fits" : true, "origin" : origin, "normal" : normal, "xAxis" : xAxis, "points" : pts, "halfTan" : halfTan, "radius" : fitRadius };
}

// Draws the hole outline as a sketch on the shape's plane, splits the surface with it
// and removes the middle.
function cutHole(context is Context, faceId is Id, target is Query, hole is map) returns boolean
{
    const origin = vector(hole.origin[0], hole.origin[1], hole.origin[2]) * meter;
    const sketchPlane = plane(origin, vector(hole.normal[0], hole.normal[1], hole.normal[2]), vector(hole.xAxis[0], hole.xAxis[1], hole.xAxis[2]));
    const sk = newSketchOnPlane(context, faceId + "hole", { "sketchPlane" : sketchPlane });
    drawSegments(sk, "h", holeSegments(hole));
    try
    {
        skSolve(sk);
    }
    catch (sketchError)
    {
        throw "sketching the outline: " ~ toString(sketchError);
    }
    // Push the hole through the face as a thin plug, and split the face along its sides.
    const n = vector(hole.normal[0], hole.normal[1], hole.normal[2]);
    try
    {
        const region = qSketchRegion(faceId + "hole");
        if (size(evaluateQuery(context, region)) == 0)
            throw "the outline sketch has no closed region";
        opExtrude(context, faceId + "plug", {
                    "entities" : region,
                    "direction" : n,
                    "endBound" : BoundingType.BLIND,
                    "endDepth" : hole.span * meter,
                    "startBound" : BoundingType.BLIND,
                    "startDepth" : hole.span * meter
                });
        opSplitFace(context, faceId + "split", {
                    "faceTargets" : target,
                    "faceTools" : qNonCapEntity(faceId + "plug", EntityType.FACE)
                });
        opDeleteBodies(context, faceId + "plugGone", { "entities" : qCreatedBy(faceId + "plug", EntityType.BODY) });
    }
    catch (splitError)
    {
        throw "splitting the face: " ~ toString(splitError);
    }
    const pieces = qUnion([target, qCreatedBy(faceId + "split", EntityType.FACE)]);
    if (size(evaluateQuery(context, pieces)) < 2)
        throw "the outline did not split the face";
    try
    {
    opDeleteFace(context, faceId + "cut", {
                "deleteFaces" : qClosestTo(pieces, vector(hole.inside[0], hole.inside[1], hole.inside[2]) * meter),
                "includeFillet" : false,
                "capVoid" : false,
                "leaveOpen" : true
            });
    }
    catch (deleteError)
    {
        throw "removing the middle: " ~ toString(deleteError);
    }
    return true;
}

// Largest distance (meters) of a loop's corners from its best-fit plane.
function outOfFlat(loop is array) returns number
{
    const count = size(loop);
    var normal = [0, 0, 0];
    var centre = [0, 0, 0];
    for (var k = 0; k < count; k += 1)
    {
        const a = loop[k];
        const b = loop[(k + 1) % count];
        normal = add3(normal, [(a[1] - b[1]) * (a[2] + b[2]), (a[2] - b[2]) * (a[0] + b[0]), (a[0] - b[0]) * (a[1] + b[1])]);
        centre = add3(centre, scale3(a, 1 / count));
    }
    if (length3(normal) < 1e-15)
        return 0;
    normal = scale3(normal, 1 / length3(normal));
    var worst = 0;
    for (var pt in loop)
        worst = max(worst, abs(dot3(sub3(pt, centre), normal)));
    return worst;
}

// The hole's outline as segments in its own plane (meters): straight lines between
// rounded corners. Each segment is { kind : "line" | "arc", pts : [start, end] or
// [start, mid, end, centre] }.
function holeSegments(hole is map) returns array
{
    const pts = hole.points;
    const count = size(pts);
    const r = hole.radius;
    var out = [];
    if (r < 0.0005)
    {
        for (var k = 0; k < count; k += 1)
            out = append(out, { "kind" : "line", "pts" : [pts[k], pts[(k + 1) % count]] });
        return out;
    }
    // Each corner: an arc from the incoming edge to the outgoing one.
    var arcStart = [];
    var arcEnd = [];
    for (var k = 0; k < count; k += 1)
    {
        const v1 = unit2(pts[(k + count - 1) % count], pts[k]);
        const v2 = unit2(pts[(k + 1) % count], pts[k]);
        const reachAlong = r / hole.halfTan[k];
        const a = [pts[k][0] + v1[0] * reachAlong, pts[k][1] + v1[1] * reachAlong];
        const b = [pts[k][0] + v2[0] * reachAlong, pts[k][1] + v2[1] * reachAlong];
        var bis = [v1[0] + v2[0], v1[1] + v2[1]];
        const bisLength = sqrt(bis[0] * bis[0] + bis[1] * bis[1]);
        bis = [bis[0] / bisLength, bis[1] / bisLength];
        // The arc's centre sits on the bisector, r / sin(half angle) from the corner.
        const sinHalf = hole.halfTan[k] / sqrt(1 + hole.halfTan[k] * hole.halfTan[k]);
        const toCentre = r / sinHalf;
        const centre = [pts[k][0] + bis[0] * toCentre, pts[k][1] + bis[1] * toCentre];
        const mid = [pts[k][0] + bis[0] * (toCentre - r), pts[k][1] + bis[1] * (toCentre - r)];
        out = append(out, { "kind" : "arc", "pts" : [a, mid, b, centre] });
        arcStart = append(arcStart, a);
        arcEnd = append(arcEnd, b);
    }
    for (var k = 0; k < count; k += 1)
        out = append(out, { "kind" : "line", "pts" : [arcEnd[k], arcStart[(k + 1) % count]] });
    return out;
}

// Adds segments (2D points in meters) to a sketch.
function drawSegments(sk is Sketch, prefix is string, segs is array)
{
    for (var k = 0; k < size(segs); k += 1)
    {
        const p = segs[k].pts;
        if (segs[k].kind == "line")
            skLineSegment(sk, prefix ~ k, { "start" : vector(p[0][0], p[0][1]) * meter, "end" : vector(p[1][0], p[1][1]) * meter });
        else
            skArc(sk, prefix ~ k, { "start" : vector(p[0][0], p[0][1]) * meter, "mid" : vector(p[1][0], p[1][1]) * meter, "end" : vector(p[2][0], p[2][1]) * meter });
    }
}

// Unit vector in 2D from `from` toward `to`.
function unit2(to is array, from is array) returns array
{
    const d = [to[0] - from[0], to[1] - from[1]];
    const len = sqrt(d[0] * d[0] + d[1] * d[1]);
    return [d[0] / len, d[1] / len];
}

// ---------------------------------------------------------------- helpers ----

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

// Cumulative length (meters) along the tube at v = k/100, averaged over 8 lines.
function lengthTable(context is Context, face is Query, swap is boolean) returns array
{
    const K = 100;
    const lines = 8;
    var params = [];
    for (var k = 0; k <= K; k += 1)
        for (var q = 0; q < lines; q += 1)
            params = append(params, uvParam(q / lines, k / K, swap));
    const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
    var lens = [0];
    for (var k = 1; k <= K; k += 1)
    {
        var step = 0;
        for (var q = 0; q < lines; q += 1)
            step += norm(pl[k * lines + q].origin - pl[(k - 1) * lines + q].origin) / meter;
        lens = append(lens, lens[k - 1] + step / lines);
    }
    return lens;
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

// Circumference (meters) at parameter v.
function circumference(context is Context, face is Query, swap is boolean, v is number) returns number
{
    const S = 48;
    var params = [];
    for (var k = 0; k <= S; k += 1)
        params = append(params, uvParam(k / S, v, swap));
    // uvParam wraps u = 1 to 0, which closes the loop.
    const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
    var total = 0;
    for (var k = 0; k < S; k += 1)
        total += norm(pl[k + 1].origin - pl[k].origin) / meter;
    return total;
}

// Cairo pentagonal tiling built from a square grid: grid points with an even
// coordinate sum are 4-valent vertices; odd ones split into a short diagonal
// "bar". Every unit square (i, j) becomes exactly one equilateral pentagon.
function cairoPentagon(i is number, j is number) returns array
{
    const square = [[i, j], [i + 1, j], [i + 1, j + 1], [i, j + 1]];
    var out = [];
    for (var k = 0; k < 4; k += 1)
    {
        const c = square[k];
        if ((c[0] + c[1]) % 2 == 0)
        {
            out = append(out, c);
            continue;
        }
        const prev = square[(k + 3) % 4];
        const next = square[(k + 1) % 4];
        const a = barEnd(c, [prev[0] - c[0], prev[1] - c[1]]);
        const b = barEnd(c, [next[0] - c[0], next[1] - c[1]]);
        out = append(out, a);
        if (a[0] != b[0] || a[1] != b[1])
            out = append(out, b);
    }
    return out;
}

function barEnd(c is array, d is array) returns array
{
    const t = CAIRO_T;
    if (c[0] % 2 == 1)
    {
        if ((d[0] == 0 && d[1] == 1) || (d[0] == 1 && d[1] == 0))
            return [c[0] + t, c[1] + t];
        return [c[0] - t, c[1] - t];
    }
    if ((d[0] == 0 && d[1] == 1) || (d[0] == -1 && d[1] == 0))
        return [c[0] - t, c[1] + t];
    return [c[0] + t, c[1] - t];
}

function meters(p is Vector) returns array
{
    return [p[0] / meter, p[1] / meter, p[2] / meter];
}

function add3(a is array, b is array) returns array
{
    return [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
}

function sub3(a is array, b is array) returns array
{
    return [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
}

function scale3(a is array, s is number) returns array
{
    return [a[0] * s, a[1] * s, a[2] * s];
}

function dot3(a is array, b is array) returns number
{
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

function cross3(a is array, b is array) returns array
{
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}

function length3(a is array) returns number
{
    return sqrt(dot3(a, a));
}

// A loop of [x, y, z] corners (meters) as points, closed back to the first.
function closedLoop(loop is array) returns array
{
    var pts = [];
    for (var p in loop)
        pts = append(pts, vector(p[0], p[1], p[2]) * meter);
    pts = append(pts, vector(loop[0][0], loop[0][1], loop[0][2]) * meter);
    return pts;
}

// Closed straight-edged wire through the corners, then a fill surface across it.
function fillLoop(context is Context, wireId is Id, surfaceId is Id, loop is array) returns boolean
{
    opPolyline(context, wireId, { "points" : closedLoop(loop) });
    opFillSurface(context, surfaceId, {
                "edgesG0" : qCreatedBy(wireId, EntityType.EDGE),
                "edgesG1" : qNothing(),
                "edgesG2" : qNothing(),
                "guideVertices" : qNothing()
            });
    return true;
}

// Shortest way around the tube from one x to another.
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

// Moves that pull every pentagon toward a regular one with the given side: springs
// on its 5 sides and 5 diagonals.
function pentagonMoves(X is array, faces is array, pentagons is array, pentagonsAt is array, side is number) returns array
{
    var move = makeArray(size(X), [0, 0, 0]);
    for (var fi in pentagons)
    {
        const f = faces[fi];
        for (var k = 0; k < 5; k += 1)
        {
            for (var skip = 1; skip <= 2; skip += 1)
            {
                const a = f[k];
                const b = f[(k + skip) % 5];
                const d = sub3(X[b], X[a]);
                const len = length3(d);
                const target = skip == 1 ? side : GOLDEN * side;
                // Each corner sits on 4 springs per pentagon it belongs to, so share the correction.
                const fix = scale3(d, 0.5 * (len - target) / len / 4);
                move[a] = add3(move[a], scale3(fix, 1 / pentagonsAt[a]));
                move[b] = sub3(move[b], scale3(fix, 1 / pentagonsAt[b]));
            }
        }
    }
    return move;
}
