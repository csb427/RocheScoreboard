// ─────────────────────────────────────────────────────────────────────────────
// trainingHero.ts
//
// Training-only sticky chip rendered inside the phone shell's top bar:
// the active timer's title, the big remaining countdown, and the
// session-total remaining time. Defensive against partial wire payloads
// and the "no active timer" state.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../../ui/dom.js";
import { safeText, safeMinSec, intOr } from "../../lib/safe.js";
import type { LiveState } from "../../contract/liveState.js";

export function trainingHero(state: LiveState | null): HTMLElement | null {
	if (!state) {
		return h(
			"div",
			{ class: "text-center text-[11px] text-slate-400" },
			"Waiting for training data…"
		);
	}

	const t = state.training;
	const hasActive = t.activeIndex >= 0 && t.activeTitle.length > 0;
	const activeTitle = hasActive ? safeText(t.activeTitle, "Active timer") : "No active timer";
	const remaining = safeMinSec(t.activeRemainingSeconds);
	const sessionRemaining = safeMinSec(t.sessionRemainingSeconds);
	const running = t.isRunning === true;
	const queueCount = intOr(t.timers.length, 0);

	return h(
		"div",
		{ class: "flex flex-col gap-1.5" },
		h(
			"div",
			{ class: "flex items-center justify-between gap-2" },
			h(
				"div",
				{ class: "min-w-0 truncate text-xs font-bold text-slate-200" },
				activeTitle
			),
			h(
				"span",
				{
					class: "pill",
					style: `background:${running ? "rgba(63,185,80,0.18)" : "rgba(148,163,184,0.18)"};color:${running ? "#3FB950" : "#94A3B8"}`,
				},
				running ? "● RUNNING" : "○ PAUSED"
			)
		),
		h(
			"div",
			{
				class: "text-center font-mono text-5xl font-black numeric-hero",
				style: "color:var(--accent)",
			},
			remaining
		),
		h(
			"div",
			{
				class:
					"flex items-center justify-between text-[10px] font-bold uppercase tracking-wide text-slate-400",
			},
			h("span", null, `Queue: ${queueCount}`),
			h("span", null, `Total left: ${sessionRemaining}`)
		)
	);
}
