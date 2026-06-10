-- ============================================================================
-- Scoreflux Cloud Remote — initial schema
--
-- Tables created in this migration:
--   game_sessions     active and historical game sessions
--   game_events       append-only event log used to drive scoring
--   teams             reusable team identities (future: presets / fixtures)
--   competitions      future grouping (season / comp / round)
--   profiles          mirror of auth.users with role (future RLS use)
--
-- v1 access model
--   No authentication is required. The 8-char `join_code` on game_sessions IS
--   the access control: anyone who has the code can read the session row and
--   INSERT events. Only the host (identified by the `host_key` secret returned
--   at session creation) may update `live_state` or end the session.
--
-- Future-ready
--   Role / user / team / competition tables are scaffolded now so the cloud
--   surface can evolve without breaking changes. They are left unreferenced
--   from game_sessions in v1 so the system works with zero account setup.
-- ============================================================================

create extension if not exists "uuid-ossp";
create extension if not exists "pgcrypto";

-- ──────────────────────────────────────────────────────────────────────────
-- profiles (future use; populated by Supabase auth trigger)
-- ──────────────────────────────────────────────────────────────────────────
create table if not exists public.profiles (
	id            uuid primary key,                -- mirrors auth.users.id
	display_name  text,
	role          text not null default 'viewer',  -- admin | scorekeeper | viewer
	created_at    timestamptz not null default now()
);

-- ──────────────────────────────────────────────────────────────────────────
-- competitions / teams (reserved for future fixture management)
-- ──────────────────────────────────────────────────────────────────────────
create table if not exists public.competitions (
	id          uuid primary key default gen_random_uuid(),
	name        text not null,
	season      text,
	created_at  timestamptz not null default now()
);

create table if not exists public.teams (
	id              uuid primary key default gen_random_uuid(),
	competition_id  uuid references public.competitions(id) on delete set null,
	name            text not null,
	abbr            text not null,
	primary_color   text,
	secondary_color text,
	logo_url        text,
	created_at      timestamptz not null default now()
);

-- ──────────────────────────────────────────────────────────────────────────
-- game_sessions
--   `live_state` is the rendered snapshot the web app reads to draw the UI.
--   The host_key column is a per-session secret returned ONLY at creation
--   time; remote clients never see it. We index join_code (lookup path).
-- ──────────────────────────────────────────────────────────────────────────
create table if not exists public.game_sessions (
	id              uuid primary key default gen_random_uuid(),
	join_code       text not null unique,            -- 8-char A-Z 0-9 (no I/O/0/1)
	host_key        text not null,                   -- secret known only to host
	sport           text not null default 'afl',
	status          text not null default 'lobby',   -- lobby | live | full_time | ended
	home_name       text not null default 'HOME',
	away_name       text not null default 'AWAY',
	live_state      jsonb not null default '{}'::jsonb,
	competition_id  uuid references public.competitions(id) on delete set null,
	created_at      timestamptz not null default now(),
	updated_at      timestamptz not null default now(),
	ended_at        timestamptz
);

create index if not exists idx_game_sessions_status on public.game_sessions(status);
create index if not exists idx_game_sessions_updated on public.game_sessions(updated_at desc);

-- ──────────────────────────────────────────────────────────────────────────
-- game_events
--   Append-only log of remote / local actions.
--   `id` is client-generated (uuid v4) so the WPF host can dedupe replays.
-- ──────────────────────────────────────────────────────────────────────────
create table if not exists public.game_events (
	id            uuid primary key,                  -- client-generated
	session_id    uuid not null references public.game_sessions(id) on delete cascade,
	event_type    text not null,
	payload       jsonb not null default '{}'::jsonb,
	client_id     uuid,                              -- per-device id from web app
	client_label  text,                              -- "Scorekeeper iPhone"
	created_at    timestamptz not null default now()
);

create index if not exists idx_game_events_session on public.game_events(session_id, created_at);

-- ──────────────────────────────────────────────────────────────────────────
-- Helpers
-- ──────────────────────────────────────────────────────────────────────────
create or replace function public.touch_updated_at() returns trigger
language plpgsql as $$
begin
	new.updated_at := now();
	return new;
end;
$$;

drop trigger if exists trg_game_sessions_touch on public.game_sessions;
create trigger trg_game_sessions_touch
	before update on public.game_sessions
	for each row execute function public.touch_updated_at();

-- ──────────────────────────────────────────────────────────────────────────
-- Realtime publication
-- ──────────────────────────────────────────────────────────────────────────
do $$ begin
	perform 1 from pg_publication where pubname = 'supabase_realtime';
	if found then
		execute 'alter publication supabase_realtime add table public.game_events';
		execute 'alter publication supabase_realtime add table public.game_sessions';
	end if;
exception
	when duplicate_object then null;
end $$;
