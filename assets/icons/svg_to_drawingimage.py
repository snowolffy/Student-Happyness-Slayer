#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
svg_to_drawingimage.py - แปลง SVG ของชุดไอคอน Onion ProcOparetor เป็น WPF DrawingImage XAML
(ใช้แทน PNG 256px ที่ WPF ย่อแล้วเบลอ - WP UI-7c)

รองรับเฉพาะรูปแบบ SVG ที่ export/<name>/<name>.svg และ export/<name>/<name>-small.svg ใช้จริง:
  - <path fill="#RRGGBB" fill-rule="evenodd|nonzero" d="..."/> ระดับบนสุด
  - <g transform="translate(tx ty) scale(s)"> ที่มี <g fill=... [stroke=... stroke-width=...
    stroke-linejoin="round"]> ซ้อนอยู่ข้างใน ซึ่งมี <circle cx cy r/> และ/หรือ <path d="..."/>
    (ใช้กับ roster-app.svg ที่วาดไอคอนกลุ่มผู้ใช้สามจุดทับโคนหอม)

ไม่ใช่ตัวแปลง SVG ทั่วไป - ตั้งใจให้พังตรงๆ (raise) ถ้าเจอ element/attribute ที่ไม่รู้จัก แทนที่จะ
เงียบแล้วได้โลโก้ผิดรูป

ใช้:
    python3 svg_to_drawingimage.py <path/to/icon.svg> <ResourceKey>

พิมพ์ XAML fragment ของ <DrawingImage x:Key="ResourceKey">...</DrawingImage> ออก stdout
พร้อมคอมเมนต์บอกไฟล์ต้นทาง ให้ก็อปวางเข้า Theme/Logos.xaml เอง (ตั้งใจไม่ auto-inject เข้าไฟล์อื่น
เพราะ Logos.xaml มีคอมเมนต์ไทยกำกับที่ต้องคงไว้)
"""
import re
import sys
import xml.etree.ElementTree as ET

SVG_NS = "{http://www.w3.org/2000/svg}"


def strip_ns(tag: str) -> str:
    return tag.split("}", 1)[-1] if "}" in tag else tag


def map_fill_rule(value: str | None) -> str:
    v = (value or "evenodd").strip().lower()
    if v == "evenodd":
        return "EvenOdd"
    if v == "nonzero":
        return "Nonzero"
    raise ValueError(f"fill-rule ที่ไม่รู้จัก: {value!r}")


def parse_transform(transform: str) -> tuple[float, float, float]:
    """คืน (tx, ty, scale) จาก "translate(tx ty) scale(s)" เท่านั้น - พังถ้ารูปแบบอื่น"""
    m = re.fullmatch(
        r"\s*translate\(\s*([-\d.]+)\s+([-\d.]+)\s*\)\s*scale\(\s*([-\d.]+)\s*\)\s*",
        transform,
    )
    if not m:
        raise ValueError(f"transform ที่ไม่รองรับ: {transform!r} (รองรับแค่ translate()+scale() เดียว)")
    tx, ty, s = (float(x) for x in m.groups())
    return tx, ty, s


def geometry_xaml(el: ET.Element, indent: str) -> str:
    tag = strip_ns(el.tag)
    if tag == "circle":
        cx, cy, r = el.get("cx"), el.get("cy"), el.get("r")
        return f'{indent}<EllipseGeometry Center="{cx},{cy}" RadiusX="{r}" RadiusY="{r}"/>\n'
    if tag == "path":
        d = el.get("d")
        return f'{indent}<PathGeometry Figures="{d}"/>\n'
    raise ValueError(f"geometry element ที่ไม่รู้จัก: <{tag}>")


def geometry_group_xaml(children: list[ET.Element], indent: str) -> str:
    out = f'{indent}<GeometryGroup FillRule="Nonzero">\n'
    for child in children:
        out += geometry_xaml(child, indent + "    ")
    out += f"{indent}</GeometryGroup>\n"
    return out


def render_top_path(el: ET.Element, indent: str) -> str:
    fill = el.get("fill")
    if fill is None:
        raise ValueError("<path> ไม่มี fill")
    fill_rule = map_fill_rule(el.get("fill-rule"))
    d = el.get("d")
    out = f'{indent}<GeometryDrawing Brush="{fill}">\n'
    out += f"{indent}    <GeometryDrawing.Geometry>\n"
    out += f'{indent}        <PathGeometry Figures="{d}" FillRule="{fill_rule}"/>\n'
    out += f"{indent}    </GeometryDrawing.Geometry>\n"
    out += f"{indent}</GeometryDrawing>\n"
    return out


def render_styled_group(g: ET.Element, indent: str) -> str:
    """<g fill=... [stroke=... stroke-width=... stroke-linejoin=...]> ที่มี circle/path ลูก"""
    fill = g.get("fill")
    if fill is None:
        raise ValueError("<g> ชั้นในไม่มี fill")
    stroke = g.get("stroke")
    stroke_width = g.get("stroke-width")
    stroke_linejoin = g.get("stroke-linejoin")
    children = list(g)
    out = f'{indent}<GeometryDrawing Brush="{fill}">\n'
    if stroke is not None:
        if stroke_width is None:
            raise ValueError("<g> มี stroke แต่ไม่มี stroke-width")
        linejoin = {"round": "Round", "miter": "Miter", "bevel": "Bevel"}.get(
            (stroke_linejoin or "miter").lower()
        )
        if linejoin is None:
            raise ValueError(f"stroke-linejoin ที่ไม่รู้จัก: {stroke_linejoin!r}")
        out += f"{indent}    <GeometryDrawing.Pen>\n"
        out += f'{indent}        <Pen Brush="{stroke}" Thickness="{stroke_width}" LineJoin="{linejoin}"/>\n'
        out += f"{indent}    </GeometryDrawing.Pen>\n"
    out += f"{indent}    <GeometryDrawing.Geometry>\n"
    out += geometry_group_xaml(children, indent + "        ")
    out += f"{indent}    </GeometryDrawing.Geometry>\n"
    out += f"{indent}</GeometryDrawing>\n"
    return out


def render_transformed_group(g: ET.Element, indent: str) -> str:
    """<g transform="translate(tx ty) scale(s)"> ที่มี <g fill=...> ซ้อนอยู่ข้างใน"""
    transform = g.get("transform")
    if transform is None:
        raise ValueError("<g> ชั้นนอกไม่มี transform")
    tx, ty, s = parse_transform(transform)
    # SVG "translate(tx ty) scale(s)" ใช้กับจุด p แบบ p' = translate * scale * p
    # กล่าวคือ scale ก่อน แล้วค่อยเลื่อน - ตรงกับ matrix affine (s,0,0,s,tx,ty) ตรงๆ
    # (เขียนเป็น MatrixTransform เดียวแทน TransformGroup เพื่อไม่ให้งงเรื่องลำดับ apply ของ WPF)
    matrix = f"{s},0,0,{s},{tx},{ty}"
    inner_groups = [c for c in g if strip_ns(c.tag) == "g"]
    if not inner_groups:
        raise ValueError("<g transform=...> ไม่มี <g fill=...> ซ้อนอยู่ข้างใน")
    out = f'{indent}<DrawingGroup>\n'
    out += f"{indent}    <DrawingGroup.Transform>\n"
    out += f'{indent}        <MatrixTransform Matrix="{matrix}"/>\n'
    out += f"{indent}    </DrawingGroup.Transform>\n"
    out += f"{indent}    <DrawingGroup.Children>\n"
    for inner in inner_groups:
        out += render_styled_group(inner, indent + "        ")
    out += f"{indent}    </DrawingGroup.Children>\n"
    out += f"{indent}</DrawingGroup>\n"
    return out


def convert(svg_path: str, key: str) -> str:
    tree = ET.parse(svg_path)
    root = tree.getroot()
    if strip_ns(root.tag) != "svg":
        raise ValueError("root element ไม่ใช่ <svg>")

    item_indent = "                    "
    body = ""
    for el in root:
        tag = strip_ns(el.tag)
        if tag == "path":
            body += render_top_path(el, item_indent)
        elif tag == "g":
            body += render_transformed_group(el, item_indent)
        else:
            raise ValueError(f"top-level element ที่ไม่รู้จัก: <{tag}>")

    return (
        f'    <!-- แปลงจาก {svg_path} ด้วย svg_to_drawingimage.py (WP UI-7c) - ห้ามแก้ไฟล์นี้ด้วยมือ\n'
        f'         ถ้าต้นฉบับ SVG เปลี่ยน ให้รันสคริปต์ใหม่แล้วแทนที่ทั้งก้อน -->\n'
        f'    <DrawingImage x:Key="{key}">\n'
        f"        <DrawingImage.Drawing>\n"
        f"            <DrawingGroup>\n"
        f"                <DrawingGroup.Children>\n"
        + body
        + f"                </DrawingGroup.Children>\n"
        f"            </DrawingGroup>\n"
        f"        </DrawingImage.Drawing>\n"
        f"    </DrawingImage>\n"
    )


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("ใช้: python3 svg_to_drawingimage.py <icon.svg> <ResourceKey>", file=sys.stderr)
        sys.exit(1)
    print(convert(sys.argv[1], sys.argv[2]))
