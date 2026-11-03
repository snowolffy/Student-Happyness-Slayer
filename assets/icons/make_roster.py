#!/usr/bin/env python3
"""Roster manager icon: the onion, in blue, with the roots replaced by a group of users.

Reuses the tracing helpers from make_pack.py (importing it also regenerates the app icons - harmless).
Run:  python3 make_roster.py   then   python3 build_icon_db.py
"""
import json
import os

import cv2
import numpy as np

import make_pack as mp

DARK, PALE = "#2A6FD6", "#D9E8FF"
OUT = os.path.join(mp.HERE, "icon-src", "roster-app")

# ---------- cut the root hairs off the source masks -----------------------
# roots hang below the line from the bulb's bottom edge (850,1005) to its base joint (975,915)
H, W = mp.sil_u8.shape
yy, xx = np.mgrid[0:H, 0:W]
roots = (xx > 855) & (yy > 1005 - (xx - 850) * 0.72 + 4)
sil_u8 = mp.sil_u8.copy();       sil_u8[roots] = 0
cream_u8 = mp.cream_u8.copy();   cream_u8[roots] = 0
sil_soft = mp.sil_soft.copy();   sil_soft[roots] = 0
cream_soft = mp.cream_soft.copy(); cream_soft[roots] = 0


def simplified(close_r, erode_r, min_area, blur):
    sil = cv2.morphologyEx(sil_u8, cv2.MORPH_CLOSE, mp.disk(close_r))
    cr = cv2.erode(cream_u8, mp.disk(erode_r))
    n, lab, stats, _ = cv2.connectedComponentsWithStats(cr, connectivity=8)
    for i in range(1, n):
        if stats[i, cv2.CC_STAT_AREA] < min_area:
            cr[lab == i] = 0
    cr &= sil
    return mp.trace(mp.to_mask(sil, blur)), mp.trace(mp.to_mask(cr, blur))


FULL = (mp.trace(mp.to_mask(sil_soft, 2.2)), mp.trace(mp.to_mask(cream_soft, 2.2)))
SMALL = simplified(close_r=14, erode_r=9, min_area=3000, blur=2.5)
TINY = simplified(close_r=26, erode_r=24, min_area=14000, blur=4.0)


# ---------- the user group -------------------------------------------------
SIDE = "#5B9BEA"      # lighter blue for the two people behind
WHITE = "#FFFFFF"


def person(cx, cy, s, ring, fill=DARK):
    """Head + shoulders with a white sticker-style outline.
    (cx, cy) = bottom-centre of the shoulders; s = scale; ring = outline width in source px (outside the shape)."""
    shapes = ('<circle cx="0" cy="-98" r="34"/>'
              '<path d="M-62 0 C-62 -38 -38 -58 0 -58 C38 -58 62 -38 62 0 Z"/>')
    return (f'  <g transform="translate({cx} {cy}) scale({s})">\n'
            f'    <g fill="{WHITE}" stroke="{WHITE}" stroke-width="{2 * ring / s:.1f}" stroke-linejoin="round">{shapes}</g>\n'
            f'    <g fill="{fill}">{shapes}</g>\n'
            f'  </g>\n')


def group(kind):
    """returns (svg, right_reach, bottom_reach) in source px, outlines included.
    Placement measured from the reference mock-up: the group overlaps the bulb's lower-right."""
    if kind == "full":      # three people, centre one in front
        return (person(722, 1064, 1.75, 20, SIDE) + person(1058, 1064, 1.75, 20, SIDE) + person(888, 1090, 2.6, 22), 1190, 1115)
    if kind == "small":     # same layout, wider outline for 17..32px
        return (person(722, 1064, 1.75, 24, SIDE) + person(1058, 1064, 1.75, 24, SIDE) + person(888, 1090, 2.6, 26), 1195, 1120)
    # tiny (16px): one large user - a group would be a smudge
    return (person(905, 1095, 2.6, 22), 1095, 1120)


# ---------- geometry: onion bbox + the user group, per variant ------------------
ys, xs = np.nonzero(sil_u8)


def viewbox(right, bottom):
    x0, x1, y0, y1 = xs.min(), max(xs.max(), right), ys.min(), bottom
    side = max(x1 - x0, y1 - y0) * 1.10
    return f"{(x0 + x1) / 2 - side / 2:.1f} {(y0 + y1) / 2 - side / 2:.1f} {side:.1f} {side:.1f}"


def svg_doc(base, kind):
    people, right, bottom = group(kind)
    body = (f'  <path fill="{DARK}" fill-rule="evenodd" d="{base[0]}"/>\n'
            f'  <path fill="{PALE}" fill-rule="evenodd" d="{base[1]}"/>\n' + people)
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{viewbox(right, bottom)}" width="256" height="256">\n{body}</svg>\n'


os.makedirs(OUT, exist_ok=True)
for variant, base in (("full", FULL), ("small", SMALL), ("tiny", TINY)):
    open(os.path.join(OUT, f"{variant}.svg"), "w", encoding="utf-8").write(svg_doc(base, variant))
json.dump({"key": "roster.app", "name": "Roster Manager", "name_th": "จัดการรายชื่อผู้ใช้",
           "category": "app",
           "description": "Roster manager app icon: blue onion whose roots are a group of users."},
          open(os.path.join(OUT, "meta.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("roster-app written")
