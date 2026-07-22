// ─────────────────────────────────────────────────────────────────────────────
// recentTimers.ts
//
// Phone-local persistence for the "Recent timers" chip strip that lives in
// the Training builder. Mirrors the desktop's recent-timer list — when the
// operator adds a custom timer from the phone, the title + duration are
// stashed in localStorage so they can be re-added with a single tap later.
//
// Storage is intentionally per-device: each phone keeps its own MRU list
// rather than syncing through Supabase, because the desktop already runs
// its own copy.
// ─────────────────────────────────────────────────────────────────────────────

const STORAGE_KEY = "roche.training.recent.v1";
const MAX_ENTRIES = 12;

export interface RecentTimerEntry {
	title: string;
	seconds: number;
}

interface StoredEntry {
	title?: unknown;
	seconds?: unknown;
}

function safeStorage(): Storage | null {
	try {
		return window.localStorage;
	} catch {
		return null;
	}
}

function normalize(entry: unknown): RecentTimerEntry | null {
	if (entry === null || typeof entry !== "object") return null;
	const e = entry as StoredEntry;
	const title = typeof e.title === "string" ? e.title.trim() : "";
	const seconds = typeof e.seconds === "number" ? Math.trunc(e.seconds) : Number.NaN;
	if (title.length === 0) return null;
	if (!Number.isFinite(seconds) || seconds <= 0) return null;
	return { title, seconds };
}

export function loadRecent(): RecentTimerEntry[] {
	const store = safeStorage();
	if (!store) return [];
	const raw = store.getItem(STORAGE_KEY);
	if (!raw) return [];
	try {
		const parsed = JSON.parse(raw);
		if (!Array.isArray(parsed)) return [];
		const out: RecentTimerEntry[] = [];
		for (const item of parsed) {
			const norm = normalize(item);
			if (norm) out.push(norm);
			if (out.length >= MAX_ENTRIES) break;
		}
		return out;
	} catch {
		return [];
	}
}

export function recordRecent(title: string, seconds: number): RecentTimerEntry[] {
	const norm = normalize({ title, seconds });
	if (!norm) return loadRecent();
	const store = safeStorage();
	if (!store) return [norm];

	const current = loadRecent();
	const next: RecentTimerEntry[] = [norm];
	for (const entry of current) {
		if (entry.title === norm.title && entry.seconds === norm.seconds) continue;
		next.push(entry);
		if (next.length >= MAX_ENTRIES) break;
	}
	try {
		store.setItem(STORAGE_KEY, JSON.stringify(next));
	} catch {
		// quota / private mode — ignore, in-memory list still returned
	}
	return next;
}

export function clearRecent(): void {
	const store = safeStorage();
	if (!store) return;
	try {
		store.removeItem(STORAGE_KEY);
	} catch {
		// ignore
	}
}
