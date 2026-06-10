// ─────────────────────────────────────────────────────────────────────────────
// liveState.ts
//
// TypeScript mirror of `Roche_Scoreboard.Services.Cloud.LiveStateSnapshot`.
// The desktop host writes JSON in this exact shape into
// `game_sessions.live_state`. We keep field names PascalCase for the wire
// payload but expose a friendlier camelCase view to the UI.
// ─────────────────────────────────────────────────────────────────────────────

import type { Screen, SessionStatus, ClockMode, Layout, GoalAnimation } from "./events.js";

/** Wire shape — exactly what `JsonSerializer` emits on the desktop. */
export interface LiveStateWire {
	HomeName: string;
	HomeAbbr: string;
	AwayName: string;
	AwayAbbr: string;
	HomeGoals: number;
	HomeBehinds: number;
	HomeTotal: number;
	AwayGoals: number;
	AwayBehinds: number;
	AwayTotal: number;
	Margin: number;
	Quarter: number;
	Clock: string;
	ClockRunning: boolean;
	ClockMode: ClockMode;
	QuarterMinutes: number;
	QuarterSeconds: number;
	Q4Ended: boolean;
	HomePrimaryColor: string;
	HomeSecondaryColor: string;
	AwayPrimaryColor: string;
	AwaySecondaryColor: string;
	ActiveScreen: Screen;
	Layout: Layout;
	GoalAnimation: GoalAnimation;
	EventCount: number;
	LastEvent: string;
	MatchResult: string;
	FinalsMode: boolean;
	WeatherLocation: string;
	Messages: string[];
	QuarterSnapshots: QuarterSnapshotWire[];
	SessionStatus: SessionStatus;
	UpdatedAt: string;
}

export interface QuarterSnapshotWire {
	Q: number;
	Hg: number;
	Hb: number;
	Ht: number;
	Ag: number;
	Ab: number;
	At: number;
}

/** Friendly camelCase shape used by the UI components. */
export interface LiveState {
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

export function fromWire(wire: LiveStateWire): LiveState {
	return {
		homeName: wire.HomeName,
		homeAbbr: wire.HomeAbbr,
		awayName: wire.AwayName,
		awayAbbr: wire.AwayAbbr,
		homeGoals: wire.HomeGoals,
		homeBehinds: wire.HomeBehinds,
		homeTotal: wire.HomeTotal,
		awayGoals: wire.AwayGoals,
		awayBehinds: wire.AwayBehinds,
		awayTotal: wire.AwayTotal,
		margin: wire.Margin,
		quarter: wire.Quarter,
		clock: wire.Clock,
		clockRunning: wire.ClockRunning,
		clockMode: wire.ClockMode,
		quarterMinutes: wire.QuarterMinutes,
		quarterSeconds: wire.QuarterSeconds,
		q4Ended: wire.Q4Ended,
		homePrimaryColor: wire.HomePrimaryColor,
		homeSecondaryColor: wire.HomeSecondaryColor,
		awayPrimaryColor: wire.AwayPrimaryColor,
		awaySecondaryColor: wire.AwaySecondaryColor,
		activeScreen: wire.ActiveScreen,
		layout: wire.Layout,
		goalAnimation: wire.GoalAnimation,
		eventCount: wire.EventCount,
		lastEvent: wire.LastEvent,
		matchResult: wire.MatchResult,
		finalsMode: wire.FinalsMode,
		weatherLocation: wire.WeatherLocation,
		messages: wire.Messages ?? [],
		quarterSnapshots: (wire.QuarterSnapshots ?? []).map((s) => ({
			quarter: s.Q,
			homeGoals: s.Hg,
			homeBehinds: s.Hb,
			homeTotal: s.Ht,
			awayGoals: s.Ag,
			awayBehinds: s.Ab,
			awayTotal: s.At,
		})),
		sessionStatus: wire.SessionStatus,
		updatedAt: wire.UpdatedAt,
	};
}
