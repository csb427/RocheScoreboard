// ─────────────────────────────────────────────────────────────────────────────
// trainingApp.ts
//
// Orchestrates the Training phone experience. The mobile UI is a near
// 1:1 port of the desktop Training control panel — every section the
// desktop shows is also on the phone, just stacked vertically.
//
// Tabs:
//   • Control — now playing, transport, adjust, queue, add timer,
//               recent timers, interval builder, and exit-training.
//   • More    — diagnostics / device label / leave session.
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
import { safeMinSec, safeText } from "../../lib/safe.js";
import { trainingHero } from "./trainingHero.js";
import { controlTab } from "./screens/controlTab.js";
import { moreTab } from "./screens/moreTab.js";

type TrainingTabKey = "control" | "more";

const TABS: TabDef<TrainingTabKey>[] = [
	{ key: "control", label: "Control", icon: "⏱" },
	{ key: "more", label: "More", icon: "•••" },
];

export function runTrainingApp(
	host: HTMLElement,
	session: GameSession,
	onLeave: () => void
): void {
	let activeTab: TrainingTabKey = "control";
	let connectionStatus: ConnectionStatus = "connecting";
	let cleanedUp = false;

	const onLocalRefresh = (): void => render(liveStateStore.state);

	const leave = (): void => {
		if (cleanedUp) return;
		cleanedUp = true;
		document.removeEventListener("roche:training:requeue", onLocalRefresh);
		unsubscribeState();
		unsubscribeConn();
		void liveStateStore.stop();
		onLeave();
	};

	const render = (state: LiveState | null): void => {
		const subtitle = state
			? `${safeText(state.training.activeTitle, "Idle")} · ${safeMinSec(state.training.activeRemainingSeconds)}`
			: "Waiting for desktop…";

		renderPhoneShell<TrainingTabKey>(host, {
			sport: "training",
			title: "Training",
			subtitle,
			tabs: TABS,
			activeTab,
			onTabChange: (k) => {
				activeTab = k;
				render(liveStateStore.state);
			},
			connectionStatus,
			hero: trainingHero(state),
			body: state ? bodyFor(activeTab, session, state, leave) : waitingCard(),
		});
	};

	const unsubscribeState = liveStateStore.subscribe(render);
	const unsubscribeConn = liveStateStore.onConnection((status) => {
		connectionStatus = status;
		render(liveStateStore.state);
	});

	// controlTab keeps a per-row "is being edited" flag at module scope
	// and fires this event when the operator opens/closes an inline edit.
	// We rebuild the entire phone shell without mutating the server-owned
	// live state.
	document.addEventListener("roche:training:requeue", onLocalRefresh);

	void liveStateStore.start(session.id).catch((err: unknown) => {
		const msg = err instanceof Error ? err.message : String(err);
		mount(
			host,
			h(
				"div",
				{ class: "phone-shell theme-training p-6 text-sm text-accent-red" },
				`Couldn't load training session: ${msg}`
			)
		);
	});
}

function bodyFor(
	tab: TrainingTabKey,
	session: GameSession,
	state: LiveState,
	onLeave: () => void
): HTMLElement {
	switch (tab) {
		case "control":
			return controlTab(session.id, state, onLeave);
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
		"Waiting for the desktop to send the first training snapshot…"
	);
}
