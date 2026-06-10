-- ============================================================================
-- Scoreflux Cloud Remote — Row Level Security
--
-- v1 model (no auth)
--   game_sessions: SELECT allowed to anon (so the web client can fetch the
--                  session row by join_code). UPDATE / DELETE blocked at the
--                  anon role; the WPF host writes via the service-role key.
--   game_events:   anon may INSERT (remote scorers post actions). anon SELECT
--                  is also enabled so devices can see their own confirmations
--                  via realtime.
--   teams, competitions, profiles: locked down for now (service-role only).
--
-- We intentionally keep policies coarse in v1 — the join_code is the secret.
-- A future migration will introduce per-role policies once auth is on.
-- ============================================================================

alter table public.game_sessions   enable row level security;
alter table public.game_events     enable row level security;
alter table public.teams           enable row level security;
alter table public.competitions    enable row level security;
alter table public.profiles        enable row level security;

-- game_sessions: anyone may read; writes only via service role
drop policy if exists "sessions_read_anon" on public.game_sessions;
create policy "sessions_read_anon" on public.game_sessions
	for select using (true);

-- game_events: anyone may insert; anyone may read (realtime relies on it)
drop policy if exists "events_insert_anon" on public.game_events;
create policy "events_insert_anon" on public.game_events
	for insert with check (true);

drop policy if exists "events_read_anon" on public.game_events;
create policy "events_read_anon" on public.game_events
	for select using (true);

-- Future-ready: profiles only readable by the owner (no rows yet in v1)
drop policy if exists "profiles_self_read" on public.profiles;
create policy "profiles_self_read" on public.profiles
	for select using (auth.uid() = id);
