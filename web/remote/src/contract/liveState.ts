// ─────────────────────────────────────────────────────────────────────────────
// liveState.ts
//
// TypeScript mirror of `Roche_Scoreboard.Services.Cloud.LiveStateSnapshot`.
// The desktop host writes JSON in this exact shape into
// `game_sessions.live_state`.
//
// IMPORTANT: the desktop's `SupabaseRestClient` serializes with
// `JsonNamingPolicy.SnakeCaseLower`, so the wire shape is snake_case. The
// friendlier camelCase view below is what every UI module consumes.
// ─────────────────────────────────────────────────────────────────────────────

import type { Screen, SessionStatus, ClockMode, Layout, GoalAnimation, Sport } from "./events.js";

/** Wire shape — exactly what `JsonSerializer` emits on the desktop. */
export interface LiveStateWire {
	sport?: Sport;
	home_name?: string;
	home_abbr?: string;
	away_name?: string;
	away_abbr?: string;
	home_goals?: number;
	home_behinds?: number;
	home_total?: number;
	away_goals?: number;
	away_behinds?: number;
	away_total?: number;
	margin?: number;
	quarter?: number;
	clock?: string;
	clock_running?: boolean;
	clock_mode?: ClockMode;
	quarter_minutes?: number;
	quarter_seconds?: number;
	q4_ended?: boolean;
	home_primary_color?: string;
	home_secondary_color?: string;
	away_primary_color?: string;
	away_secondary_color?: string;
	active_screen?: Screen;
	layout?: Layout;
	goal_animation?: GoalAnimation;
	event_count?: number;
	last_event?: string;
	match_result?: string;
	finals_mode?: boolean;
	weather_location?: string;
	messages?: string[] | null;
	quarter_snapshots?: QuarterSnapshotWire[] | null;
	session_status?: SessionStatus;
	updated_at?: string;
	training?: TrainingSnapshotWire | null;
}

export interface QuarterSnapshotWire {
	q?: number;
	hg?: number;
	hb?: number;
	ht?: number;
	ag?: number;
	ab?: number;
	at?: number;
}

export interface TrainingSnapshotWire {
	is_running?: boolean;
	auto_advance?: boolean;
	active_index?: number;
	active_title?: string;
	active_total_seconds?: number;
	active_remaining_seconds?: number;
	session_total_seconds?: number;
	session_remaining_seconds?: number;
	timers?: TrainingTimerWire[] | null;
}

export interface TrainingTimerWire {
	id?: string;
	title?: string;
	total_seconds?: number;
	remaining_seconds?: number;
	has_finished?: boolean;
}

/** Friendly camelCase shape used by the UI components. */
export interface LiveState {
	sport: Sport;
	homeName: string;
	homeAbbr: string;
	awayName: string;
	awayAbbr: string;
	homeGoals: number;
	homeBehinds: number;
	homeTotal: number;
	awayGoals: number;
	awayBehinds: number;
	awayTotal: number;
	margin: number;
	quarter: number;
	clock: string;
	clockRunning: boolean;
	clockMode: ClockMode;
	quarterMinutes: number;
	quarterSeconds: number;
	q4Ended: boolean;
	homePrimaryColor: string;
	homeSecondaryColor: string;
	awayPrimaryColor: string;
	awaySecondaryColor: string;
	activeScreen: Screen;
	layout: Layout;
	goalAnimation: GoalAnimation;
	eventCount: number;
	lastEvent: string;
	matchResult: string;
	finalsMode: boolean;
	weatherLocation: string;
	messages: string[];
	quarterSnapshots: QuarterSnapshot[];
	sessionStatus: SessionStatus;
	updatedAt: string;
	training: TrainingSnapshot;
}

export interface QuarterSnapshot {
	quarter: number;
	homeGoals: number;
	homeBehinds: number;
	homeTotal: number;
	awayGoals: number;
	awayBehinds: number;
	awayTotal: number;
}

export interface TrainingSnapshot {
	isRunning: boolean;
	autoAdvance: boolean;
	activeIndex: number;
	activeTitle: string;
	activeTotalSeconds: number;
	activeRemainingSeconds: number;
	sessionTotalSeconds: number;
	sessionRemainingSeconds: number;
	timers: TrainingTimer[];
}

export interface TrainingTimer {
	id: string;
	title: string;
	totalSeconds: number;
	remainingSeconds: number;
	hasFinished: boolean;
}

const EMPTY_TRAINING: TrainingSnapshot = {
	isRunning: false,
	autoAdvance: true,
	activeIndex: -1,
	activeTitle: "",
	activeTotalSeconds: 0,
	activeRemainingSeconds: 0,
	sessionTotalSeconds: 0,
	sessionRemainingSeconds: 0,
	timers: [],
};

export function fromWire(wire: LiveStateWire | null | undefined): LiveState {
	const w: LiveStateWire = wire ?? {};
	return {
		sport: w.sport ?? "afl",
		homeName: w.home_name ?? "Home",
		homeAbbr: w.home_abbr ?? "HOM",
		awayName: w.away_name ?? "Away",
		awayAbbr: w.away_abbr ?? "AWA",
		homeGoals: w.home_goals ?? 0,
		homeBehinds: w.home_behinds ?? 0,
		homeTotal: w.home_total ?? 0,
		awayGoals: w.away_goals ?? 0,
		awayBehinds: w.away_behinds ?? 0,
		awayTotal: w.away_total ?? 0,
		margin: w.margin ?? 0,
		quarter: w.quarter ?? 1,
		clock: w.clock ?? "00:00",
		clockRunning: w.clock_running ?? false,
		clockMode: w.clock_mode ?? "CountUp",
		quarterMinutes: w.quarter_minutes ?? 20,
		quarterSeconds: w.quarter_seconds ?? 0,
		q4Ended: w.q4_ended ?? false,
		homePrimaryColor: w.home_primary_color ?? "#0A1428",
		homeSecondaryColor: w.home_secondary_color ?? "#FFFFFF",
		awayPrimaryColor: w.away_primary_color ?? "#1C0A0A",
		awaySecondaryColor: w.away_secondary_color ?? "#FFFFFF",
		activeScreen: w.active_screen ?? "scorebug",
		layout: w.layout ?? "classic",
		goalAnimation: w.goal_animation ?? "broadcast",
		eventCount: w.event_count ?? 0,
		lastEvent: w.last_event ?? "",
		matchResult: w.match_result ?? "",
		finalsMode: w.finals_mode ?? false,
		weatherLocation: w.weather_location ?? "",
		messages: w.messages ?? [],
		quarterSnapshots: (w.quarter_snapshots ?? []).map((s) => ({
			quarter: s.q ?? 0,
			homeGoals: s.hg ?? 0,
			homeBehinds: s.hb ?? 0,
			homeTotal: s.ht ?? 0,
			awayGoals: s.ag ?? 0,
			awayBehinds: s.ab ?? 0,
			awayTotal: s.at ?? 0,
		})),
		sessionStatus: w.session_status ?? "lobby",
		updatedAt: w.updated_at ?? "",
		training: w.training
			? {
				isRunning: w.training.is_running ?? false,
				autoAdvance: w.training.auto_advance ?? true,
				activeIndex: w.training.active_index ?? -1,
				activeTitle: w.training.active_title ?? "",
				activeTotalSeconds: w.training.active_total_seconds ?? 0,
				activeRemainingSeconds: w.training.active_remaining_seconds ?? 0,
				sessionTotalSeconds: w.training.session_total_seconds ?? 0,
				sessionRemainingSeconds: w.training.session_remaining_seconds ?? 0,
				timers: (w.training.timers ?? []).map((t) => ({
					id: t.id ?? "",
					title: t.title ?? "Timer",
					totalSeconds: t.total_seconds ?? 0,
					remainingSeconds: t.remaining_seconds ?? 0,
					hasFinished: t.has_finished ?? false,
				})),
			}
			: EMPTY_TRAINING,
	};
}
