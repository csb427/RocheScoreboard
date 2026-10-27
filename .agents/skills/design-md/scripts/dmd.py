#!/usr/bin/env python3
"""
dmd.py — Self-contained DESIGN.md toolkit (pure Python stdlib, no Node, no deps).

Faithfully reimplements the google-labs-code/design.md CLI logic:
  - parse:    YAML front matter (+ fenced yaml blocks) + markdown sections
  - lint:     the 10 linting rules with upstream-compatible messages
  - diff:     token-level added/removed/modified + regression detection
  - export:   json-tailwind (v3), css-tailwind (v4), dtcg (W3C Design Tokens)
  - spec:     emit the format spec / linting-rules table
  - selftest: built-in fixtures + assertions

Spec version targeted: "alpha".

Security notes:
  - The YAML reader is a *restricted* indentation parser for the DESIGN.md
    subset (block maps + scalars). It executes no code and resolves no tags,
    so it is not vulnerable to the unsafe-load class of YAML attacks.
  - No network access. Reads only the file paths you pass in.

Usage:
  python dmd.py lint DESIGN.md
  python dmd.py diff OLD.md NEW.md
  python dmd.py export --format css-tailwind DESIGN.md
  python dmd.py spec [--rules] [--rules-only] [--format json|markdown]
  python dmd.py selftest
All commands accept '-' to read from stdin. Output defaults to JSON.
"""
from __future__ import annotations

import argparse
import json
import math
import re
import sys

SPEC_VERSION = "alpha"

# ---------------------------------------------------------------------------
# Constants (mirror packages/cli/src spec-config)
# ---------------------------------------------------------------------------
SCHEMA_KEYS = ["version", "name", "description", "colors", "typography",
               "rounded", "spacing", "components"]

CANONICAL_ORDER = ["Overview", "Colors", "Typography", "Layout",
                   "Elevation & Depth", "Shapes", "Components", "Do's and Don'ts"]

# alias (lowercased) -> canonical
SECTION_ALIASES = {
    "brand & style": "Overview",
    "layout & spacing": "Layout",
    "elevation": "Elevation & Depth",
}

VALID_COMPONENT_SUB_TOKENS = ["backgroundColor", "textColor", "typography",
                              "rounded", "padding", "size", "height", "width"]

STANDARD_UNITS = {"px", "em", "rem"}
CSS_UNITS = {"px", "cm", "mm", "in", "pt", "pc", "em", "rem", "ex", "ch", "cap",
             "ic", "lh", "rlh", "vh", "vw", "vmin", "vmax", "dvh", "dvw", "dvmin",
             "dvmax", "svh", "svw", "svmin", "svmax", "lvh", "lvw", "lvmin",
             "lvmax", "cqw", "cqh", "cqi", "cqb", "cqmin", "cqmax", "%"}

MD3_STANDARD_FAMILIES = {"primary", "secondary", "tertiary", "error", "surface",
                         "background", "outline"}

WCAG_AA_MINIMUM = 4.5
MAX_TYPO_DISTANCE = 2
MAX_REFERENCE_DEPTH = 10
MAX_TOKEN_NESTING_DEPTH = 20

DIMENSION_RE = re.compile(r"^(-?\d*\.?\d+)([a-zA-Z%]+)$")
REFERENCE_RE = re.compile(r"^\{[a-zA-Z0-9._-]+\}$")
HEX_RE = re.compile(r"^#([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$")
TYPO_PROP_KEYS = {"fontFamily", "fontSize", "fontWeight", "lineHeight",
                  "letterSpacing", "fontFeature", "fontVariation"}

# ---------------------------------------------------------------------------
# Restricted YAML reader (block mappings + scalars only)
# ---------------------------------------------------------------------------
class YamlError(Exception):
    pass


def _strip_comment(line: str) -> str:
    out, in_s, in_d = [], False, False
    i = 0
    while i < len(line):
        c = line[i]
        if c == "'" and not in_d:
            in_s = not in_s
        elif c == '"' and not in_s:
            in_d = not in_d
        elif c == "#" and not in_s and not in_d:
            # only a comment if preceded by whitespace or start
            if i == 0 or line[i - 1] in " \t":
                break
        out.append(c)
        i += 1
    return "".join(out).rstrip()


def _parse_scalar(raw: str):
    s = raw.strip()
    if s == "" or s == "~" or s.lower() == "null":
        return None
    if len(s) >= 2 and s[0] == s[-1] and s[0] in "\"'":
        return s[1:-1]
    low = s.lower()
    if low == "true":
        return True
    if low == "false":
        return False
    # number?
    if re.match(r"^-?\d+$", s):
        try:
            return int(s)
        except ValueError:
            pass
    if re.match(r"^-?\d*\.?\d+([eE][-+]?\d+)?$", s):
        try:
            return float(s)
        except ValueError:
            pass
    return s


def parse_yaml(text: str) -> dict:
    """Parse a restricted YAML subset: nested block mappings of scalars.

    Inline flow maps/lists are not supported (DESIGN.md does not use them in
    token front matter). Raises YamlError on structural problems.
    """
    lines = []
    for raw in text.splitlines():
        if raw.strip() == "" or raw.lstrip().startswith("#"):
            continue
        stripped = _strip_comment(raw)
        if stripped.strip() == "":
            continue
        indent = len(raw) - len(raw.lstrip(" "))
        if "\t" in raw[:indent]:
            raise YamlError("Tabs are not allowed for indentation in YAML.")
        lines.append((indent, stripped.strip(), raw))

    pos = 0

    def parse_block(min_indent):
        nonlocal pos
        result = {}
        while pos < len(lines):
            indent, content, _raw = lines[pos]
            if indent < min_indent:
                break
            if indent > min_indent:
                raise YamlError(f"Unexpected indentation near: {content!r}")
            if ":" not in content:
                raise YamlError(f"Expected 'key: value' near: {content!r}")
            # split on first colon not inside quotes
            key, _, rest = _split_kv(content)
            key = _unquote_key(key)
            rest = rest.strip()
            pos += 1
            if rest == "":
                # nested block or empty
                if pos < len(lines) and lines[pos][0] > indent:
                    child = parse_block(lines[pos][0])
                    result[key] = child
                else:
                    result[key] = {}
            elif rest.startswith("{") and not REFERENCE_RE.match(rest):
                # inline flow mapping, e.g. { fontFamily: Inter, fontSize: 1rem }
                result[key] = _parse_flow(rest)
            else:
                result[key] = _parse_scalar(rest)
        return result

    if not lines:
        return {}
    base_indent = lines[0][0]
    data = parse_block(base_indent)
    if pos != len(lines):
        raise YamlError("Could not fully parse YAML (inconsistent indentation).")
    return data


def _split_kv(content: str):
    in_s = in_d = False
    for i, c in enumerate(content):
        if c == "'" and not in_d:
            in_s = not in_s
        elif c == '"' and not in_s:
            in_d = not in_d
        elif c == ":" and not in_s and not in_d:
            if i + 1 >= len(content) or content[i + 1] in " \t" or content[i + 1] == "":
                return content[:i], ":", content[i + 1:]
            if i + 1 >= len(content):
                return content[:i], ":", ""
    # whole line is a key with trailing colon
    if content.endswith(":"):
        return content[:-1], ":", ""
    raise YamlError(f"Expected 'key: value' near: {content!r}")


def _split_top(s, sep):
    """Split on `sep` at brace-depth 0, respecting quotes."""
    parts, buf, depth, in_s, in_d = [], [], 0, False, False
    for c in s:
        if c == "'" and not in_d:
            in_s = not in_s
        elif c == '"' and not in_s:
            in_d = not in_d
        if c in "{[" and not in_s and not in_d:
            depth += 1
        elif c in "}]" and not in_s and not in_d:
            depth -= 1
        if c == sep and depth == 0 and not in_s and not in_d:
            parts.append("".join(buf)); buf = []
        else:
            buf.append(c)
    if buf:
        parts.append("".join(buf))
    return parts


def _parse_flow(s):
    """Parse an inline flow mapping like '{ a: 1, b: { c: 2 } }'."""
    s = s.strip()
    if not (s.startswith("{") and s.endswith("}")):
        raise YamlError(f"Malformed inline mapping: {s!r}")
    inner = s[1:-1].strip()
    d = {}
    if not inner:
        return d
    for part in _split_top(inner, ","):
        part = part.strip()
        if not part:
            continue
        kv = _split_top(part, ":")
        if len(kv) < 2:
            raise YamlError(f"Malformed inline entry: {part!r}")
        k = _unquote_key(kv[0])
        v = ":".join(kv[1:]).strip()
        if v.startswith("{") and not REFERENCE_RE.match(v):
            d[k] = _parse_flow(v)
        else:
            d[k] = _parse_scalar(v)
    return d


def _unquote_key(k: str) -> str:
    k = k.strip()
    if len(k) >= 2 and k[0] == k[-1] and k[0] in "\"'":
        return k[1:-1]
    return k


# ---------------------------------------------------------------------------
# Color parsing + WCAG contrast
# ---------------------------------------------------------------------------
NAMED_COLORS = {
    "aliceblue": "#f0f8ff", "antiquewhite": "#faebd7", "aqua": "#00ffff",
    "aquamarine": "#7fffd4", "azure": "#f0ffff", "beige": "#f5f5dc",
    "bisque": "#ffe4c4", "black": "#000000", "blanchedalmond": "#ffebcd",
    "blue": "#0000ff", "blueviolet": "#8a2be2", "brown": "#a52a2a",
    "burlywood": "#deb887", "cadetblue": "#5f9ea0", "chartreuse": "#7fff00",
    "chocolate": "#d2691e", "coral": "#ff7f50", "cornflowerblue": "#6495ed",
    "cornsilk": "#fff8dc", "crimson": "#dc143c", "cyan": "#00ffff",
    "darkblue": "#00008b", "darkcyan": "#008b8b", "darkgoldenrod": "#b8860b",
    "darkgray": "#a9a9a9", "darkgreen": "#006400", "darkgrey": "#a9a9a9",
    "darkkhaki": "#bdb76b", "darkmagenta": "#8b008b", "darkolivegreen": "#556b2f",
    "darkorange": "#ff8c00", "darkorchid": "#9932cc", "darkred": "#8b0000",
    "darksalmon": "#e9967a", "darkseagreen": "#8fbc8f", "darkslateblue": "#483d8b",
    "darkslategray": "#2f4f4f", "darkslategrey": "#2f4f4f", "darkturquoise": "#00ced1",
    "darkviolet": "#9400d3", "deeppink": "#ff1493", "deepskyblue": "#00bfff",
    "dimgray": "#696969", "dimgrey": "#696969", "dodgerblue": "#1e90ff",
    "firebrick": "#b22222", "floralwhite": "#fffaf0", "forestgreen": "#228b22",
    "fuchsia": "#ff00ff", "gainsboro": "#dcdcdc", "ghostwhite": "#f8f8ff",
    "gold": "#ffd700", "goldenrod": "#daa520", "gray": "#808080", "green": "#008000",
    "greenyellow": "#adff2f", "grey": "#808080", "honeydew": "#f0fff0",
    "hotpink": "#ff69b4", "indianred": "#cd5c5c", "indigo": "#4b0082",
    "ivory": "#fffff0", "khaki": "#f0e68c", "lavender": "#e6e6fa",
    "lavenderblush": "#fff0f5", "lawngreen": "#7cfc00", "lemonchiffon": "#fffacd",
    "lightblue": "#add8e6", "lightcoral": "#f08080", "lightcyan": "#e0ffff",
    "lightgoldenrodyellow": "#fafad2", "lightgray": "#d3d3d3", "lightgreen": "#90ee90",
    "lightgrey": "#d3d3d3", "lightpink": "#ffb6c1", "lightsalmon": "#ffa07a",
    "lightseagreen": "#20b2aa", "lightskyblue": "#87cefa", "lightslategray": "#778899",
    "lightslategrey": "#778899", "lightsteelblue": "#b0c4de", "lightyellow": "#ffffe0",
    "lime": "#00ff00", "limegreen": "#32cd32", "linen": "#faf0e6",
    "magenta": "#ff00ff", "maroon": "#800000", "mediumaquamarine": "#66cdaa",
    "mediumblue": "#0000cd", "mediumorchid": "#ba55d3", "mediumpurple": "#9370db",
    "mediumseagreen": "#3cb371", "mediumslateblue": "#7b68ee",
    "mediumspringgreen": "#00fa9a", "mediumturquoise": "#48d1cc",
    "mediumvioletred": "#c71585", "midnightblue": "#191970", "mintcream": "#f5fffa",
    "mistyrose": "#ffe4e1", "moccasin": "#ffe4b5", "navajowhite": "#ffdead",
    "navy": "#000080", "oldlace": "#fdf5e6", "olive": "#808000",
    "olivedrab": "#6b8e23", "orange": "#ffa500", "orangered": "#ff4500",
    "orchid": "#da70d6", "palegoldenrod": "#eee8aa", "palegreen": "#98fb98",
    "paleturquoise": "#afeeee", "palevioletred": "#db7093", "papayawhip": "#ffefd5",
    "peachpuff": "#ffdab9", "peru": "#cd853f", "pink": "#ffc0cb", "plum": "#dda0dd",
    "powderblue": "#b0e0e6", "purple": "#800080", "rebeccapurple": "#663399",
    "red": "#ff0000", "rosybrown": "#bc8f8f", "royalblue": "#4169e1",
    "saddlebrown": "#8b4513", "salmon": "#fa8072", "sandybrown": "#f4a460",
    "seagreen": "#2e8b57", "seashell": "#fff5ee", "sienna": "#a0522d",
    "silver": "#c0c0c0", "skyblue": "#87ceeb", "slateblue": "#6a5acd",
    "slategray": "#708090", "slategrey": "#708090", "snow": "#fffafa",
    "springgreen": "#00ff7f", "steelblue": "#4682b4", "tan": "#d2b48c",
    "teal": "#008080", "thistle": "#d8bfd8", "tomato": "#ff6347",
    "turquoise": "#40e0d0", "violet": "#ee82ee", "wheat": "#f5deb3",
    "white": "#ffffff", "whitesmoke": "#f5f5f5", "yellow": "#ffff00",
    "yellowgreen": "#9acd32", "transparent": "#00000000",
}


def _clamp(v, lo=0.0, hi=1.0):
    return max(lo, min(hi, v))


def _gamma_encode(c):
    c = _clamp(c)
    return 12.92 * c if c <= 0.0031308 else 1.055 * (c ** (1 / 2.4)) - 0.055


def _rgb255(r, g, b, a=1.0):
    r = int(round(max(0, min(255, r))))
    g = int(round(max(0, min(255, g))))
    b = int(round(max(0, min(255, b))))
    hexs = f"#{r:02x}{g:02x}{b:02x}"
    if a < 1:
        hexs += f"{int(round(_clamp(a) * 255)):02x}"
    lum = relative_luminance(r, g, b)
    res = {"type": "color", "hex": hexs, "r": r, "g": g, "b": b, "luminance": lum}
    if a < 1:
        res["a"] = a
    return res


def relative_luminance(r, g, b):
    def lin(c):
        s = c / 255.0
        return s / 12.92 if s <= 0.03928 else ((s + 0.055) / 1.055) ** 2.4
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)


def contrast_ratio(c1, c2):
    l1, l2 = c1["luminance"], c2["luminance"]
    hi, lo = max(l1, l2), min(l1, l2)
    return (hi + 0.05) / (lo + 0.05)


def _split_args(body):
    # split alpha on depth-0 '/'
    depth = 0
    slash = -1
    for i, c in enumerate(body):
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
        elif c == "/" and depth == 0:
            slash = i
            break
    alpha = None
    main = body
    if slash >= 0:
        main = body[:slash]
        alpha = body[slash + 1:].strip()
    parts, buf, depth = [], [], 0
    for c in main:
        if c == "(":
            depth += 1
            buf.append(c)
        elif c == ")":
            depth -= 1
            buf.append(c)
        elif c in ", \t" and depth == 0:
            if buf:
                parts.append("".join(buf))
                buf = []
        else:
            buf.append(c)
    if buf:
        parts.append("".join(buf))
    return parts, alpha


def _num(tok, scale=1.0):
    tok = tok.strip()
    if tok.endswith("%"):
        return float(tok[:-1]) / 100.0 * scale
    return float(tok)


def _hue_deg(tok):
    tok = tok.strip().lower()
    for unit, factor in (("deg", 1), ("grad", 0.9), ("rad", 180 / math.pi), ("turn", 360)):
        if tok.endswith(unit):
            return float(tok[:-len(unit)]) * factor
    return float(tok)


def _alpha(tok):
    if tok is None:
        return 1.0
    tok = tok.strip()
    if tok.endswith("%"):
        return _clamp(float(tok[:-1]) / 100.0)
    return _clamp(float(tok))


def _hsl_to_rgb(h, s, l):
    h = (h % 360) / 360.0
    if s == 0:
        v = l * 255
        return v, v, v
    def hue2rgb(p, q, t):
        if t < 0: t += 1
        if t > 1: t -= 1
        if t < 1/6: return p + (q - p) * 6 * t
        if t < 1/2: return q
        if t < 2/3: return p + (q - p) * (2/3 - t) * 6
        return p
    q = l * (1 + s) if l < 0.5 else l + s - l * s
    p = 2 * l - q
    r = hue2rgb(p, q, h + 1/3)
    g = hue2rgb(p, q, h)
    b = hue2rgb(p, q, h - 1/3)
    return r * 255, g * 255, b * 255


def _oklab_to_rgb(L, a, b):
    l_ = L + 0.3963377774 * a + 0.2158037573 * b
    m_ = L - 0.1055613458 * a - 0.0638541728 * b
    s_ = L - 0.0894841775 * a - 1.2914855480 * b
    l, m, s = l_ ** 3, m_ ** 3, s_ ** 3
    R = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s
    G = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s
    B = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    return _gamma_encode(R) * 255, _gamma_encode(G) * 255, _gamma_encode(B) * 255


def _lab_to_rgb(L, a, b):
    # D50 reference white, Bradford-adapted to D65 sRGB
    fy = (L + 16) / 116
    fx = fy + a / 500
    fz = fy - b / 200
    def f_inv(t):
        return t ** 3 if t ** 3 > 0.008856 else (t - 16 / 116) / 7.787
    xn, yn, zn = 0.96422, 1.0, 0.82521  # D50
    X = f_inv(fx) * xn
    Y = f_inv(fy) * yn
    Z = f_inv(fz) * zn
    # Bradford D50 -> D65
    Xd = 0.9555766 * X - 0.0230393 * Y + 0.0631636 * Z
    Yd = -0.0282895 * X + 1.0099416 * Y + 0.0210077 * Z
    Zd = 0.0122982 * X - 0.0204830 * Y + 1.3299098 * Z
    R = 3.2404542 * Xd - 1.5371385 * Yd - 0.4985314 * Zd
    G = -0.9692660 * Xd + 1.8760108 * Yd + 0.0415560 * Zd
    B = 0.0556434 * Xd - 0.2040259 * Yd + 1.0572252 * Zd
    return _gamma_encode(R) * 255, _gamma_encode(G) * 255, _gamma_encode(B) * 255


def parse_css_color(value):
    if not isinstance(value, str):
        return None
    s = value.strip().lower()
    # hex
    m = HEX_RE.match(s)
    if m:
        h = m.group(1)
        if len(h) in (3, 4):
            h = "".join(ch * 2 for ch in h)
        r = int(h[0:2], 16); g = int(h[2:4], 16); b = int(h[4:6], 16)
        a = int(h[6:8], 16) / 255.0 if len(h) == 8 else 1.0
        return _rgb255(r, g, b, a)
    # named
    if s in NAMED_COLORS:
        return parse_css_color(NAMED_COLORS[s])
    # functional
    fm = re.match(r"^([a-z-]{3,15})\((.*)\)$", s)
    if not fm:
        return None
    fn, body = fm.group(1), fm.group(2)
    try:
        parts, alpha_tok = _split_args(body)
        # 4th positional arg as alpha (rgb(r g b a))
        if alpha_tok is None and len(parts) == 4:
            alpha_tok = parts[3]
            parts = parts[:3]
        a = _alpha(alpha_tok)
        if fn in ("rgb", "rgba"):
            r = _num(parts[0], 255); g = _num(parts[1], 255); b = _num(parts[2], 255)
            return _rgb255(r, g, b, a)
        if fn in ("hsl", "hsla"):
            h = _hue_deg(parts[0]); sl = _num(parts[1]); l = _num(parts[2])
            r, g, b = _hsl_to_rgb(h, sl, l)
            return _rgb255(r, g, b, a)
        if fn == "hwb":
            h = _hue_deg(parts[0]); w = _num(parts[1]); bl = _num(parts[2])
            r, g, b = _hsl_to_rgb(h, 1.0, 0.5)
            def mix(c):
                c = c / 255.0
                return (c * (1 - w - bl) + w) * 255
            return _rgb255(mix(r), mix(g), mix(b), a)
        if fn in ("oklch", "lch"):
            L = _num(parts[0]); C = _num(parts[1]); H = _hue_deg(parts[2])
            if fn == "oklch":
                if "%" in parts[0]:
                    L = _num(parts[0])  # already 0..1
                aa = C * math.cos(math.radians(H)); bb = C * math.sin(math.radians(H))
                r, g, b = _oklab_to_rgb(L, aa, bb)
            else:  # lch
                aa = C * math.cos(math.radians(H)); bb = C * math.sin(math.radians(H))
                r, g, b = _lab_to_rgb(L, aa, bb)
            return _rgb255(r, g, b, a)
        if fn in ("oklab", "lab"):
            L = _num(parts[0]); aa = float(parts[1]); bb = float(parts[2])
            if fn == "oklab":
                r, g, b = _oklab_to_rgb(L, aa, bb)
            else:
                r, g, b = _lab_to_rgb(L, aa, bb)
            return _rgb255(r, g, b, a)
    except (ValueError, IndexError):
        return None
    return None  # color-mix and other rare forms: unsupported


# ---------------------------------------------------------------------------
# Dimension parsing
# ---------------------------------------------------------------------------
def parse_dimension(raw):
    if isinstance(raw, (int, float)):
        return {"type": "dimension", "value": float(raw), "unit": None, "raw": raw}
    if not isinstance(raw, str):
        return None
    m = DIMENSION_RE.match(raw.strip())
    if not m:
        return None
    return {"type": "dimension", "value": float(m.group(1)), "unit": m.group(2), "raw": raw}


def is_reference(v):
    return isinstance(v, str) and bool(REFERENCE_RE.match(v.strip()))


def ref_path(v):
    return v.strip()[1:-1]


# ---------------------------------------------------------------------------
# Document parser: front matter + fenced yaml + markdown sections
# ---------------------------------------------------------------------------
class ParseResult:
    def __init__(self):
        self.tokens = {}
        self.sections = []          # ordered list of H2 heading texts
        self.findings = []          # model/parse findings
        self.recoverable = True
        self.error = None


FENCE_RE = re.compile(r"^```(yaml|yml)\s*$", re.IGNORECASE)
H2_RE = re.compile(r"^## (.+)$")


def _extract_yaml_blocks(text):
    """Return list of (description, raw_yaml) blocks: front matter + fenced yaml."""
    blocks = []
    lines = text.splitlines()
    i = 0
    # front matter
    if lines and lines[0].strip() == "---":
        j = 1
        buf = []
        while j < len(lines) and lines[j].strip() != "---":
            buf.append(lines[j]); j += 1
        if j < len(lines):
            blocks.append(("frontmatter", "\n".join(buf)))
            i = j + 1
    # fenced yaml blocks
    n = len(lines)
    fenced_idx = 0
    while i < n:
        if FENCE_RE.match(lines[i].strip()):
            buf = []
            i += 1
            while i < n and lines[i].strip() != "```":
                buf.append(lines[i]); i += 1
            blocks.append((f"code block {fenced_idx + 1}", "\n".join(buf)))
            fenced_idx += 1
        i += 1
    return blocks


def parse_document(text):
    pr = ParseResult()
    # sections (H2)
    for line in text.splitlines():
        m = H2_RE.match(line.rstrip())
        if m:
            pr.sections.append(m.group(1).strip())

    blocks = _extract_yaml_blocks(text)
    if not blocks:
        pr.findings.append({"severity": "warning", "path": None,
                            "message": "No YAML content found. Expected frontmatter (---) "
                            "or fenced yaml code blocks."})
        pr.recoverable = True
        return pr

    merged = {}
    key_origin = {}
    for desc, raw in blocks:
        try:
            data = parse_yaml(raw)
        except YamlError as e:
            pr.findings.append({"severity": "warning", "path": None,
                                "message": f"YAML parse error in {desc}: {e}"})
            continue
        if not isinstance(data, dict):
            continue
        for k, v in data.items():
            if k in key_origin:
                # Upstream treats duplicate top-level keys as a recoverable warning,
                # emits only this finding, and yields an empty design system.
                pr.tokens = {}
                pr.findings = [{"severity": "warning", "path": None,
                                "message": f"Section '{k}' is defined in both "
                                f"{key_origin[k]} and {desc}."}]
                pr.recoverable = True
                return pr
            key_origin[k] = desc
            merged[k] = v
    pr.tokens = merged
    return pr


# ---------------------------------------------------------------------------
# Model: build symbol table, resolve references, parse components
# ---------------------------------------------------------------------------
class Model:
    def __init__(self):
        self.colors = {}        # name -> resolved color (or {"ref": path} unresolved)
        self.typography = {}    # name -> resolved typography dict
        self.rounded = {}       # name -> dimension
        self.spacing = {}       # name -> dimension or number
        self.components = {}    # name -> {prop: resolved}
        self.unresolved_refs = {}   # comp name -> [refs]
        self.unknown_props = {}     # comp name -> [prop names]
        self.unknown_keys = []      # top-level unknown keys
        self.unknown_key_values = {}
        self.sections = []
        self.findings = []          # model-phase errors
        self.symbol_table = {}      # "colors.primary" -> resolved


def build_model(pr: ParseResult) -> Model:
    m = Model()
    m.sections = pr.sections
    m.findings = [f for f in pr.findings if f.get("severity") == "error"]
    info_warn = [f for f in pr.findings if f.get("severity") != "error"]
    tokens = pr.tokens

    # colors
    colors = tokens.get("colors") or {}
    if isinstance(colors, dict):
        for name, raw in colors.items():
            if is_reference(raw):
                m.colors[name] = {"ref": ref_path(raw)}
            else:
                parsed = parse_css_color(raw) if isinstance(raw, str) else None
                if parsed is None:
                    m.findings.append({"severity": "error", "path": f"colors.{name}",
                        "message": f"'{raw}' is not a valid color. Expected a CSS color "
                        "value (e.g., #ffffff, rgb(0 0 0), oklch(0.5 0.2 240))."})
                    m.colors[name] = None
                else:
                    m.colors[name] = parsed
                    m.symbol_table[f"colors.{name}"] = parsed

    # typography
    typo = tokens.get("typography") or {}
    if isinstance(typo, dict):
        for name, obj in typo.items():
            if not isinstance(obj, dict):
                continue
            t = {"type": "typography"}
            for prop, val in obj.items():
                if prop == "fontFamily":
                    if isinstance(val, str) and parse_css_color(val) and HEX_RE.match(val.strip().lower()):
                        m.findings.append({"severity": "error",
                            "path": f"typography.{name}.fontFamily",
                            "message": f"fontFamily '{val}' looks like a color, not a font."})
                    t["fontFamily"] = val
                elif prop in ("fontSize", "letterSpacing"):
                    d = parse_dimension(val)
                    if d is None:
                        m.findings.append({"severity": "error",
                            "path": f"typography.{name}.{prop}",
                            "message": f"'{val}' is not a valid dimension."})
                    elif d.get("unit") and d["unit"] not in STANDARD_UNITS:
                        m.findings.append({"severity": "error",
                            "path": f"typography.{name}.{prop}",
                            "message": f"'{val}' has an invalid unit '{d['unit']}'. "
                            "Only px, rem, and em are allowed."})
                    t[prop] = d if d else val
                elif prop == "fontWeight":
                    try:
                        t["fontWeight"] = int(val) if not isinstance(val, bool) else val
                        float(val)
                    except (ValueError, TypeError):
                        m.findings.append({"severity": "error",
                            "path": f"typography.{name}.fontWeight",
                            "message": f"'{val}' is not a valid font weight. Expected a number."})
                        t["fontWeight"] = val
                elif prop == "lineHeight":
                    if isinstance(val, (int, float)) and not isinstance(val, bool):
                        t["lineHeight"] = {"type": "dimension", "value": float(val), "unit": None, "raw": val}
                    else:
                        t["lineHeight"] = parse_dimension(val) or val
                else:
                    t[prop] = val
            m.typography[name] = t
            m.symbol_table[f"typography.{name}"] = t

    # rounded
    rounded = tokens.get("rounded") or {}
    if isinstance(rounded, dict):
        for name, val in rounded.items():
            if is_reference(val):
                m.rounded[name] = {"ref": ref_path(val)}
                continue
            d = parse_dimension(val)
            if d is None:
                m.findings.append({"severity": "error", "path": f"rounded.{name}",
                    "message": f"'{val}' is not a valid dimension."})
            elif d.get("unit") and d["unit"] not in STANDARD_UNITS:
                m.findings.append({"severity": "error", "path": f"rounded.{name}",
                    "message": f"'{val}' has an invalid unit '{d['unit']}'. "
                    "Only px, rem, and em are allowed."})
            m.rounded[name] = d if d else val
            if d:
                m.symbol_table[f"rounded.{name}"] = d

    # spacing
    spacing = tokens.get("spacing") or {}
    if isinstance(spacing, dict):
        for name, val in spacing.items():
            if is_reference(val):
                m.spacing[name] = {"ref": ref_path(val)}
                continue
            if isinstance(val, (int, float)) and not isinstance(val, bool):
                m.spacing[name] = {"type": "dimension", "value": float(val), "unit": None, "raw": val}
            else:
                d = parse_dimension(val)
                m.spacing[name] = d if d else val
            m.symbol_table[f"spacing.{name}"] = m.spacing[name]

    # resolve chained refs for colors/rounded/spacing
    for group in (m.colors, m.rounded, m.spacing):
        for name, val in list(group.items()):
            if isinstance(val, dict) and "ref" in val:
                resolved = _resolve_ref(m.symbol_table, val["ref"], set(), 0)
                if resolved is not None:
                    group[name] = resolved
                    # register family path
                    # (kept as resolved; orphan logic handles families)

    # components
    comps = tokens.get("components") or {}
    if isinstance(comps, dict):
        for cname, props in comps.items():
            if not isinstance(props, dict):
                continue
            resolved_props = {}
            for prop, val in props.items():
                if prop not in VALID_COMPONENT_SUB_TOKENS:
                    m.unknown_props.setdefault(cname, []).append(prop)
                if is_reference(val):
                    rp = ref_path(val)
                    resolved = _resolve_ref(m.symbol_table, rp, set(), 0)
                    if resolved is None:
                        m.unresolved_refs.setdefault(cname, []).append("{" + rp + "}")
                        resolved_props[prop] = val
                    else:
                        resolved_props[prop] = resolved
                elif isinstance(val, (int, float, bool)):
                    resolved_props[prop] = val
                elif isinstance(val, str) and parse_css_color(val) and prop in ("backgroundColor", "textColor"):
                    resolved_props[prop] = parse_css_color(val)
                elif isinstance(val, str) and parse_dimension(val):
                    resolved_props[prop] = parse_dimension(val)
                else:
                    resolved_props[prop] = val
            m.components[cname] = resolved_props

    # unknown top-level keys
    for k, v in tokens.items():
        if k not in SCHEMA_KEYS:
            m.unknown_keys.append(k)
            m.unknown_key_values[k] = v

    # nesting depth check
    if _max_depth(tokens) > MAX_TOKEN_NESTING_DEPTH:
        m.findings.append({"severity": "error", "path": None,
            "message": f"Token nesting depth exceeds maximum allowed depth of "
            f"{MAX_TOKEN_NESTING_DEPTH}."})

    m._info_warn = info_warn
    return m


def _resolve_ref(table, path, visited, depth):
    if depth > MAX_REFERENCE_DEPTH:
        return None
    if path in visited:
        return None
    visited.add(path)
    val = table.get(path)
    if val is None:
        return None
    if isinstance(val, dict) and "ref" in val:
        return _resolve_ref(table, val["ref"], visited, depth + 1)
    return val


def _max_depth(obj, d=0):
    if isinstance(obj, dict):
        if not obj:
            return d
        return max(_max_depth(v, d + 1) for v in obj.values())
    return d


# ---------------------------------------------------------------------------
# Linting rules (10)
# ---------------------------------------------------------------------------
def _resolve_alias(name):
    return SECTION_ALIASES.get(name.strip().lower(), name)


def _color_family(name):
    n = name
    n = re.sub(r"^on-", "", n)
    n = re.sub(r"^inverse-", "", n)
    n = re.sub(r"^on-", "", n)
    n = re.sub(r"-container.*$", "", n)
    n = re.sub(r"-fixed.*$", "", n)
    n = re.sub(r"-(dim|bright|tint|variant)$", "", n)
    return n


def _levenshtein(a, b):
    a, b = a.lower(), b.lower()
    if a == b:
        return 0
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        for j, cb in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (ca != cb)))
        prev = cur
    return prev[-1]


def _pluralize(n, word):
    return f"{n} {word}{'' if n == 1 else 's'}"


def lint_rules(m: Model):
    findings = []

    # 1. broken-ref (error for unresolved refs; warning for unknown sub-tokens)
    for cname in m.components:
        for ref in m.unresolved_refs.get(cname, []):
            findings.append({"severity": "error", "path": f"components.{cname}",
                "message": f"Reference {ref} does not resolve to any defined token."})
        for prop in m.unknown_props.get(cname, []):
            valid = ", ".join(VALID_COMPONENT_SUB_TOKENS)
            findings.append({"severity": "warning", "path": f"components.{cname}.{prop}",
                "message": f"'{prop}' is not a recognized component sub-token. "
                f"Valid sub-tokens: {valid}."})

    # 2. missing-primary
    real_colors = {k: v for k, v in m.colors.items() if isinstance(v, dict) and v.get("type") == "color"}
    if len(real_colors) > 0 and "primary" not in m.colors:
        findings.append({"severity": "warning", "path": "colors",
            "message": "No 'primary' color defined. The agent will auto-generate key "
            "colors, reducing your control over the palette."})

    # 3. contrast-ratio
    for cname, props in m.components.items():
        bg = props.get("backgroundColor")
        tx = props.get("textColor")
        if isinstance(bg, dict) and bg.get("type") == "color" and isinstance(tx, dict) and tx.get("type") == "color":
            ratio = contrast_ratio(bg, tx)
            if ratio < WCAG_AA_MINIMUM:
                findings.append({"severity": "warning", "path": f"components.{cname}",
                    "message": f"textColor ({tx['hex']}) on backgroundColor ({bg['hex']}) "
                    f"has contrast ratio {ratio:.2f}:1, below WCAG AA minimum of 4.5:1."})

    # 4. orphaned-tokens (only if components exist)
    if len(m.components) > 0:
        referenced_families = set()
        referenced_color_names = set()
        for props in m.components.values():
            for val in props.values():
                if isinstance(val, dict) and val.get("type") == "color":
                    # find which color name(s) match this resolved value
                    for cn, cv in real_colors.items():
                        if cv is val or (isinstance(cv, dict) and cv.get("hex") == val.get("hex")):
                            referenced_color_names.add(cn)
                            referenced_families.add(_color_family(cn))
        for name in real_colors:
            fam = _color_family(name)
            if (name not in referenced_color_names and fam not in referenced_families
                    and fam not in MD3_STANDARD_FAMILIES):
                findings.append({"severity": "warning", "path": f"colors.{name}",
                    "message": f"'{name}' is defined but never referenced by any component."})

    # 5. token-summary (info)
    parts = []
    if len(m.colors) > 0:
        parts.append(_pluralize(len(m.colors), "color"))
    if len(m.typography) > 0:
        parts.append(_pluralize(len(m.typography), "typography scale"))
    if len(m.rounded) > 0:
        parts.append(_pluralize(len(m.rounded), "rounding level"))
    if len(m.spacing) > 0:
        parts.append(_pluralize(len(m.spacing), "spacing token"))
    if len(m.components) > 0:
        parts.append(_pluralize(len(m.components), "component"))
    if parts:
        findings.append({"severity": "info", "path": None,
            "message": f"Design system defines {', '.join(parts)}."})

    # 6. missing-sections (info) — only when colors exist
    if len(m.colors) > 0:
        if len(m.spacing) == 0:
            findings.append({"severity": "info", "path": "spacing",
                "message": "No 'spacing' section defined. Layout spacing will fall back "
                "to agent defaults."})
        if len(m.rounded) == 0:
            findings.append({"severity": "info", "path": "rounded",
                "message": "No 'rounded' section defined. Corner rounding will fall back "
                "to agent defaults."})

    # 7. missing-typography (warning)
    if len(m.typography) == 0 and len(m.colors) > 0:
        findings.append({"severity": "warning", "path": "typography",
            "message": "No typography tokens defined. Agents will use default font "
            "choices, reducing your control over the design system's typographic identity."})

    # 8. section-order (warning)
    known = [(_resolve_alias(s)) for s in m.sections]
    known = [s for s in known if s in CANONICAL_ORDER]
    for i in range(len(known) - 1):
        cur_idx = CANONICAL_ORDER.index(known[i])
        nxt_idx = CANONICAL_ORDER.index(known[i + 1])
        if cur_idx > nxt_idx:
            findings.append({"severity": "warning", "path": None,
                "message": f"Section '{known[i]}' appears before '{known[i + 1]}', which "
                f"is out of order. Expected order: {', '.join(CANONICAL_ORDER)}"})
            break

    # 9. unknown-key (warning) — typo of schema key
    typo_flagged = set()
    for key in m.unknown_keys:
        best, best_d = None, 99
        for sk in SCHEMA_KEYS:
            d = _levenshtein(key, sk)
            if d < best_d:
                best_d, best = d, sk
        if best is not None and 0 < best_d <= MAX_TYPO_DISTANCE:
            typo_flagged.add(key)
            findings.append({"severity": "warning", "path": key,
                "message": f'Unknown key "{key}" — did you mean "{best}"?'})

    return findings


# ---------------------------------------------------------------------------
# lint() entrypoint
# ---------------------------------------------------------------------------
def lint(text):
    pr = parse_document(text)
    if not pr.recoverable:
        m = build_model(pr)
        findings = list(m.findings)
        summary = _summarize(findings)
        return {"findings": findings, "summary": summary, "sections": pr.sections}
    m = build_model(pr)
    findings = list(m.findings) + list(getattr(m, "_info_warn", [])) + lint_rules(m)
    summary = _summarize(findings)
    return {"findings": findings, "summary": summary, "sections": pr.sections,
            "_model": m}


def _summarize(findings):
    s = {"errors": 0, "warnings": 0, "infos": 0}
    for f in findings:
        sev = f.get("severity")
        if sev == "error":
            s["errors"] += 1
        elif sev == "warning":
            s["warnings"] += 1
        elif sev == "info":
            s["infos"] += 1
    return s


def _clean_findings(findings):
    """Drop null 'path' keys to match upstream JSON shape."""
    out = []
    for f in findings:
        g = {k: v for k, v in f.items() if not (k == "path" and v is None)}
        out.append(g)
    return out


# ---------------------------------------------------------------------------
# diff()
# ---------------------------------------------------------------------------
def _canon(v):
    return json.dumps(v, sort_keys=True, default=str)


def _diff_maps(before, after):
    added = [k for k in after if k not in before]
    removed = [k for k in before if k not in after]
    modified = [k for k in after if k in before and _canon(before[k]) != _canon(after[k])]
    return {"added": added, "removed": removed, "modified": modified}


def diff(before_text, after_text):
    rb = lint(before_text)
    ra = lint(after_text)
    mb = rb.get("_model")
    ma = ra.get("_model")
    cats = {}
    if mb and ma:
        cats["colors"] = _diff_maps(mb.colors, ma.colors)
        cats["typography"] = _diff_maps(mb.typography, ma.typography)
        cats["rounded"] = _diff_maps(mb.rounded, ma.rounded)
        cats["spacing"] = _diff_maps(mb.spacing, ma.spacing)
        cats["components"] = _diff_maps(mb.components, ma.components)
    sb, sa = rb["summary"], ra["summary"]
    regression = sa["errors"] > sb["errors"] or sa["warnings"] > sb["warnings"]
    return {
        "tokens": cats,
        "findings": {
            "before": sb, "after": sa,
            "delta": {"errors": sa["errors"] - sb["errors"],
                      "warnings": sa["warnings"] - sb["warnings"]},
        },
        "regression": regression,
    }


# ---------------------------------------------------------------------------
# Exporters
# ---------------------------------------------------------------------------
def _dim_str(d):
    if isinstance(d, dict) and d.get("type") == "dimension":
        if d.get("unit"):
            return f"{_numfmt(d['value'])}{d['unit']}"
        return _numfmt(d["value"])
    return str(d)


def _numfmt(v):
    if isinstance(v, float) and v.is_integer():
        return str(int(v))
    return str(v)


def export_json_tailwind(m: Model):
    colors = {k: v["hex"] for k, v in m.colors.items() if isinstance(v, dict) and v.get("type") == "color"}
    font_family = {}
    font_size = {}
    for name, t in m.typography.items():
        if "fontFamily" in t:
            font_family[name] = [t["fontFamily"]]
        if "fontSize" in t:
            meta = {}
            if "lineHeight" in t:
                meta["lineHeight"] = _dim_str(t["lineHeight"]) if isinstance(t["lineHeight"], dict) else str(t["lineHeight"])
            if "letterSpacing" in t:
                meta["letterSpacing"] = _dim_str(t["letterSpacing"])
            if "fontWeight" in t:
                meta["fontWeight"] = str(t["fontWeight"])
            size = _dim_str(t["fontSize"])
            font_size[name] = [size, meta] if meta else [size]
    border_radius = {k: _dim_str(v) for k, v in m.rounded.items() if isinstance(v, dict)}
    spacing = {k: _dim_str(v) for k, v in m.spacing.items() if isinstance(v, dict)}
    return {"theme": {"extend": {
        "colors": colors, "fontFamily": font_family, "fontSize": font_size,
        "borderRadius": border_radius, "spacing": spacing}}}


CSS_IDENT_RE = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9-]*$")


def export_css_tailwind(m: Model):
    namespaces = []  # (prefix, name, value) in fixed order
    def check(name):
        if not CSS_IDENT_RE.match(name):
            raise ValueError(f'Token name "{name}" is not a valid CSS identifier for '
                             "Tailwind v4 export (must match ^[a-zA-Z0-9][a-zA-Z0-9-]*$).")
    for name, v in m.colors.items():
        if isinstance(v, dict) and v.get("type") == "color":
            check(name); namespaces.append(("--color-", name, v["hex"]))
    for name, t in m.typography.items():
        if "fontFamily" in t:
            check(name)
            val = '"' + str(t["fontFamily"]).replace("\\", "\\\\").replace('"', '\\"') + '"'
            namespaces.append(("--font-", name, val))
    for name, t in m.typography.items():
        if "fontSize" in t:
            check(name); namespaces.append(("--text-", name, _dim_str(t["fontSize"])))
    for name, t in m.typography.items():
        if "lineHeight" in t:
            check(name)
            lv = _dim_str(t["lineHeight"]) if isinstance(t["lineHeight"], dict) else str(t["lineHeight"])
            namespaces.append(("--leading-", name, lv))
    for name, t in m.typography.items():
        if "letterSpacing" in t:
            check(name); namespaces.append(("--tracking-", name, _dim_str(t["letterSpacing"])))
    for name, t in m.typography.items():
        if "fontWeight" in t:
            check(name); namespaces.append(("--font-weight-", name, str(t["fontWeight"])))
    for name, v in m.rounded.items():
        if isinstance(v, dict):
            check(name); namespaces.append(("--radius-", name, _dim_str(v)))
    for name, v in m.spacing.items():
        if isinstance(v, dict):
            check(name); namespaces.append(("--spacing-", name, _dim_str(v)))
    if not namespaces:
        return "@theme {\n}\n"
    lines = ["@theme {"]
    for prefix, name, val in namespaces:
        lines.append(f"  {prefix}{name}: {val};")
    lines.append("}")
    return "\n".join(lines) + "\n"


def _jn(x):
    """Match JS JSON.stringify numeric formatting: integral floats become ints."""
    if isinstance(x, float) and x.is_integer():
        return int(x)
    return x


def export_dtcg(m: Model, name=None, description=None):
    out = {"$schema": "https://www.designtokens.org/schemas/2025.10/format.json"}
    if name or description:
        out["$description"] = description or name
    real_colors = {k: v for k, v in m.colors.items() if isinstance(v, dict) and v.get("type") == "color"}
    if real_colors:
        grp = {"$type": "color"}
        for k, v in real_colors.items():
            grp[k] = {"$value": {"colorSpace": "srgb",
                                 "components": [_jn(round(v["r"] / 255, 3)), _jn(round(v["g"] / 255, 3)),
                                                _jn(round(v["b"] / 255, 3))],
                                 "hex": v["hex"]}}
        out["color"] = grp
    if m.spacing:
        grp = {"$type": "dimension"}
        for k, v in m.spacing.items():
            if isinstance(v, dict):
                grp[k] = {"$value": {"value": _jn(v["value"]), "unit": v.get("unit")}}
        out["spacing"] = grp
    if m.rounded:
        grp = {"$type": "dimension"}
        for k, v in m.rounded.items():
            if isinstance(v, dict):
                grp[k] = {"$value": {"value": _jn(v["value"]), "unit": v.get("unit")}}
        out["rounded"] = grp
    if m.typography:
        grp = {}
        for k, t in m.typography.items():
            val = {}
            if "fontFamily" in t:
                val["fontFamily"] = t["fontFamily"]
            if "fontSize" in t and isinstance(t["fontSize"], dict):
                val["fontSize"] = {"value": _jn(t["fontSize"]["value"]), "unit": t["fontSize"].get("unit")}
            if "fontWeight" in t:
                val["fontWeight"] = _jn(t["fontWeight"])
            if "letterSpacing" in t and isinstance(t["letterSpacing"], dict):
                val["letterSpacing"] = {"value": _jn(t["letterSpacing"]["value"]), "unit": t["letterSpacing"].get("unit")}
            if "lineHeight" in t and isinstance(t["lineHeight"], dict):
                val["lineHeight"] = _jn(t["lineHeight"]["value"])
            grp[k] = {"$type": "typography", "$value": val}
        out["typography"] = grp
    return out


# ---------------------------------------------------------------------------
# spec text + rules table
# ---------------------------------------------------------------------------
RULES_TABLE = [
    ("broken-ref", "error", "Token references that don't resolve to any defined token"),
    ("missing-primary", "warning", "Colors defined but no 'primary' color exists"),
    ("contrast-ratio", "warning", "Component bg/text pairs below WCAG AA (4.5:1)"),
    ("orphaned-tokens", "warning", "Color tokens defined but never referenced by a component"),
    ("token-summary", "info", "Summary of how many tokens exist in each section"),
    ("missing-sections", "info", "Optional sections (spacing, rounded) absent"),
    ("missing-typography", "warning", "Colors defined but no typography tokens"),
    ("section-order", "warning", "Sections out of the canonical order"),
    ("unknown-key", "warning", "Top-level YAML key looks like a typo of a schema key"),
]

SPEC_MD = """# DESIGN.md Specification (v{version})

A DESIGN.md file has two layers:
1. **YAML front matter** — machine-readable design tokens, delimited by `---` fences.
2. **Markdown body** — human-readable rationale in `##` sections.

## Token Schema
```
version: <string>      # optional, current: "alpha"
name: <string>
description: <string>  # optional
colors:      {{ <token-name>: <Color> }}
typography:  {{ <token-name>: <Typography> }}
rounded:     {{ <scale-level>: <Dimension> }}
spacing:     {{ <scale-level>: <Dimension | number> }}
components:  {{ <component-name>: {{ <token-name>: <string | token reference> }} }}
```

## Token Types
- Color: any CSS color (hex, rgb(), hsl(), oklch(), named, ...).
- Dimension: number + unit; standard units px / em / rem.
- Token Reference: {{path.to.token}}, e.g. {{colors.primary}}.
- Typography: object with fontFamily, fontSize, fontWeight, lineHeight,
  letterSpacing, fontFeature, fontVariation.

## Section Order (## headings; omit freely, but order matters)
{order}

## Component sub-tokens
Valid: {subtokens}

## Consumer Behavior
- Unknown section heading: preserve, do not error.
- Unknown color/typography token name: accept if valid.
- Unknown component property: accept with warning.
- Duplicate top-level YAML key across blocks: error; reject the file.
""".format(
    version=SPEC_VERSION,
    order="\n".join(f"{i+1}. {s}" for i, s in enumerate(CANONICAL_ORDER)),
    subtokens=", ".join(VALID_COMPONENT_SUB_TOKENS),
)


def spec_text(rules=False, rules_only=False, fmt="markdown"):
    if fmt == "json":
        payload = {"version": SPEC_VERSION}
        if not rules_only:
            payload.update({
                "schemaKeys": SCHEMA_KEYS,
                "canonicalOrder": CANONICAL_ORDER,
                "sectionAliases": SECTION_ALIASES,
                "componentSubTokens": VALID_COMPONENT_SUB_TOKENS,
                "standardUnits": sorted(STANDARD_UNITS),
            })
        if rules or rules_only:
            payload["rules"] = [{"rule": r, "severity": s, "checks": d} for r, s, d in RULES_TABLE]
        return json.dumps(payload, indent=2)
    out = []
    if not rules_only:
        out.append(SPEC_MD)
    if rules or rules_only:
        out.append("## Linting Rules\n")
        out.append("| Rule | Severity | What it checks |")
        out.append("| --- | --- | --- |")
        for r, s, d in RULES_TABLE:
            out.append(f"| `{r}` | {s} | {d} |")
    return "\n".join(out)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------
def _read(path):
    if path == "-":
        return sys.stdin.read()
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


def _public_findings(result):
    return {"findings": _clean_findings(result["findings"]), "summary": result["summary"]}


def cmd_lint(args):
    result = lint(_read(args.file))
    print(json.dumps(_public_findings(result), indent=2))
    return 1 if result["summary"]["errors"] > 0 else 0


def cmd_diff(args):
    result = diff(_read(args.before), _read(args.after))
    print(json.dumps(result, indent=2))
    return 1 if result["regression"] else 0


def cmd_export(args):
    text = _read(args.file)
    result = lint(text)
    if result["summary"]["errors"] > 0:
        sys.stderr.write("Refusing to export: file has errors. Run lint first.\n")
        return 1
    m = result["_model"]
    pr_tokens = parse_document(text).tokens
    fmt = args.format
    try:
        if fmt in ("json-tailwind", "tailwind"):
            print(json.dumps(export_json_tailwind(m), indent=2))
        elif fmt == "css-tailwind":
            print(export_css_tailwind(m))
        elif fmt == "dtcg":
            print(json.dumps(export_dtcg(m, pr_tokens.get("name"), pr_tokens.get("description")), indent=2))
        else:
            sys.stderr.write(f"Unknown format: {fmt}\n")
            return 1
    except ValueError as e:
        sys.stderr.write(str(e) + "\n")
        return 1
    return 0


def cmd_spec(args):
    print(spec_text(rules=args.rules, rules_only=args.rules_only, fmt=args.format))
    return 0


def cmd_selftest(args):
    import selftest as st  # type: ignore
    return st.run()


def main(argv=None):
    p = argparse.ArgumentParser(prog="dmd", description="Self-contained DESIGN.md toolkit")
    sub = p.add_subparsers(dest="cmd", required=True)

    sp = sub.add_parser("lint"); sp.add_argument("file"); sp.set_defaults(func=cmd_lint)
    sp = sub.add_parser("diff"); sp.add_argument("before"); sp.add_argument("after"); sp.set_defaults(func=cmd_diff)
    sp = sub.add_parser("export")
    sp.add_argument("file")
    sp.add_argument("--format", required=True,
                    choices=["json-tailwind", "css-tailwind", "tailwind", "dtcg"])
    sp.set_defaults(func=cmd_export)
    sp = sub.add_parser("spec")
    sp.add_argument("--rules", action="store_true")
    sp.add_argument("--rules-only", action="store_true")
    sp.add_argument("--format", choices=["markdown", "json"], default="markdown")
    sp.set_defaults(func=cmd_spec)
    sp = sub.add_parser("selftest"); sp.set_defaults(func=cmd_selftest)

    args = p.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
