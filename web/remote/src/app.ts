// ─────────────────────────────────────────────────────────────────────────────
// app.ts
//
// Top-level shell. Boots the SPA in two stages:
//   1. JOIN  — show the join code form (prefilled if `?code=` was scanned).
//   2. LIVE  — once a session is resolved, mount the score header, the
//              active control-panel screen, and the bottom navigation.
//
// All screens are pure functions of `LiveState` so we just re-render
// whenever the live-state store fires.
// ─────────────────────────────────────────────────────────────────────────────

import { h, mount } from "./ui/dom.js";
import { scoreHeader } from "./ui/scoreHeader.js";
import { bottomNav, type ScreenKey } from "./ui/bottomNav.js";
import { joinScreen } from "./screens/joinScreen.js";
import { scoreScreen } from "./screens/scoreScreen.js";
import { clockScreen } from "./screens/clockScreen.js";
import { matchScreen } from "./screens/matchScreen.js";
import { teamsScreen } from "./screens/teamsScreen.js";
import { displayScreen } from "./screens/displayScreen.js";
import { marqueeScreen } from "./screens/marqueeScreen.js";
import { advancedScreen } from "./screens/advancedScreen.js";
import { liveStateStore } from "./state/liveStateStore.js";
import type { LiveState } from "./contract/liveState.js";
import type { GameSession } from "./lib/session.js";

const SESSION_KEY = "roche.remote.session";

interface SessionStorageShape {
	id: string;
	joinCode: string;
}

function loadSavedSession(): SessionStorageShape | null {
	try {
		const raw = localStorage.getItem(SESSION_KEY);
		if (!raw) return null;
		return JSON.parse(raw) as SessionStorageShape;
	} catch {
		return null;
	}
}

function saveSession(session: GameSession): void {
	localStorage.setItem(
		SESSION_KEY,
		JSON.stringify({ id: session.id, joinCode: session.joinCode } satisfies SessionStorageShape)
	);
}

function clearSession(): void {
	localStorage.removeItem(SESSION_KEY);
}

function readJoinCodeFromUrl(): string {
	const params = new URLSearchParams(window.location.search);
	return params.get("code")?.toUpperCase() ?? "";
}

export function bootApp(host: HTMLElement): void {
	const prefill = readJoinCodeFromUrl();
	const saved = loadSavedSession();

	if (saved && !prefill) {
		// Resume previous session — we don't re-validate against Supabase
		// here; the live-state store will surface a stale state and the
		// operator can leave from the More tab if needed.
		startLive(host, { id: saved.id, joinCode: saved.joinCode, sport: "afl", status: "live" });
		return;
	}

	showJoin(host, prefill);
}

function showJoin(host: HTMLElement, prefill: string): void {
	mount(
		host,
		joinScreen(prefill, (session) => {
			saveSession(session);
			startLive(host, session);
		})
	);
}

function startLive(host: HTMLElement, session: GameSession): void {
	let activeTab: ScreenKey = "score";
	let connectionStatus: "connecting" | "connected" | "disconnected" = "connecting";

	const render = (state: LiveState | null): void => {
		const shell = h(
			"div",
			{ class: "min-h-dvh pb-24" },
			scoreHeader(state),
			connectionBanner(connectionStatus),
			h(
				"main",
				{ class: "mx-auto max-w-3xl space-y-3 px-3 py-3" },
				state
					? screenFor(activeTab, session, state, () => leave(host))
					: h("div", { class: "card text-center text-sm text-slate-400" }, "Waiting for live state…")
			),
			bottomNav(activeTab, (key) => {
				activeTab = key;
				render(liveStateStore.state);
			})
		);
		mount(host, shell);
	};

	const unsubscribeState = liveStateStore.subscribe(render);
	const unsubscribeConn = liveStateStore.onConnection((status) => {
		connectionStatus = status;
		render(liveStateStore.state);
	});

	void liveStateStore.start(session.id).catch((err: unknown) => {
		const msg = err instanceof Error ? err.message : String(err);
		mount(host, h("div", { class: "p-6 text-sm text-accent-red" }, msg));
	});

	function leave(_h: HTMLElement): void {
		unsubscribeState();
		unsubscribeConn();
		void liveStateStore.stop();
		clearSession();
		// Strip ?code= so a refresh doesn't re-join automatically.
		history.replaceState(null, "", window.location.pathname);
		showJoin(host, "");
	}
}

function screenFor(
	tab: ScreenKey,
	session: GameSession,
	state: LiveState,
	onLeave: () => void
): HTMLElement {
	switch (tab) {
		case "score":
			return scoreScreen(session.id, state);
		case "clock":
			return clockScreen(session.id, state);
		case "match":
			return matchScreen(session.id, state);
		case "teams":
			return teamsScreen(session.id, state);
		case "display":
			return displayScreen(session.id, state);
		case "marquee":
			return marqueeScreen(session.id, state);
		case "advanced":
			return advancedScreen(state, {
				sessionId: session.id,
				joinCode: session.joinCode,
				onLeave,
			});
	}
}

function connectionBanner(status: "connecting" | "connected" | "disconnected"): HTMLElement | null {
	if (status === "connected") return null;
	return h(
		"div",
		{
			class: `px-3 py-1 text-center text-[11px] font-bold uppercase tracking-wide ${
				status === "connecting" ? "bg-accent-yellow/10 text-accent-yellow" : "bg-accent-red/10 text-accent-red"
			}`,
		},
		status === "connecting" ? "Connecting…" : "Disconnected — retrying"
	);
}
