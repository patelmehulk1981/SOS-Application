/** Locale-aware date/time for display ("15 Nov 2026, 3:42 pm" in en-IN). Hermes ships Intl. */
export function formatDateTime(iso: string, locale?: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

export function formatDate(iso: string, locale?: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(date);
}

/** "350 m" (to the nearest 50 m) below 1 km, "1.2 km" above; for responder and alert distances. */
export function formatDistance(meters: number): string {
  if (!Number.isFinite(meters) || meters < 0) return '';
  if (meters < 950) return `${Math.max(50, Math.round(meters / 50) * 50)} m`;
  return `${(Math.round(meters / 100) / 10).toFixed(1)} km`;
}

export function roundAccuracy(meters: number | undefined): number | undefined {
  return meters === undefined ? undefined : Math.max(1, Math.round(meters));
}
