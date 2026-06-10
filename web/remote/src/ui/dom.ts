// ─────────────────────────────────────────────────────────────────────────────
// dom.ts
//
// Lightweight DOM helpers — there's no React/Vue here so we keep a tiny
// h() builder + a render() helper to swap the active screen module in
// place. Components are plain functions returning `HTMLElement`.
// ─────────────────────────────────────────────────────────────────────────────

export type Child = Node | string | number | boolean | null | undefined;

type Attrs = Record<string, unknown>;

export function h<K extends keyof HTMLElementTagNameMap>(
	tag: K,
	attrs?: Attrs | null,
	...children: Child[]
): HTMLElementTagNameMap[K] {
	const el = document.createElement(tag);
	if (attrs) {
		for (const [k, v] of Object.entries(attrs)) {
			if (v === null || v === undefined || v === false) continue;
			if (k === "class") el.className = String(v);
			else if (k === "html") el.innerHTML = String(v);
			else if (k.startsWith("on") && typeof v === "function") {
				el.addEventListener(k.slice(2).toLowerCase(), v as EventListener);
			} else if (k === "value" && el instanceof HTMLInputElement) {
				el.value = String(v);
			} else if (k === "checked" && el instanceof HTMLInputElement) {
				el.checked = Boolean(v);
			} else if (v === true) {
				el.setAttribute(k, "");
			} else {
				el.setAttribute(k, String(v));
			}
		}
	}
	for (const c of children.flat()) {
		if (c === null || c === undefined || c === false || c === true) continue;
		el.appendChild(typeof c === "string" || typeof c === "number" ? document.createTextNode(String(c)) : c);
	}
	return el;
}

export function clear(el: HTMLElement): void {
	while (el.firstChild) el.removeChild(el.firstChild);
}

export function mount(host: HTMLElement, content: HTMLElement): void {
	clear(host);
	host.appendChild(content);
}
