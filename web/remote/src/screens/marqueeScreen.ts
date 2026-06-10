// ─────────────────────────────────────────────────────────────────────────────
// marqueeScreen.ts
//
// Mirrors the desktop marquee card: live message list with add / remove,
// plus a Clear all button. The desktop is the source of truth so the list
// re-renders straight from the next live-state snapshot.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../ui/dom.js";
import { toast } from "../ui/toast.js";
import { submitEvent, EventTypes } from "../lib/events.js";
import type { LiveState } from "../contract/liveState.js";

export function marqueeScreen(sessionId: string, state: LiveState): HTMLElement {
	const input = h("input", {
		type: "text",
		class: "field-input",
		placeholder: "Type a message…",
		maxlength: 200,
	}) as HTMLInputElement;

	async function add(): Promise<void> {
		const text = input.value.trim();
		if (!text) return;
		try {
			await submitEvent({ sessionId, type: EventTypes.MessageAdd, payload: { text } });
			input.value = "";
			toast("Message queued");
		} catch (err) {
			toast(err instanceof Error ? err.message : String(err), "error");
		}
	}

	input.addEventListener("keydown", (e) => {
		if (e.key === "Enter") {
			e.preventDefault();
			void add();
		}
	});

	const list = h(
		"ul",
		{ class: "divide-y divide-ink-400/60" },
		...(state.messages.length === 0
			? [h("li", { class: "py-3 text-sm text-slate-500" }, "No messages")]
			: state.messages.map((text, idx) =>
				  h(
					  "li",
					  { class: "flex items-center gap-2 py-2 text-sm" },
					  h("span", { class: "flex-1 truncate text-slate-200" }, text),
					  h(
						  "button",
						  {
							  type: "button",
							  class: "btn-ghost px-3 py-1 text-xs",
							  onclick: async () => {
								  try {
									  await submitEvent({
										  sessionId,
										  type: EventTypes.MessageRemove,
										  payload: { index: idx },
									  });
									  toast("Message removed");
								  } catch (err) {
									  toast(err instanceof Error ? err.message : String(err), "error");
								  }
							  },
						  },
						  "Remove"
					  )
				  )
			  ))
	);

	return h(
		"div",
		{ class: "space-y-3" },
		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Add marquee message"),
			input,
			h(
				"button",
				{ type: "button", class: "btn-primary w-full", onclick: () => void add() },
				"Add message"
			)
		),

		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "flex items-center justify-between" },
				h("div", { class: "field-label" }, `Messages (${state.messages.length})`),
				h(
					"button",
					{
						type: "button",
						class: "btn-ghost px-3 py-1 text-xs",
						onclick: async () => {
							if (!confirm("Remove all messages?")) return;
							try {
								await submitEvent({
									sessionId,
									type: EventTypes.MessagesSet,
									payload: { messages: [] },
								});
								toast("Messages cleared");
							} catch (err) {
								toast(err instanceof Error ? err.message : String(err), "error");
							}
						},
					},
					"Clear all"
				)
			),
			list
		)
	);
}
