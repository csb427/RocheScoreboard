# Roche Scoreboard Cloud Remote — Supabase Setup

The Scoreflux WPF app and the remote scoring web app both talk to a **single
Supabase project** that you control. Follow these steps once per environment
(dev / staging / production).

> Time to set up: **~10 minutes**, no credit card needed for the free tier.

---

## 1. Create the Supabase project

1. Go to <https://supabase.com> and sign up / log in.
2. Click **New project**, give it a name (e.g. `scoreflux-prod`).
3. Pick a region close to your venue (e.g. Sydney for AU).
4. Set a strong database password and save it somewhere safe.
5. Wait for the project to finish provisioning (~1 minute).

## 2. Apply the schema

You have two options.

### Option A — Supabase Dashboard (easiest)

1. In the project, open **SQL Editor → New query**.
2. Paste the contents of [`migrations/0001_initial.sql`](migrations/0001_initial.sql) and click **Run**.
3. Paste the contents of [`migrations/0002_rls.sql`](migrations/0002_rls.sql) and click **Run**.
4. Open **Database → Replication** and confirm that the `supabase_realtime`
   publication includes `public.game_sessions` and `public.game_events`. If it
   does not, click *Add tables* and add them.

### Option B — Supabase CLI

```powershell
# from the repo root
npx supabase login
npx supabase link --project-ref <your-project-ref>
npx supabase db push
```

## 3. Get your keys

In the Supabase Dashboard open **Project Settings → API** and copy:

| Key | Used by | Where it lives |
|---|---|---|
| **Project URL** | WPF + web app | `supabase.config.json` and `web/remote/.env` |
| **anon public key** | WPF + web app | `supabase.config.json` and `web/remote/.env` |
| **service_role key** | WPF host only | `supabase.config.json` (DO NOT publish) |

> The **service_role key bypasses RLS**. Treat it like a password. Never put it
> in the web app or in source control — it lives only in the host scoreboard's
> local `supabase.config.json` next to the EXE.

## 4. Configure the WPF scoreboard

The host scoreboard expects a `supabase.config.json` file at runtime. Place it in **one** of these locations:

1. Next to the published `Roche Scoreboard.exe` in the release folder, or
2. `%LOCALAPPDATA%\RocheScoreboard\supabase.config.json` as a fallback.

Use this template:

```jsonc
{
  "url":              "https://<your-ref>.supabase.co",
  "anonKey":          "<anon public key>",
  "serviceRoleKey":   "<service role key>",
  "hostLabel":        "<optional operator label>",
  "remoteAppBaseUrl": "https://<your-deployed-remote>.pages.dev"
}
```

The app auto-detects the file on launch. If it is missing or incomplete, the
*Cloud Remote* settings card stays disabled and the rest of the desktop keeps
working normally.

> `serviceRoleKey` is required only on the host scoreboard machine. Do **not**
> ship it in the web app or commit it to source control.
>
> `remoteAppBaseUrl` is optional. When set, the desktop QR encodes
> `<remoteAppBaseUrl>/?code=<JOIN-CODE>` so phones land in the right session
> with one scan. Leave it blank during initial setup; you can fill it in once
> step 5 finishes.

## 5. Configure the web remote app

In `web/remote/`:

```powershell
cp .env.example .env
# edit .env and set:
#   VITE_SUPABASE_URL=...
#   VITE_SUPABASE_ANON_KEY=...
```

Then build it (`npm install && npm run build`) and deploy the contents of
`web/remote/dist/` to any static host (Cloudflare Pages, Vercel, Netlify, GitHub
Pages, S3, etc.). See `web/remote/README.md` for deploy details.

## 6. Verify

1. Launch Scoreflux. Open **Settings → Cloud Remote** and click **Start cloud session**.
2. A join code (e.g. `K7HM-2QPR`) and QR code will appear.
3. On your phone, open the deployed remote URL, scan the QR (or type the code).
4. Tap **+6 GOAL HOME** — the score should bump immediately on the scoreboard,
   exactly as if you'd clicked it locally, with the full goal animation.

## 7. Release checklist

For the desktop release, include:

- `Roche Scoreboard.exe` and its publish output
- `libvlc\` native files created by the publish step
- `supabase.config.json` **only if** you want cloud remote enabled on that machine
- any additional assets that are already copied by the publish pipeline

Do **not** include secret config in the web remote deployment. The web app only needs its own `.env` values at build time.

If you ship a build without `supabase.config.json`, the app should still run; only the Cloud Remote card remains disabled until a valid file is added.

## 8. Migrations going forward

New migrations live in this folder as `0003_*.sql`, `0004_*.sql`, etc.
Apply them in order in the SQL editor (or `npx supabase db push`).
