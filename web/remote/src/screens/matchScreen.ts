// ─────────────────────────────────────────────────────────────────────────────
// matchScreen.ts
//
// Mirrors the desktop "Match" controls: end / continue quarter, finals
// mode toggle, weather location, full match reset.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../ui/dom.js";
import { toast } from "../ui/toast.js";
import { submitEvent, EventTypes } from "../lib/events.js";
import type { LiveState } from "../contract/liveState.js";

export function matchScreen(sessionId: string, state: LiveState): HTMLElement {
	const weather = h("input", {
		type: "text",
		class: "field-input",
		placeholder: "Suburb, postcode or town",
		value: state.weatherLocation,
	}) as HTMLInputElement;

	return h(
		"div",
		{ class: "space-y-3" },
		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Quarter"),
			h("div", { class: "text-center text-3xl font-black text-accent-blue" }, `Q${state.quarter}${state.q4Ended ? " · FINAL" : ""}`),
			h(
				"div",
				{ class: "grid grid-cols-2 gap-2" },
				h(
					"button",
					{
						type: "button",
						class: "btn-primary",
						onclick: () => fire(sessionId, EventTypes.EndQuarter, "End-of-quarter sent"),
					},
					"🏁 End quarter"
				),
				h(
					"button",
					{
						type: "button",
						class: "btn-ghost",
						onclick: () => fire(sessionId, EventTypes.ContinueQuarter, "Continue quarter sent"),
					},
					"▶ Continue"
				)
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost w-full",
					onclick: () => fire(sessionId, EventTypes.ResetQuarterLabel, "Quarter labels reset"),
				},
				"Reset quarter labels"
			)
		),

		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Finals mode"),
			h(
				"p",
				{ class: "text-xs text-slate-400" },
				"Adds the gold finals trim and shows the result banner at full time."
			),
			h(
				"div",
				{ class: "grid grid-cols-2 gap-2" },
				h(
					"button",
					{
						type: "button",
						class: state.finalsMode ? "btn-primary" : "btn-ghost",
						onclick: () => firePayload(sessionId, EventTypes.SetFinalsMode, "Finals ON", { enabled: true }),
					},
					"On"
				),
				h(
					"button",
					{
						type: "button",
						class: !state.finalsMode ? "btn-primary" : "btn-ghost",
						onclick: () => firePayload(sessionId, EventTypes.SetFinalsMode, "Finals OFF", { enabled: false }),
					},
					"Off"
				)
			)
		),

		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Weather location"),
			weather,
			h(
				"button",
				{
					type: "button",
					class: "btn-primary w-full",
					onclick: () => firePayload(sessionId, EventTypes.SetWeatherLocation, "Weather updated", { location: weather.value.trim() }),
				},
				"Apply weather location"
			)
		),

		h(
			"section",
			{ class: "card space-y-2 border-accent-red/30 bg-accent-red/5" },
			h("div", { class: "field-label text-accent-red" }, "Danger zone"),
			h("p", { class: "text-xs text-slate-300" }, "Resets the score, clock, quarter, and event log."),
			h(
				"button",
				{
					type: "button",
					class: "btn-danger w-full",
					onclick: () => confirmAndFire(sessionId, EventTypes.MatchReset, "Match reset"),
				},
				"⟲ Reset match"
			)
		)
	);
}

async function fire(sessionId: string, type: string, label: string): Promise<void> {
	try {
		await submitEvent({ sessionId, type: type as never });
		toast(label);
	} catch (err) {
		toast(err instanceof Error ? err.message : String(err), "error");
	}
}

async function firePayload(sessionId: string, type: string, label: string, payload: unknown): Promise<void> {
	try {
		await submitEvent({ sessionId, type: type as never, payload });
		toast(label);
	} catch (err) {
		toast(err instanceof Error ? err.message : String(err), "error");
	}
}

async function confirmAndFire(sessionId: string, type: string, label: string): Promise<void> {
	if (!confirm("Reset the match? This clears the score, clock, and event log.")) return;
	await fire(sessionId, type, label);
}
