// ─────────────────────────────────────────────────────────────────────────────
// joinScreen.ts
//
// Login-less join screen — the user types or scans the join code shown
// on the desktop. On success we resolve the session id and hand control
// back to the app shell.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../ui/dom.js";
import { findSessionByCode, type GameSession } from "../lib/session.js";
import { toast } from "../ui/toast.js";

export function joinScreen(prefill: string, onJoined: (session: GameSession) => void): HTMLElement {
	const input = h("input", {
		type: "text",
		class: "field-input text-center font-mono text-2xl uppercase tracking-widest",
		placeholder: "ABCD-1234",
		autocomplete: "off",
		autocapitalize: "characters",
		spellcheck: "false",
		maxlength: 9,
		value: prefill,
	}) as HTMLInputElement;

	const submit = h(
		"button",
		{
			type: "submit",
			class: "btn-primary w-full",
		},
		"Join session"
	) as HTMLButtonElement;

	const form = h(
		"form",
		{
			class: "space-y-3",
			onsubmit: async (e: Event) => {
				e.preventDefault();
				submit.disabled = true;
				submit.textContent = "Looking up…";
				try {
					const session = await findSessionByCode(input.value);
					if (!session) {
						toast("Join code not found", "error");
						return;
					}
					onJoined(session);
				} catch (err) {
					toast(err instanceof Error ? err.message : String(err), "error");
				} finally {
					submit.disabled = false;
					submit.textContent = "Join session";
				}
			},
		},
		h(
			"label",
			{ class: "block" },
			h("span", { class: "field-label" }, "Join code"),
			input
		),
		submit
	);

	return h(
		"main",
		{ class: "mx-auto flex min-h-dvh max-w-md flex-col justify-center p-6" },
		h(
			"div",
			{ class: "card space-y-4" },
			h(
				"header",
				{ class: "space-y-1 text-center" },
				h("div", { class: "text-3xl" }, "🏉"),
				h("h1", { class: "text-xl font-black text-accent-blue" }, "Roche Remote"),
				h("p", { class: "text-xs text-slate-400" }, "Type the join code shown on the desktop scoreboard.")
			),
			form
		)
	);
}
