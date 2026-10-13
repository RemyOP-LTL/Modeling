// Hex Lattice Tube — starter FeatureScript for Onshape
//
// Turns the side face of a lofted tube into a hexagon lattice.
//   Mode "Cut holes in existing solid": punches hex holes through a solid tube wall
//     (e.g. a loft you already shelled), leaving struts between the holes.
//   Mode "Thicken surface into lattice": thickens a surface loft to a wall
//     thickness, then punches the holes.
//
// Cells are laid out in the face's UV parameter space, so the hex count around
// stays constant and cells shrink/grow with the tube radius (a taper gives
// smaller cells toward the narrow end).
//
// INSTALL: create a new Feature Studio, KEEP the two header lines Onshape puts
// at the top (FeatureScript NNNN; import(...std/common.fs...)), delete the rest,
// then paste everything below the "8<" line.
//
// ------------------------------------------------------------------- 8< ----

export enum HexMode
{
    annotation { "Name" : "Cut holes in existing solid" }
    CUT_SOLID,
    annotation { "Name" : "Thicken surface into lattice" }
    THICKEN_SURFACE
}

const CELLS_AROUND_BOUNDS = { (unitless) : [3, 12, 500] } as IntegerBoundSpec;
const CELLS_ALONG_BOUNDS = { (unitless) : [1, 20, 2000] } as IntegerBoundSpec;

const STRUT_BOUNDS = {
            (meter) : [0, 0.02, 500],
            (centimeter) : 2,
            (millimeter) : 20,
            (inch) : 0.75,
            (foot) : 0.0625,
            (yard) : 0.02
        } as LengthBoundSpec;

const THICKNESS_BOUNDS = {
            (meter) : [1e-5, 0.006, 500],
            (centimeter) : 0.6,
            (millimeter) : 6,
            (inch) : 0.25,
            (foot) : 0.02,
            (yard) : 0.007
        } as LengthBoundSpec;

const DEPTH_BOUNDS = {
            (meter) : [1e-5, 0.05, 500],
            (centimeter) : 5,
            (millimeter) : 50,
            (inch) : 2,
            (foot) : 0.17,
            (yard) : 0.055
        } as LengthBoundSpec;

annotation { "Feature Type Name" : "Hex lattice tube",
             "Feature Type Description" : "Cuts a hexagon lattice into the side face of a lofted tube." }
export const hexLatticeTube = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Tube side face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        annotation { "Name" : "Mode" }
        definition.mode is HexMode;

        if (definition.mode == HexMode.CUT_SOLID)
        {
            annotation { "Name" : "Solid to cut", "Filter" : EntityType.BODY && BodyType.SOLID, "MaxNumberOfPicks" : 1 }
            definition.target is Query;
        }
        else
        {
            annotation { "Name" : "Wall thickness" }
            isLength(definition.thickness, THICKNESS_BOUNDS);

            annotation { "Name" : "Flip thickness side", "UIHint" : UIHint.OPPOSITE_DIRECTION }
            definition.flipThicken is boolean;
        }

        annotation { "Name" : "Cells around" }
        isInteger(definition.cellsAround, CELLS_AROUND_BOUNDS);

        annotation { "Name" : "Rows along" }
        isInteger(definition.cellsAlong, CELLS_ALONG_BOUNDS);

        annotation { "Name" : "Strut width" }
        isLength(definition.strutWidth, STRUT_BOUNDS);

        annotation { "Name" : "Cut depth (through wall, centered on face)" }
        isLength(definition.cutDepth, DEPTH_BOUNDS);

        annotation { "Name" : "Closed tube (wrap around seam)" }
        definition.wrap is boolean;

        annotation { "Name" : "Swap around/along directions" }
        definition.swapUV is boolean;
    }
    {
        // ---- 1. Body to cut ----
        var target = definition.target;
        if (definition.mode == HexMode.THICKEN_SURFACE)
        {
            var t1 = definition.thickness;
            var t2 = 0 * meter;
            if (definition.flipThicken)
            {
                t2 = definition.thickness;
                t1 = 0 * meter;
            }
            opThicken(context, id + "thicken", {
                        "entities" : definition.face,
                        "thickness1" : t1,
                        "thickness2" : t2
                    });
            target = qCreatedBy(id + "thicken", EntityType.BODY);
        }

        // ---- 2. Hex grid in normalized UV space ----
        // Pointy-top hexes in offset rows. Row pitch dv = 3/4 of hex height,
        // sized so the first and last rows sit exactly inside v = [0, 1].
        const N = definition.cellsAround;
        const M = definition.cellsAlong;
        const du = 1 / N;
        const dv = 1 / ((M - 1) + 4 / 3);
        const hw = du / 2;      // half width
        const hh = dv * 2 / 3;  // half height
        const offsets = [[0, hh], [-hw, hh / 2], [-hw, -hh / 2], [0, -hh], [hw, -hh / 2], [hw, hh / 2]];

        const depth = definition.cutDepth;
        var tools = [];
        var sketches = [];
        var count = 0;

        for (var j = 0; j < M; j += 1)
        {
            const vc = hh + j * dv;
            const rowShift = (j % 2 == 1) ? 0.5 : 0;
            for (var i = 0; i < N; i += 1)
            {
                const uc = (i + rowShift) * du + du / 2;

                // Collect center + 6 corner parameters; skip cells off an open face.
                var params = [uvParam(wrapU(uc, definition.wrap), vc, definition.swapUV)];
                var inside = definition.wrap || (uc - hw >= -1e-9 && uc + hw <= 1 + 1e-9);
                for (var o in offsets)
                {
                    params = append(params, uvParam(wrapU(uc + o[0], definition.wrap), vc + o[1], definition.swapUV));
                }
                if (!inside)
                    continue;

                const planes = evFaceTangentPlanes(context, { "face" : definition.face, "parameters" : params });
                const center = planes[0];

                // Corners projected into the tangent plane at the cell center.
                var corners = [];
                for (var k = 1; k < 7; k += 1)
                {
                    corners = append(corners, worldToPlane(center, planes[k].origin));
                }

                // Shrink the hexagon by half a strut on every edge.
                const hole = insetPolygon(corners, definition.strutWidth / 2);
                if (hole == undefined)
                    continue;

                // Sketch the hole on a plane pushed half the cut depth inward, extrude through.
                const sketchPlane = plane(center.origin - center.normal * (depth / 2), center.normal, center.x);
                const sketchId = id + ("cellSketch" ~ count);
                const sk = newSketchOnPlane(context, sketchId, { "sketchPlane" : sketchPlane });
                for (var k = 0; k < 6; k += 1)
                {
                    skLineSegment(sk, "edge" ~ k, { "start" : hole[k], "end" : hole[(k + 1) % 6] });
                }
                skSolve(sk);

                const extrudeId = id + ("cellCut" ~ count);
                opExtrude(context, extrudeId, {
                            "entities" : qSketchRegion(sketchId),
                            "direction" : sketchPlane.normal,
                            "endBound" : BoundingType.BLIND,
                            "endDepth" : depth
                        });

                sketches = append(sketches, qCreatedBy(sketchId, EntityType.BODY));
                tools = append(tools, qCreatedBy(extrudeId, EntityType.BODY));
                count += 1;
            }
        }

        if (count == 0)
            throw regenError("No hex cells fit. Try a smaller strut width or fewer cells.", ["strutWidth"]);

        opDeleteBodies(context, id + "deleteSketches", { "entities" : qUnion(sketches) });

        // ---- 3. Subtract all cells at once ----
        opBoolean(context, id + "cut", {
                    "tools" : qUnion(tools),
                    "targets" : target,
                    "operationType" : BooleanOperationType.SUBTRACTION
                });
    }, { "wrap" : true, "swapUV" : false, "flipThicken" : false });

function wrapU(u is number, wrap is boolean) returns number
{
    if (!wrap)
        return min(max(u, 0), 1);
    return u - floor(u);
}

function uvParam(u is number, v is number, swap is boolean) returns Vector
{
    return swap ? vector(v, u) : vector(u, v);
}

function cross2(a is Vector, b is Vector)
{
    return a[0] * b[1] - a[1] * b[0];
}

// Offsets each edge of a convex polygon inward by d. Returns undefined if the
// polygon collapses (strut too wide for the cell).
function insetPolygon(pts is array, d is ValueWithUnits)
{
    const n = size(pts);

    // Force counter-clockwise order so the left normal points inward.
    var area = 0 * meter ^ 2;
    for (var k = 0; k < n; k += 1)
        area += cross2(pts[k], pts[(k + 1) % n]);
    var p = pts;
    if (area < 0 * meter ^ 2)
    {
        p = [];
        for (var k = n - 1; k >= 0; k -= 1)
            p = append(p, pts[k]);
    }

    // Offset line for each edge: point q[k], unit direction t[k].
    var q = [];
    var t = [];
    for (var k = 0; k < n; k += 1)
    {
        const e = p[(k + 1) % n] - p[k];
        const len = norm(e);
        if (len < 1e-9 * meter)
            return undefined;
        const dir = e / len;
        t = append(t, dir);
        q = append(q, p[k] + vector(-dir[1], dir[0]) * d);
    }

    // New vertex k = intersection of offset edges k-1 and k.
    var out = [];
    for (var k = 0; k < n; k += 1)
    {
        const km = (k + n - 1) % n;
        const denom = cross2(t[km], t[k]);
        if (abs(denom) < 1e-9)
        {
            out = append(out, q[k]);
        }
        else
        {
            const s = cross2(q[k] - q[km], t[k]) / denom;
            out = append(out, q[km] + t[km] * s);
        }
    }

    // Reject if any edge flipped direction (polygon collapsed).
    for (var k = 0; k < n; k += 1)
    {
        const e = out[(k + 1) % n] - out[k];
        if (dot(e, t[k]) <= 0 * meter)
            return undefined;
    }
    return out;
}
