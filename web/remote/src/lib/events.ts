// ─────────────────────────────────────────────────────────────────────────────
// events.ts (lib)
//
// Submits a `RemoteEvent` to the Supabase `game_events` table. Each event
// is one INSERT — the desktop host's realtime subscription picks it up
// and dispatches it to the existing operator click handlers.
//
// Each remote device gets a sticky `clientId` (UUID v4 in localStorage)
// and a friendly `clientLabel` so the desktop event log can identify who
// pressed what.
// ─────────────────────────────────────────────────────────────────────────────

import { getSupabase } from "./supabase.js";
import { EventTypes, type EventType } from "../contract/events.js";

const CLIENT_ID_KEY = "roche.remote.clientId";
const CLIENT_LABEL_KEY = "roche.remote.clientLabel";

export function getClientId(): string {
	let existing = localStorage.getItem(CLIENT_ID_KEY);
	if (existing) return existing;
	existing = crypto.randomUUID();
	localStorage.setItem(CLIENT_ID_KEY, existing);
	return existing;
}

export function getClientLabel(): string {
	return localStorage.getItem(CLIENT_LABEL_KEY) ?? defaultLabel();
}

export function setClientLabel(label: string): void {
	const trimmed = label.trim();
	if (!trimmed) {
		localStorage.removeItem(CLIENT_LABEL_KEY);
		return;
	}
	localStorage.setItem(CLIENT_LABEL_KEY, trimmed.slice(0, 64));
}

function defaultLabel(): string {
	const ua = navigator.userAgent;
	if (/iPhone/i.test(ua)) return "iPhone";
	if (/iPad/i.test(ua)) return "iPad";
	if (/Android/i.test(ua)) return "Android phone";
	if (/Macintosh/i.test(ua)) return "Mac";
	if (/Windows/i.test(ua)) return "Windows";
	return "Remote";
}

export interface SubmitOptions {
	sessionId: string;
	type: EventType;
	payload?: unknown;
}

export async function submitEvent(opts: SubmitOptions): Promise<void> {
	const sb = getSupabase();
	const row = {
		id: crypto.randomUUID(),
		session_id: opts.sessionId,
		event_type: opts.type,
		payload: opts.payload ?? {},
		client_id: getClientId(),
		client_label: getClientLabel(),
	};
	const { error } = await sb.from("game_events").insert(row);
	if (error) {
		throw new Error(`Submit failed (${opts.type}): ${error.message}`);
	}
}

export { EventTypes };
