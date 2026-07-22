// ─────────────────────────────────────────────────────────────────────────────
// fire.ts (Training)
//
// Training-specific event helper. Mirrors `apps/afl/fire.ts` but only
// pulls in the training event-type constants so the two apps stay
// completely independent.
// ─────────────────────────────────────────────────────────────────────────────

import { submitEvent, EventTypes } from "../../lib/events.js";
import { toast } from "../../ui/toast.js";
import type { EventType } from "../../contract/events.js";

export async function fireTraining(
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
