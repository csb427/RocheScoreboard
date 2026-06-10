// ─────────────────────────────────────────────────────────────────────────────
// bottomNav.ts
//
// Bottom tab bar with one icon per mirrored desktop section. Tapping a tab
// swaps the currently-rendered screen module via the supplied callback.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "./dom.js";

export type ScreenKey =
	| "score"
	| "clock"
	| "match"
	| "teams"
	| "display"
	| "marquee"
	| "advanced";

const TABS: Array<{ key: ScreenKey; label: string; icon: string }> = [
	{ key: "score", label: "Score", icon: "🏉" },
	{ key: "clock", label: "Clock", icon: "⏱" },
	{ key: "match", label: "Match", icon: "🏆" },
	{ key: "teams", label: "Teams", icon: "👥" },
	{ key: "display", label: "Display", icon: "📺" },
	{ key: "marquee", label: "Marquee", icon: "💬" },
	{ key: "advanced", label: "More", icon: "⚙" },
];

export function bottomNav(active: ScreenKey, onSelect: (key: ScreenKey) => void): HTMLElement {
	return h(
		"nav",
		{
			class:
				"fixed inset-x-0 bottom-0 z-30 border-t border-ink-400 bg-ink-900/95 pb-[env(safe-area-inset-bottom)] backdrop-blur",
		},
		h(
			"div",
			{ class: "mx-auto flex max-w-3xl items-stretch" },
			...TABS.map((tab) =>
				h(
					"button",
					{
						type: "button",
						class: `flex flex-1 flex-col items-center gap-0.5 px-1 py-2 text-[10px] font-black uppercase tracking-wide ${
							active === tab.key ? "text-accent-blue" : "text-slate-500"
						}`,
						onclick: () => onSelect(tab.key),
					},
					h("span", { class: "text-base leading-none" }, tab.icon),
					h("span", null, tab.label)
				)
			)
		)
	);
}
