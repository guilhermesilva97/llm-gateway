const integer = new Intl.NumberFormat('pt-BR');
const compact = new Intl.NumberFormat('pt-BR', { notation: 'compact', maximumFractionDigits: 1 });

export function formatNumber(value: number): string {
  return Math.abs(value) >= 100_000 ? compact.format(value) : integer.format(value);
}

export function formatUsd(value: number): string {
  if (value === 0) {
    return 'US$ 0,00';
  }
  const digits = Math.abs(value) >= 1 ? 2 : Math.abs(value) >= 0.01 ? 4 : 6;
  return `US$ ${value.toLocaleString('pt-BR', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  })}`;
}

export function formatPercent(part: number, total: number): string {
  if (total <= 0) {
    return '0%';
  }
  return `${((part / total) * 100).toLocaleString('pt-BR', { maximumFractionDigits: 1 })}%`;
}

export function formatLatency(milliseconds: number | null | undefined): string {
  if (milliseconds === null || milliseconds === undefined) {
    return '—';
  }
  return milliseconds >= 1000
    ? `${(milliseconds / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 2 })} s`
    : `${Math.round(milliseconds)} ms`;
}

export function formatTime(iso: string | null): string {
  if (!iso) {
    return '—';
  }
  return new Date(iso).toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

export function quotaShare(used: number, quota: number): number {
  if (quota <= 0) {
    return 0;
  }
  return Math.min(100, Math.round((used / quota) * 100));
}
