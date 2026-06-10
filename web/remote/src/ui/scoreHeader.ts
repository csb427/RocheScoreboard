// ─────────────────────────────────────────────────────────────────────────────
// scoreHeader.ts
//
// Sticky header: shows live home/away abbr + score + clock + quarter, in
// team colours. Visible on every screen so the operator always knows what
// the scoreboard looks like right now.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "./dom.js";
import type { LiveState } from "../contract/liveState.js";

export function scoreHeader(state: LiveState | null): HTMLElement {
	if (!state) {
		return h(
			"header",
			{ class: "sticky top-0 z-20 border-b border-ink-400 bg-ink-900/95 px-3 py-2 backdrop-blur" },
			h("div", { class: "text-center text-xs text-slate-400" }, "Loading match…")
		);
	}

	const homeBox = h(
		"div",
		{
			class: "flex flex-1 items-center justify-between rounded-md px-3 py-2",
			style: `background:${state.homePrimaryColor};color:${state.homeSecondaryColor}`,
		},
		h("span", { class: "font-mono text-sm font-black" }, state.homeAbbr || "HOM"),
		h("span", { class: "font-mono text-2xl font-black tabular-nums" }, String(state.homeTotal))
	);

	const awayBox = h(
		"div",
		{
			class: "flex flex-1 items-center justify-between rounded-md px-3 py-2",
			style: `background:${state.awayPrimaryColor};color:${state.awaySecondaryColor}`,
		},
		h("span", { class: "font-mono text-sm font-black" }, state.awayAbbr || "AWA"),
		h("span", { class: "font-mono text-2xl font-black tabular-nums" }, String(state.awayTotal))
	);

	const meta = h(
		"div",
		{ class: "mt-1 flex items-center justify-between text-[10px] font-bold uppercase tracking-wide text-slate-300" },
		h("span", null, `Q${state.quarter}${state.q4Ended ? " · FINAL" : ""}`),
		h("span", { class: "font-mono text-base text-accent-blue" }, state.clock),
		h("span", null, state.clockRunning ? "● LIVE" : "○ PAUSED")
	);

	const detail = h(
		"div",
		{ class: "mt-1 grid grid-cols-2 gap-2 text-[10px] text-slate-400" },
		h("span", null, `${state.homeAbbr} ${state.homeGoals}.${state.homeBehinds}`),
		h("span", { class: "text-right" }, `${state.awayAbbr} ${state.awayGoals}.${state.awayBehinds}`)
	);

	return h(
		"header",
		{ class: "sticky top-0 z-20 border-b border-ink-400 bg-ink-900/95 px-3 py-2 backdrop-blur" },
		h("div", { class: "flex items-center gap-2" }, homeBox, awayBox),
		meta,
		detail
	);
}
