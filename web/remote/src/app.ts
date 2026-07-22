// ─────────────────────────────────────────────────────────────────────────────
// app.ts
//
// Top-level shell. Two responsibilities:
//   1. Show the join screen until a session id + sport are resolved.
//   2. Boot either the AFL phone app or the Training phone app based on
//      `game_sessions.sport`. Each sport has its own self-contained
//      directory under `apps/` — no shared screens, no AFL leaking into
//      Training, no `"undefined"` rendering anywhere.
// ─────────────────────────────────────────────────────────────────────────────

import { mount } from "./ui/dom.js";
import { joinScreen } from "./screens/joinScreen.js";
import { runAflApp } from "./apps/afl/aflApp.js";
import { runTrainingApp } from "./apps/training/trainingApp.js";
import type { GameSession } from "./lib/session.js";
import type { Sport } from "./contract/events.js";

const SESSION_KEY = "roche.remote.session";

interface SessionStorageShape {
	id: string;
	joinCode: string;
	sport: Sport;
}

function loadSavedSession(): SessionStorageShape | null {
	try {
		const raw = localStorage.getItem(SESSION_KEY);
		if (!raw) return null;
		const parsed = JSON.parse(raw) as Partial<SessionStorageShape>;
		if (!parsed.id || !parsed.joinCode) return null;
		return {
			id: parsed.id,
			joinCode: parsed.joinCode,
			sport: parsed.sport === "training" ? "training" : "afl",
		};
	} catch {
		return null;
	}
}

function saveSession(session: GameSession): void {
	const sport: Sport = session.sport === "training" ? "training" : "afl";
	localStorage.setItem(
		SESSION_KEY,
		JSON.stringify({
			id: session.id,
			joinCode: session.joinCode,
			sport,
		} satisfies SessionStorageShape)
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
		startLive(host, {
			id: saved.id,
			joinCode: saved.joinCode,
			sport: saved.sport,
			status: "live",
		});
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
	const sport: Sport = session.sport === "training" ? "training" : "afl";
	const onLeave = (): void => {
		clearSession();
		history.replaceState(null, "", window.location.pathname);
		showJoin(host, "");
	};

	if (sport === "training") {
		runTrainingApp(host, session, onLeave);
	} else {
		runAflApp(host, session, onLeave);
	}
}

