// ─────────────────────────────────────────────────────────────────────────────
// clockScreen.ts
//
// Mirrors the desktop clock card: start/pause, count-up vs countdown mode,
// quarter duration adjuster, manual clock set, and siren trigger.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../ui/dom.js";
import { toast } from "../ui/toast.js";
import { submitEvent, EventTypes } from "../lib/events.js";
import type { LiveState } from "../contract/liveState.js";
import type { ClockMode } from "../contract/events.js";

export function clockScreen(sessionId: string, state: LiveState): HTMLElement {
	let pendingMin = state.quarterMinutes;
	let pendingSec = state.quarterSeconds;
	let pendingMode: ClockMode = state.clockMode;

	const minutesInput = h("input", {
		type: "number",
		min: "1",
		max: "60",
		inputmode: "numeric",
		class: "field-input text-center font-mono text-xl",
		value: String(pendingMin),
		oninput: (e: Event) => {
			pendingMin = clampInt((e.target as HTMLInputElement).value, 1, 60, state.quarterMinutes);
		},
	}) as HTMLInputElement;

	const secondsInput = h("input", {
		type: "number",
		min: "0",
		max: "59",
		inputmode: "numeric",
		class: "field-input text-center font-mono text-xl",
		value: String(pendingSec).padStart(2, "0"),
		oninput: (e: Event) => {
			pendingSec = clampInt((e.target as HTMLInputElement).value, 0, 59, state.quarterSeconds);
		},
	}) as HTMLInputElement;

	const setMin = h("input", {
		type: "number",
		min: "0",
		max: "60",
		inputmode: "numeric",
		class: "field-input text-center font-mono text-xl",
		value: state.clock.split(":")[0] ?? "0",
	}) as HTMLInputElement;
	const setSec = h("input", {
		type: "number",
		min: "0",
		max: "59",
		inputmode: "numeric",
		class: "field-input text-center font-mono text-xl",
		value: state.clock.split(":")[1] ?? "00",
	}) as HTMLInputElement;

	return h(
		"div",
		{ class: "space-y-3" },
		h(
			"section",
			{ class: "card space-y-3" },
			h("div", { class: "field-label" }, "Clock"),
			h("div", { class: "text-center font-mono text-5xl font-black tabular-nums text-accent-blue" }, state.clock),
			h(
				"button",
				{
					type: "button",
					class: state.clockRunning ? "btn-danger w-full" : "btn-success w-full",
					onclick: () => fire(sessionId, EventTypes.ClockToggle, state.clockRunning ? "Paused" : "Started"),
				},
				state.clockRunning ? "⏸ Pause clock" : "▶ Start clock"
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost w-full",
					onclick: () => fire(sessionId, EventTypes.Siren, "Siren queued"),
				},
				"📣 Trigger siren"
			)
		),

		h(
			"section",
			{ class: "card space-y-3" },
			h("div", { class: "field-label" }, "Clock mode"),
			h(
				"div",
				{ class: "grid grid-cols-2 gap-2" },
				modeButton("Count up", pendingMode === "CountUp", () => {
					pendingMode = "CountUp";
					submitClockMode(sessionId, pendingMode, pendingMin, pendingSec);
				}),
				modeButton("Countdown", pendingMode === "Countdown", () => {
					pendingMode = "Countdown";
					submitClockMode(sessionId, pendingMode, pendingMin, pendingSec);
				})
			),
			h(
				"div",
				{ class: "grid grid-cols-2 gap-2" },
				h("div", null, h("span", { class: "field-label" }, "Quarter min"), minutesInput),
				h("div", null, h("span", { class: "field-label" }, "Quarter sec"), secondsInput)
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-primary w-full",
					onclick: () => submitClockMode(sessionId, pendingMode, pendingMin, pendingSec),
				},
				"Apply quarter duration"
			)
		),

		h(
			"section",
			{ class: "card space-y-3" },
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
						fireWithPayload(sessionId, EventTypes.ClockSet, "Clock updated", {
							minutes: clampInt(setMin.value, 0, 60, 0),
							seconds: clampInt(setSec.value, 0, 59, 0),
						}),
				},
				"Apply elapsed clock"
			)
		)
	);
}

function modeButton(label: string, active: boolean, onclick: () => void): HTMLElement {
	return h(
		"button",
		{
			type: "button",
			class: active ? "btn-primary" : "btn-ghost",
			onclick,
		},
		label
	);
}

function clampInt(value: string, min: number, max: number, fallback: number): number {
	const n = Math.trunc(Number(value));
	if (!Number.isFinite(n)) return fallback;
	return Math.max(min, Math.min(max, n));
}

async function submitClockMode(
	sessionId: string,
	mode: ClockMode,
	minutes: number,
	seconds: number
): Promise<void> {
	await fireWithPayload(sessionId, EventTypes.ClockModeSet, `${mode} ${minutes}:${String(seconds).padStart(2, "0")}`, { mode, minutes, seconds });
}

async function fire(sessionId: string, type: string, label: string): Promise<void> {
	try {
		await submitEvent({ sessionId, type: type as never });
		toast(label);
	} catch (err) {
		toast(err instanceof Error ? err.message : String(err), "error");
	}
}

async function fireWithPayload(sessionId: string, type: string, label: string, payload: unknown): Promise<void> {
	try {
		await submitEvent({ sessionId, type: type as never, payload });
		toast(label);
	} catch (err) {
		toast(err instanceof Error ? err.message : String(err), "error");
	}
}
