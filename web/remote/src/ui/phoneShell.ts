// ─────────────────────────────────────────────────────────────────────────────
// phoneShell.ts
//
// Reusable phone-first chrome: a sticky themed top bar, a connection
// banner, a scrolling content well, and a fixed bottom tab nav. Both the
// AFL and Training apps wrap their screen content in this shell, so the
// outer shape is identical and there are no `"undefined"` placeholders
// hidden in either app — every chrome element accepts plain strings.
// ─────────────────────────────────────────────────────────────────────────────

import { h, mount } from "./dom.js";
import type { Sport } from "../contract/events.js";

export type ConnectionStatus = "connecting" | "connected" | "disconnected";

export interface TabDef<TKey extends string> {
	key: TKey;
	label: string;
	icon: string;
}

export interface PhoneShellOptions<TKey extends string> {
	sport: Sport;
	title: string;
	subtitle?: string;
	tabs: TabDef<TKey>[];
	activeTab: TKey;
	onTabChange: (key: TKey) => void;
	connectionStatus: ConnectionStatus;
	hero: HTMLElement | null;
	body: HTMLElement;
}

export function renderPhoneShell<TKey extends string>(
	host: HTMLElement,
	opts: PhoneShellOptions<TKey>
): void {
	const themeClass = opts.sport === "training" ? "theme-training" : "theme-afl";

	const shell = h(
		"div",
		{ class: `phone-shell ${themeClass}` },
		topBar(opts.title, opts.subtitle, opts.sport, opts.hero),
		connectionBanner(opts.connectionStatus),
		h(
			"main",
			{ class: "phone-content mx-auto max-w-md space-y-3 px-3" },
			opts.body
		),
		bottomNav(opts.tabs, opts.activeTab, opts.onTabChange)
	);

	mount(host, shell);
}

function topBar(
	title: string,
	subtitle: string | undefined,
	sport: Sport,
	hero: HTMLElement | null
): HTMLElement {
	const safeTitle = title.trim().length > 0 ? title : "Roche Remote";
	const safeSub = subtitle && subtitle.trim().length > 0 ? subtitle : null;
	const sportTag = sport === "training" ? "Training" : "AFL";

	return h(
		"header",
		{ class: "top-bar" },
		h(
			"div",
			{ class: "mx-auto flex max-w-md flex-col gap-2 px-3 py-2" },
			h(
				"div",
				{ class: "flex items-center justify-between gap-2" },
				h(
					"div",
					{ class: "min-w-0" },
					h(
						"div",
						{
							class: "truncate text-sm font-black uppercase tracking-wide",
							style: "color:var(--accent)",
						},
						safeTitle
					),
					safeSub
						? h(
							"div",
							{ class: "truncate text-[11px] text-slate-400" },
							safeSub
						)
						: null
				),
				h(
					"span",
					{
						class: "pill",
						style: "background:var(--accent-dim);color:var(--accent)",
					},
					sportTag
				)
			),
			hero
		)
	);
}

function connectionBanner(status: ConnectionStatus): HTMLElement | null {
	if (status === "connected") return null;
	const isConnecting = status === "connecting";
	return h(
		"div",
		{
			class: `px-3 py-1 text-center text-[11px] font-bold uppercase tracking-wide ${
				isConnecting
					? "bg-accent-yellow/10 text-accent-yellow"
					: "bg-accent-red/10 text-accent-red"
			}`,
		},
		isConnecting ? "Connecting…" : "Disconnected — retrying"
	);
}

function bottomNav<TKey extends string>(
	tabs: TabDef<TKey>[],
	active: TKey,
	onSelect: (key: TKey) => void
): HTMLElement {
	return h(
		"nav",
		{ class: "bottom-nav" },
		h(
			"div",
			{ class: "bottom-nav-inner" },
			...tabs.map((tab) =>
				h(
					"button",
					{
						type: "button",
						class: `bottom-nav-btn ${active === tab.key ? "bottom-nav-btn-on" : ""}`,
						onclick: () => onSelect(tab.key),
					},
					h("span", { class: "icon" }, tab.icon),
					h("span", { class: "leading-none" }, tab.label)
				)
			)
		)
	);
}
