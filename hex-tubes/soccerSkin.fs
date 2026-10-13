FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");

// Soccer skin — FeatureScript for Onshape   (v2: coloured pentagons, random sizes for the connecting shapes)
//
// Rebuilds the side of a tube as a skin of hexagons with pentagons mixed in,
// like a soccer ball wrapped around the tube.
//
// How it works:
//   1. Honeycomb: a fixed count of hexagons around, rows spaced in the tube's
//      own parameter, so hexagons stretch and shrink with the tube.
//   2. Pentagons: a tube only curves one way, so every pentagon needs a 7-sided
//      partner. Random hexagon edges are rotated a quarter turn; each rotation
//      turns four hexagons into two pentagons and two heptagons.
//   3. Evening: vertices slide on the tube until every pentagon has sides (and
//      diagonals) as equal as the surface allows, and all pentagons are one size.
//      The hexagons and heptagons around them take up the slack; each of those
//      is also given a random size, so the connecting shapes vary.
//   4. Each face is a fill surface across its straight edges; the faces are
//      knitted into one surface named "Skin - N faces", with the pentagons coloured.
//
// "Wireframe preview" skips step 4 and only draws the edges. It is much faster,
// so use it while choosing the counts and the seed.
//
// INSTALL: in a Feature Studio, select all, delete, paste this whole file, Commit.
//
// ------------------------------------------------------------------- 8< ----

export enum SkinOutput
{
    annotation { "Name" : "Surface" }
    SURFACE,
    annotation { "Name" : "Wireframe preview (fast)" }
    WIRES
}

const AROUND_BOUNDS = { (unitless) : [3, 12, 200] } as IntegerBoundSpec;
const ALONG_BOUNDS = { (unitless) : [2, 40, 2000] } as IntegerBoundSpec;
const PERCENT_BOUNDS = { (unitless) : [0, 8, 25] } as RealBoundSpec;
const SEED_BOUNDS = { (unitless) : [1, 1, 100000] } as IntegerBoundSpec;
const PASS_BOUNDS = { (unitless) : [0, 40, 500] } as IntegerBoundSpec;
const VARIATION_BOUNDS = { (unitless) : [0, 25, 60] } as RealBoundSpec;

// Honeycomb with hexagons 1 unit wide, points up: row spacing and corner heights.
const ROW_PITCH = 0.8660254037844386;   // sqrt(3) / 2
const HEX_HALF = 0.5773502691896258;    // 1 / sqrt(3)
const HEX_QUARTER = 0.2886751345948129;
// Diagonal / side of a regular pentagon.
const GOLDEN = 1.618033988749895;
// Step used to measure how the surface moves when a parameter changes.
const PARAM_STEP = 1e-4;

annotation { "Feature Type Name" : "Soccer skin",
             "Feature Type Description" : "Rebuilds a tube as a skin of hexagons with evened-out pentagons mixed in." }
export const soccerSkin = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Tube side face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        annotation { "Name" : "Hexagons around" }
        isInteger(definition.around, AROUND_BOUNDS);

        annotation { "Name" : "Auto rows", "Default" : true }
        definition.autoRows is boolean;

        if (!definition.autoRows)
        {
            annotation { "Name" : "Rows along" }
            isInteger(definition.along, ALONG_BOUNDS);
        }

        annotation { "Name" : "Pentagons (% of faces)" }
        isReal(definition.pentagonPercent, PERCENT_BOUNDS);

        annotation { "Name" : "Size variation of connecting shapes (%)" }
        isReal(definition.sizeVariation, VARIATION_BOUNDS);

        annotation { "Name" : "Random seed" }
        isInteger(definition.seed, SEED_BOUNDS);

        annotation { "Name" : "Evening passes" }
        isInteger(definition.passes, PASS_BOUNDS);

        annotation { "Name" : "Output" }
        definition.output is SkinOutput;

        annotation { "Name" : "Color pentagons", "Default" : true }
        definition.colorPentagons is boolean;

        annotation { "Name" : "Swap around/along (if auto-detect gets it wrong)" }
        definition.swapUV is boolean;
    }
    {
        var stage = "reading inputs";
        try
        {
        const face = definition.face;
        const swap = detectSwap(context, face) != definition.swapUV;
        const N = definition.around;

        // ---- 1. Rows from the tube's length and girth ----
        stage = "measuring the tube";
        var M = definition.along;
        if (definition.autoRows)
        {
            const L = tubeLength(context, face, swap);
            const C = circumference(context, face, swap, 0.5);
            M = max(2, round(L / (ROW_PITCH * C / N)));
        }
        const yMax = M * ROW_PITCH;

        // ---- 2. Honeycomb in tiling coordinates: x in [0, N) around, y in [0, yMax] along ----
        stage = "building the honeycomb";
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
        stage = "adding pentagons";
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
        var pentagons = [];
        const want = round(definition.pentagonPercent / 100 * nf / 2);
        var rng = definition.seed * 7919 + 13;
        var made = 0;
        for (var attempt = 0; attempt < 60 * want + 100 && made < want; attempt += 1)
        {
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
                if (blocked[fi] || size(faces[fi]) != 6)
                    usable = false;
            }
            if (!usable)
                continue;

            // Keep other pairs at least one hexagon away, so pentagons have room to even out.
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

            pentagons = append(pentagons, shared[0]);
            pentagons = append(pentagons, shared[1]);
            made += 1;
        }

        // ---- 4. Neighbours, and which vertices may move ----
        stage = "relaxing the pattern";
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
        var isPentagon = makeArray(nf, false);
        for (var fi in pentagons)
        {
            isPentagon[fi] = true;
            for (var vtx in faces[fi])
                inPentagon[vtx] = true;
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
        stage = "evening out the pentagons";
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

            // Pentagons: springs on the 5 sides and 5 diagonals toward a regular pentagon.
            // All pentagons aim for the same side: the average over the whole skin.
            var side = 0;
            for (var fi in pentagons)
            {
                const f = faces[fi];
                for (var k = 0; k < 5; k += 1)
                    side += length3(sub3(X[f[(k + 1) % 5]], X[f[k]])) / 5 / size(pentagons);
            }
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
                        // Each corner sits on 4 springs, so share the correction.
                        const fix = scale3(d, 0.5 * (len - target) / len / 4);
                        move[a] = add3(move[a], fix);
                        move[b] = sub3(move[b], fix);
                    }
                }
            }

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
        stage = "placing points on the tube";
        var finalParams = makeArray(nv, vector(0, 0));
        for (var vtx = 0; vtx < nv; vtx += 1)
            finalParams[vtx] = uvParam(U[vtx], V[vtx], swap);
        const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : finalParams });
        var P = makeArray(nv, [0, 0, 0]);
        for (var vtx = 0; vtx < nv; vtx += 1)
            P[vtx] = meters(planes[vtx].origin);

        var worst = 0;
        var typical = 0;
        var smallest = 1e9;
        var largest = 0;
        for (var fi in pentagons)
        {
            const f = faces[fi];
            var lo = 1e9;
            var hi = 0;
            var side = 0;
            for (var k = 0; k < 5; k += 1)
            {
                const len = length3(sub3(P[f[(k + 1) % 5]], P[f[k]]));
                lo = min(lo, len);
                hi = max(hi, len);
                side += len / 5;
            }
            worst = max(worst, (hi - lo) / side);
            typical += (hi - lo) / side / size(pentagons);
            smallest = min(smallest, side);
            largest = max(largest, side);
        }
        const note = nf ~ " faces, " ~ size(pentagons) ~ " pentagons. Pentagon sides differ by " ~
            roundToPrecision(typical * 100, 1) ~ "% on average, " ~ roundToPrecision(worst * 100, 1) ~ "% at worst. Pentagon side " ~
            roundToPrecision(smallest * 1000, 1) ~ " to " ~ roundToPrecision(largest * 1000, 1) ~ " mm.";
        setVariable(context, "soccerSkinStats", { "faces" : nf, "pentagons" : size(pentagons), "rows" : M + 1,
                    "averageSideSpreadPercent" : typical * 100, "worstSideSpreadPercent" : worst * 100 });

        // ---- 7a. Wireframe preview: one closed wire per face ----
        if (definition.output == SkinOutput.WIRES)
        {
            stage = "drawing the wireframe";
            for (var fi = 0; fi < nf; fi += 1)
                opPolyline(context, id + ("wire" ~ fi), { "points" : loopPoints(P, faces[fi]) });
            reportFeatureInfo(context, id, note);
            return;
        }

        // ---- 7b. One surface per face, each with its own straight edges ----
        stage = "building faces";
        // Onshape requires all operations under one parent Id to be contiguous in
        // history, so every face gets its own parent: id.faceN.{wire,surf,triK,surfK,cleanup}.
        var failed = [];
        var surfs = [];
        for (var fi = 0; fi < nf; fi += 1)
        {
            const f = faces[fi];
            const faceId = id + ("face" ~ fi);
            var faceSurfs = [qCreatedBy(faceId + "surf", EntityType.BODY)];
            const filled = try silent(fillPolygon(context, faceId + "wire", faceId + "surf", P, f));
            var triError = undefined;
            if (filled != true)
            {
                // Fall back to a fan of flat triangles, each made from a sketch.
                try
                {
                    for (var k = 1; k + 1 < size(f); k += 1)
                    {
                        planarTriangle(context, faceId + ("tri" ~ k), faceId + ("surf" ~ k), P, [f[0], f[k], f[k + 1]]);
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
            if (triError != undefined)
            {
                failed = append(failed, fi);
                if (size(failed) <= 3)
                    println("Soccer skin face " ~ fi ~ " failed: " ~ toString(triError));
            }
        }
        if (size(failed) == nf)
            throw regenError("No faces could be built. Details are in the FeatureScript console.");
        var failNote = "";
        if (size(failed) > 0)
            failNote = size(failed) ~ " of " ~ nf ~ " faces could not be built (details in the FeatureScript console). ";

        // ---- 8. Knit into one surface, named with its face count ----
        stage = "knitting faces";
        const sheetBodies = qBodyType(qUnion(surfs), BodyType.SHEET);
        if (size(evaluateQuery(context, sheetBodies)) == 0)
            throw regenError("No faces were created.");
        const skinName = "Skin - " ~ (nf - size(failed)) ~ " faces";
        setProperty(context, { "entities" : sheetBodies, "propertyType" : PropertyType.NAME, "value" : skinName });
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
        setProperty(context, { "entities" : knitted, "propertyType" : PropertyType.NAME, "value" : skinName });
        if (definition.colorPentagons && size(pentagons) > 0)
        {
            var pentagonFaces = [];
            for (var fi in pentagons)
                pentagonFaces = append(pentagonFaces, qCreatedBy(id + ("face" ~ fi), EntityType.FACE));
            setProperty(context, { "entities" : qUnion(pentagonFaces), "propertyType" : PropertyType.APPEARANCE, "value" : color(0.93, 0.42, 0.25) });
        }
        if (size(evaluateQuery(context, knitted)) > 1)
            reportFeatureWarning(context, id, failNote ~ "Faces did not all knit into one surface.");
        else if (size(failed) > 0)
            reportFeatureWarning(context, id, failNote);
        else
            reportFeatureInfo(context, id, note);
        }
        catch (error)
        {
            throw regenError("Failed while " ~ stage ~ ": " ~ toString(error));
        }
    }, { "autoRows" : true, "along" : 40, "pentagonPercent" : 8, "seed" : 1, "passes" : 40, "swapUV" : false,
          "sizeVariation" : 25, "colorPentagons" : true });

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

// Length (meters) along the tube, averaged over 8 lines.
function tubeLength(context is Context, face is Query, swap is boolean) returns number
{
    const K = 100;
    const lines = 8;
    var params = [];
    for (var k = 0; k <= K; k += 1)
        for (var q = 0; q < lines; q += 1)
            params = append(params, uvParam(q / lines, k / K, swap));
    const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
    var total = 0;
    for (var k = 1; k <= K; k += 1)
        for (var q = 0; q < lines; q += 1)
            total += norm(pl[k * lines + q].origin - pl[(k - 1) * lines + q].origin) / meter / lines;
    return total;
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

function length3(a is array) returns number
{
    return sqrt(dot3(a, a));
}

function point(P is array, v is number) returns Vector
{
    return vector(P[v][0], P[v][1], P[v][2]) * meter;
}

// A face's corners as a closed loop of points.
function loopPoints(P is array, f is array) returns array
{
    var pts = [];
    for (var v in f)
        pts = append(pts, point(P, v));
    pts = append(pts, point(P, f[0]));
    return pts;
}

// Closed straight-edged wire through the face's corners, then a fill surface across it.
function fillPolygon(context is Context, wireId is Id, surfaceId is Id, P is array, f is array) returns boolean
{
    opPolyline(context, wireId, { "points" : loopPoints(P, f) });
    opFillSurface(context, surfaceId, {
                "edgesG0" : qCreatedBy(wireId, EntityType.EDGE),
                "edgesG1" : qNothing(),
                "edgesG2" : qNothing(),
                "guideVertices" : qNothing()
            });
    return true;
}

// A flat triangle surface: sketch it on its own plane, extrude a thin slab,
// keep only the slab face that lies on the sketch plane.
function planarTriangle(context is Context, helperId is Id, surfaceId is Id, P is array, tri is array)
{
    const p0 = point(P, tri[0]);
    const p1 = point(P, tri[1]);
    const p2 = point(P, tri[2]);
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
