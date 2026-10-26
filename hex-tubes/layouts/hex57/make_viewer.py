"""Build viewer.html: a 3D viewer for the layouts that opens in any web browser.

Run it with `python3 make_viewer.py`. hex57_layout.py also runs it at the end,
so viewer.html is always up to date with the latest layouts.

It packs every coverage/layout_*.json into the page (one per step of the
pentagon coverage slider) and starts on the coverage used for layout.json.
"""
import glob
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))


def slim(layout):
    # The viewer only needs the shapes, their numbers and the summary.
    keep = {k: layout[k] for k in ("params", "vertices", "polygons", "polygon_info", "metrics")}
    keep["polygon_info"] = [{k: v for k, v in p.items() if k not in ("centre", "normal")}
                            for p in keep["polygon_info"]]
    return keep


def make_viewer(folder=HERE):
    with open(os.path.join(folder, "layout.json")) as f:
        main = json.load(f)
    layouts = []
    for path in glob.glob(os.path.join(folder, "coverage", "layout_*.json")):
        with open(path) as f:
            layouts.append(slim(json.load(f)))
    if not layouts:
        layouts = [slim(main)]
    layouts.sort(key=lambda l: l["params"].get("PENTAGON_COVERAGE", 0))
    want = main["params"].get("PENTAGON_COVERAGE", 0)
    start = min(range(len(layouts)), key=lambda i: abs(layouts[i]["params"].get("PENTAGON_COVERAGE", 0) - want))
    data = dict(layouts=layouts, start=start)

    with open(os.path.join(HERE, "viewer_template.html")) as f:
        page = f.read()
    page = page.replace("/*LAYOUT_JSON*/null", json.dumps(data, separators=(",", ":")))
    # The doctype and charset make it open cleanly when double-clicked.
    page = '<!doctype html>\n<meta charset="utf-8">\n<meta name="viewport" content="width=device-width, initial-scale=1">\n' + page
    out = os.path.join(folder, "viewer.html")
    with open(out, "w") as f:
        f.write(page)
    return out


if __name__ == "__main__":
    print("wrote", make_viewer())
