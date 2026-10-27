# Security & safety notes

This skill processes files and renders HTML, so it follows defensive defaults.
Keep these properties intact when modifying the scripts.

## Scripts are offline and dependency-free
- `dmd.py`, `scaffold.py`, `preview.py`, `catalog.py` use only the Python
  standard library and perform **no network I/O**. Fetching catalog files is
  delegated to the `web_fetch` tool in the skill workflow, never to the scripts.
- The YAML reader in `dmd.py` is a *restricted* indentation parser for the
  DESIGN.md token subset. It executes no code and resolves no YAML tags, so it
  is not exposed to the unsafe-load / arbitrary-object-construction class of
  YAML vulnerabilities.

## Project scanning is read-only and bounded (`scaffold.py scan`)
- Walks the target directory only; **does not follow symlinks** (`os.walk
  followlinks=False`, and per-file `islink` skip), so it can't escape the root.
- Skips vendored/build directories (`node_modules`, `.git`, `dist`, `build`,
  `.next`, `venv`, …) and dotfolders.
- Only reads known text extensions, skips files larger than 512 KB, caps the
  number of files, and decodes with `errors="ignore"`. It never writes.

## HTML preview is injection-hardened (`preview.py`)
- Every value taken from the document is HTML-escaped (`html.escape(..., quote=True)`)
  before display.
- Values that go into a `style` attribute (colors, dimensions, font names) are
  validated against strict allow-list regexes first. A value that doesn't match
  (e.g. `1px;}</style><script>…`) is dropped rather than injected — the element
  falls back to a safe default.
- The generated file declares a strict `Content-Security-Policy`
  (`default-src 'none'; style-src 'unsafe-inline'; img-src data:;`), contains
  **no JavaScript**, and loads **no remote assets**. It is fully self-contained
  and safe to open locally.

## Output safety
- `scaffold.py` always quotes color hexes and references in emitted YAML (a bare
  `#...` would otherwise be a YAML comment), and validates the result with the
  linter before reporting success.
- The skill writes only to paths the user chose; it does not modify files
  outside the working folder.

## Not a place for secrets
DESIGN.md files are meant to be committed and shared. Never put API keys,
tokens, or other secrets in a DESIGN.md or in a scan spec.
