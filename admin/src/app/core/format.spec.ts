import { formatLatency, formatNumber, formatPercent, formatUsd, quotaShare } from './format';

describe('format', () => {
  it('shows more decimals for tiny costs', () => {
    expect(formatUsd(0)).toBe('US$ 0,00');
    expect(formatUsd(12.5)).toBe('US$ 12,50');
    expect(formatUsd(0.0425)).toBe('US$ 0,0425');
    expect(formatUsd(0.000065)).toBe('US$ 0,000065');
  });

  it('compacts large numbers', () => {
    expect(formatNumber(1234)).toBe('1.234');
    expect(formatNumber(2_500_000)).toContain('mi');
  });

  it('formats percentages safely', () => {
    expect(formatPercent(1, 4)).toBe('25%');
    expect(formatPercent(1, 0)).toBe('0%');
  });

  it('formats latency in ms or seconds', () => {
    expect(formatLatency(null)).toBe('—');
    expect(formatLatency(87.4)).toBe('87 ms');
    expect(formatLatency(1530)).toBe('1,53 s');
  });

  it('caps the quota meter at 100%', () => {
    expect(quotaShare(50, 200)).toBe(25);
    expect(quotaShare(500, 200)).toBe(100);
    expect(quotaShare(10, 0)).toBe(0);
  });
});
