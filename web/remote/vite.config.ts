import { defineConfig, loadEnv } from "vite";

// Roche Scoreboard remote control — mobile-first SPA. We keep the Vite
// config minimal: TypeScript + Tailwind via PostCSS, and the SPA index is
// served from /. The Supabase URL and anon key are injected at build time
// through `VITE_SUPABASE_URL` / `VITE_SUPABASE_ANON_KEY` (see .env.example).
export default defineConfig(({ mode }) => {
	const env = loadEnv(mode, process.cwd(), "VITE_");
	return {
		envPrefix: "VITE_",
		define: {
			__SUPABASE_URL__: JSON.stringify(env.VITE_SUPABASE_URL ?? ""),
			__SUPABASE_ANON_KEY__: JSON.stringify(env.VITE_SUPABASE_ANON_KEY ?? ""),
		},
		server: {
			host: true,
			port: 5173,
		},
		build: {
			outDir: "dist",
			sourcemap: true,
			target: "es2022",
		},
	};
});
