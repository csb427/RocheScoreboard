// Entry point — wired in `index.html`.
import { bootApp } from "./app.js";

const host = document.getElementById("app");
if (!host) {
	throw new Error("Missing #app host element in index.html");
}
bootApp(host);
