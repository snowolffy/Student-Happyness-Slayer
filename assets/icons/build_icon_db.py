#!/usr/bin/env python3
"""Build icons.db (+ exported .ico/.png/.svg files) from icon-src/.

icon-src/<folder>/
    meta.json   {"key": "tray.connected", "name": "...", "name_th": "...",
                 "category": "status", "description": "...",
                 "png_sizes": [16, 20, ...], "ico_sizes": [16, 24, ...]}   (sizes optional)
    full.svg    required - detailed version, used for >= 48px
    small.svg   optional - simplified, used for 17..32px
    tiny.svg    optional - simplified, used for <= 16px

Add a new icon = add a folder, rerun:  python3 build_icon_db.py
Requires: pip install cairosvg pillow
"""
import datetime
import hashlib
import io
import json
import os
import sqlite3
import sys

import cairosvg
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "icon-src")
OUT = os.path.join(HERE, "export")
DB = os.path.join(HERE, "icons.db")

DEFAULT_PNG = [16, 20, 24, 32, 48, 64, 128, 256]
DEFAULT_ICO = [16, 24, 32, 48, 64, 128, 256]


def pick(svgs, size):
    """Choose the SVG variant that stays legible at this pixel size."""
    if size <= 16:
        order = ("tiny", "small", "full")
    elif size <= 32:
        order = ("small", "full")
    else:
        order = ("full",)
    for v in order:
        if v in svgs:
            return svgs[v]
    return svgs["full"]


def render(svg, size):
    data = cairosvg.svg2png(bytestring=svg.encode(), output_width=size, output_height=size)
    return Image.open(io.BytesIO(data)).convert("RGBA")


def png_bytes(im):
    b = io.BytesIO()
    im.save(b, "PNG", optimize=True)
    return b.getvalue()


def ico_bytes(svgs, sizes):
    sizes = sorted(set(sizes))
    ims = [render(pick(svgs, s), s) for s in sizes]
    b = io.BytesIO()
    # Pillow uses a provided image when its size matches an entry in `sizes`
    ims[-1].save(b, "ICO", sizes=[(s, s) for s in sizes], append_images=ims[:-1])
    return b.getvalue()


def main():
    if os.path.exists(DB):
        os.remove(DB)
    os.makedirs(OUT, exist_ok=True)
    con = sqlite3.connect(DB)
    con.executescript(open(os.path.join(HERE, "schema.sql"), encoding="utf-8").read())
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    folders = sorted(d for d in os.listdir(SRC) if os.path.isfile(os.path.join(SRC, d, "meta.json")))
    for folder in folders:
        base = os.path.join(SRC, folder)
        meta = json.load(open(os.path.join(base, "meta.json"), encoding="utf-8"))
        svgs = {}
        for v in ("full", "small", "tiny"):
            p = os.path.join(base, f"{v}.svg")
            if os.path.isfile(p):
                svgs[v] = open(p, encoding="utf-8").read()
        if "full" not in svgs:
            sys.exit(f"{folder}: full.svg is required")

        png_sizes = meta.get("png_sizes", DEFAULT_PNG)
        ico_sizes = meta.get("ico_sizes", DEFAULT_ICO)
        digest = hashlib.sha256("".join(svgs[v] for v in sorted(svgs)).encode()).hexdigest()
        ico = ico_bytes(svgs, ico_sizes)

        cur = con.execute(
            "INSERT INTO icon(key,name,name_th,category,description,ico,sha256,created_utc) "
            "VALUES (?,?,?,?,?,?,?,?)",
            (meta["key"], meta["name"], meta.get("name_th"), meta["category"],
             meta.get("description"), ico, digest, now))
        icon_id = cur.lastrowid
        con.executemany("INSERT INTO icon_svg VALUES (?,?,?)",
                        [(icon_id, v, s) for v, s in svgs.items()])

        out_dir = os.path.join(OUT, folder)
        os.makedirs(out_dir, exist_ok=True)
        open(os.path.join(out_dir, f"{folder}.ico"), "wb").write(ico)
        for v, s in svgs.items():
            open(os.path.join(out_dir, f"{folder}-{v}.svg" if v != "full" else f"{folder}.svg"),
                 "w", encoding="utf-8").write(s)
        for s in png_sizes:
            data = png_bytes(render(pick(svgs, s), s))
            con.execute("INSERT INTO icon_raster VALUES (?,?,?)", (icon_id, s, data))
            open(os.path.join(out_dir, f"{folder}-{s}.png"), "wb").write(data)
        print(f"  {meta['key']:<20} {len(svgs)} svg, {len(png_sizes)} png, ico {len(ico)//1024} KB")

    con.commit()
    con.execute("VACUUM")
    con.close()
    print(f"icons.db: {len(folders)} icons, {os.path.getsize(DB)//1024} KB")


if __name__ == "__main__":
    main()
