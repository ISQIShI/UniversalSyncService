import { LocaleCode } from './types';

export function formatDate(date: Date | string | number, locale: LocaleCode): string {
  const d = new Date(date);
  if (isNaN(d.getTime())) return '';
  return new Intl.DateTimeFormat(locale, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: 'numeric',
    minute: 'numeric',
    second: 'numeric'
  }).format(d);
}

export function formatNumber(num: number, locale: LocaleCode): string {
  return new Intl.NumberFormat(locale).format(num);
}
