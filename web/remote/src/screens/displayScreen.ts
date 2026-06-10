// ─────────────────────────────────────────────────────────────────────────────
// displayScreen.ts
//
// Mirrors the desktop "Presentation" / layout / animation cards: switch
// active screen (scorebug, summary, worm, stats, weather, video), pick a
// scorebug layout, and pick a goal animation style.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../ui/dom.js";
import { toast } from "../ui/toast.js";
import { submitEvent, EventTypes } from "../lib/events.js";
import type { LiveState } from "../contract/liveState.js";
import type { GoalAnimation, Layout, Screen } from "../contract/events.js";

const SCREENS: Array<{ key: Screen; label: string; emoji: string }> = [
	{ key: "scorebug", label: "Scorebug", emoji: "🏉" },
	{ key: "summary", label: "Summary", emoji: "📊" },
	{ key: "worm", label: "Worm", emoji: "📈" },
	{ key: "stats", label: "Stats", emoji: "📋" },
	{ key: "weather", label: "Weather", emoji: "🌧" },
	{ key: "video", label: "Video", emoji: "🎬" },
];

const LAYOUTS: Array<{ key: Layout; label: string }> = [
	{ key: "classic", label: "Classic" },
];

const ANIMATIONS: Array<{ key: GoalAnimation; label: string }> = [
	{ key: "classic", label: "Classic" },
	{ key: "broadcast", label: "Broadcast" },
	{ key: "customVideo", label: "Custom video" },
];

export function displayScreen(sessionId: string, state: LiveState): HTMLElement {
	return h(
		"div",
		{ class: "space-y-3" },

		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Active screen"),
			h(
				"div",
				{ class: "grid grid-cols-3 gap-2" },
				...SCREENS.map((s) =>
					h(
						"button",
						{
							type: "button",
							class: state.activeScreen === s.key ? "btn-primary flex-col" : "btn-ghost flex-col",
							onclick: () => firePayload(sessionId, EventTypes.SwitchScreen, `→ ${s.label}`, { screen: s.key }),
						},
						h("span", { class: "text-2xl leading-none" }, s.emoji),
						h("span", { class: "text-[11px]" }, s.label)
					)
				)
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost w-full",
					onclick: () => fire(sessionId, EventTypes.ShowScoreboardWindow, "Scoreboard window shown"),
				},
				"📺 Show scoreboard window"
			)
		),

		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Scorebug layout"),
			h(
				"div",
				{ class: "grid grid-cols-2 gap-2" },
				...LAYOUTS.map((l) =>
					h(
						"button",
						{
							type: "button",
							class: state.layout === l.key ? "btn-primary" : "btn-ghost",
							onclick: () => firePayload(sessionId, EventTypes.SetLayout, `Layout → ${l.label}`, { layout: l.key }),
						},
						l.label
					)
				)
			)
		),

		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Goal animation"),
			h(
				"div",
				{ class: "grid grid-cols-3 gap-2" },
				...ANIMATIONS.map((a) =>
					h(
						"button",
						{
							type: "button",
							class: state.goalAnimation === a.key ? "btn-primary" : "btn-ghost",
							onclick: () => firePayload(sessionId, EventTypes.SetGoalAnimation, `Animation → ${a.label}`, { style: a.key }),
						},
						a.label
					)
				)
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
