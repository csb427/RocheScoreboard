// ─────────────────────────────────────────────────────────────────────────────
// settingsTab.ts
//
// AFL "SETTINGS" tab. Mirrors the desktop settings tab:
//   • Teams (names, abbreviations).
//   • Colours (primary + text for each side, with native pickers).
//   • Clock (mode picker, quarter duration, manual elapsed clock).
//   • Match (finals mode toggle, weather location, quarter-label reset).
//   • Danger zone (match reset).
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../../../ui/dom.js";
import { fireAfl, EventTypes } from "../fire.js";
import { safeText, safeColor, intOr } from "../../../lib/safe.js";
import type { LiveState } from "../../../contract/liveState.js";
import type { ClockMode } from "../../../contract/events.js";

export function settingsTab(sessionId: string, state: LiveState): HTMLElement {
	return h(
		"div",
		{ class: "space-y-3" },
		teamsCard(sessionId, state),
		coloursCard(sessionId, state),
		clockCard(sessionId, state),
		matchCard(sessionId, state),
		dangerCard(sessionId)
	);
}

// ── Teams ───────────────────────────────────────────────────────────────────
function teamsCard(sessionId: string, state: LiveState): HTMLElement {
	const homeName = textField("Home name", safeText(state.homeName, ""));
	const homeAbbr = textField("Home abbr", safeText(state.homeAbbr, ""), 4);
	const awayName = textField("Away name", safeText(state.awayName, ""));
	const awayAbbr = textField("Away abbr", safeText(state.awayAbbr, ""), 4);

	return h(
		"section",
		{ class: "card space-y-3" },
		h("div", { class: "field-label" }, "Teams"),
		h("div", { class: "grid grid-cols-2 gap-2" }, homeName.wrap, homeAbbr.wrap),
		h("div", { class: "grid grid-cols-2 gap-2" }, awayName.wrap, awayAbbr.wrap),
		h(
			"button",
			{
				type: "button",
				class: "btn-primary w-full",
				onclick: () =>
					fireAfl(sessionId, EventTypes.SetTeams, "Teams updated", {
						homeName: homeName.input.value.trim(),
						homeAbbr: homeAbbr.input.value.trim().slice(0, 4),
						awayName: awayName.input.value.trim(),
						awayAbbr: awayAbbr.input.value.trim().slice(0, 4),
					}),
			},
			"Save team names"
		)
	);
}

// ── Colours ─────────────────────────────────────────────────────────────────
function coloursCard(sessionId: string, state: LiveState): HTMLElement {
	const homePrimary = colourField("Home primary", safeColor(state.homePrimaryColor, "#0A1428"));
	const homeText = colourField("Home text", safeColor(state.homeSecondaryColor, "#FFFFFF"));
	const awayPrimary = colourField("Away primary", safeColor(state.awayPrimaryColor, "#1C0A0A"));
	const awayText = colourField("Away text", safeColor(state.awaySecondaryColor, "#FFFFFF"));

	return h(
		"section",
		{ class: "card space-y-3" },
		h("div", { class: "field-label" }, "Colours"),
		h("div", { class: "grid grid-cols-2 gap-2" }, homePrimary.wrap, homeText.wrap),
		h("div", { class: "grid grid-cols-2 gap-2" }, awayPrimary.wrap, awayText.wrap),
		h(
			"button",
			{
				type: "button",
				class: "btn-primary w-full",
				onclick: () =>
					fireAfl(sessionId, EventTypes.SetColors, "Colours updated", {
						homePrimary: homePrimary.input.value,
						homeSecondary: homeText.input.value,
						awayPrimary: awayPrimary.input.value,
						awaySecondary: awayText.input.value,
					}),
			},
			"Save colours"
		),
		h(
			"p",
			{ class: "text-[11px] text-slate-400" },
			"Logos are still set on the desktop."
		)
	);
}

// ── Clock ───────────────────────────────────────────────────────────────────
function clockCard(sessionId: string, state: LiveState): HTMLElement {
	const initMin = intOr(state.quarterMinutes, 20);
	const initSec = intOr(state.quarterSeconds, 0);
	let pendingMin = initMin;
	let pendingSec = initSec;
	let pendingMode: ClockMode = state.clockMode === "Countdown" ? "Countdown" : "CountUp";

	const minutesInput = h("input", {
		type: "number",
		min: "1",
		max: "60",
		inputmode: "numeric",
		class: "field-input text-center font-mono text-xl",
		value: String(initMin),
		oninput: (e: Event) => {
			pendingMin = clampInt((e.target as HTMLInputElement).value, 1, 60, initMin);
		},
	}) as HTMLInputElement;

	const secondsInput = h("input", {
		type: "number",
		min: "0",
		max: "59",
		inputmode: "numeric",
		class: "field-input text-center font-mono text-xl",
		value: String(initSec).padStart(2, "0"),
		oninput: (e: Event) => {
			pendingSec = clampInt((e.target as HTMLInputElement).value, 0, 59, initSec);
		},
	}) as HTMLInputElement;

	const parts = safeText(state.clock, "0:00").split(":");
	const setMin = h("input", {
		type: "number",
		min: "0",
		max: "60",
		inputmode: "numeric",
		class: "field-input text-center font-mono text-xl",
		value: parts[0] ?? "0",
	}) as HTMLInputElement;
	const setSec = h("input", {
		type: "number",
		min: "0",
		max: "59",
		inputmode: "numeric",
		class: "field-input text-center font-mono text-xl",
		value: parts[1] ?? "00",
	}) as HTMLInputElement;

	const sendMode = (): void => {
		void fireAfl(
			sessionId,
			EventTypes.ClockModeSet,
			`${pendingMode} ${pendingMin}:${String(pendingSec).padStart(2, "0")}`,
			{ mode: pendingMode, minutes: pendingMin, seconds: pendingSec }
		);
	};

	return h(
		"section",
		{ class: "card space-y-3" },
		h("div", { class: "field-label" }, "Clock mode"),
		h(
			"div",
			{ class: "segmented", style: "grid-template-columns:repeat(2,minmax(0,1fr))" },
			modeChip("Count up", pendingMode === "CountUp", () => {
				pendingMode = "CountUp";
				sendMode();
			}),
			modeChip("Countdown", pendingMode === "Countdown", () => {
				pendingMode = "Countdown";
				sendMode();
			})
		),

		h("div", { class: "field-label" }, "Quarter duration"),
		h(
			"div",
			{ class: "grid grid-cols-2 gap-2" },
			h("div", null, h("span", { class: "field-label" }, "Min"), minutesInput),
			h("div", null, h("span", { class: "field-label" }, "Sec"), secondsInput)
		),
		h(
			"button",
			{ type: "button", class: "btn-primary w-full", onclick: sendMode },
			"Apply quarter duration"
		),

		h("div", { class: "h-px bg-ink-400/60" }),

		h("div", { class: "field-label" }, "Set elapsed clock"),
		h(
			"div",
			{ class: "grid grid-cols-2 gap-2" },
			h("div", null, h("span", { class: "field-label" }, "Min"), setMin),
			h("div", null, h("span", { class: "field-label" }, "Sec"), setSec)
		),
		h(
			"button",
			{
				type: "button",
				class: "btn-primary w-full",
				onclick: () =>
					fireAfl(sessionId, EventTypes.ClockSet, "Clock updated", {
						minutes: clampInt(setMin.value, 0, 60, 0),
						seconds: clampInt(setSec.value, 0, 59, 0),
					}),
			},
			"Apply elapsed clock"
		)
	);
}

// ── Match ───────────────────────────────────────────────────────────────────
function matchCard(sessionId: string, state: LiveState): HTMLElement {
	const weather = h("input", {
		type: "text",
		class: "field-input",
		placeholder: "Suburb, postcode or town",
		value: safeText(state.weatherLocation, ""),
	}) as HTMLInputElement;

	return h(
		"section",
		{ class: "card space-y-3" },
		h("div", { class: "field-label" }, "Finals mode"),
		h(
			"div",
			{ class: "segmented", style: "grid-template-columns:repeat(2,minmax(0,1fr))" },
			modeChip("On", state.finalsMode === true, () =>
				fireAfl(sessionId, EventTypes.SetFinalsMode, "Finals ON", { enabled: true })
			),
			modeChip("Off", state.finalsMode === false, () =>
				fireAfl(sessionId, EventTypes.SetFinalsMode, "Finals OFF", { enabled: false })
			)
		),

		h("div", { class: "h-px bg-ink-400/60" }),

		h("div", { class: "field-label" }, "Weather location"),
		weather,
		h(
			"button",
			{
				type: "button",
				class: "btn-primary w-full",
				onclick: () =>
					fireAfl(sessionId, EventTypes.SetWeatherLocation, "Weather updated", {
						location: weather.value.trim(),
					}),
			},
			"Apply weather location"
		),

		h("div", { class: "h-px bg-ink-400/60" }),

		h(
			"button",
			{
				type: "button",
				class: "btn-ghost w-full",
				onclick: () =>
					fireAfl(sessionId, EventTypes.ResetQuarterLabel, "Quarter labels reset"),
			},
			"Reset quarter labels"
		)
	);
}

// ── Danger zone ─────────────────────────────────────────────────────────────
function dangerCard(sessionId: string): HTMLElement {
	return h(
		"section",
		{ class: "card space-y-2 border-accent-red/30" },
		h("div", { class: "field-label text-accent-red" }, "Danger zone"),
		h(
			"p",
			{ class: "text-xs text-slate-300" },
			"Resets the score, clock, quarter, and event log."
		),
		h(
			"button",
			{
				type: "button",
				class: "btn-danger w-full",
				onclick: () => {
					if (!confirm("Reset the match? This clears the score, clock, and event log.")) return;
					void fireAfl(sessionId, EventTypes.MatchReset, "Match reset");
				},
			},
			"⟲ Reset match"
		)
	);
}

// ── Helpers ─────────────────────────────────────────────────────────────────
interface Field {
	wrap: HTMLElement;
	input: HTMLInputElement;
}

function textField(label: string, value: string, maxLength?: number): Field {
	const input = h("input", {
		type: "text",
		class: "field-input",
		value,
		maxlength: maxLength,
	}) as HTMLInputElement;
	const wrap = h("label", { class: "block" }, h("span", { class: "field-label" }, label), input);
	return { wrap, input };
}

function colourField(label: string, value: string): Field {
	const text = h("input", {
		type: "text",
		class: "field-input flex-1 font-mono uppercase",
		value,
		maxlength: 7,
	}) as HTMLInputElement;
	const picker = h("input", {
		type: "color",
		class: "h-12 w-14 cursor-pointer rounded-xl border border-ink-400 bg-ink-800",
		value,
		oninput: (e: Event) => {
			text.value = (e.target as HTMLInputElement).value.toUpperCase();
		},
	}) as HTMLInputElement;
	text.addEventListener("input", () => {
		if (/^#[0-9a-fA-F]{6}$/.test(text.value)) picker.value = text.value;
	});
	const wrap = h(
		"label",
		{ class: "block" },
		h("span", { class: "field-label" }, label),
		h("div", { class: "flex items-center gap-2" }, picker, text)
	);
	return { wrap, input: text };
}

function modeChip(label: string, active: boolean, onclick: () => void): HTMLElement {
	return h(
		"button",
		{ type: "button", class: `seg ${active ? "seg-on" : ""}`, onclick },
		label
	);
}

function clampInt(value: string, min: number, max: number, fallback: number): number {
	const n = Math.trunc(Number(value));
	if (!Number.isFinite(n)) return fallback;
	return Math.max(min, Math.min(max, n));
}
