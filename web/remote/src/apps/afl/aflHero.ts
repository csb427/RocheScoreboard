// ─────────────────────────────────────────────────────────────────────────────
// aflHero.ts
//
// AFL-only sticky scoreboard chip rendered inside the phone shell's top
// bar: home/away team boxes in team colours, big totals, the live clock,
// quarter, and a goals.behinds line. Every field is defensively coerced
// so a partial wire payload never renders `undefined`.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../../ui/dom.js";
import { safeText, safeColor, intOr, EM_DASH } from "../../lib/safe.js";
import type { LiveState } from "../../contract/liveState.js";

export function aflHero(state: LiveState | null): HTMLElement | null {
	if (!state) {
		return h(
			"div",
			{ class: "text-center text-[11px] text-slate-400" },
			"Waiting for live match data…"
		);
	}

	const homeAbbr = safeText(state.homeAbbr, "HOM").slice(0, 4);
	const awayAbbr = safeText(state.awayAbbr, "AWA").slice(0, 4);
	const homeTotal = intOr(state.homeTotal, 0);
	const awayTotal = intOr(state.awayTotal, 0);
	const homeGoals = intOr(state.homeGoals, 0);
	const homeBehinds = intOr(state.homeBehinds, 0);
	const awayGoals = intOr(state.awayGoals, 0);
	const awayBehinds = intOr(state.awayBehinds, 0);
	const homePrimary = safeColor(state.homePrimaryColor, "#0A1428");
	const homeText = safeColor(state.homeSecondaryColor, "#FFFFFF");
	const awayPrimary = safeColor(state.awayPrimaryColor, "#1C0A0A");
	const awayText = safeColor(state.awaySecondaryColor, "#FFFFFF");
	const quarter = intOr(state.quarter, 1);
	const clock = safeText(state.clock, "00:00");
	const running = state.clockRunning === true;

	const homeBox = h(
		"div",
		{
			class: "flex flex-1 items-center justify-between rounded-lg px-2.5 py-2",
			style: `background:${homePrimary};color:${homeText}`,
		},
		h("span", { class: "font-mono text-xs font-black" }, homeAbbr),
		h(
			"span",
			{ class: "font-mono text-2xl font-black numeric-hero" },
			String(homeTotal)
		)
	);

	const awayBox = h(
		"div",
		{
			class: "flex flex-1 items-center justify-between rounded-lg px-2.5 py-2",
			style: `background:${awayPrimary};color:${awayText}`,
		},
		h("span", { class: "font-mono text-xs font-black" }, awayAbbr),
		h(
			"span",
			{ class: "font-mono text-2xl font-black numeric-hero" },
			String(awayTotal)
		)
	);

	const meta = h(
		"div",
		{
			class:
				"flex items-center justify-between gap-2 text-[10px] font-bold uppercase tracking-wide text-slate-300",
		},
		h("span", null, `Q${quarter}${state.q4Ended ? " · FINAL" : ""}`),
		h(
			"span",
			{ class: "font-mono text-sm", style: "color:var(--accent)" },
			clock
		),
		h("span", null, running ? "● LIVE" : "○ PAUSED")
	);

	const detail = h(
		"div",
		{ class: "grid grid-cols-2 gap-2 text-[10px] text-slate-400" },
		h("span", null, `${homeAbbr} ${homeGoals}.${homeBehinds}`),
		h("span", { class: "text-right" }, `${awayAbbr} ${awayGoals}.${awayBehinds}`)
	);

	const lastEvent = safeText(state.lastEvent, EM_DASH);

	return h(
		"div",
		{ class: "flex flex-col gap-1.5" },
		h("div", { class: "flex items-center gap-2" }, homeBox, awayBox),
		meta,
		detail,
		h(
			"div",
			{ class: "truncate text-center text-[10px] text-slate-500" },
			`Last: ${lastEvent}`
		)
	);
}
