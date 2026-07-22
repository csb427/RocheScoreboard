// ─────────────────────────────────────────────────────────────────────────────
// moreTab.ts (AFL)
//
// AFL diagnostics + leave-session tab. Shows session metadata, the
// device label that gets attached to every event (so the desktop log can
// identify who pressed what), and a destructive "leave session" action.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../../../ui/dom.js";
import { toast } from "../../../ui/toast.js";
import { getClientId, getClientLabel, setClientLabel } from "../../../lib/events.js";
import { safeText, safeTime, EM_DASH } from "../../../lib/safe.js";
import type { LiveState } from "../../../contract/liveState.js";

export interface MoreDeps {
	sessionId: string;
	joinCode: string;
	onLeave: () => void;
}

export function moreTab(state: LiveState | null, deps: MoreDeps): HTMLElement {
	const labelInput = h("input", {
		type: "text",
		class: "field-input",
		value: getClientLabel(),
		maxlength: 32,
	}) as HTMLInputElement;

	const status = safeText(state?.sessionStatus, EM_DASH);
	const lastUpdate = state?.updatedAt ? safeTime(state.updatedAt) : EM_DASH;
	const joinCode = safeText(deps.joinCode, EM_DASH);
	const sessionId = safeText(deps.sessionId, EM_DASH);

	return h(
		"div",
		{ class: "space-y-3" },
		h(
			"section",
			{ class: "card space-y-2" },
			h("div", { class: "field-label" }, "Session"),
			kv("Join code", joinCode),
			kv("Game id", sessionId),
			kv("Status", status),
			kv("Last update", lastUpdate),
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost w-full",
					onclick: () => {
						if (!navigator.clipboard) {
							toast("Clipboard unavailable", "error");
							return;
						}
						navigator.clipboard.writeText(joinCode).then(
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
			h(
				"p",
				{ class: "text-xs text-slate-400" },
				"The desktop event log uses this label to credit each press."
			),
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
			kv("Client id", safeText(getClientId(), EM_DASH))
		),

		h(
			"section",
			{ class: "card space-y-2 border-accent-red/30" },
			h("div", { class: "field-label text-accent-red" }, "Leave session"),
			h(
				"p",
				{ class: "text-xs text-slate-300" },
				"Returns you to the join screen on this device only — the desktop session keeps running."
			),
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
