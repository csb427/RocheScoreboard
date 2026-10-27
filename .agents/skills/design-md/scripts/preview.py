#!/usr/bin/env python3
"""
preview.py — render a DESIGN.md into a self-contained, offline HTML live preview.

Builds a single .html file (no remote assets, strict CSP) that visualizes the
design system: color palette, type scale, components, spacing and radius scales,
plus the prose sections. Inspired by the getdesign.md preview layout.

Usage:
  python preview.py DESIGN.md -o preview.html
  python preview.py DESIGN.md            # writes DESIGN.preview.html next to it

SECURITY: every value pulled from the document is HTML-escaped before display.
Values injected into CSS (colors, dimensions) are validated against strict
allow-list patterns first; anything that doesn't match is rendered as inert text
rather than injected into a style attribute. No JavaScript, no external fetches.
"""
from __future__ import annotations

import argparse
import html
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dmd  # noqa: E402

# Allow-lists for values that may enter a style attribute.
SAFE_COLOR = re.compile(r"^#[0-9a-fA-F]{3,8}$")
SAFE_DIM = re.compile(r"^-?\d*\.?\d+(px|rem|em|%)?$")
SAFE_FONTNAME = re.compile(r"^[A-Za-z0-9 _'\-]{1,48}$")


def esc(s):
    return html.escape(str(s), quote=True)


def safe_color(v):
    if isinstance(v, dict) and v.get("type") == "color":
        return v["hex"]
    if isinstance(v, str) and SAFE_COLOR.match(v.strip()):
        return v.strip()
    return None


def safe_dim(v):
    if isinstance(v, dict) and v.get("type") == "dimension":
        if v.get("unit"):
            return f"{dmd._numfmt(v['value'])}{v['unit']}"
        return f"{dmd._numfmt(v['value'])}px"
    if isinstance(v, str) and SAFE_DIM.match(v.strip()):
        return v.strip()
    return None


def safe_font(v):
    if isinstance(v, str) and SAFE_FONTNAME.match(v.strip()):
        return v.strip()
    return None


def _luminance_text(color):
    """Pick black/white text for a swatch background based on luminance."""
    if isinstance(color, dict):
        return "#000" if color.get("luminance", 0) > 0.4 else "#fff"
    return "#000"


def render(text, title=None):
    pr = dmd.parse_document(text)
    model = dmd.build_model(pr)
    res = dmd.lint(text)
    summary = res["summary"]
    name = pr.tokens.get("name") or title or "DESIGN.md preview"
    desc = pr.tokens.get("description") or ""
    prose = {}
    # quick prose extraction
    from scaffold import extract_prose  # local import; same dir
    prose = extract_prose(text)

    sections = []

    # --- Colors ---
    swatches = []
    for n, v in model.colors.items():
        hexv = safe_color(v)
        if not hexv:
            continue
        txt = _luminance_text(v)
        swatches.append(
            f'<div class="swatch" style="background:{esc(hexv)};color:{esc(txt)}">'
            f'<span class="sw-name">{esc(n)}</span>'
            f'<span class="sw-hex">{esc(hexv)}</span></div>')
    if swatches:
        sections.append(('Color Palette', '<div class="swatches">' + "".join(swatches) + '</div>'))

    # --- Typography ---
    type_rows = []
    for n, t in model.typography.items():
        fam = safe_font(t.get("fontFamily")) if isinstance(t, dict) else None
        size = safe_dim(t.get("fontSize")) if isinstance(t, dict) else None
        weight = t.get("fontWeight") if isinstance(t, dict) else None
        wsafe = str(weight) if isinstance(weight, (int, float)) else "400"
        style = []
        if fam:
            style.append(f"font-family:'{esc(fam)}',sans-serif")
        if size:
            style.append(f"font-size:{esc(size)}")
        style.append(f"font-weight:{esc(wsafe)}")
        meta = " / ".join(filter(None, [size or "", str(weight) if weight else "", fam or ""]))
        type_rows.append(
            f'<div class="type-row"><div class="type-label">{esc(n)}<br>'
            f'<span class="type-meta">{esc(meta)}</span></div>'
            f'<div class="type-sample" style="{";".join(style)}">{esc(name)}</div></div>')
    if type_rows:
        sections.append(('Typography', "".join(type_rows)))

    # --- Components ---
    comp_cards = []
    for cname, props in model.components.items():
        bg = safe_color(props.get("backgroundColor"))
        fg = safe_color(props.get("textColor"))
        rad = safe_dim(props.get("rounded"))
        pad = safe_dim(props.get("padding")) or "10px 16px"
        style = []
        if bg:
            style.append(f"background:{esc(bg)}")
        if fg:
            style.append(f"color:{esc(fg)}")
        if rad:
            style.append(f"border-radius:{esc(rad)}")
        style.append(f"padding:{esc(pad)}")
        if not bg:
            style.append("border:1px solid var(--line)")
        comp_cards.append(
            f'<div class="comp"><span class="comp-name">{esc(cname)}</span>'
            f'<span class="comp-chip" style="{";".join(style)}">{esc(cname)}</span></div>')
    if comp_cards:
        sections.append(('Components', '<div class="comps">' + "".join(comp_cards) + '</div>'))

    # --- Spacing ---
    sp_rows = []
    for n, v in model.spacing.items():
        d = safe_dim(v)
        if not d:
            continue
        sp_rows.append(
            f'<div class="scale-row"><span class="scale-name">{esc(n)}</span>'
            f'<span class="scale-bar" style="width:{esc(d)}"></span>'
            f'<span class="scale-val">{esc(d)}</span></div>')
    if sp_rows:
        sections.append(('Spacing Scale', "".join(sp_rows)))

    # --- Radius ---
    rad_rows = []
    for n, v in model.rounded.items():
        d = safe_dim(v)
        if not d:
            continue
        rad_rows.append(
            f'<div class="rad-box"><span class="rad-demo" style="border-radius:{esc(d)}"></span>'
            f'<span class="scale-name">{esc(n)}</span>'
            f'<span class="scale-val">{esc(d)}</span></div>')
    if rad_rows:
        sections.append(('Border Radius', '<div class="rads">' + "".join(rad_rows) + '</div>'))

    # --- Prose sections ---
    prose_html = []
    for sec in dmd.CANONICAL_ORDER:
        if sec in prose and prose[sec].strip():
            body = esc(prose[sec])
            prose_html.append(f'<div class="prose"><h3>{esc(sec)}</h3><pre>{body}</pre></div>')

    badges = (f'<span class="badge err">{summary["errors"]} errors</span>'
              f'<span class="badge warn">{summary["warnings"]} warnings</span>'
              f'<span class="badge info">{summary["infos"]} info</span>')

    body_sections = ""
    for i, (heading, content) in enumerate(sections):
        body_sections += (f'<section><div class="sec-num">{i+1:02d}</div>'
                          f'<h2>{esc(heading)}</h2>{content}</section>')
    body_sections += "".join(prose_html)

    return HTML_TEMPLATE.format(
        title=esc(name), desc=esc(desc), badges=badges, body=body_sections)


HTML_TEMPLATE = """<!DOCTYPE html>
<html lang="en"><head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:;">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} — DESIGN.md preview</title>
<style>
  :root {{ --bg:#0c0d12; --panel:#15171f; --ink:#e8e9ee; --muted:#9aa0ad; --line:#2a2d39; --accent:#7c5cff; }}
  * {{ box-sizing:border-box; }}
  body {{ margin:0; background:var(--bg); color:var(--ink); font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif; line-height:1.5; }}
  header {{ padding:48px 32px 24px; border-bottom:1px solid var(--line); }}
  header h1 {{ margin:0 0 8px; font-size:2.4rem; letter-spacing:-0.02em; }}
  header p {{ margin:0; color:var(--muted); max-width:70ch; }}
  .badges {{ margin-top:16px; display:flex; gap:8px; }}
  .badge {{ font-size:.75rem; padding:3px 10px; border-radius:999px; border:1px solid var(--line); color:var(--muted); }}
  .badge.err {{ color:#ff6b6b; }} .badge.warn {{ color:#ffd166; }} .badge.info {{ color:#7cc4ff; }}
  main {{ max-width:1100px; margin:0 auto; padding:24px 32px 80px; }}
  section {{ padding:32px 0; border-bottom:1px solid var(--line); position:relative; }}
  .sec-num {{ color:var(--muted); font-size:.72rem; letter-spacing:.18em; }}
  h2 {{ margin:4px 0 20px; font-size:1.4rem; }}
  h3 {{ margin:0 0 8px; font-size:1rem; }}
  .swatches {{ display:grid; grid-template-columns:repeat(auto-fill,minmax(150px,1fr)); gap:12px; }}
  .swatch {{ aspect-ratio:3/2; border-radius:12px; padding:12px; display:flex; flex-direction:column; justify-content:flex-end; border:1px solid var(--line); }}
  .sw-name {{ font-weight:600; font-size:.85rem; }} .sw-hex {{ font-size:.72rem; opacity:.85; font-variant-numeric:tabular-nums; }}
  .type-row {{ display:flex; gap:24px; align-items:baseline; padding:14px 0; border-top:1px solid var(--line); }}
  .type-label {{ width:140px; flex:none; color:var(--muted); font-size:.78rem; }}
  .type-meta {{ font-size:.7rem; opacity:.7; }}
  .type-sample {{ color:var(--ink); overflow:hidden; }}
  .comps {{ display:flex; flex-wrap:wrap; gap:20px; }}
  .comp {{ display:flex; flex-direction:column; gap:8px; align-items:flex-start; }}
  .comp-name {{ font-size:.72rem; color:var(--muted); }}
  .comp-chip {{ font-size:.9rem; font-weight:600; }}
  .scale-row {{ display:flex; align-items:center; gap:16px; padding:6px 0; }}
  .scale-name {{ width:80px; color:var(--muted); font-size:.8rem; }}
  .scale-bar {{ height:14px; background:var(--accent); border-radius:4px; min-width:2px; }}
  .scale-val {{ color:var(--muted); font-size:.78rem; font-variant-numeric:tabular-nums; }}
  .rads {{ display:flex; flex-wrap:wrap; gap:20px; }}
  .rad-box {{ display:flex; flex-direction:column; align-items:center; gap:6px; }}
  .rad-demo {{ width:72px; height:72px; background:var(--panel); border:1px solid var(--accent); }}
  .prose {{ padding:24px 0; border-bottom:1px solid var(--line); }}
  .prose pre {{ white-space:pre-wrap; font-family:inherit; color:var(--muted); margin:0; }}
  footer {{ text-align:center; color:var(--muted); font-size:.75rem; padding:24px; }}
</style></head>
<body>
<header><h1>{title}</h1><p>{desc}</p><div class="badges">{badges}</div></header>
<main>{body}</main>
<footer>Generated offline by the design-md skill · DESIGN.md spec (alpha)</footer>
</body></html>
"""


def main(argv=None):
    p = argparse.ArgumentParser(prog="preview", description="Render DESIGN.md to a self-contained HTML preview")
    p.add_argument("file")
    p.add_argument("-o", "--out")
    args = p.parse_args(argv)
    with open(args.file, "r", encoding="utf-8") as f:
        text = f.read()
    out = args.out
    if not out:
        base = os.path.splitext(args.file)[0]
        out = base + ".preview.html"
    htmlout = render(text)
    with open(out, "w", encoding="utf-8") as f:
        f.write(htmlout)
    sys.stderr.write(f"Wrote {out}\n")
    print(out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
