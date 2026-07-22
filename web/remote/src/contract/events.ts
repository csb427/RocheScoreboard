// ─────────────────────────────────────────────────────────────────────────────
// events.ts
//
// TypeScript mirror of `Roche_Scoreboard.Services.Cloud.RemoteEventTypes`
// (C# constants in `Roche_Scoreboard/Services/Cloud/RemoteEvent.cs`).
// Keep these names byte-identical to the desktop dispatcher — the cloud
// dispatcher matches on `event_type` literal strings, so any drift here
// silently dead-ends a remote button press.
// ─────────────────────────────────────────────────────────────────────────────

export const EventTypes = {
	// Scoring
	HomeGoal: "home_goal",
	HomeBehind: "home_behind",
	AwayGoal: "away_goal",
	AwayBehind: "away_behind",
	Undo: "undo",

	// Clock
	ClockToggle: "clock_toggle",
	ClockSet: "clock_set",
	ClockModeSet: "clock_mode_set",
	Siren: "siren",

	// Quarter
	EndQuarter: "end_quarter",
	ContinueQuarter: "continue_quarter",
	ResetQuarterLabel: "reset_quarter_label",

	// Match
	MatchReset: "match_reset",
	SetTeams: "set_teams",
	SetColors: "set_colors",
	SetFinalsMode: "set_finals_mode",
	SetWeatherLocation: "set_weather_location",

	// Presentation
	SwitchScreen: "switch_screen",
	ShowScoreboardWindow: "show_scoreboard_window",

	// Marquee
	MessageAdd: "message_add",
	MessageRemove: "message_remove",
	MessagesSet: "messages_set",

	// Layout / animation
	SetLayout: "set_layout",
	SetGoalAnimation: "set_goal_animation",

	// Training
	TrainingStartPause: "training_start_pause",
	TrainingSkip: "training_skip",
	TrainingResetActive: "training_reset_active",
	TrainingAddTimer: "training_add_timer",
	TrainingUpdateTimer: "training_update_timer",
	TrainingRemoveTimer: "training_remove_timer",
	TrainingClearQueue: "training_clear_queue",
	TrainingSetAutoAdvance: "training_set_auto_advance",
	TrainingReorder: "training_reorder",
	TrainingAdjustActive: "training_adjust_active",
	TrainingExit: "training_exit",

	// Lifecycle
	SessionStarted: "session_started",
	SessionEnded: "session_ended",
	Heartbeat: "heartbeat",
} as const;

export type EventType = (typeof EventTypes)[keyof typeof EventTypes];

/** Sport identifier for the active cloud session. Mirrors `game_sessions.sport`. */
export type Sport = "afl" | "training";

/** Active screen identifiers — must match `_activeScreenKey` in WPF. */
export const Screens = ["scorebug", "summary", "worm", "stats", "weather", "video"] as const;
export type Screen = (typeof Screens)[number];

/** Clock modes — must match `Roche_Scoreboard.Models.ClockMode`. */
export type ClockMode = "CountUp" | "Countdown";

/** Layout identifiers consumed by `IRemoteCommandTarget.SetLayout`. */
export type Layout = "classic" | "expanded" | "retro" | "noLogos";

/** Goal animation identifiers consumed by `IRemoteCommandTarget.SetGoalAnimation`. */
export type GoalAnimation = "classic" | "broadcast" | "electric" | "cinematic" | "clean" | "customVideo";

/** Session lifecycle status values written to `live_state.sessionStatus`. */
export type SessionStatus = "lobby" | "live" | "fullTime";
