// ─────────────────────────────────────────────────────────────────────────────
// displayTab.ts
//
// AFL "DISPLAY & MESSAGES" tab. Mirrors the desktop tab section-for-
// section:
//   • Active screen picker (Scorebug / Summary / Worm / Stats / Weather
//     / Video).
//   • Show scoreboard window button.
//   • Scorebug layout picker.
//   • Goal animation picker.
//   • Marquee message list with add / remove / clear.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../../../ui/dom.js";
import { fireAfl, EventTypes } from "../fire.js";
import { safeText } from "../../../lib/safe.js";
import type { LiveState } from "../../../contract/liveState.js";
import type { GoalAnimation, Layout, Screen } from "../../../contract/events.js";

const SCREENS: Array<{ key: Screen; label: string; icon: string }> = [
	{ key: "scorebug", label: "Scorebug", icon: "🏉" },
	{ key: "summary", label: "Summary", icon: "📊" },
	{ key: "worm", label: "Worm", icon: "📈" },
	{ key: "stats", label: "Stats", icon: "📋" },
	{ key: "weather", label: "Weather", icon: "🌧" },
	{ key: "video", label: "Video", icon: "🎬" },
];

const LAYOUTS: Array<{ key: Layout; label: string }> = [
	{ key: "classic", label: "Classic" },
	{ key: "expanded", label: "Expanded" },
	{ key: "retro", label: "Retro" },
	{ key: "noLogos", label: "No logos" },
];

const ANIMATIONS: Array<{ key: GoalAnimation; label: string }> = [
	{ key: "classic", label: "Classic" },
	{ key: "broadcast", label: "Broadcast" },
	{ key: "electric", label: "Electric" },
	{ key: "cinematic", label: "Cinematic" },
	{ key: "clean", label: "Clean" },
	{ key: "customVideo", label: "Custom" },
];

export function displayTab(sessionId: string, state: LiveState): HTMLElement {
	const messageInput = h("input", {
		type: "text",
		class: "field-input",
		placeholder: "Type a message…",
		maxlength: 200,
	}) as HTMLInputElement;

	const addMessage = (): void => {
		const text = messageInput.value.trim();
		if (!text) return;
		void fireAfl(sessionId, EventTypes.MessageAdd, "Message queued", { text }).then(() => {
			messageInput.value = "";
		});
	};

	messageInput.addEventListener("keydown", (e) => {
		if (e.key === "Enter") {
			e.preventDefault();
			addMessage();
		}
	});

	const messages = Array.isArray(state.messages) ? state.messages : [];
	const messageList = h(
		"ul",
		{ class: "divide-y divide-ink-400/60" },
		...(messages.length === 0
			? [h("li", { class: "py-3 text-sm text-slate-500" }, "No messages")]
			: messages.map((text, idx) =>
				h(
					"li",
					{ class: "flex items-center gap-2 py-2 text-sm" },
					h(
						"span",
						{ class: "flex-1 truncate text-slate-200" },
						safeText(text, "(empty)")
					),
					h(
						"button",
						{
							type: "button",
							class: "btn-ghost px-3 py-1 text-xs",
							onclick: () =>
								fireAfl(sessionId, EventTypes.MessageRemove, "Message removed", { index: idx }),
						},
						"Remove"
					)
				)
			))
	);

	return h(
		"div",
		{ class: "space-y-3" },

		h(
			"section",
			{ class: "card space-y-3" },
			h("div", { class: "field-label" }, "Active screen"),
			h(
				"div",
				{ class: "segmented", style: "grid-template-columns:repeat(3,minmax(0,1fr))" },
				...SCREENS.map((s) =>
					h(
						"button",
						{
							type: "button",
							class: `seg ${state.activeScreen === s.key ? "seg-on" : ""}`,
							onclick: () =>
								fireAfl(sessionId, EventTypes.SwitchScreen, `→ ${s.label}`, { screen: s.key }),
						},
						h("span", { class: "seg-icon" }, s.icon),
						h("span", null, s.label)
					)
				)
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost w-full",
					onclick: () =>
						fireAfl(sessionId, EventTypes.ShowScoreboardWindow, "Scoreboard window shown"),
				},
				"📺 Show scoreboard window"
			)
		),

		h(
			"section",
			{ class: "card space-y-3" },
			h("div", { class: "field-label" }, "Scorebug layout"),
			h(
				"div",
				{ class: "segmented", style: "grid-template-columns:repeat(2,minmax(0,1fr))" },
				...LAYOUTS.map((l) =>
					h(
						"button",
						{
							type: "button",
							class: `seg ${state.layout === l.key ? "seg-on" : ""}`,
							onclick: () =>
								fireAfl(sessionId, EventTypes.SetLayout, `Layout → ${l.label}`, { layout: l.key }),
						},
						l.label
					)
				)
			)
		),

		h(
			"section",
			{ class: "card space-y-3" },
			h("div", { class: "field-label" }, "Goal animation"),
			h(
				"div",
				{ class: "segmented", style: "grid-template-columns:repeat(3,minmax(0,1fr))" },
				...ANIMATIONS.map((a) =>
					h(
						"button",
						{
							type: "button",
							class: `seg ${state.goalAnimation === a.key ? "seg-on" : ""}`,
							onclick: () =>
								fireAfl(sessionId, EventTypes.SetGoalAnimation, `Animation → ${a.label}`, { style: a.key }),
						},
						a.label
					)
				)
			)
		),

		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Add marquee message"),
			messageInput,
			h(
				"button",
				{ type: "button", class: "btn-primary w-full", onclick: () => addMessage() },
				"Add message"
			)
		),

		h(
			"section",
			{ class: "card space-y-2" },
			h(
				"div",
				{ class: "flex items-center justify-between" },
				h("div", { class: "field-label" }, `Messages (${messages.length})`),
				messages.length > 0
					? h(
						"button",
						{
							type: "button",
							class: "btn-ghost px-3 py-1 text-xs",
							onclick: () => {
								if (!confirm("Remove all messages?")) return;
								void fireAfl(sessionId, EventTypes.MessagesSet, "Messages cleared", { messages: [] });
							},
						},
						"Clear all"
					)
					: null
			),
			messageList
		)
	);
}
