#!/usr/bin/env python3
"""Trace source/onion-source.png into SVG variants and write icon-src/ folders.

Run order:  python3 make_pack.py   then   python3 build_icon_db.py
Requires:   pip install potracer opencv-python-headless numpy pillow
"""
import json
import os

import cv2
import numpy as np
import potrace
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_IMG = os.path.join(HERE, "source", "onion-source.png")
OUT = os.path.join(HERE, "icon-src")

AMBER, CREAM = "#F5A623", "#FFE3AD"
UP = 2  # trace at 2x for sub-pixel accuracy

# ---------- source masks -------------------------------------------------
a = np.array(Image.open(SRC_IMG).convert("RGBA")).astype(np.int32)
R, G, B, A = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
white = (R > 250) & (G > 250) & (B > 250)          # 1px white fringe in the source
opaque = (A > 128) & ~white
t = np.clip((G - 166) / (227 - 166), 0, 1)          # cream-ness along amber -> cream
t[~opaque] = 0
sil_soft = opaque.astype(np.float32)
cream_soft = t.astype(np.float32)
sil_u8 = opaque.astype(np.uint8)
cream_u8 = (t > 0.5).astype(np.uint8)


def to_mask(soft, blur):
    big = cv2.resize(soft.astype(np.float32), None, fx=UP, fy=UP, interpolation=cv2.INTER_CUBIC)
    big = cv2.GaussianBlur(big, (0, 0), blur * UP)
    return big > 0.5


def trace(mask, turd=150, alpha=1.3, opt=1.0):
    # potracer inverts its input, so pass the complement
    plist = potrace.Bitmap(~mask).trace(
        turdsize=turd, turnpolicy=potrace.POTRACE_TURNPOLICY_MINORITY,
        alphamax=alpha, opticurve=True, opttolerance=opt)
    f = lambda p: f"{p.x / UP:.1f} {p.y / UP:.1f}"
    out = []
    for curve in plist:
        s = f"M{f(curve.start_point)}"
        for seg in curve.segments:
            if seg.is_corner:
                s += f"L{f(seg.c)}L{f(seg.end_point)}"
            else:
                s += f"C{f(seg.c1)} {f(seg.c2)} {f(seg.end_point)}"
        out.append(s + "Z")
    return "".join(out)


def disk(r):
    return cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))


def simplified(close_r, erode_r, min_area, blur):
    """Thicker amber separators, fewer tiny details - for 16..32px."""
    sil = cv2.morphologyEx(sil_u8, cv2.MORPH_CLOSE, disk(close_r))
    cr = cv2.erode(cream_u8, disk(erode_r))
    n, lab, stats, _ = cv2.connectedComponentsWithStats(cr, connectivity=8)
    for i in range(1, n):
        if stats[i, cv2.CC_STAT_AREA] < min_area:
            cr[lab == i] = 0
    cr &= sil
    return trace(to_mask(sil, blur)), trace(to_mask(cr, blur))


# ---------- geometry -----------------------------------------------------
ys, xs = np.nonzero(opaque)
cx, cy = (xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2
side = max(xs.max() - xs.min(), ys.max() - ys.min()) * 1.10
VB = (round(cx - side / 2, 1), round(cy - side / 2, 1), round(side, 1))
VB_STR = f"{VB[0]} {VB[1]} {VB[2]} {VB[2]}"


def svg_doc(body):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{VB_STR}" width="256" height="256">\n{body}</svg>\n'


def onion(sil, cream):
    return (f'  <path fill="{AMBER}" fill-rule="evenodd" d="{sil}"/>\n'
            f'  <path fill="{CREAM}" fill-rule="evenodd" d="{cream}"/>\n')


def badge(color, glyph):
    bx, by, s = VB[0] + 0.795 * VB[2], VB[1] + 0.795 * VB[2], VB[2]
    r = 0.185 * s
    o = (f'  <circle cx="{bx:.1f}" cy="{by:.1f}" r="{r * 1.14:.1f}" fill="#FFFFFF"/>\n'
         f'  <circle cx="{bx:.1f}" cy="{by:.1f}" r="{r:.1f}" fill="{color}"/>\n')
    sw = f'stroke="#FFFFFF" stroke-width="{r * 0.28:.1f}" stroke-linecap="round" stroke-linejoin="round" fill="none"'
    if glyph == "check":
        pts = [(-0.45, 0.02), (-0.12, 0.36), (0.5, -0.34)]
        o += f'  <polyline points="{" ".join(f"{bx + x * r:.1f},{by + y * r:.1f}" for x, y in pts)}" {sw}/>\n'
    elif glyph == "dash":
        o += f'  <line x1="{bx - 0.45 * r:.1f}" y1="{by:.1f}" x2="{bx + 0.45 * r:.1f}" y2="{by:.1f}" {sw}/>\n'
    elif glyph == "bang":
        o += (f'  <line x1="{bx:.1f}" y1="{by - 0.5 * r:.1f}" x2="{bx:.1f}" y2="{by + 0.1 * r:.1f}" {sw}/>\n'
              f'  <circle cx="{bx:.1f}" cy="{by + 0.48 * r:.1f}" r="{r * 0.15:.1f}" fill="#FFFFFF"/>\n')
    return o


# ---------- trace the three onion bases ---------------------------------
FULL = (trace(to_mask(sil_soft, 2.2)), trace(to_mask(cream_soft, 2.2)))
SMALL = simplified(close_r=14, erode_r=9, min_area=3000, blur=2.5)
TINY = simplified(close_r=26, erode_r=24, min_area=14000, blur=4.0)

ICONS = [
    # folder, key, name, name_th, category, description, badge(color, glyph) or None
    ("app", "app", "Onion ProcOperator", "Onion ProcOperator", "app",
     "Application icon (halved onion, amber + cream).", None),
    ("tray-connected", "tray.connected", "Connected", "เชื่อมต่อแล้ว", "status",
     "Agent/client is connected to the server.", ("#2E9E4F", "check")),
    ("tray-disconnected", "tray.disconnected", "Disconnected", "ไม่ได้เชื่อมต่อ", "status",
     "Not connected / connection lost.", ("#6F7782", "dash")),
    ("tray-error", "tray.error", "Error", "ผิดพลาด", "status",
     "Something is wrong (generic error state).", ("#D64545", "bang")),
]

for folder, key, name, name_th, cat, desc, bdg in ICONS:
    d = os.path.join(OUT, folder)
    os.makedirs(d, exist_ok=True)
    parts = {}
    for variant, base in (("full", FULL), ("small", SMALL), ("tiny", TINY)):
        body = onion(*base)
        if bdg:
            # glyph only where it stays readable (>= 24px); plain dot for 16px
            body += badge(bdg[0], bdg[1] if variant != "tiny" else None)
        parts[variant] = svg_doc(body)
    for v, s in parts.items():
        open(os.path.join(d, f"{v}.svg"), "w", encoding="utf-8").write(s)
    meta = {"key": key, "name": name, "name_th": name_th, "category": cat, "description": desc}
    if cat == "status":
        meta["ico_sizes"] = [16, 20, 24, 32, 48, 64]
        meta["png_sizes"] = [16, 20, 24, 32, 48, 64]
    json.dump(meta, open(os.path.join(d, "meta.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=2)
    print(folder, {v: len(s) for v, s in parts.items()})
