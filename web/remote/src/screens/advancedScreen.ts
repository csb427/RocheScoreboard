// ─────────────────────────────────────────────────────────────────────────────
// advancedScreen.ts
//
// "More" tab: device label + diagnostics, session info, copy join code,
// and the leave-session button.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../ui/dom.js";
import { toast } from "../ui/toast.js";
import { getClientId, getClientLabel, setClientLabel } from "../lib/events.js";
import type { LiveState } from "../contract/liveState.js";

export interface AdvancedDeps {
	sessionId: string;
	joinCode: string;
	onLeave: () => void;
}

export function advancedScreen(state: LiveState | null, deps: AdvancedDeps): HTMLElement {
	const labelInput = h("input", {
		type: "text",
		class: "field-input",
		value: getClientLabel(),
		maxlength: 32,
	}) as HTMLInputElement;

	return h(
		"div",
		{ class: "space-y-3" },
		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Session"),
			kv("Join code", deps.joinCode),
			kv("Game id", deps.sessionId),
			kv("Status", state?.sessionStatus ?? "—"),
			kv("Last update", state?.updatedAt ?? "—"),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost w-full",
					onclick: () => {
						navigator.clipboard?.writeText(deps.joinCode).then(
							() => toast("Join code copied"),
							() => toast("Copy failed", "error")
						);
					},
				},
				"Copy join code"
			)
		),

		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "This device"),
			h("p", { class: "text-xs text-slate-400" }, "The desktop event log uses this label to credit each press."),
			labelInput,
			h(
				"button",
				{
					type: "button",
					class: "btn-primary w-full",
					onclick: () => {
						setClientLabel(labelInput.value);
						toast("Device label saved");
					},
				},
				"Save label"
			),
			kv("Client id", getClientId())
		),

		h(
			"section",
			{ class: "card space-y-2 border-accent-red/30 bg-accent-red/5" },
			h("div", { class: "field-label text-accent-red" }, "Leave session"),
			h("p", { class: "text-xs text-slate-300" }, "Returns you to the join screen on this device only — the desktop session keeps running."),
			h(
				"button",
				{
					type: "button",
					class: "btn-danger w-full",
					onclick: deps.onLeave,
				},
				"Leave session"
			)
		)
	);
}

function kv(label: string, value: string): HTMLElement {
	return h(
		"div",
		{ class: "flex items-center justify-between gap-3 text-sm" },
		h("span", { class: "text-slate-400" }, label),
		h("span", { class: "truncate font-mono text-slate-200" }, value)
	);
}
