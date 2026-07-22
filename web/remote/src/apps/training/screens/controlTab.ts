// ─────────────────────────────────────────────────────────────────────────────
// controlTab.ts (Training)
//
// Phone-fitted port of the entire desktop Training control panel. The
// sections are stacked top-down in the same order an operator scans on
// the PC:
//   1. NOW PLAYING       — active title + big countdown + status pill.
//   2. TRANSPORT         — Start/Pause (color shifts), Reset, Skip.
//   3. ADJUST            — +1:00 / +0:10 quick adds to the active timer.
//   4. QUEUE             — auto-advance toggle, clear-all, per-row
//                          index/title/duration + reorder/edit/remove.
//   5. ADD A TIMER       — title + minutes + seconds + Add to queue.
//   6. RECENT TIMERS     — chips for phone-local MRU, tap to re-add.
//   7. INTERVAL BUILDER  — rounds + work + rest, builds N timers.
//   8. EXIT TRAINING     — same exit hook as the desktop “Exit” button.
//
// Module-level `editingTimerId` / `editingDraft` survive across renders
// so the inline edit row stays open while the desktop pushes ticks.
// ─────────────────────────────────────────────────────────────────────────────

import { h } from "../../../ui/dom.js";
import { fireTraining, EventTypes } from "../fire.js";
import { safeText, safeMinSec, intOr, EM_DASH } from "../../../lib/safe.js";
import type { LiveState, TrainingTimer } from "../../../contract/liveState.js";
import { loadRecent, recordRecent, type RecentTimerEntry } from "../../../lib/recentTimers.js";

// Held across renders so editing a row survives the next state push. Cleared
// automatically when the row disappears from the queue or finishes, and
// explicitly when the user hits Save / Cancel.
let editingTimerId: string | null = null;
let editingDraft: { title: string; minutes: string; seconds: string } | null = null;

export function controlTab(
	sessionId: string,
	state: LiveState,
	onLeave: () => void
): HTMLElement {
	if (editingTimerId !== null) {
		const stillThere = state.training.timers.some(
			(t) => t.id === editingTimerId && t.hasFinished !== true
		);
		if (!stillThere) {
			editingTimerId = null;
			editingDraft = null;
		}
	}

	return h(
		"div",
		{ class: "space-y-3" },
		activeCard(state),
		transportCard(sessionId, state),
		adjustCard(sessionId, state),
		queueCard(sessionId, state),
		addTimerCard(sessionId),
		recentTimersCard(sessionId),
		intervalBuilderCard(sessionId),
		exitCard(sessionId, onLeave)
	);
}

function activeCard(state: LiveState): HTMLElement {
	const t = state.training;
	const hasActive = t.activeIndex >= 0 && t.activeTitle.length > 0;
	const title = hasActive ? safeText(t.activeTitle, "Active timer") : "No timer";
	const remaining = safeMinSec(t.activeRemainingSeconds);
	const totalLeft = safeMinSec(t.sessionRemainingSeconds);

	let statusLabel: string;
	let statusColor: string;
	let statusBg: string;
	if (!hasActive) {
		statusLabel = "READY";
		statusColor = "#58A6FF";
		statusBg = "rgba(88,166,255,0.18)";
	} else if (t.activeRemainingSeconds <= 0) {
		statusLabel = "FINISHED";
		statusColor = "#FF6B6B";
		statusBg = "rgba(255,107,107,0.18)";
	} else if (!t.isRunning) {
		statusLabel = "PAUSED";
		statusColor = "#FBBF24";
		statusBg = "rgba(251,191,36,0.18)";
	} else {
		statusLabel = "RUNNING";
		statusColor = "#3FB950";
		statusBg = "rgba(63,185,80,0.18)";
	}

	return h(
		"section",
		{ class: "card space-y-3" },
		h(
			"div",
			{ class: "flex items-center justify-between" },
			h(
				"div",
				{ class: "text-[10px] font-black uppercase tracking-wider text-slate-400" },
				"Now playing"
			),
			h(
				"span",
				{ class: "pill", style: "background:" + statusBg + ";color:" + statusColor },
				statusLabel
			)
		),
		h("div", { class: "truncate text-lg font-black text-slate-100" }, title),
		h(
			"div",
			{
				class:
					"text-center font-mono text-[64px] font-black leading-none numeric-hero",
				style: "color:" + statusColor + ";text-shadow:0 0 22px " + statusBg,
			},
			remaining
		),
		h(
			"div",
			{
				class:
					"flex items-center justify-between text-[11px] uppercase tracking-wide text-slate-400",
			},
			h("span", null, hasActive ? "Active timer" : "Add a timer below"),
			h("span", null, "Total left " + totalLeft)
		)
	);
}

function transportCard(sessionId: string, state: LiveState): HTMLElement {
	const t = state.training;
	const hasActive =
		t.activeIndex >= 0 && t.activeTitle.length > 0 && t.activeRemainingSeconds > 0;
	const running = t.isRunning === true;
	const primaryLabel = running ? "\u23F8 Pause" : "\u25B6 Start";
	const primaryClass = running ? "btn-danger" : "btn-success";

	return h(
		"section",
		{ class: "card space-y-2" },
		h("div", { class: "field-label" }, "Transport"),
		h(
			"button",
			{
				type: "button",
				class: primaryClass + " w-full text-base",
				disabled: !hasActive,
				onclick: () =>
					fireTraining(
						sessionId,
						EventTypes.TrainingStartPause,
						running ? "Paused" : "Started"
					),
			},
			primaryLabel
		),
		h(
			"div",
			{ class: "grid grid-cols-2 gap-2" },
			h(
				"button",
				{
					type: "button",
					class: "btn-ghost",
					disabled: !hasActive,
					onclick: () =>
						fireTraining(sessionId, EventTypes.TrainingResetActive, "Reset active"),
				},
				"\u21BA Reset"
			),
			h(
				"button",
				{
					type: "button",
					class: "btn-warn",
					disabled: !hasActive,
					onclick: () => fireTraining(sessionId, EventTypes.TrainingSkip, "Skipped"),
				},
				"\u23ED Skip"
			)
		)
	);
}

function adjustCard(sessionId: string, state: LiveState): HTMLElement {
	const t = state.training;
	const hasActive =
		t.activeIndex >= 0 && t.activeTitle.length > 0 && t.activeRemainingSeconds > 0;

	const mk = (label: string, seconds: number, successLabel: string): HTMLElement =>
		h(
			"button",
			{
				type: "button",
				class: "btn-primary text-base",
				disabled: !hasActive,
				onclick: () =>
					fireTraining(sessionId, EventTypes.TrainingAdjustActive, successLabel, {
						seconds,
					}),
			},
			label
		);

	return h(
		"section",
		{ class: "card space-y-2" },
		h("div", { class: "field-label" }, "Adjust active timer"),
		h(
			"div",
			{ class: "grid grid-cols-2 gap-2" },
			mk("+1:00", 60, "Added 1 min"),
			mk("+0:10", 10, "Added 10 sec")
		)
	);
}

function queueCard(sessionId: string, state: LiveState): HTMLElement {
	const timers = state.training.timers;
	const activeIndex = intOr(state.training.activeIndex, -1);
	const autoAdvance = state.training.autoAdvance === true;

	const header = h(
		"div",
		{ class: "flex items-center justify-between gap-2" },
		h(
			"div",
			{ class: "flex items-center gap-2" },
			h(
				"div",
				{ class: "text-[11px] font-black uppercase tracking-wide text-slate-400" },
				"Queue"
			),
			h(
				"span",
				{
					class: "pill",
					style: "background:rgba(148,163,184,0.18);color:#94A3B8",
				},
				String(timers.length)
			)
		),
		timers.length > 0
			? h(
					"button",
					{
						type: "button",
						class: "btn-danger px-3 py-2 text-[11px]",
						onclick: () => {
							if (!confirm("Clear all timers?")) return;
							void fireTraining(
								sessionId,
								EventTypes.TrainingClearQueue,
								"Queue cleared"
							);
						},
					},
					"Clear all"
				)
			: null
	);

	const autoToggle = h(
		"label",
		{
			class:
				"flex items-center justify-between gap-3 rounded-xl border border-ink-400 bg-ink-700/60 px-3 py-2.5",
		},
		h(
			"span",
			{ class: "text-[11px] font-black uppercase tracking-wide text-slate-200" },
			"Auto-advance"
		),
		toggleSwitch(autoAdvance, (next) =>
			fireTraining(
				sessionId,
				EventTypes.TrainingSetAutoAdvance,
				next ? "Auto-advance ON" : "Auto-advance OFF",
				{ enabled: next }
			)
		)
	);

	const list =
		timers.length === 0
			? h(
					"div",
					{
						class:
							"rounded-xl border border-dashed border-ink-400 bg-ink-800/40 px-3 py-6 text-center text-sm text-slate-500",
					},
					"Queue is empty - add a timer from the Build tab."
				)
			: h(
					"ul",
					{ class: "space-y-2" },
					...timers.map((timer, idx) =>
						queueRow(sessionId, timer, idx, idx === activeIndex)
					)
				);

	return h(
		"section",
		{ class: "card space-y-3" },
		header,
		autoToggle,
		h(
			"div",
			{ class: "text-[10px] uppercase tracking-wide text-slate-500" },
			"Tap to edit, reorder, or remove"
		),
		list
	);
}

function toggleSwitch(on: boolean, onChange: (next: boolean) => void): HTMLElement {
	return h(
		"button",
		{
			type: "button",
			class:
				"relative inline-flex h-7 w-12 shrink-0 rounded-full border transition " +
				(on
					? "border-accent-green/60 bg-accent-green/30"
					: "border-ink-400 bg-ink-800"),
			onclick: () => onChange(!on),
			"aria-pressed": on ? "true" : "false",
		},
		h("span", {
			class:
				"absolute top-0.5 h-6 w-6 rounded-full bg-slate-100 shadow transition-all " +
				(on ? "left-[22px]" : "left-0.5"),
		})
	);
}

function queueRow(
	sessionId: string,
	timer: TrainingTimer,
	index: number,
	isActive: boolean
): HTMLElement {
	const finished = timer.hasFinished === true;
	const editable = !finished;
	const id = safeText(timer.id, EM_DASH);

	if (editingTimerId === id && editable) {
		return editRow(sessionId, timer, index);
	}

	const accent = isActive ? "#FBBF24" : finished ? "#374151" : "#58A6FF";
	const title = safeText(timer.title, "Timer");
	const duration = safeMinSec(timer.totalSeconds);

	const indexPill = h(
		"span",
		{
			class:
				"inline-flex shrink-0 items-center justify-center rounded-md border px-2 py-0.5 font-mono text-[11px] font-black",
			style:
				"border-color:" + accent + ";color:" + accent + ";background:rgba(15,23,42,0.65)",
		},
		"#" + (index + 1)
	);

	const titleBlock = h(
		"div",
		{ class: "min-w-0 flex-1" },
		h(
			"div",
			{
				class:
					"truncate text-sm font-black " +
					(finished ? "text-slate-500 line-through" : "text-slate-100"),
			},
			title
		),
		isActive
			? h(
					"div",
					{
						class: "mt-0.5 text-[10px] font-black uppercase tracking-wider",
						style: "color:#FBBF24",
					},
					"Now playing"
				)
			: finished
				? h(
						"div",
						{
							class:
								"mt-0.5 text-[10px] font-black uppercase tracking-wider text-slate-500",
						},
						"Done"
					)
				: null
	);

	const durationColor = isActive ? "#FBBF24" : finished ? "#6B7280" : "#E5E7EB";
	const durationBlock = h(
		"div",
		{
			class: "shrink-0 font-mono text-base font-black numeric-hero",
			style: "color:" + durationColor,
		},
		duration
	);

	const actions = h(
		"div",
		{ class: "flex shrink-0 items-center gap-1" },
		actionChip("\u25B2", "Move up", () =>
			fireTraining(sessionId, EventTypes.TrainingReorder, "Moved up", {
				id,
				direction: "up",
			})
		),
		actionChip("\u25BC", "Move down", () =>
			fireTraining(sessionId, EventTypes.TrainingReorder, "Moved down", {
				id,
				direction: "down",
			})
		),
		editable
			? actionChip("\u270E", "Edit", () => {
					editingTimerId = id;
					editingDraft = {
						title,
						minutes: String(Math.trunc(timer.totalSeconds / 60)),
						seconds: String(timer.totalSeconds % 60).padStart(2, "0"),
					};
					queueRowRefresh();
				})
			: null,
		actionChip(
			"\u2715",
			"Remove",
			() => {
				if (!confirm('Remove "' + title + '"?')) return;
				void fireTraining(sessionId, EventTypes.TrainingRemoveTimer, "Removed", { id });
			},
			"text-accent-red"
		)
	);

	const wrapperClass = isActive
		? "border-accent-yellow/60 bg-accent-yellow/10"
		: finished
			? "border-ink-400 bg-ink-800/40 opacity-60"
			: "border-ink-400 bg-ink-800";

	return h(
		"li",
		{ class: "flex items-center gap-2 rounded-xl border px-2.5 py-2 " + wrapperClass },
		indexPill,
		titleBlock,
		durationBlock,
		actions
	);
}

function editRow(sessionId: string, timer: TrainingTimer, index: number): HTMLElement {
	const id = safeText(timer.id, EM_DASH);
	const draft = editingDraft ?? {
		title: safeText(timer.title, "Timer"),
		minutes: String(Math.trunc(timer.totalSeconds / 60)),
		seconds: String(timer.totalSeconds % 60).padStart(2, "0"),
	};
	editingDraft = draft;

	const titleInput = h("input", {
		type: "text",
		class: "field-input flex-1 text-sm",
		value: draft.title,
		maxlength: 64,
		"aria-label": "Timer title",
	}) as HTMLInputElement;

	const minutesInput = h("input", {
		type: "number",
		min: "0",
		max: "99",
		inputmode: "numeric",
		class: "field-input w-16 text-center font-mono text-base",
		value: draft.minutes,
		"aria-label": "Minutes",
	}) as HTMLInputElement;

	const secondsInput = h("input", {
		type: "number",
		min: "0",
		max: "59",
		inputmode: "numeric",
		class: "field-input w-16 text-center font-mono text-base",
		value: draft.seconds,
		"aria-label": "Seconds",
	}) as HTMLInputElement;

	const captureDraft = (): void => {
		editingDraft = {
			title: titleInput.value,
			minutes: minutesInput.value,
			seconds: secondsInput.value,
		};
	};
	titleInput.addEventListener("input", captureDraft);
	minutesInput.addEventListener("input", captureDraft);
	secondsInput.addEventListener("input", captureDraft);

	const commit = (): void => {
		const t = titleInput.value.trim() || "Timer";
		const m = clampInt(minutesInput.value, 0, 99, 0);
		const s = clampInt(secondsInput.value, 0, 59, 0);
		const total = Math.max(1, m * 60 + s);
		void fireTraining(sessionId, EventTypes.TrainingUpdateTimer, "Timer updated", {
			id,
			title: t,
			seconds: total,
		});
		editingTimerId = null;
		editingDraft = null;
		queueRowRefresh();
	};

	const cancel = (): void => {
		editingTimerId = null;
		editingDraft = null;
		queueRowRefresh();
	};

	const onKey = (e: KeyboardEvent): void => {
		if (e.key === "Enter") {
			e.preventDefault();
			commit();
		} else if (e.key === "Escape") {
			e.preventDefault();
			cancel();
		}
	};
	titleInput.addEventListener("keydown", onKey);
	minutesInput.addEventListener("keydown", onKey);
	secondsInput.addEventListener("keydown", onKey);

	queueMicrotask(() => {
		try {
			titleInput.focus();
			titleInput.select();
		} catch {
			// best-effort
		}
	});

	return h(
		"li",
		{
			class:
				"space-y-2 rounded-xl border border-accent-yellow/60 bg-accent-yellow/10 p-3",
		},
		h(
			"div",
			{ class: "flex items-center gap-2" },
			h(
				"span",
				{
					class:
						"inline-flex shrink-0 items-center justify-center rounded-md border px-2 py-0.5 font-mono text-[11px] font-black",
					style:
						"border-color:#FBBF24;color:#FBBF24;background:rgba(15,23,42,0.65)",
				},
				"#" + (index + 1)
			),
			titleInput
		),
		h(
			"div",
			{ class: "flex items-center gap-2" },
			h(
				"div",
				{ class: "flex items-center gap-1 text-sm font-black text-slate-200" },
				minutesInput,
				h("span", { class: "font-mono" }, ":"),
				secondsInput
			),
			h("span", { class: "flex-1" }),
			h(
				"button",
				{ type: "button", class: "btn-ghost px-3 py-2 text-xs", onclick: cancel },
				"Cancel"
			),
			h(
				"button",
				{ type: "button", class: "btn-success px-3 py-2 text-xs", onclick: commit },
				"Save"
			)
		)
	);
}

function actionChip(
	label: string,
	tooltip: string,
	onClick: () => void,
	extra: string = ""
): HTMLElement {
	return h(
		"button",
		{
			type: "button",
			class: "chip px-2 " + extra,
			title: tooltip,
			"aria-label": tooltip,
			onclick: onClick,
		},
		label
	);
}

function clampInt(value: string, min: number, max: number, fallback: number): number {
	const n = Math.trunc(Number(value));
	if (!Number.isFinite(n)) return fallback;
	return Math.max(min, Math.min(max, n));
}

function queueRowRefresh(): void {
	if (typeof document === "undefined") return;
	document.dispatchEvent(new CustomEvent("roche:training:requeue"));
}

// ─────────── Add timer / recent / interval / exit cards ───────────

function addTimerCard(sessionId: string): HTMLElement {
	const titleInput = h("input", {
		type: "text",
		class: "field-input",
		placeholder: "e.g. Warm-up",
		maxlength: 64,
		value: "",
	}) as HTMLInputElement;

	const minutesInput = numberInput("5", 0, 99);
	const secondsInput = numberInput("0", 0, 59);

	const submit = (): void => {
		const t = titleInput.value.trim() || "Timer";
		const m = clampInt(minutesInput.value, 0, 99, 0);
		const s = clampInt(secondsInput.value, 0, 59, 0);
		const total = m * 60 + s;
		if (total <= 0) return;
		recordRecent(t, total);
		void fireTraining(sessionId, EventTypes.TrainingAddTimer, "Added " + t, {
			title: t,
			seconds: total,
		}).then(() => {
			titleInput.value = "";
			queueRowRefresh();
		});
	};

	const onEnter = (e: KeyboardEvent): void => {
		if (e.key === "Enter") {
			e.preventDefault();
			submit();
		}
	};
	titleInput.addEventListener("keydown", onEnter);
	minutesInput.addEventListener("keydown", onEnter);
	secondsInput.addEventListener("keydown", onEnter);

	return h(
		"section",
		{ class: "card space-y-3" },
		sectionHeader("Add a timer", "#58A6FF", "Create one custom interval and append it to the queue."),
		h(
			"label",
			{ class: "block" },
			h("span", { class: "field-label" }, "Title"),
			titleInput
		),
		h(
			"div",
			{ class: "grid grid-cols-2 gap-2" },
			h(
				"label",
				{ class: "block" },
				h("span", { class: "field-label" }, "Minutes"),
				minutesInput
			),
			h(
				"label",
				{ class: "block" },
				h("span", { class: "field-label" }, "Seconds"),
				secondsInput
			)
		),
		h(
			"button",
			{ type: "button", class: "btn-primary w-full", onclick: submit },
			"+ Add to queue"
		)
	);
}

function recentTimersCard(sessionId: string): HTMLElement {
	const entries = loadRecent();
	const header = sectionHeader(
		"Recent timers",
		"#3FB950",
		"Your last-used timers — tap to append. Stored on this device."
	);

	if (entries.length === 0) {
		return h(
			"section",
			{ class: "card space-y-2" },
			header,
			h(
				"p",
				{ class: "text-xs italic text-slate-400" },
				"No recent timers yet — add one above and it will appear here."
			)
		);
	}

	const chips = h(
		"div",
		{ class: "flex flex-wrap gap-2" },
		...entries.map((entry: RecentTimerEntry) => recentChip(sessionId, entry))
	);

	return h("section", { class: "card space-y-2" }, header, chips);
}

function recentChip(sessionId: string, entry: RecentTimerEntry): HTMLElement {
	const mins = Math.trunc(entry.seconds / 60);
	const secs = entry.seconds % 60;
	const label = entry.title + " · " + mins + ":" + String(secs).padStart(2, "0");
	return h(
		"button",
		{
			type: "button",
			class:
				"inline-flex items-center gap-1 rounded-xl border border-ink-400 bg-ink-800 px-3 py-2 text-xs font-bold text-slate-200 transition active:scale-95",
			onclick: () => {
				recordRecent(entry.title, entry.seconds);
				void fireTraining(
					sessionId,
					EventTypes.TrainingAddTimer,
					"Added " + entry.title,
					{ title: entry.title, seconds: entry.seconds }
				).then(() => queueRowRefresh());
			},
		},
		label
	);
}

function intervalBuilderCard(sessionId: string): HTMLElement {
	const prefixInput = h("input", {
		type: "text",
		class: "field-input",
		placeholder: "Set",
		maxlength: 32,
		value: "Set",
	}) as HTMLInputElement;
	const roundsInput = numberInput("8", 1, 99);
	const workInput = numberInput("20", 1, 600);
	const restInput = numberInput("10", 0, 600);

	const build = (): void => {
		const prefix = prefixInput.value.trim() || "Set";
		const r = clampInt(roundsInput.value, 1, 99, 1);
		const w = clampInt(workInput.value, 1, 600, 20);
		const rest = clampInt(restInput.value, 0, 600, 0);
		void fireTraining(
			sessionId,
			EventTypes.TrainingAddTimer,
			"Built " + r + "\u00d7" + w + "/" + rest,
			{
				interval: {
					rounds: r,
					workSeconds: w,
					restSeconds: rest,
					titlePrefix: prefix,
				},
			}
		).then(() => queueRowRefresh());
	};

	return h(
		"section",
		{ class: "card space-y-3" },
		sectionHeader(
			"Interval builder",
			"#FBBF24",
			"Tabata-style work / rest rounds. Each round adds two timers."
		),
		h(
			"label",
			{ class: "block" },
			h("span", { class: "field-label" }, "Title prefix"),
			prefixInput
		),
		h(
			"div",
			{ class: "grid grid-cols-3 gap-2" },
			h(
				"label",
				{ class: "block" },
				h("span", { class: "field-label" }, "Rounds"),
				roundsInput
			),
			h(
				"label",
				{ class: "block" },
				h("span", { class: "field-label" }, "Work s"),
				workInput
			),
			h(
				"label",
				{ class: "block" },
				h("span", { class: "field-label" }, "Rest s"),
				restInput
			)
		),
		h(
			"button",
			{
				type: "button",
				class: "btn-warn w-full",
				onclick: build,
			},
			"+ Build interval session"
		)
	);
}

function exitCard(sessionId: string, onLeave: () => void): HTMLElement {
	return h(
		"section",
		{ class: "card space-y-2 border-accent-red/30" },
		sectionHeader(
			"Exit training mode",
			"#FF6B6B",
			"Asks the desktop to leave training mode and return to the sport picker."
		),
		h(
			"button",
			{
				type: "button",
				class: "btn-danger w-full",
				onclick: () => {
					if (!confirm("Exit training mode on the desktop?")) return;
					void fireTraining(
						sessionId,
						EventTypes.TrainingExit,
						"Exit requested"
					);
				},
			},
			"Exit training on desktop"
		),
		h(
			"button",
			{
				type: "button",
				class: "btn-ghost w-full",
				onclick: onLeave,
			},
			"Leave session on this phone"
		)
	);
}

function sectionHeader(
	title: string,
	accent: string,
	description: string
): HTMLElement {
	return h(
		"div",
		{ class: "space-y-1" },
		h(
			"div",
			{ class: "flex items-center gap-2" },
			h("span", {
				class: "inline-block h-3 w-1 rounded",
				style: "background:" + accent,
			}),
			h(
				"span",
				{
					class: "text-[11px] font-black uppercase tracking-wide",
					style: "color:" + accent,
				},
				title
			)
		),
		h("p", { class: "text-[11px] text-slate-400" }, description)
	);
}

function numberInput(initial: string, min: number, max: number): HTMLInputElement {
	return h("input", {
		type: "number",
		min: String(min),
		max: String(max),
		inputmode: "numeric",
		class: "field-input text-center font-mono text-base",
		value: initial,
	}) as HTMLInputElement;
}
