/** Compares dotted numeric versions ("1.10.0" > "1.9.3"). Missing parts count as 0; suffixes are ignored. */
export function compareVersions(a: string, b: string): number {
  const pa = parse(a);
  const pb = parse(b);
  const length = Math.max(pa.length, pb.length);
  for (let i = 0; i < length; i++) {
    const diff = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (diff !== 0) return diff > 0 ? 1 : -1;
  }
  return 0;
}

export function isBelowMinimum(current: string, minimum: string | undefined): boolean {
  return minimum !== undefined && minimum !== '' && compareVersions(current, minimum) < 0;
}

function parse(version: string): number[] {
  return version
    .split(/[-+]/, 1)[0]!
    .split('.')
    .map((part) => {
      const n = Number.parseInt(part, 10);
      return Number.isFinite(n) ? n : 0;
    });
}
