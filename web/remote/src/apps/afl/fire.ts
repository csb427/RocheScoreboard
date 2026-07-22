// ─────────────────────────────────────────────────────────────────────────────
// fire.ts (AFL)
//
// AFL-specific event helper that wraps every cloud event submission in a
// try/catch + toast. Used by every AFL screen module so each callback
// stays a one-liner and we never let an `await` reject bubble up to the
// shell.
// ─────────────────────────────────────────────────────────────────────────────

import { submitEvent, EventTypes } from "../../lib/events.js";
import { toast } from "../../ui/toast.js";
import type { EventType } from "../../contract/events.js";

export async function fireAfl(
	sessionId: string,
	type: EventType,
	successLabel: string,
	payload?: unknown
): Promise<void> {
	try {
		await submitEvent({ sessionId, type, payload });
		if (typeof navigator !== "undefined" && navigator.vibrate) {
			navigator.vibrate(8);
		}
		toast(successLabel);
	} catch (err) {
		toast(err instanceof Error ? err.message : String(err), "error");
	}
}

export { EventTypes };
