FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");

// Pentagon Origami Tube — FeatureScript for Onshape   (v8: Irregularity + Random seed for a more organic pattern)
//
// Rebuilds the side of a lofted tube as ONE solid shell made of pentagon
// facets that follows the same path, ready for thin sheet (e.g. 1 mm plastic).
//
// How it works:
//   1. Pentagon layout: Cairo pentagonal tiling.
//        Organic (default): fixed count around, rows spaced in the loft's own
//          parameter, so pentagons stretch and shrink with the tube.
//        Even sizing: bands with their own count around, joined by thin rings
//          of filler triangles, so pentagons stay close to one size.
//   2. Every pentagon shares its 5 straight edges exactly with its neighbours.
//      Each pentagon face is a fill surface across those edges: on a curved
//      tube it bows very slightly (a few % of the tube radius at 12 around,
//      less with more pentagons), which thin plastic flexes to easily.
//   3. The faces are knitted into one surface and thickened into one solid.
//
// Options for perfectly flat faces (both change the look):
//   Flattening passes > 0: moves vertices until pentagons are flat. Tends to
//     merge neighbours into long flat strips and shifts the tube shape.
//   Split non-flat pentagons: 5 shallow triangles per pentagon.
//
// The loft is only a guide; hide it afterwards.
//
// INSTALL: in a Feature Studio, select all, delete, paste this whole file, Commit.
//
// ------------------------------------------------------------------- 8< ----

const EDGE_BOUNDS = {
            (meter) : [0.001, 0.05, 50],
            (centimeter) : 5,
            (millimeter) : 50,
            (inch) : 2,
            (foot) : 0.17,
            (yard) : 0.055
        } as LengthBoundSpec;

const THICKNESS_BOUNDS = {
            (meter) : [1e-5, 0.001, 1],
            (centimeter) : 0.1,
            (millimeter) : 1,
            (inch) : 0.04,
            (foot) : 0.0033,
            (yard) : 0.0011
        } as LengthBoundSpec;

export enum PentagonLayout
{
    annotation { "Name" : "Organic (fixed count around)" }
    ORGANIC,
    annotation { "Name" : "Even sizing (bands + filler rings)" }
    EVEN
}

export enum TubeOutput
{
    annotation { "Name" : "3D shell" }
    SHELL,
    annotation { "Name" : "Flat pattern (score + cut lines)" }
    FLAT,
    annotation { "Name" : "Both" }
    BOTH
}

const TAB_BOUNDS = {
            (meter) : [0, 0, 1],
            (centimeter) : 0,
            (millimeter) : 0,
            (inch) : 0,
            (foot) : 0,
            (yard) : 0
        } as LengthBoundSpec;

const GAP_BOUNDS = {
            (meter) : [0, 0.02, 10],
            (centimeter) : 2,
            (millimeter) : 20,
            (inch) : 0.75,
            (foot) : 0.0625,
            (yard) : 0.02
        } as LengthBoundSpec;

const AROUND_BOUNDS = { (unitless) : [4, 12, 400] } as IntegerBoundSpec;
const ALONG_BOUNDS = { (unitless) : [1, 20, 2000] } as IntegerBoundSpec;
const TOLERANCE_BOUNDS = { (unitless) : [1, 15, 100] } as RealBoundSpec;
const ITERATION_BOUNDS = { (unitless) : [0, 0, 2000] } as IntegerBoundSpec;
const IRREGULARITY_BOUNDS = { (unitless) : [0, 60, 100] } as RealBoundSpec;
const SEED_BOUNDS = { (unitless) : [1, 1, 9999] } as IntegerBoundSpec;

// Cairo bar offset that makes every pentagon edge equal; edge = EDGE_RATIO * grid cell.
const CAIRO_T = 0.2742918851774318;     // (-2 + sqrt(28)) / 12
const EDGE_RATIO = 0.7758146081336154;  // sqrt((1 - t)^2 + t^2)
// Height of a filler triangle ring, in grid cells.
const FILL_HEIGHT = 0.75;
// At 100 % irregularity: random nudge of each corner, and height of each of the
// slow waves that vary pentagon size, both in grid cells.
const JITTER_MAX = 0.22;
const WARP_MAX = 0.18;
// Pull toward the previous position while flattening (keeps the shape from drifting).
const FLATTEN_DAMPING = 0.05;

annotation { "Feature Type Name" : "Pentagon origami tube",
             "Feature Type Description" : "Rebuilds a lofted tube as one solid shell of flat Cairo pentagon facets." }
export const pentagonOrigamiTube = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Tube side face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        annotation { "Name" : "Layout" }
        definition.layout is PentagonLayout;

        if (definition.layout == PentagonLayout.ORGANIC)
        {
            annotation { "Name" : "Pentagons around (even number)" }
            isInteger(definition.around, AROUND_BOUNDS);

            annotation { "Name" : "Auto rows", "Default" : true }
            definition.autoRows is boolean;

            if (!definition.autoRows)
            {
                annotation { "Name" : "Rows along" }
                isInteger(definition.along, ALONG_BOUNDS);
            }

            annotation { "Name" : "Irregularity (%)" }
            isReal(definition.irregularity, IRREGULARITY_BOUNDS);

            annotation { "Name" : "Random seed" }
            isInteger(definition.seed, SEED_BOUNDS);
        }
        else
        {
            annotation { "Name" : "Pentagon edge length" }
            isLength(definition.edge, EDGE_BOUNDS);

            annotation { "Name" : "Size variation allowed before a new band (%)" }
            isReal(definition.tolerance, TOLERANCE_BOUNDS);
        }

        annotation { "Name" : "Output" }
        definition.output is TubeOutput;

        if (definition.output != TubeOutput.FLAT)
        {
            annotation { "Name" : "Wall thickness" }
            isLength(definition.thickness, THICKNESS_BOUNDS);
        }

        if (definition.output != TubeOutput.SHELL)
        {
            annotation { "Name" : "Glue tab depth (0 = none)" }
            isLength(definition.tabDepth, TAB_BOUNDS);

            annotation { "Name" : "Gap between strips" }
            isLength(definition.stripGap, GAP_BOUNDS);

            annotation { "Name" : "Print DXF to console", "Default" : true }
            definition.printDxf is boolean;
        }

        annotation { "Name" : "Flip (thicken outward)", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flip is boolean;

        annotation { "Name" : "Flattening passes (0 = keep loft shape)" }
        isInteger(definition.iterations, ITERATION_BOUNDS);

        annotation { "Name" : "Swap around/along (if auto-detect gets it wrong)" }
        definition.swapUV is boolean;

        annotation { "Name" : "Split non-flat pentagons into triangles" }
        definition.splitNonFlat is boolean;

        annotation { "Name" : "Keep surface (skip thicken)", "Default" : true }
        definition.surfaceOnly is boolean;
    }
    {
        var stage = "reading inputs";
        try
        {
        const face = definition.face;
        // Work out which loft direction goes around the tube (the closed one).
        const swap = detectSwap(context, face) != definition.swapUV;
        const organic = definition.layout == PentagonLayout.ORGANIC;

        // ---- 1. Arc length along the tube ----
        stage = "measuring the tube";
        const lens = lengthTable(context, face, swap);
        const L = lens[size(lens) - 1];

        // ---- 2. Rows: one band (organic) or bands with their own count around (even) ----
        stage = "laying out rows";
        var bands = [];
        var s = 0;
        var ell = 0;
        if (organic)
        {
            const N = definition.around;
            if (N % 2 != 0)
                throw regenError("Pentagons around must be an even number so the pattern closes.", ["around"]);
            var M = definition.along;
            if (definition.autoRows)
                M = max(1, round(N * L / circumference(context, face, swap, 0.5)));
            bands = [{ "start" : 0, "rows" : M, "N" : N }];
        }
        else
        {
            s = definition.edge / meter / EDGE_RATIO;   // grid cell, in meters
        }
        const tol = organic ? 0 : definition.tolerance / 100;
        while (!organic)
        {
            const N = evenRound(circumference(context, face, swap, vOf(lens, min(ell + s / 2, L))) / s);
            const start = ell;
            var rows = 0;
            while (ell + s <= L + 0.5 * s)
            {
                const ratio = circumference(context, face, swap, vOf(lens, min(ell + s / 2, L))) / (N * s);
                if (rows > 0 && abs(ratio - 1) > tol)
                    break;
                rows += 1;
                ell += s;
            }
            if (rows > 0)
                bands = append(bands, { "start" : start, "rows" : rows, "N" : N });
            if (rows == 0 || ell + FILL_HEIGHT * s + s > L + 0.5 * s)
                break;
            ell += FILL_HEIGHT * s;
        }
        if (size(bands) == 0)
            throw regenError("Pentagon edge length is too large for this tube.", ["edge"]);
        const scale = organic ? 1 : L / ell;   // stretch so the last row lands on the end

        // ---- 3. Pentagons (shared vertices keyed by band + tiling coords) ----
        stage = "building the pentagon pattern";
        var keys = {};
        var vinfo = [];
        var faces = [];
        var fband = [];
        var fcenter = [];
        for (var b = 0; b < size(bands); b += 1)
        {
            const band = bands[b];
            for (var j = 0; j < band.rows; j += 1)
            {
                for (var i = 0; i < band.N; i += 1)
                {
                    var f = [];
                    for (var p in cairoPentagon(i, j))
                    {
                        var x = p[0] - floor(p[0] / band.N) * band.N;
                        const y = min(max(p[1], 0), band.rows);
                        if (abs(x - band.N) < 1e-9)
                            x = 0;
                        const k = "" ~ b ~ "_" ~ round(x * 1e6) ~ "_" ~ round(y * 1e6);
                        if (keys[k] == undefined)
                        {
                            keys[k] = size(vinfo);
                            vinfo = append(vinfo, { "band" : b, "x" : x, "y" : y });
                        }
                        if (size(f) == 0 || f[size(f) - 1] != keys[k])
                            f = append(f, keys[k]);
                    }
                    if (f[0] == f[size(f) - 1])
                        f = subArray(f, 0, size(f) - 1);
                    faces = append(faces, f);
                    fband = append(fband, b);
                    fcenter = append(fcenter, { "band" : b, "x" : i + 0.5, "y" : j + 0.5 });
                }
            }
        }

        // ---- 4. Filler triangle rings between bands ----
        stage = "adding filler rings";
        for (var b = 0; b + 1 < size(bands); b += 1)
        {
            const P = boundaryRing(faces, fband, vinfo, bands, b, true);
            const Q = boundaryRing(faces, fband, vinfo, bands, b + 1, false);
            const nP = size(P);
            const nQ = size(Q);
            var i = 0;
            var j = 0;
            while (i < nP || j < nQ)
            {
                const pn = P[(i + 1) % nP].u + (i + 1 >= nP ? 1 : 0);
                const qn = Q[(j + 1) % nQ].u + (j + 1 >= nQ ? 1 : 0);
                if (j >= nQ || (i < nP && pn < qn))
                {
                    faces = append(faces, [P[i % nP].id, P[(i + 1) % nP].id, Q[j % nQ].id]);
                    i += 1;
                }
                else
                {
                    faces = append(faces, [P[i % nP].id, Q[(j + 1) % nQ].id, Q[j % nQ].id]);
                    j += 1;
                }
                fband = append(fband, -1);
                fcenter = append(fcenter, undefined);
            }
        }

        // ---- 5. Place vertices on the loft ----
        stage = "placing points on the loft";
        var params = [];
        const irr = organic ? definition.irregularity / 100 : 0;
        for (var n = 0; n < size(vinfo); n += 1)
        {
            const vi = vinfo[n];
            const band = bands[vi.band];
            var x = vi.x;
            var y = vi.y;
            if (irr > 0)
            {
                const d = irregularOffset(n, x, y, band.N, definition.seed);
                // Corners on the tube ends stay on the ends; ones near an end move less along the tube.
                const room = min(1, min(y, band.rows - y));
                x += irr * d[0];
                y += irr * d[1] * room;
            }
            // Organic: rows evenly spaced in the loft's own parameter (cells stretch with it).
            const v = organic ? y / band.rows : vOf(lens, (band.start + y * s) * scale);
            params = append(params, uvParam(x / band.N, v, swap));
        }
        const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
        const nv = size(vinfo);
        var X = makeArray(3 * nv, 0);
        for (var v = 0; v < nv; v += 1)
        {
            for (var c = 0; c < 3; c += 1)
                X[3 * v + c] = planes[v].origin[c] / meter;
        }

        // ---- 6. Flatten: alternate plane fits and least-squares vertex updates ----
        stage = "flattening";
        for (var it = 0; it < definition.iterations; it += 1)
        {
            var A = makeArray(9 * nv, 0);
            var B = makeArray(3 * nv, 0);
            for (var f in faces)
            {
                if (size(f) < 4)
                    continue;   // triangles are always flat
                const pl = fitPlane(X, f);
                for (var v in f)
                {
                    for (var r = 0; r < 3; r += 1)
                    {
                        for (var c = 0; c < 3; c += 1)
                            A[9 * v + 3 * r + c] = A[9 * v + 3 * r + c] + pl.n[r] * pl.n[c];
                        B[3 * v + r] = B[3 * v + r] + pl.n[r] * pl.d;
                    }
                }
            }
            for (var v = 0; v < nv; v += 1)
            {
                var m = makeArray(9, 0);
                var rhs = makeArray(3, 0);
                for (var r = 0; r < 3; r += 1)
                {
                    for (var c = 0; c < 3; c += 1)
                        m[3 * r + c] = A[9 * v + 3 * r + c] + (r == c ? FLATTEN_DAMPING : 0);
                    rhs[r] = B[3 * v + r] + FLATTEN_DAMPING * X[3 * v + r];
                }
                const sol = solve3(m, rhs);
                for (var r = 0; r < 3; r += 1)
                    X[3 * v + r] = sol[r];
            }
        }

        // ---- 7. Flat pattern: one strip per row, unrolled pentagon by pentagon ----
        stage = "making the flat pattern";
        if (definition.output != TubeOutput.SHELL)
        {
            if (!organic)
                throw regenError("Flat pattern currently needs the Organic layout.", ["output"]);
            buildFlatPattern(context, id + "flat", X, faces, planes, bands[0], definition);
        }
        if (definition.output == TubeOutput.FLAT)
            return;

        // ---- 6b. Pentagons that still aren't flat: split into 5 shallow triangles
        //          meeting at a point on the loft, so every facet is exactly flat
        //          and the pentagon reads as an almost-flat panel. ----
        stage = "splitting pentagons";
        var splitFaces = [];
        var centerParams = [];
        for (var fi = 0; fi < size(faces); fi += 1)
        {
            if (size(faces[fi]) < 4 || planarity(X, faces[fi]) < 1e-6)
                continue;
            const c = fcenter[fi];
            const band = bands[c.band];
            const v = organic ? c.y / band.rows : vOf(lens, (band.start + c.y * s) * scale);
            splitFaces = append(splitFaces, fi);
            centerParams = append(centerParams, uvParam(c.x / band.N, v, swap));
        }
        if (definition.splitNonFlat && size(splitFaces) > 0)
        {
            const centers = evFaceTangentPlanes(context, { "face" : face, "parameters" : centerParams });
            var isSplit = {};
            for (var k = 0; k < size(splitFaces); k += 1)
                isSplit[splitFaces[k]] = k;
            var newFaces = [];
            for (var fi = 0; fi < size(faces); fi += 1)
            {
                const f = faces[fi];
                if (isSplit[fi] == undefined)
                {
                    newFaces = append(newFaces, f);
                    continue;
                }
                const cIndex = size(X) / 3;
                const o = centers[isSplit[fi]].origin;
                X = append(append(append(X, o[0] / meter), o[1] / meter), o[2] / meter);
                for (var k = 0; k < size(f); k += 1)
                    newFaces = append(newFaces, [f[k], f[(k + 1) % size(f)], cIndex]);
            }
            faces = newFaces;
        }

        // ---- 8. One surface per face, each with its own straight edges ----
        //          (neighbours use identical end points, so the faces line up exactly)
        stage = "building pentagon faces";
        // Onshape requires all operations under one parent Id to be contiguous in
        // history, so every face gets its own parent: id.faceN.{wire,surf,triK,surfK,cleanup}.
        var failed = [];
        var surfs = [];
        for (var fi = 0; fi < size(faces); fi += 1)
        {
            const f = faces[fi];
            const faceId = id + ("face" ~ fi);
            var faceSurfs = [qCreatedBy(faceId + "surf", EntityType.BODY)];
            const filled = try silent(fillPolygon(context, faceId + "wire", faceId + "surf", X, f));
            var fillError = undefined;
            var triError = undefined;
            if (filled != true)
            {
                fillError = "surface fill refused this pentagon";
                // Fall back to a fan of flat triangles, each made from a sketch.
                try
                {
                    for (var k = 1; k + 1 < size(f); k += 1)
                    {
                        planarTriangle(context, faceId + ("tri" ~ k), faceId + ("surf" ~ k), X, [f[0], f[k], f[k + 1]]);
                        faceSurfs = append(faceSurfs, qCreatedBy(faceId + ("surf" ~ k), EntityType.BODY));
                    }
                }
                catch (error)
                {
                    triError = error;
                }
            }
            // Remove helper wires, sketches and slabs; keep only the surfaces.
            try silent(opDeleteBodies(context, faceId + "cleanup", { "entities" : qSubtraction(qCreatedBy(faceId, EntityType.BODY), qUnion(faceSurfs)) }));
            surfs = concatenateArrays([surfs, faceSurfs]);

            if (triError == undefined)
                continue;
            failed = append(failed, fi);
            if (size(failed) <= 3)
            {
                var corners = [];
                for (var v in f)
                    corners = append(corners, [X[3 * v] * 1000, X[3 * v + 1] * 1000, X[3 * v + 2] * 1000]);
                println("Pentagon face " ~ fi ~ " failed. Corners (mm): " ~ toString(corners));
                println("  pentagon error: " ~ toString(fillError));
                println("  triangle error: " ~ toString(triError));
            }
        }
        if (size(failed) == size(faces))
            throw regenError("No pentagon faces could be built. Details are in the FeatureScript console.");
        var failNote = "";
        if (size(failed) > 0)
        {
            failNote = size(failed) ~ " of " ~ size(faces) ~ " faces could not be built (details in the FeatureScript console). ";
            reportFeatureWarning(context, id, failNote);
        }

        // ---- 9. Knit into one surface ----
        stage = "knitting faces";
        const sheetBodies = qBodyType(qUnion(surfs), BodyType.SHEET);
        if (size(evaluateQuery(context, sheetBodies)) == 0)
            throw regenError("No pentagon faces were created.");
        // The face count goes in the part name so it shows in the parts list.
        const skinName = "Skin - " ~ (size(faces) - size(failed)) ~ " faces";
        setProperty(context, { "entities" : sheetBodies, "propertyType" : PropertyType.NAME, "value" : skinName });
        if (size(evaluateQuery(context, sheetBodies)) < 2)
            return;
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
            reportFeatureWarning(context, id, failNote ~ "Faces were built but could not be joined into one surface (" ~ toString(knitError) ~ "). They are left as separate faces.");
            return;
        }
        const knitted = qBodyType(qUnion(append(surfs, qCreatedBy(id + "knit", EntityType.BODY))), BodyType.SHEET);
        if (size(evaluateQuery(context, knitted)) > 1)
            reportFeatureWarning(context, id, "Faces did not all knit into one surface.");
        setProperty(context, { "entities" : knitted, "propertyType" : PropertyType.NAME, "value" : skinName });

        if (definition.surfaceOnly)
            return;

        // ---- 10. Thicken inward (relative to the loft's face normal) ----
        stage = "thickening";
        const probe = point(X, faces[0][0]);
        const loftNormal = planes[faces[0][0]].normal;
        const hit = evDistance(context, { "side0" : qOwnedByBody(knitted, EntityType.FACE), "side1" : probe });
        const sheetFaces = evaluateQuery(context, qOwnedByBody(knitted, EntityType.FACE));
        const tp = evFaceTangentPlane(context, { "face" : sheetFaces[hit.sides[0].index], "parameter" : hit.sides[0].parameter });
        var sameSide = dot(tp.normal, loftNormal) > 0;
        if (definition.flip)
            sameSide = !sameSide;
        opThicken(context, id + "thicken", {
                    "entities" : knitted,
                    "thickness1" : sameSide ? 0 * meter : definition.thickness,
                    "thickness2" : sameSide ? definition.thickness : 0 * meter
                });
        setProperty(context, { "entities" : qCreatedBy(id + "thicken", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : skinName });
        try silent(opDeleteBodies(context, id + "deleteSurface", { "entities" : knitted }));
        }
        catch (error)
        {
            throw regenError("Failed while " ~ stage ~ ": " ~ toString(error));
        }
    }, { "autoRows" : true, "along" : 20, "tolerance" : 15, "iterations" : 0, "flip" : false, "swapUV" : false, "surfaceOnly" : true, "splitNonFlat" : false, "irregularity" : 60, "seed" : 1,
          "thickness" : 0.001 * meter, "tabDepth" : 0 * meter, "stripGap" : 0.02 * meter, "printDxf" : true });

// ---------------------------------------------------------------- helpers ----

function uvParam(u is number, v is number, swap is boolean) returns Vector
{
    const w = u - floor(u);
    return swap ? vector(v, w) : vector(w, v);
}

// Repeatable pseudo-random number in [0, 1) from two integers.
function rand01(n is number, salt is number) returns number
{
    const r = sin((n * 12.9898 + salt * 78.233) * radian) * 43758.5453;
    return r - floor(r);
}

// How far a corner moves at 100 % irregularity, in grid cells [around, along]:
// a random nudge per corner plus slow waves that make pentagons swell and shrink.
// The waves repeat exactly once or twice around the tube, so the seam still closes.
function irregularOffset(n is number, x is number, y is number, N is number, seed is number) returns array
{
    const ax = 2 * PI * x / N;
    const p = [rand01(seed, 1), rand01(seed, 2), rand01(seed, 3), rand01(seed, 4)];
    const wx = sin((ax + 0.55 * y + 2 * PI * p[0]) * radian) + sin((2 * ax - 0.31 * y + 2 * PI * p[1]) * radian);
    const wy = sin((ax - 0.47 * y + 2 * PI * p[2]) * radian) + sin((2 * ax + 0.83 * y + 2 * PI * p[3]) * radian);
    return [JITTER_MAX * (2 * rand01(n, seed + 10) - 1) + WARP_MAX * wx,
            JITTER_MAX * (2 * rand01(n, seed + 20) - 1) + WARP_MAX * wy];
}

function evenRound(x is number) returns number
{
    return max(4, 2 * round(x / 2));
}

// Cumulative length (meters) along the tube at v = k/100, averaged over 8 lines.
function lengthTable(context is Context, face is Query, swap is boolean) returns array
{
    const K = 100;
    const U = 8;
    var params = [];
    for (var k = 0; k <= K; k += 1)
        for (var q = 0; q < U; q += 1)
            params = append(params, uvParam(q / U, k / K, swap));
    const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
    var lens = [0];
    for (var k = 1; k <= K; k += 1)
    {
        var step = 0;
        for (var q = 0; q < U; q += 1)
            step += norm(pl[k * U + q].origin - pl[(k - 1) * U + q].origin) / meter;
        lens = append(lens, lens[k - 1] + step / U);
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
    const corners = [[i, j], [i + 1, j], [i + 1, j + 1], [i, j + 1]];
    var out = [];
    for (var k = 0; k < 4; k += 1)
    {
        const c = corners[k];
        if ((c[0] + c[1]) % 2 == 0)
        {
            out = append(out, c);
            continue;
        }
        const prev = corners[(k + 3) % 4];
        const next = corners[(k + 1) % 4];
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

// Ordered top (or bottom) boundary of a band: [{ u, id }] sorted around the tube.
function boundaryRing(faces is array, fband is array, vinfo is array, bands is array, b is number, top is boolean) returns array
{
    var directed = {};
    for (var fi = 0; fi < size(faces); fi += 1)
    {
        if (fband[fi] != b)
            continue;
        const f = faces[fi];
        for (var k = 0; k < size(f); k += 1)
            directed["" ~ f[k] ~ "_" ~ f[(k + 1) % size(f)]] = [f[k], f[(k + 1) % size(f)]];
    }
    var onBoundary = {};
    for (var e in directed)
    {
        const ab = e.value;
        if (directed["" ~ ab[1] ~ "_" ~ ab[0]] == undefined)
        {
            onBoundary[ab[0]] = true;
            onBoundary[ab[1]] = true;
        }
    }
    const rows = bands[b].rows;
    var ring = [];
    for (var v = 0; v < size(vinfo); v += 1)
    {
        const vi = vinfo[v];
        if (vi.band != b || onBoundary[v] == undefined)
            continue;
        const near = top ? vi.y >= rows - CAIRO_T - 1e-6 : vi.y <= CAIRO_T + 1e-6;
        if (near)
            ring = append(ring, { "u" : vi.x / bands[b].N, "id" : v });
    }
    return sort(ring, function(a, c) { return a.u - c.u; });
}

function edgeKey(a is number, b is number) returns string
{
    return "" ~ min(a, b) ~ "_" ~ max(a, b);
}

function point(X is array, v is number) returns Vector
{
    return vector(X[3 * v], X[3 * v + 1], X[3 * v + 2]) * meter;
}

// Plane through a face's vertices: unit Newell normal n and offset d (n . x = d).
function fitPlane(X is array, f is array) returns map
{
    const n = size(f);
    var nx = 0;
    var ny = 0;
    var nz = 0;
    var cx = 0;
    var cy = 0;
    var cz = 0;
    for (var k = 0; k < n; k += 1)
    {
        const a = 3 * f[k];
        const b = 3 * f[(k + 1) % n];
        nx += (X[a + 1] - X[b + 1]) * (X[a + 2] + X[b + 2]);
        ny += (X[a + 2] - X[b + 2]) * (X[a] + X[b]);
        nz += (X[a] - X[b]) * (X[a + 1] + X[b + 1]);
        cx += X[a] / n;
        cy += X[a + 1] / n;
        cz += X[a + 2] / n;
    }
    const len = sqrt(nx * nx + ny * ny + nz * nz);
    if (len < 1e-15)
        return { "n" : [0, 0, 0], "d" : 0 };
    const nn = [nx / len, ny / len, nz / len];
    return { "n" : nn, "d" : nn[0] * cx + nn[1] * cy + nn[2] * cz };
}

// Largest distance (meters) of a face's vertices from its fitted plane.
function planarity(X is array, f is array) returns number
{
    const pl = fitPlane(X, f);
    var worst = 0;
    for (var v in f)
        worst = max(worst, abs(pl.n[0] * X[3 * v] + pl.n[1] * X[3 * v + 1] + pl.n[2] * X[3 * v + 2] - pl.d));
    return worst;
}

// Solves a 3x3 system (row-major m) by Cramer's rule.
function solve3(m is array, r is array) returns array
{
    const det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
    const dx = r[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (r[1] * m[8] - m[5] * r[2]) + m[2] * (r[1] * m[7] - m[4] * r[2]);
    const dy = m[0] * (r[1] * m[8] - m[5] * r[2]) - r[0] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * r[2] - r[1] * m[6]);
    const dz = m[0] * (m[4] * r[2] - r[1] * m[7]) - m[1] * (m[3] * r[2] - r[1] * m[6]) + r[0] * (m[3] * m[7] - m[4] * m[6]);
    return [dx / det, dy / det, dz / det];
}

// ------------------------------------------------------------ flat pattern ----

// Lays each row of pentagons out flat as one strip (cut open at the seam).
// Shared edges inside a strip become SCORE lines; everything else is CUT.
// Output: two sketches on the Top plane beside the tube, plus an optional DXF
// (millimeters, layers SCORE / CUT / LABELS) printed to the FeatureScript console.
function buildFlatPattern(context is Context, id is Id, X is array, faces is array, planes is array, band is map, definition is map)
{
    const N = band.N;
    const M = band.rows;
    const tab = definition.tabDepth / meter;
    const gap = definition.stripGap / meter;

    var edgeFaces = {};
    for (var fi = 0; fi < N * M; fi += 1)
    {
        const f = faces[fi];
        for (var k = 0; k < size(f); k += 1)
        {
            const ek = edgeKey(f[k], f[(k + 1) % size(f)]);
            edgeFaces[ek] = append(edgeFaces[ek] == undefined ? [] : edgeFaces[ek], fi);
        }
    }

    // Start the layout beside the tube.
    var maxX = -1e9;
    for (var v = 0; v < size(X) / 3; v += 1)
        maxX = max(maxX, X[3 * v]);
    const x0 = maxX + 0.1;

    var score = [];
    var cut = [];
    var labels = [];
    var yCursor = 0;
    for (var j = 0; j < M; j += 1)
    {
        // Unroll the row: each pentagon is rotated flat onto the edge it shares with the previous one.
        var pos = [];
        var stripScore = [];
        var stripCut = [];
        for (var i = 0; i < N; i += 1)
        {
            const fi = j * N + i;
            const f = faces[fi];
            const local = localCoords(X, f, planes[f[0]].normal);
            var placed = local;
            if (i > 0)
            {
                const prevF = faces[fi - 1];
                var shared = [];
                for (var k = 0; k < size(f); k += 1)
                {
                    for (var q = 0; q < size(prevF); q += 1)
                    {
                        if (f[k] == prevF[q])
                            shared = append(shared, [k, q]);
                    }
                }
                if (size(shared) >= 2)
                {
                    const la = local[shared[0][0]];
                    const lb = local[shared[1][0]];
                    const A = pos[i - 1][shared[0][1]];
                    const B = pos[i - 1][shared[1][1]];
                    const theta = atan2(B[1] - A[1], B[0] - A[0]) - atan2(lb[1] - la[1], lb[0] - la[0]);
                    placed = [];
                    for (var p in local)
                        placed = append(placed, add2(A, rot2(sub2(p, la), theta)));
                }
                else
                {
                    // No shared edge found: park it to the right of the previous pentagon.
                    var shift = -1e9;
                    for (var p in pos[i - 1])
                        shift = max(shift, p[0]);
                    placed = [];
                    for (var p in local)
                        placed = append(placed, [p[0] + shift + gap, p[1]]);
                }
            }
            pos = append(pos, placed);

            // Classify this pentagon's edges.
            var cx = 0;
            var cy = 0;
            for (var p in placed)
            {
                cx += p[0] / size(placed);
                cy += p[1] / size(placed);
            }
            for (var k = 0; k < size(f); k += 1)
            {
                const a = placed[k];
                const b = placed[(k + 1) % size(f)];
                const owners = edgeFaces[edgeKey(f[k], f[(k + 1) % size(f)])];
                var other = -1;
                for (var o in owners)
                {
                    if (o != fi)
                        other = o;
                }
                if (other == fi - 1 && i > 0)
                {
                    stripScore = append(stripScore, [a, b]);
                    continue;
                }
                if (other == fi + 1 && i < N - 1)
                    continue;   // drawn by the next pentagon
                // Cut edge. Tabs go on edges glued to the next row up, and on the seam.
                const wantsTab = tab > 0 && (floor(other / N) == j + 1 || (i == N - 1 && other == j * N));
                if (!wantsTab)
                {
                    stripCut = append(stripCut, [a, b]);
                    continue;
                }
                stripScore = append(stripScore, [a, b]);
                const len = sqrt((b[0] - a[0]) ^ 2 + (b[1] - a[1]) ^ 2);
                const t = [(b[0] - a[0]) / len, (b[1] - a[1]) / len];
                var nrm = [-t[1], t[0]];
                if (nrm[0] * ((a[0] + b[0]) / 2 - cx) + nrm[1] * ((a[1] + b[1]) / 2 - cy) < 0)
                    nrm = [t[1], -t[0]];
                if (len > 2 * tab)
                {
                    const a2 = [a[0] + nrm[0] * tab + t[0] * tab, a[1] + nrm[1] * tab + t[1] * tab];
                    const b2 = [b[0] + nrm[0] * tab - t[0] * tab, b[1] + nrm[1] * tab - t[1] * tab];
                    stripCut = concatenateArrays([stripCut, [[a, a2], [a2, b2], [b2, b]]]);
                }
                else
                {
                    const h = len / 2;
                    const apex = [(a[0] + b[0]) / 2 + nrm[0] * h, (a[1] + b[1]) / 2 + nrm[1] * h];
                    stripCut = concatenateArrays([stripCut, [[a, apex], [apex, b]]]);
                }
            }
        }

        // Turn the strip so it runs left to right, then stack it below the previous one.
        const first = centroid2(pos[0]);
        const last = centroid2(pos[N - 1]);
        const turn = -atan2(last[1] - first[1], last[0] - first[0]);
        var minX = 1e9;
        var minY = 1e9;
        var maxY = -1e9;
        var all = concatenateArrays([stripScore, stripCut]);
        for (var seg in all)
        {
            for (var p in seg)
            {
                const r = rot2(p, turn);
                minX = min(minX, r[0]);
                minY = min(minY, r[1]);
                maxY = max(maxY, r[1]);
            }
        }
        const offset = [x0 - minX, yCursor - maxY];
        for (var seg in stripScore)
            score = append(score, [add2(rot2(seg[0], turn), offset), add2(rot2(seg[1], turn), offset)]);
        for (var seg in stripCut)
            cut = append(cut, [add2(rot2(seg[0], turn), offset), add2(rot2(seg[1], turn), offset)]);
        labels = append(labels, { "text" : "R" ~ (j + 1), "at" : [x0 - 0.04, yCursor - (maxY - minY) / 2] });
        yCursor -= (maxY - minY) + gap;
    }

    const top = plane(vector(0, 0, 0) * meter, vector(0, 0, 1), vector(1, 0, 0));
    drawLines(context, id + "scoreLines", top, score);
    drawLines(context, id + "cutLines", top, cut);
    reportFeatureInfo(context, id, "Flat pattern: " ~ M ~ " strips of " ~ N ~ " pentagons, labelled R1 (one tube end) to R" ~ M ~ ".");

    if (definition.printDxf)
        printDxf(score, cut, labels);
}

// A face's corners in its own best-fit plane, viewed from outside the tube.
function localCoords(X is array, f is array, outward is Vector) returns array
{
    const pl = fitPlane(X, f);
    var n = pl.n;
    if (n[0] * outward[0] + n[1] * outward[1] + n[2] * outward[2] < 0)
        n = [-n[0], -n[1], -n[2]];
    const o = [X[3 * f[0]], X[3 * f[0] + 1], X[3 * f[0] + 2]];
    var d = [X[3 * f[1]] - o[0], X[3 * f[1] + 1] - o[1], X[3 * f[1] + 2] - o[2]];
    const dn = d[0] * n[0] + d[1] * n[1] + d[2] * n[2];
    d = [d[0] - n[0] * dn, d[1] - n[1] * dn, d[2] - n[2] * dn];
    const dl = sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
    const e1 = [d[0] / dl, d[1] / dl, d[2] / dl];
    const e2 = [n[1] * e1[2] - n[2] * e1[1], n[2] * e1[0] - n[0] * e1[2], n[0] * e1[1] - n[1] * e1[0]];
    var out = [];
    for (var v in f)
    {
        const r = [X[3 * v] - o[0], X[3 * v + 1] - o[1], X[3 * v + 2] - o[2]];
        out = append(out, [r[0] * e1[0] + r[1] * e1[1] + r[2] * e1[2], r[0] * e2[0] + r[1] * e2[1] + r[2] * e2[2]]);
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

function rot2(p is array, theta is ValueWithUnits) returns array
{
    const c = cos(theta);
    const s = sin(theta);
    return [p[0] * c - p[1] * s, p[0] * s + p[1] * c];
}

function centroid2(pts is array) returns array
{
    var c = [0, 0];
    for (var p in pts)
        c = [c[0] + p[0] / size(pts), c[1] + p[1] / size(pts)];
    return c;
}

function drawLines(context is Context, id is Id, sketchPlane is Plane, segs is array)
{
    if (size(segs) == 0)
        return;
    const sk = newSketchOnPlane(context, id, { "sketchPlane" : sketchPlane });
    for (var k = 0; k < size(segs); k += 1)
    {
        skLineSegment(sk, "line" ~ k, {
                    "start" : vector(segs[k][0][0], segs[k][0][1]) * meter,
                    "end" : vector(segs[k][1][0], segs[k][1][1]) * meter
                });
    }
    skSolve(sk);
}

// Minimal ASCII DXF (R12), millimeters. Copy everything between the markers into a .dxf file.
function printDxf(score is array, cut is array, labels is array)
{
    println("----- DXF START (copy the lines below into pentagon-strips.dxf) -----");
    println("0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES");
    for (var seg in score)
        println(dxfLine("SCORE", seg));
    for (var seg in cut)
        println(dxfLine("CUT", seg));
    for (var l in labels)
        println("0\nTEXT\n8\nLABELS\n10\n" ~ mm(l.at[0]) ~ "\n20\n" ~ mm(l.at[1]) ~ "\n30\n0\n40\n12\n1\n" ~ l.text);
    println("0\nENDSEC\n0\nEOF");
    println("----- DXF END -----");
}

function dxfLine(layer is string, seg is array) returns string
{
    return "0\nLINE\n8\n" ~ layer ~ "\n10\n" ~ mm(seg[0][0]) ~ "\n20\n" ~ mm(seg[0][1]) ~ "\n30\n0\n11\n" ~
        mm(seg[1][0]) ~ "\n21\n" ~ mm(seg[1][1]) ~ "\n31\n0";
}

function mm(x is number) returns string
{
    return "" ~ (round(x * 1e6) / 1000);
}

// Closed straight-edged wire through the face's corners, then a fill surface across it.
function fillPolygon(context is Context, wireId is Id, surfaceId is Id, X is array, f is array) returns boolean
{
    var pts = [];
    for (var v in f)
        pts = append(pts, point(X, v));
    pts = append(pts, point(X, f[0]));
    opPolyline(context, wireId, { "points" : pts });
    opFillSurface(context, surfaceId, {
                "edgesG0" : qCreatedBy(wireId, EntityType.EDGE),
                "edgesG1" : qNothing(),
                "edgesG2" : qNothing(),
                "guideVertices" : qNothing()
            });
    return true;
}

// True when the loft's v direction (not u) is the closed one around the tube.
function detectSwap(context is Context, face is Query) returns boolean
{
    const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : [vector(0, 0.5), vector(1, 0.5), vector(0.5, 0), vector(0.5, 1)] });
    const uClosed = norm(pl[0].origin - pl[1].origin) < 1e-6 * meter;
    const vClosed = norm(pl[2].origin - pl[3].origin) < 1e-6 * meter;
    return vClosed && !uClosed;
}

// A flat triangle surface: sketch it on its own plane, extrude a thin slab,
// keep only the slab face that lies on the sketch plane.
function planarTriangle(context is Context, helperId is Id, surfaceId is Id, X is array, tri is array)
{
    const p0 = point(X, tri[0]);
    const p1 = point(X, tri[1]);
    const p2 = point(X, tri[2]);
    const normal = normalize(cross(p1 - p0, p2 - p0));
    const sketchPlane = plane(p0, normal, normalize(p1 - p0));
    const sk = newSketchOnPlane(context, helperId + "sketch", { "sketchPlane" : sketchPlane });
    const q = [worldToPlane(sketchPlane, p0), worldToPlane(sketchPlane, p1), worldToPlane(sketchPlane, p2)];
    for (var k = 0; k < 3; k += 1)
        skLineSegment(sk, "edge" ~ k, { "start" : q[k], "end" : q[(k + 1) % 3] });
    skSolve(sk);
    opExtrude(context, helperId + "slab", {
                "entities" : qSketchRegion(helperId + "sketch"),
                "direction" : -normal,
                "endBound" : BoundingType.BLIND,
                "endDepth" : 0.001 * meter
            });
    opExtractSurface(context, surfaceId, { "faces" : qCapEntity(helperId + "slab", CapType.START, EntityType.FACE) });
}
