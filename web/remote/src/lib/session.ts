// ─────────────────────────────────────────────────────────────────────────────
// session.ts
//
// Resolves a join code → game session row and exposes the session id used
// by every event submission and live-state subscription.
// ─────────────────────────────────────────────────────────────────────────────

import { getSupabase } from "./supabase.js";

export interface GameSession {
	id: string;
	joinCode: string;
	sport: string;
	status: string;
}

/** Fetches the active session matching the supplied join code. */
export async function findSessionByCode(code: string): Promise<GameSession | null> {
	const sb = getSupabase();
	const cleaned = code.trim().toUpperCase().replace(/\s+/g, "");
	if (!cleaned) return null;

	const { data, error } = await sb
		.from("game_sessions")
		.select("id, join_code, sport, status")
		.eq("join_code", cleaned)
		.neq("status", "ended")
		.limit(1)
		.maybeSingle();

	if (error) {
		throw new Error(`Lookup failed: ${error.message}`);
	}
	if (!data) return null;

	return {
		id: data.id as string,
		joinCode: data.join_code as string,
		sport: data.sport as string,
		status: data.status as string,
	};
}
