"""Build viewer.html: a 3D viewer for layout.json that opens in any web browser.

Run it with `python3 make_viewer.py`. hex57_layout.py also runs it at the end,
so viewer.html is always up to date with the latest layout.
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))


def make_viewer(folder=HERE):
    with open(os.path.join(folder, "layout.json")) as f:
        layout = json.load(f)
    # The viewer only needs the shapes, their numbers and the summary.
    keep = {k: layout[k] for k in ("params", "vertices", "polygons", "polygon_info", "metrics")}
    for p in keep["polygon_info"]:
        p.pop("centre", None)
        p.pop("normal", None)
    with open(os.path.join(HERE, "viewer_template.html")) as f:
        page = f.read()
    page = page.replace("/*LAYOUT_JSON*/null", json.dumps(keep, separators=(",", ":")))
    # The doctype and charset make it open cleanly when double-clicked.
    page = '<!doctype html>\n<meta charset="utf-8">\n<meta name="viewport" content="width=device-width, initial-scale=1">\n' + page
    out = os.path.join(folder, "viewer.html")
    with open(out, "w") as f:
        f.write(page)
    return out


if __name__ == "__main__":
    print("wrote", make_viewer())
