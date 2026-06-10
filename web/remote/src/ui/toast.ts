// ─────────────────────────────────────────────────────────────────────────────
// toast.ts
//
// Tiny bottom-of-screen toast for "Goal added", "Submit failed", etc.
// Self-dismisses after 2 seconds. Stacks vertically when fired rapidly.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "./dom.js";

let host: HTMLElement | null = null;

function ensureHost(): HTMLElement {
	if (host) return host;
	host = h("div", {
		class: "fixed inset-x-0 bottom-24 z-50 flex flex-col items-center gap-2 px-4",
	});
	document.body.appendChild(host);
	return host;
}

export function toast(message: string, kind: "info" | "error" = "info"): void {
	const el = h(
		"div",
		{
			class: `pill ${kind === "error" ? "bg-accent-red/20 text-accent-red" : "bg-accent-blueDim/40 text-accent-blue"} shadow-card`,
		},
		message
	);
	ensureHost().appendChild(el);
	setTimeout(() => el.remove(), 2000);
}
