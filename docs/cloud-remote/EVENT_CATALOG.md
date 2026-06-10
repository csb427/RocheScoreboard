# Scoreflux Cloud Remote — Event & State Catalog (AFL v1)

This catalog is the single source of truth shared by:
- the **WPF dispatcher** (`Services/Cloud/RemoteCommandDispatcher.cs`)
- the **Supabase `game_events.event_type`** column
- the **web remote app** (`web/remote/src/events.ts`)

Every operator action that the desktop ControlPanel + Settings can perform has a
matching `event_type` here. Remote clients **never** write scores or state — they
post an event, the WPF app processes it through `MatchManager` exactly as if the
local button were clicked, then writes the resulting snapshot back to
`game_sessions.live_state` for all viewers.

---

## 1. Live State Snapshot (`game_sessions.live_state` JSON)

Mirrors the fields previously sent over SignalR (`Web/ScoreboardState.cs`) so the
web app can render the full ControlPanel match strip and score zones.

| Field | Type | Notes |
|---|---|---|
| `homeName` / `homeAbbr` | string | Team display name + 3-letter abbr |
| `awayName` / `awayAbbr` | string | |
| `homeGoals`, `homeBehinds`, `homeTotal` | int | |
| `awayGoals`, `awayBehinds`, `awayTotal` | int | |
| `margin` | int | home - away |
| `quarter` | int (1-4) | |
| `clock` | string `mm:ss` | DisplayClock formatted |
| `clockRunning` | bool | |
| `clockMode` | "CountUp" \| "Countdown" | |
| `quarterMinutes`, `quarterSeconds` | int | Countdown target only |
| `q4Ended` | bool | Disables scoring buttons in UI |
| `homePrimaryColor`, `homeSecondaryColor` | string (#RRGGBB) | |
| `awayPrimaryColor`, `awaySecondaryColor` | string (#RRGGBB) | |
| `activeScreen` | string | scorebug / summary / worm / stats / weather / video |
| `eventCount` | int | |
| `lastEvent` | string | Formatted log line |
| `matchResult` | string | Headline shown at full time |
| `finalsMode` | bool | |
| `weatherLocation` | string | |
| `messages` | string[] | Marquee text lines |
| `homeLogoPath` / `awayLogoPath` | string | App-local path; web shows initials only |
| `goalAnimation` | string | classic / broadcast / customVideo |
| `layout` | string | classic / compact / etc. |
| `sessionStatus` | string | lobby / live / fullTime / ended |
| `updatedAt` | ISO8601 | Set by WPF on every write |

Quarter snapshots (Q1..Q4 cumulative) are sent as `quarterSnapshots: [{q, hg, hb, ht, ag, ab, at}, …]`.

---

## 2. Event Envelope (`game_events` row)

```jsonc
{
  "id":            "<uuid>",           // client-generated, dedupe key
  "session_id":    "<uuid>",
  "event_type":    "home_goal",        // see catalog below
  "payload":       { /* event-specific */ },
  "client_id":     "<uuid>",           // remote device GUID (debug only)
  "client_label":  "Scorekeeper iPhone",
  "created_at":    "2026-05-08T11:22:33Z"
}
```

The WPF app:
1. Subscribes to `INSERT` on `game_events WHERE session_id = <active>`.
2. Deduplicates by `id` (in-memory `HashSet<Guid>`, capped LRU).
3. Marshals to the WPF dispatcher and invokes the matching handler in
   `RemoteCommandDispatcher`.
4. Writes the resulting state snapshot back to `game_sessions.live_state`.

---

## 3. Event Catalog

### 3.1 Scoring (gated by `q4Ended == false`)

| `event_type` | Payload | Maps to |
|---|---|---|
| `home_goal` | _none_ | `HomeGoal_Click` → animation + `MatchManager.AddGoal(Home)` |
| `home_behind` | _none_ | `HomeBehind_Click` → `MatchManager.AddBehind(Home)` |
| `away_goal` | _none_ | `AwayGoal_Click` → `MatchManager.AddGoal(Away)` |
| `away_behind` | _none_ | `AwayBehind_Click` → `MatchManager.AddBehind(Away)` |
| `undo` | _none_ | `Undo_Click` → `MatchManager.UndoLastScore()` |

### 3.2 Clock

| `event_type` | Payload | Maps to |
|---|---|---|
| `clock_toggle` | _none_ | `StartPause_Click` — toggles Start/Pause |
| `clock_set` | `{ "minutes": int, "seconds": int }` | `ClockSet_Click` — set elapsed/remaining |
| `clock_mode_set` | `{ "mode": "CountUp"\|"Countdown", "minutes": int, "seconds": int }` | `HandleWebSetClockMode` |
| `siren` | _none_ | Trigger end-of-quarter siren overlay |

### 3.3 Quarter

| `event_type` | Payload | Maps to |
|---|---|---|
| `end_quarter` | _none_ | `EndQuarter_Click` |
| `continue_quarter` | _none_ | `ContinueQuarter_Click` |
| `reset_quarter_label` | _none_ | `QuarterLabelCurrentReset_Click` |

### 3.4 Match

| `event_type` | Payload | Maps to |
|---|---|---|
| `match_reset` | _none_ | `ResetMatch_Click` |
| `set_teams` | `{ "homeName", "homeAbbr", "awayName", "awayAbbr" }` | `HandleWebSetTeams` |
| `set_colors` | `{ "homePrimary", "homeSecondary", "awayPrimary", "awaySecondary" }` | `HandleWebSetColors` |
| `set_finals_mode` | `{ "enabled": bool }` | `SettingsFinalsToggle` |
| `set_weather_location` | `{ "location": string }` | `ApplyWeatherLocation_Click` |

### 3.5 Presentation (screen routing)

| `event_type` | Payload | Maps to |
|---|---|---|
| `switch_screen` | `{ "screen": "scorebug"\|"summary"\|"worm"\|"stats"\|"weather"\|"video" }` | `HandleWebScreenSwitch` |
| `show_scoreboard_window` | _none_ | `ShowScoreboard_Click` |

### 3.6 Marquee messages

| `event_type` | Payload | Maps to |
|---|---|---|
| `message_add` | `{ "text": string }` | `AddMessage_Click` |
| `message_remove` | `{ "index": int }` | `RemoveMessageItem_Click` |
| `messages_set` | `{ "messages": string[] }` | Bulk set |

### 3.7 Layout / Animation

| `event_type` | Payload | Maps to |
|---|---|---|
| `set_layout` | `{ "layout": "classic"\|"compact"\|... }` | `LayoutClassicRadio` etc. |
| `set_goal_animation` | `{ "style": "classic"\|"broadcast"\|"customVideo" }` | Anim radio cards |

### 3.8 Session lifecycle (server-issued or operator-issued)

| `event_type` | Payload | Notes |
|---|---|---|
| `session_started` | _none_ | Emitted by WPF when subscribing |
| `session_ended` | `{ "reason": string }` | Emitted on Reset / app close |
| `heartbeat` | `{ "ts": iso8601 }` | Either side; surfaces "connected" badge |

---

## 4. Idempotency rules

- All event handlers must tolerate being called twice with the same `id`.
- `MatchManager.UndoLastScore`, `EndQuarter`, `ContinueQuarter` already return
  early when no-op preconditions are met.
- Score additions are gated by the dispatcher's `HashSet<Guid> _processedIds`,
  not by re-reading the database — this keeps cold-reconnect cheap.

## 5. Authority / write rules (RLS)

- `game_events`: anyone with the join code can INSERT; nobody can UPDATE/DELETE.
- `game_sessions.live_state`: only the WPF "host" key may UPDATE.
- `game_sessions` row: created by operator (host key); join code is unique.

(See `supabase/migrations/0001_initial.sql` for exact policies.)
