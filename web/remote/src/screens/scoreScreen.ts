// ─────────────────────────────────────────────────────────────────────────────
// scoreScreen.ts
//
// Mirrors the desktop scoring panel: home/away goal & behind, undo, last
// event, and a quarter-by-quarter breakdown table.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../ui/dom.js";
import { toast } from "../ui/toast.js";
import { submitEvent, EventTypes } from "../lib/events.js";
import type { LiveState } from "../contract/liveState.js";

export function scoreScreen(sessionId: string, state: LiveState): HTMLElement {
	return h(
		"div",
		{ class: "space-y-3" },
		scoreCard("HOME", state.homeName, state.homeAbbr, state.homePrimaryColor, state.homeSecondaryColor, state.homeGoals, state.homeBehinds, state.homeTotal, async () => fire(sessionId, EventTypes.HomeGoal, "Home goal"), async () => fire(sessionId, EventTypes.HomeBehind, "Home behind")),
		scoreCard("AWAY", state.awayName, state.awayAbbr, state.awayPrimaryColor, state.awaySecondaryColor, state.awayGoals, state.awayBehinds, state.awayTotal, async () => fire(sessionId, EventTypes.AwayGoal, "Away goal"), async () => fire(sessionId, EventTypes.AwayBehind, "Away behind")),
		h(
			"div",
			{ class: "card flex items-center justify-between" },
			h("div", null,
				h("div", { class: "field-label" }, "Last event"),
				h("div", { class: "text-sm text-slate-200" }, state.lastEvent || "—")
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost",
					onclick: async () => fire(sessionId, EventTypes.Undo, "Undo last"),
				},
				"↶ Undo"
			)
		),
		quarterTable(state)
	);
}

function scoreCard(
	badge: string,
	name: string,
	abbr: string,
	primary: string,
	secondary: string,
	goals: number,
	behinds: number,
	total: number,
	onGoal: () => void,
	onBehind: () => void
): HTMLElement {
	return h(
		"section",
		{ class: "card space-y-3", style: `border-color:${primary}` },
		h(
			"header",
			{ class: "flex items-center justify-between" },
			h(
				"div",
				{ class: "flex items-center gap-2" },
				h(
					"span",
					{
						class: "pill",
						style: `background:${primary};color:${secondary}`,
					},
					badge
				),
				h("span", { class: "font-bold" }, name)
			),
			h(
				"div",
				{ class: "text-right font-mono" },
				h("div", { class: "text-2xl font-black tabular-nums", style: `color:${primary}` }, String(total)),
				h("div", { class: "text-[10px] uppercase tracking-wide text-slate-400" }, `${goals}.${behinds}`)
			)
		),
		h(
			"div",
			{ class: "grid grid-cols-2 gap-3" },
			h(
				"button",
				{
					type: "button",
					class: "btn-success min-h-[64px] flex-col text-base",
					onclick: onGoal,
				},
				h("span", { class: "text-2xl leading-none" }, "+6"),
				h("span", null, `${abbr} GOAL`)
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost min-h-[64px] flex-col text-base",
					onclick: onBehind,
				},
				h("span", { class: "text-2xl leading-none" }, "+1"),
				h("span", null, `${abbr} BEHIND`)
			)
		)
	);
}

function quarterTable(state: LiveState): HTMLElement {
	const rows: HTMLElement[] = [];
	const header = h(
		"tr",
		{ class: "text-[10px] uppercase tracking-wide text-slate-400" },
		h("th", { class: "py-1 text-left" }, "Q"),
		h("th", { class: "py-1 text-right" }, "Home"),
		h("th", { class: "py-1 text-right" }, "Away")
	);
	for (let q = 1; q <= 4; q++) {
		const snap = state.quarterSnapshots.find((s) => s.quarter === q);
		rows.push(
			h(
				"tr",
				{ class: "border-t border-ink-400/60 text-sm" },
				h("td", { class: "py-1 font-bold text-slate-300" }, `Q${q}`),
				h("td", { class: "py-1 text-right font-mono tabular-nums" }, snap ? `${snap.homeGoals}.${snap.homeBehinds} (${snap.homeTotal})` : "—"),
				h("td", { class: "py-1 text-right font-mono tabular-nums" }, snap ? `${snap.awayGoals}.${snap.awayBehinds} (${snap.awayTotal})` : "—")
			)
		);
	}
	return h(
		"section",
		{ class: "card" },
		h("div", { class: "field-label mb-2" }, "Quarter by quarter"),
		h("table", { class: "w-full" }, h("thead", null, header), h("tbody", null, ...rows))
	);
}

async function fire(sessionId: string, type: string, label: string): Promise<void> {
	try {
		await submitEvent({ sessionId, type: type as never });
		if (navigator.vibrate) navigator.vibrate(10);
		toast(`${label} sent`);
	} catch (err) {
		toast(err instanceof Error ? err.message : String(err), "error");
	}
}
