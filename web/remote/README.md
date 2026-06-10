# Roche Scoreboard · Remote

Mobile-first remote control for the **Roche Scoreboard** desktop app. Mirrors **every option, button, and setting** of the desktop AFL control panel onto an iPhone, iPad, or laptop.

It is a **login-less** Vite + TypeScript + Tailwind SPA backed by Supabase Realtime — operators type or scan a join code, the SPA streams the live match state down, and every button press is dispatched on the desktop scoreboard exactly as if it had been clicked there.

```
┌──────────┐   game_events.insert     ┌──────────────┐
│  Phone   │ ───────────────────────▶ │   Supabase   │
│ (this    │                          │  (postgres + │
│  app)    │ ◀─── postgres_changes ── │   realtime)  │
└──────────┘     live_state UPDATE    └──────────────┘
											  ▲
											  │ live_state UPDATE
											  │
									   ┌──────────────┐
									   │ Roche WPF    │
									   │ scoreboard   │
									   └──────────────┘
```

The desktop is the **only authoritative scorer**. The phone never edits state directly — it just emits events.

---

## Mirrored screens

| Tab | Mirrors desktop section | Buttons / fields |
| --- | --- | --- |
| Score | Scoring panel + event log | Home/away goal, home/away behind, undo, last event, quarter-by-quarter table |
| Clock | Clock / quarter card | Start ⇄ pause, count-up vs countdown, quarter min/sec, set elapsed clock, manual siren |
| Match | Match card | End quarter, continue, reset quarter labels, finals on/off, weather location, full match reset |
| Teams | Teams card | Home/away name + abbr, primary/secondary colour, picker + hex |
| Display | Presentation + layout + animation | Switch active screen (scorebug / summary / worm / stats / weather / video), show scoreboard window, layout, goal animation style |
| Marquee | Marquee messages | Add, remove single, clear all |
| More | Diagnostics + leave | Device label, client id, session info, copy join code, leave session |

Every event maps 1:1 to an existing operator click on the desktop, so all animations, validations, and quarter snapshots keep working.

---

## Prerequisites

- Node.js 20 or newer
- A Supabase project with the migrations from `supabase/migrations/` applied (see `supabase/README.md` in the repo root)
- The Supabase **anon** key — *not* the service-role key. The service-role key stays on the desktop scoreboard

---

## One-time setup

```bash
cd web/remote
npm install
cp .env.example .env
# edit .env so VITE_SUPABASE_URL / VITE_SUPABASE_ANON_KEY match your project
```

### Local development

```bash
npm run dev
```

This serves the SPA at `http://localhost:5173`. From the desktop scoreboard, click **Settings → Cloud Remote → Start cloud session** and copy the join code into the dev SPA.

For LAN testing on a phone, hit `http://<your-laptop-ip>:5173/?code=<JOIN-CODE>` from the phone's browser.

### Production build

```bash
npm run build
```

Outputs static files to `web/remote/dist/`. Any static host (Cloudflare Pages, Vercel, Netlify, GitHub Pages, S3 + CloudFront) will serve them.

### Wiring the desktop QR

Add the deployed base URL to `supabase.config.json` next to the desktop EXE:

```json
{
	"url": "https://YOUR-PROJECT.supabase.co",
	"anonKey": "YOUR_ANON_KEY",
	"serviceRoleKey": "YOUR_SERVICE_ROLE_KEY",
	"remoteAppBaseUrl": "https://roche-remote.pages.dev"
}
```

When `remoteAppBaseUrl` is set, the desktop QR encodes `https://roche-remote.pages.dev/?code=<JOIN-CODE>` — one scan and the phone lands directly in the right session.

---

## Deploying

### Cloudflare Pages

1. Connect the repo, set the build command to `npm run build` and the output directory to `web/remote/dist`.
2. Set the project root to `web/remote`.
3. Add `VITE_SUPABASE_URL` and `VITE_SUPABASE_ANON_KEY` as build-time env vars.
4. Deploy. The page works from any device — no server-side runtime needed.

### Vercel

1. New project → import the repo.
2. Set the **Root Directory** to `web/remote`.
3. Framework preset: **Vite**.
4. Add the same two env vars and deploy.

### Netlify

1. Add a new site from the repo.
2. Base directory: `web/remote`. Build command: `npm run build`. Publish directory: `web/remote/dist`.
3. Add the env vars and deploy.

### GitHub Pages

1. Run `npm run build` in CI.
2. Publish `web/remote/dist` to `gh-pages` branch.
3. Note: GitHub Pages serves from a sub-path, so set `base: "/<repo>/"` in `vite.config.ts` if hosting under a project page.

---

## Architecture summary

```
src/
  contract/    ← TypeScript copy of the C# event catalog & live-state shape
  lib/         ← Supabase client, event submitter, session lookup
  state/       ← Tiny pub/sub store fed by realtime postgres_changes
  ui/          ← DOM helpers + sticky score header + bottom nav + toast
  screens/     ← One module per mirrored desktop control panel section
  app.ts       ← Boot logic: join screen ⇄ live shell
  main.ts      ← Entry point
```

The contract files (`contract/events.ts`, `contract/liveState.ts`) are the only places where the names of events and state fields are spelled out — they must stay in lockstep with `Roche_Scoreboard/Services/Cloud/RemoteEvent.cs` and `LiveStateSnapshot.cs`.

---

## Troubleshooting

- **Join code not found** — the desktop hasn't started a session yet, or the session was ended. Restart it from Settings → Cloud Remote.
- **Connecting…** never goes away — check `VITE_SUPABASE_URL` / `VITE_SUPABASE_ANON_KEY` and that the RLS migration `supabase/migrations/0002_rls.sql` has been applied so anon clients can read `game_sessions` and write `game_events`.
- **Buttons appear to do nothing** — open the desktop scoreboard's debug log. Each event arrival is logged; any dispatcher errors will be there. Double-check that the event constant in `src/contract/events.ts` matches the desktop `RemoteEventTypes`.
- **Phone shows stale state** — the live-state store falls back to the last seeded snapshot when the realtime channel drops. Pull-to-refresh by tapping More → Leave session and rejoining.
