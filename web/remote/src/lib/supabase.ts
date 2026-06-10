// ─────────────────────────────────────────────────────────────────────────────
// supabase.ts
//
// Single Supabase client shared across the SPA. URL + anon key are baked
// at build time via Vite envs (see `vite.config.ts`).
// ─────────────────────────────────────────────────────────────────────────────

import { createClient, type SupabaseClient } from "@supabase/supabase-js";

declare const __SUPABASE_URL__: string;
declare const __SUPABASE_ANON_KEY__: string;

let _client: SupabaseClient | null = null;

export function getSupabase(): SupabaseClient {
	if (_client) return _client;

	const url = __SUPABASE_URL__;
	const key = __SUPABASE_ANON_KEY__;
	if (!url || !key) {
		throw new Error(
			"Supabase URL or anon key missing. Copy .env.example to .env and rebuild."
		);
	}
	_client = createClient(url, key, {
		auth: { persistSession: false },
		realtime: { params: { eventsPerSecond: 20 } },
	});
	return _client;
}
