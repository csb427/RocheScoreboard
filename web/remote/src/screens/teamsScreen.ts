// ─────────────────────────────────────────────────────────────────────────────
// teamsScreen.ts
//
// Mirrors the Settings → Teams card. Edit names, abbreviations, and
// primary/secondary colours. Logos are still set on the desktop because
// the file picker can't go through Supabase yet.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../ui/dom.js";
import { toast } from "../ui/toast.js";
import { submitEvent, EventTypes } from "../lib/events.js";
import type { LiveState } from "../contract/liveState.js";

export function teamsScreen(sessionId: string, state: LiveState): HTMLElement {
	const homeName = inputField("Home name", state.homeName);
	const homeAbbr = inputField("Home abbr", state.homeAbbr, 4);
	const awayName = inputField("Away name", state.awayName);
	const awayAbbr = inputField("Away abbr", state.awayAbbr, 4);

	const homePrimary = colorField("Home primary", state.homePrimaryColor);
	const homeSecondary = colorField("Home text", state.homeSecondaryColor);
	const awayPrimary = colorField("Away primary", state.awayPrimaryColor);
	const awaySecondary = colorField("Away text", state.awaySecondaryColor);

	return h(
		"div",
		{ class: "space-y-3" },
		h(
			"section",
			{ class: "card space-y-3" },
			h("div", { class: "field-label" }, "Team names"),
			h("div", { class: "grid grid-cols-2 gap-2" }, homeName.wrap, homeAbbr.wrap),
			h("div", { class: "grid grid-cols-2 gap-2" }, awayName.wrap, awayAbbr.wrap),
			h(
				"button",
				{
					type: "button",
					class: "btn-primary w-full",
					onclick: () =>
						firePayload(sessionId, EventTypes.SetTeams, "Teams updated", {
							homeName: homeName.input.value.trim(),
							homeAbbr: homeAbbr.input.value.trim().slice(0, 4),
							awayName: awayName.input.value.trim(),
							awayAbbr: awayAbbr.input.value.trim().slice(0, 4),
						}),
				},
				"Save names"
			)
		),

		h(
			"section",
			{ class: "card space-y-3" },
			h("div", { class: "field-label" }, "Colours"),
			h("div", { class: "grid grid-cols-2 gap-2" }, homePrimary.wrap, homeSecondary.wrap),
			h("div", { class: "grid grid-cols-2 gap-2" }, awayPrimary.wrap, awaySecondary.wrap),
			h(
				"button",
				{
					type: "button",
					class: "btn-primary w-full",
					onclick: () =>
						firePayload(sessionId, EventTypes.SetColors, "Colours updated", {
							homePrimary: homePrimary.input.value,
							homeSecondary: homeSecondary.input.value,
							awayPrimary: awayPrimary.input.value,
							awaySecondary: awaySecondary.input.value,
						}),
				},
				"Save colours"
			),
			h("p", { class: "text-[11px] text-slate-400" }, "Logos are still set on the desktop scoreboard.")
		)
	);
}

interface Field {
	wrap: HTMLElement;
	input: HTMLInputElement;
}

function inputField(label: string, value: string, maxLength?: number): Field {
	const input = h("input", {
		type: "text",
		class: "field-input",
		value,
		maxlength: maxLength,
	}) as HTMLInputElement;
	const wrap = h("label", { class: "block" }, h("span", { class: "field-label" }, label), input);
	return { wrap, input };
}

function colorField(label: string, value: string): Field {
	const text = h("input", {
		type: "text",
		class: "field-input flex-1 font-mono uppercase",
		value,
		maxlength: 7,
	}) as HTMLInputElement;
	const picker = h("input", {
		type: "color",
		class: "h-11 w-12 cursor-pointer rounded-md border border-ink-400 bg-ink-800",
		value,
		oninput: (e: Event) => (text.value = (e.target as HTMLInputElement).value.toUpperCase()),
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

async function firePayload(sessionId: string, type: string, label: string, payload: unknown): Promise<void> {
	try {
		await submitEvent({ sessionId, type: type as never, payload });
		toast(label);
	} catch (err) {
		toast(err instanceof Error ? err.message : String(err), "error");
	}
}
