// ─────────────────────────────────────────────────────────────────────────────
// liveStateStore.ts
//
// Tiny pub/sub store that holds the current `LiveState` for the active
// session. It seeds the value with one REST call and then keeps it fresh
// by subscribing to `postgres_changes` on `game_sessions` for the row
// matching our session id.
//
// We deliberately avoid a heavyweight state library — the SPA only has
// one game state and the desktop is the single writer.
// ─────────────────────────────────────────────────────────────────────────────

import { getSupabase } from "../lib/supabase.js";
import { fromWire, type LiveState, type LiveStateWire } from "../contract/liveState.js";

type Listener = (state: LiveState | null) => void;
type ConnectionListener = (status: "connecting" | "connected" | "disconnected") => void;

export class LiveStateStore {
	private _state: LiveState | null = null;
	private _listeners = new Set<Listener>();
	private _connectionListeners = new Set<ConnectionListener>();
	private _channel: ReturnType<ReturnType<typeof getSupabase>["channel"]> | null = null;
	private _sessionId: string | null = null;

	get state(): LiveState | null {
		return this._state;
	}

	subscribe(listener: Listener): () => void {
		this._listeners.add(listener);
		listener(this._state);
		return () => this._listeners.delete(listener);
	}

	onConnection(listener: ConnectionListener): () => void {
		this._connectionListeners.add(listener);
		return () => this._connectionListeners.delete(listener);
	}

	/**
	 * Boots the store: seed via REST, then attach realtime.
	 */
	async start(sessionId: string): Promise<void> {
		await this.stop();
		this._sessionId = sessionId;
		this.notifyConnection("connecting");

		const sb = getSupabase();
		const { data, error } = await sb
			.from("game_sessions")
			.select("live_state")
			.eq("id", sessionId)
			.maybeSingle();

		if (error) throw new Error(`Live-state seed failed: ${error.message}`);
		this.setFromWire(data?.live_state as LiveStateWire | null);

		this._channel = sb
			.channel(`game_sessions:${sessionId}`)
			.on(
				"postgres_changes",
				{
					event: "UPDATE",
					schema: "public",
					table: "game_sessions",
					filter: `id=eq.${sessionId}`,
				},
				(payload) => {
					const next = (payload.new as { live_state?: LiveStateWire | null })?.live_state;
					this.setFromWire(next ?? null);
				}
			)
			.subscribe((status) => {
				if (status === "SUBSCRIBED") this.notifyConnection("connected");
				else if (status === "CHANNEL_ERROR" || status === "TIMED_OUT" || status === "CLOSED") {
					this.notifyConnection("disconnected");
				}
			});
	}

	async stop(): Promise<void> {
		if (this._channel) {
			try {
				await this._channel.unsubscribe();
			} catch {
				// ignore — best effort during teardown
			}
			this._channel = null;
		}
		this._sessionId = null;
		this._state = null;
		this.notify();
		this.notifyConnection("disconnected");
	}

	private setFromWire(wire: LiveStateWire | null): void {
		this._state = wire ? fromWire(wire) : null;
		this.notify();
	}

	private notify(): void {
		for (const l of this._listeners) l(this._state);
	}

	private notifyConnection(status: "connecting" | "connected" | "disconnected"): void {
		for (const l of this._connectionListeners) l(status);
	}
}

export const liveStateStore = new LiveStateStore();
