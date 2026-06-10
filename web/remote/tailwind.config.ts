/** @type {import('tailwindcss').Config} */
export default {
	content: ["./index.html", "./src/**/*.{ts,tsx,html}"],
	theme: {
		extend: {
			colors: {
				// Match the desktop control panel palette so the mirrored
				// mobile UI feels native to operators jumping between
				// devices.
				ink: {
					900: "#06080c",
					800: "#0a1018",
					700: "#0d1117",
					600: "#11192a",
					500: "#162033",
					400: "#1f2c44",
				},
				accent: {
					blue: "#58a6ff",
					blueDim: "#1a3a6a",
					green: "#3fb950",
					red: "#ff6b6b",
					yellow: "#d29922",
					purple: "#bc8cff",
				},
				home: {
					DEFAULT: "#0a2a6a",
					soft: "#1a3a6a",
					text: "#7eb4ff",
				},
				away: {
					DEFAULT: "#7a1a1a",
					soft: "#4a1a1a",
					text: "#ff9a9a",
				},
			},
			fontFamily: {
				mono: ["ui-monospace", "Consolas", "monospace"],
			},
			boxShadow: {
				card: "0 1px 0 0 rgba(255,255,255,0.04) inset, 0 0 0 1px rgba(88,166,255,0.08)",
			},
		},
	},
	plugins: [],
};
