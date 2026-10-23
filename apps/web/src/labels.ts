/** How the UI names and colours what the API sends as codes. The list, the detail and the form share these. */

type Tone = "slate" | "blue" | "emerald" | "red";

const STATUSES = new Map<string, { label: string; tone: Tone }>([
  ["Draft", { label: "Draft", tone: "slate" }],
  ["SentToClient", { label: "Sent to client", tone: "blue" }],
  ["Accepted", { label: "Accepted", tone: "emerald" }],
  ["Declined", { label: "Declined", tone: "red" }],
]);

export const statusLabel = (status: string | null | undefined) =>
  STATUSES.get(status ?? "")?.label ?? status ?? "";

export const statusTone = (status: string | null | undefined): Tone =>
  STATUSES.get(status ?? "")?.tone ?? "slate";

export const PRODUCTS = [
  { code: "LIFE", label: "Life" },
  { code: "DISABILITY", label: "Disability" },
] as const;

export const productLabel = (code: string | null | undefined) =>
  PRODUCTS.find((p) => p.code === code)?.label ?? code ?? "";

// One fixed zone, so the server render and the browser agree on every date.
const ZONE = "Africa/Johannesburg";
const day = new Intl.DateTimeFormat("en-ZA", {
  day: "numeric",
  month: "short",
  year: "numeric",
  timeZone: ZONE,
});
const dayAndTime = new Intl.DateTimeFormat("en-ZA", {
  day: "numeric",
  month: "short",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: ZONE,
});

/** An ISO date or date-time as "22 Oct 2026" (en-ZA, so September is "Sept"). A bare date is a calendar day, so it is read as UTC. */
export const formatDay = (iso: string | null | undefined) =>
  iso ? day.format(new Date(iso.length === 10 ? `${iso}T12:00:00Z` : iso)) : "";

export const formatDayAndTime = (iso: string | null | undefined) =>
  iso ? dayAndTime.format(new Date(iso)) : "";
