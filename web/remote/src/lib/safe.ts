// ─────────────────────────────────────────────────────────────────────────────
// safe.ts
//
// Tiny coercion helpers used by every UI module so a missing wire field
// renders as an em-dash instead of literal `undefined` / `null` / `NaN`.
//
// The rules are intentionally simple:
//   • Anything nullish or whitespace-only becomes the supplied placeholder.
//   • Numbers that aren't finite become the supplied placeholder.
//   • Times that fail to parse render as `—`.
// ─────────────────────────────────────────────────────────────────────────────

export const EM_DASH = "—";

/** Returns the text, trimmed; falls back to `placeholder` for nullish input. */
export function safeText(value: unknown, placeholder: string = EM_DASH): string {
	if (value === null || value === undefined) return placeholder;
	const text = String(value);
	if (text.length === 0) return placeholder;
	if (text === "undefined" || text === "null" || text === "NaN") return placeholder;
	return text;
}

/** Returns a number formatted as string; falls back to `placeholder` when not finite. */
export function safeNum(value: unknown, placeholder: string = "0"): string {
	if (value === null || value === undefined) return placeholder;
	const n = typeof value === "number" ? value : Number(value);
	if (!Number.isFinite(n)) return placeholder;
	return String(n);
}

/** Returns the absolute integer; falls back to `fallback` when not finite. */
export function intOr(value: unknown, fallback: number): number {
	if (value === null || value === undefined) return fallback;
	const n = typeof value === "number" ? value : Number(value);
	if (!Number.isFinite(n)) return fallback;
	return Math.trunc(n);
}

/** Returns a hex colour; falls back to `fallback` when blank / not hex. */
export function safeColor(value: unknown, fallback: string): string {
	if (typeof value !== "string") return fallback;
	const v = value.trim();
	if (v.length === 0) return fallback;
	return v;
}

/** Formats an ISO 8601 timestamp into the user's locale time; placeholder on failure. */
export function safeTime(iso: unknown, placeholder: string = EM_DASH): string {
	if (typeof iso !== "string" || iso.length === 0) return placeholder;
	const d = new Date(iso);
	if (Number.isNaN(d.getTime())) return placeholder;
	return d.toLocaleTimeString();
}

/** Formats `m:ss` (or `mm:ss`) from a total-seconds value. */
export function safeMinSec(totalSeconds: unknown, placeholder: string = "0:00"): string {
	const n = typeof totalSeconds === "number" ? totalSeconds : Number(totalSeconds);
	if (!Number.isFinite(n) || n < 0) return placeholder;
	const total = Math.trunc(n);
	const m = Math.trunc(total / 60);
	const s = total % 60;
	return `${m}:${String(s).padStart(2, "0")}`;
}
