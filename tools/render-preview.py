#!/usr/bin/env python3
"""Export the editable SVG with the system's librsvg/Cairo; no plugin dependency."""
import argparse
import copy
import ctypes as c
import ctypes.util
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/DualSenseLighting/Assets"


def render(source, destination, width=None, height=None):
    root = ET.parse(source).getroot()
    width = width or int(root.get("width"))
    height = height or int(root.get("height"))
    svg = c.CDLL(ctypes.util.find_library("rsvg-2"))
    cairo = c.CDLL(ctypes.util.find_library("cairo"))
    gobject = c.CDLL(ctypes.util.find_library("gobject-2.0"))
    class Rectangle(c.Structure):
        _fields_ = [(name, c.c_double) for name in ("x", "y", "width", "height")]
    svg.rsvg_handle_new_from_file.argtypes = [c.c_char_p, c.c_void_p]
    svg.rsvg_handle_new_from_file.restype = c.c_void_p
    svg.rsvg_handle_render_document.argtypes = [c.c_void_p, c.c_void_p, c.POINTER(Rectangle), c.c_void_p]
    svg.rsvg_handle_render_document.restype = c.c_int
    cairo.cairo_image_surface_create.argtypes = [c.c_int, c.c_int, c.c_int]
    cairo.cairo_image_surface_create.restype = c.c_void_p
    cairo.cairo_create.argtypes = [c.c_void_p]
    cairo.cairo_create.restype = c.c_void_p
    cairo.cairo_surface_write_to_png.argtypes = [c.c_void_p, c.c_char_p]
    cairo.cairo_surface_write_to_png.restype = c.c_int
    cairo.cairo_destroy.argtypes = [c.c_void_p]
    cairo.cairo_surface_destroy.argtypes = [c.c_void_p]
    gobject.g_object_unref.argtypes = [c.c_void_p]
    handle = svg.rsvg_handle_new_from_file(str(source).encode(), None)
    if not handle:
        raise RuntimeError(f"Could not load {source}")
    surface = cairo.cairo_image_surface_create(0, width, height)
    context = cairo.cairo_create(surface)
    try:
        if not svg.rsvg_handle_render_document(handle, context, c.byref(Rectangle(0, 0, width, height)), None):
            raise RuntimeError("librsvg rendering failed")
        if cairo.cairo_surface_write_to_png(surface, str(destination).encode()) != 0:
            raise RuntimeError("PNG export failed")
    finally:
        cairo.cairo_destroy(context)
        cairo.cairo_surface_destroy(surface)
        gobject.g_object_unref(handle)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sample", action="store_true", help="Also export a lit blue example for visual inspection")
    parser.add_argument("--player-mask", type=lambda value: int(value, 0), default=4, help="White player LED bitmask for the sample (0..31)")
    args = parser.parse_args()
    source = ET.parse(ASSETS / "dualsense-preview.svg")
    root = source.getroot()
    rgb = next(e for e in root.iter() if e.get("id") == "rgb-lightbars")
    work = ROOT / "work/preview-render"
    work.mkdir(parents=True, exist_ok=True)
    # Body and light mask are separate cached textures, both exported from this one SVG.
    unlit = copy.deepcopy(source)
    for parent in unlit.iter():
        for child in list(parent):
            if child.get("id") == "rgb-lightbars": parent.remove(child)
    unlit_path = work / "unlit.svg"
    unlit.write(unlit_path, encoding="utf-8", xml_declaration=True)
    render(unlit_path, ASSETS / "dualsense-preview.png")
    mask = ET.Element(root.tag, {key: root.get(key) for key in ("width", "height", "viewBox")})
    definitions = copy.deepcopy(root.find("{http://www.w3.org/2000/svg}defs"))
    for element in definitions.iter():
        if element.get("id") == "led-light":
            for stop in element: stop.set("stop-color", "#ffffff")
    namespace = "{http://www.w3.org/2000/svg}"
    occlusion = ET.SubElement(definitions, namespace + "mask", {
        "id": "preview-lightbar-occlusion", "maskUnits": "userSpaceOnUse",
        "x": "0", "y": "0", "width": root.get("width"), "height": root.get("height")})
    ET.SubElement(occlusion, namespace + "rect", {"width": root.get("width"), "height": root.get("height"), "fill": "#ffffff"})
    pad = next(e for e in root.iter() if e.get("id") == "white-touchpad")
    ET.SubElement(occlusion, namespace + "path", {"d": pad.get("d"), "fill": "#000000"})
    mask.append(definitions)
    emitters = copy.deepcopy(rgb)
    emitters.set("mask", "url(#preview-lightbar-occlusion)")
    mask.append(emitters)
    mask_path = work / "lightbar-mask.svg"
    ET.ElementTree(mask).write(mask_path, encoding="utf-8", xml_declaration=True)
    render(mask_path, ASSETS / "dualsense-preview-lightbar.png")
    print("Exported embedded controller body and tapered lightbar alpha mask.")
    if args.sample:
        output = ROOT / "artifacts/preview"
        output.mkdir(parents=True, exist_ok=True)
        tree = copy.deepcopy(source)
        for element in tree.iter():
            bit = element.get("data-player-bit")
            if bit is not None and args.player_mask & (1 << int(bit)):
                element.set("fill", "#ffffff")
        sample = output / "dualsense-preview-lit.svg"
        tree.write(sample, encoding="utf-8", xml_declaration=True)
        render(sample, output / "dualsense-preview-lit.png", 1440, 680)
        print(f"Exported visual sample: {output / 'dualsense-preview-lit.png'}")
