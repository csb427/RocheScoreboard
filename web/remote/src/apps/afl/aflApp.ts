// ─────────────────────────────────────────────────────────────────────────────
// aflApp.ts
//
// Orchestrates the AFL phone experience:
//   • Subscribes to the shared `liveStateStore` for this session.
//   • Re-renders the phone shell on every state / connection change.
//   • Owns the active tab and unsubscribes cleanly on leave.
//
// Self-contained — no Training imports here, no Training types leaking
// into the AFL UI.
// ─────────────────────────────────────────────────────────────────────────────

import { h, mount } from "../../ui/dom.js";
import {
	renderPhoneShell,
	type ConnectionStatus,
	type TabDef,
} from "../../ui/phoneShell.js";
import { liveStateStore } from "../../state/liveStateStore.js";
import type { LiveState } from "../../contract/liveState.js";
import type { GameSession } from "../../lib/session.js";
import { safeText } from "../../lib/safe.js";
import { aflHero } from "./aflHero.js";
import { scoreClockTab } from "./screens/scoreClockTab.js";
import { displayTab } from "./screens/displayTab.js";
import { settingsTab } from "./screens/settingsTab.js";
import { moreTab } from "./screens/moreTab.js";

type AflTabKey = "score" | "display" | "settings" | "more";

const TABS: TabDef<AflTabKey>[] = [
	{ key: "score", label: "Score & Clock", icon: "🏉" },
	{ key: "display", label: "Display", icon: "📺" },
	{ key: "settings", label: "Settings", icon: "⚙" },
	{ key: "more", label: "More", icon: "•••" },
];

export function runAflApp(
	host: HTMLElement,
	session: GameSession,
	onLeave: () => void
): void {
	let activeTab: AflTabKey = "score";
	let connectionStatus: ConnectionStatus = "connecting";
	let cleanedUp = false;

	const leave = (): void => {
		if (cleanedUp) return;
		cleanedUp = true;
		unsubscribeState();
		unsubscribeConn();
		void liveStateStore.stop();
		onLeave();
	};

	const render = (state: LiveState | null): void => {
		const title = state
			? `${safeText(state.homeAbbr, "HOM")} vs ${safeText(state.awayAbbr, "AWA")}`
			: "Roche Remote · AFL";
		const subtitle = state
			? `Q${state.quarter ?? 1} · ${safeText(state.clock, "00:00")}`
			: "Waiting for desktop…";

		renderPhoneShell<AflTabKey>(host, {
			sport: "afl",
			title,
			subtitle,
			tabs: TABS,
			activeTab,
			onTabChange: (k) => {
				activeTab = k;
				render(liveStateStore.state);
			},
			connectionStatus,
			hero: aflHero(state),
			body: state ? bodyFor(activeTab, session, state, leave) : waitingCard(),
		});
	};

	const unsubscribeState = liveStateStore.subscribe(render);
	const unsubscribeConn = liveStateStore.onConnection((status) => {
		connectionStatus = status;
		render(liveStateStore.state);
	});

	void liveStateStore.start(session.id).catch((err: unknown) => {
		const msg = err instanceof Error ? err.message : String(err);
		mount(
			host,
			h(
				"div",
				{ class: "phone-shell theme-afl p-6 text-sm text-accent-red" },
				`Couldn't load match: ${msg}`
			)
		);
	});
}

function bodyFor(
	tab: AflTabKey,
	session: GameSession,
	state: LiveState,
	onLeave: () => void
): HTMLElement {
	switch (tab) {
		case "score":
			return scoreClockTab(session.id, state);
		case "display":
			return displayTab(session.id, state);
		case "settings":
			return settingsTab(session.id, state);
		case "more":
			return moreTab(state, {
				sessionId: session.id,
				joinCode: session.joinCode,
				onLeave,
			});
	}
}

function waitingCard(): HTMLElement {
	return h(
		"div",
		{ class: "card text-center text-sm text-slate-400" },
		"Waiting for the desktop to send the first match snapshot…"
	);
}
