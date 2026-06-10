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

	// Lifecycle
	SessionStarted: "session_started",
	SessionEnded: "session_ended",
	Heartbeat: "heartbeat",
} as const;

export type EventType = (typeof EventTypes)[keyof typeof EventTypes];

/** Active screen identifiers — must match `_activeScreenKey` in WPF. */
export const Screens = ["scorebug", "summary", "worm", "stats", "weather", "video"] as const;
export type Screen = (typeof Screens)[number];

/** Clock modes — must match `Roche_Scoreboard.Models.ClockMode`. */
export type ClockMode = "CountUp" | "Countdown";

/** Layout identifiers consumed by `IRemoteCommandTarget.SetLayout`. */
export type Layout = "classic";

/** Goal animation identifiers consumed by `IRemoteCommandTarget.SetGoalAnimation`. */
export type GoalAnimation = "classic" | "broadcast" | "customVideo";

/** Session lifecycle status values written to `live_state.sessionStatus`. */
export type SessionStatus = "lobby" | "live" | "fullTime";
