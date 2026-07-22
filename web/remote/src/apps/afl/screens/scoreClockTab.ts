// ─────────────────────────────────────────────────────────────────────────────
// scoreClockTab.ts
//
// AFL "SCORING & CLOCK" tab on the phone — mirrors the desktop control
// panel block-for-block:
//   • Home / Away score cards with +6 GOAL, +1 BEHIND, –6, –1 nudges,
//     and team colours.
//   • Undo last event.
//   • Live clock card with start/pause, siren, and a quarter line.
//   • End quarter + continue quarter actions.
//   • Quarter-by-quarter table.
//
// Every label and number is coerced via `safe.ts` so the phone never
// renders raw `undefined`.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../../../ui/dom.js";
import { fireAfl, EventTypes } from "../fire.js";
import { safeText, safeColor, intOr, EM_DASH } from "../../../lib/safe.js";
import type { LiveState } from "../../../contract/liveState.js";

export function scoreClockTab(sessionId: string, state: LiveState): HTMLElement {
	return h(
		"div",
		{ class: "space-y-3" },
		scoreCard(sessionId, state, "home"),
		scoreCard(sessionId, state, "away"),
		undoBar(sessionId, state),
		clockCard(sessionId, state),
		quarterControls(sessionId, state),
		quarterTable(state)
	);
}

type Side = "home" | "away";

function scoreCard(sessionId: string, state: LiveState, side: Side): HTMLElement {
	const isHome = side === "home";
	const fullName = safeText(isHome ? state.homeName : state.awayName, isHome ? "Home" : "Away");
	const abbr = safeText(isHome ? state.homeAbbr : state.awayAbbr, isHome ? "HOM" : "AWA").slice(0, 4);
	const primary = safeColor(isHome ? state.homePrimaryColor : state.awayPrimaryColor, isHome ? "#0A1428" : "#1C0A0A");
	const secondary = safeColor(isHome ? state.homeSecondaryColor : state.awaySecondaryColor, "#FFFFFF");
	const goals = intOr(isHome ? state.homeGoals : state.awayGoals, 0);
	const behinds = intOr(isHome ? state.homeBehinds : state.awayBehinds, 0);
	const total = intOr(isHome ? state.homeTotal : state.awayTotal, 0);

	const goalType = isHome ? EventTypes.HomeGoal : EventTypes.AwayGoal;
	const behindType = isHome ? EventTypes.HomeBehind : EventTypes.AwayBehind;

	return h(
		"section",
		{ class: "card space-y-3", style: `border-color:${primary}` },
		h(
			"header",
			{ class: "flex items-center justify-between gap-2" },
			h(
				"div",
				{ class: "flex min-w-0 items-center gap-2" },
				h(
					"span",
					{ class: "pill", style: `background:${primary};color:${secondary}` },
					isHome ? "HOME" : "AWAY"
				),
				h("span", { class: "truncate font-bold" }, fullName)
			),
			h(
				"div",
				{ class: "text-right font-mono" },
				h(
					"div",
					{ class: "text-3xl font-black numeric-hero", style: `color:${primary}` },
					String(total)
				),
				h(
					"div",
					{ class: "text-[10px] uppercase tracking-wide text-slate-400" },
					`${goals}.${behinds}`
				)
			)
		),
		h(
			"div",
			{ class: "grid grid-cols-2 gap-2" },
			h(
				"button",
				{
					type: "button",
					class: "btn-success min-h-[72px] flex-col",
					onclick: () => fireAfl(sessionId, goalType, `${abbr} goal`),
				},
				h("span", { class: "text-2xl leading-none" }, "+6"),
				h("span", null, `${abbr} GOAL`)
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost min-h-[72px] flex-col",
					onclick: () => fireAfl(sessionId, behindType, `${abbr} behind`),
				},
				h("span", { class: "text-2xl leading-none" }, "+1"),
				h("span", null, `${abbr} BEHIND`)
			)
		)
	);
}

function undoBar(sessionId: string, state: LiveState): HTMLElement {
	const last = safeText(state.lastEvent, EM_DASH);
	return h(
		"div",
		{ class: "card flex items-center justify-between gap-3" },
		h(
			"div",
			{ class: "min-w-0 flex-1" },
			h("div", { class: "field-label" }, "Last event"),
			h("div", { class: "truncate text-sm text-slate-200" }, last)
		),
		h(
			"button",
			{
				type: "button",
				class: "btn-ghost shrink-0",
				onclick: () => fireAfl(sessionId, EventTypes.Undo, "Undo sent"),
			},
			"↶ Undo"
		)
	);
}

function clockCard(sessionId: string, state: LiveState): HTMLElement {
	const clock = safeText(state.clock, "00:00");
	const running = state.clockRunning === true;

	return h(
		"section",
		{ class: "card space-y-3" },
		h(
			"div",
			{ class: "flex items-center justify-between" },
			h("div", { class: "field-label" }, "Clock"),
			h(
				"span",
				{ class: "text-[11px] font-bold uppercase tracking-wide text-slate-400" },
				safeText(state.clockMode, "CountUp")
			)
		),
		h(
			"div",
			{
				class:
					"text-center font-mono text-6xl font-black numeric-hero",
				style: "color:var(--accent)",
			},
			clock
		),
		h(
			"div",
			{ class: "grid grid-cols-2 gap-2" },
			h(
				"button",
				{
					type: "button",
					class: running ? "btn-danger" : "btn-success",
					onclick: () =>
						fireAfl(sessionId, EventTypes.ClockToggle, running ? "Paused" : "Started"),
				},
				running ? "⏸ Pause" : "▶ Start"
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-warn",
					onclick: () => fireAfl(sessionId, EventTypes.Siren, "Siren queued"),
				},
				"📣 Siren"
			)
		)
	);
}

function quarterControls(sessionId: string, state: LiveState): HTMLElement {
	const quarter = intOr(state.quarter, 1);
	return h(
		"section",
		{ class: "card space-y-2" },
		h("div", { class: "field-label" }, "Quarter"),
		h(
			"div",
			{ class: "text-center text-2xl font-black", style: "color:var(--accent)" },
			`Q${quarter}${state.q4Ended ? " · FINAL" : ""}`
		),
		h(
			"div",
			{ class: "grid grid-cols-2 gap-2" },
			h(
				"button",
				{
					type: "button",
					class: "btn-primary",
					onclick: () => fireAfl(sessionId, EventTypes.EndQuarter, "End-of-quarter sent"),
				},
				"🏁 End quarter"
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost",
					onclick: () => fireAfl(sessionId, EventTypes.ContinueQuarter, "Continue quarter"),
				},
				"▶ Continue"
			)
		)
	);
}

function quarterTable(state: LiveState): HTMLElement {
	const rows: HTMLElement[] = [];
	for (let q = 1; q <= 4; q++) {
		const snap = state.quarterSnapshots.find((s) => s.quarter === q);
		rows.push(
			h(
				"tr",
				{ class: "border-t border-ink-400/60 text-sm" },
				h("td", { class: "py-1 font-bold text-slate-300" }, `Q${q}`),
				h(
					"td",
					{ class: "py-1 text-right font-mono" },
					snap
						? `${intOr(snap.homeGoals, 0)}.${intOr(snap.homeBehinds, 0)} (${intOr(snap.homeTotal, 0)})`
						: EM_DASH
				),
				h(
					"td",
					{ class: "py-1 text-right font-mono" },
					snap
						? `${intOr(snap.awayGoals, 0)}.${intOr(snap.awayBehinds, 0)} (${intOr(snap.awayTotal, 0)})`
						: EM_DASH
				)
			)
		);
	}
	return h(
		"section",
		{ class: "card" },
		h("div", { class: "field-label mb-2" }, "Quarter by quarter"),
		h(
			"table",
			{ class: "w-full" },
			h(
				"thead",
				null,
				h(
					"tr",
					{ class: "text-[10px] uppercase tracking-wide text-slate-400" },
					h("th", { class: "py-1 text-left" }, "Q"),
					h("th", { class: "py-1 text-right" }, "Home"),
					h("th", { class: "py-1 text-right" }, "Away")
				)
			),
			h("tbody", null, ...rows)
		)
	);
}
