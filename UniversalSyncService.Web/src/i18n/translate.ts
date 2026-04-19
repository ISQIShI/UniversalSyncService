import { fallbackLocale, resources, type I18nKey, type LocaleCode } from './generated/resources';

type TranslateParams = Record<string, string | number>;

function isLocaleCode(value: string | null): value is LocaleCode {
  return value !== null && Object.prototype.hasOwnProperty.call(resources, value);
}

export function resolveCurrentLocale(): LocaleCode {
  if (typeof window === 'undefined') {
    return fallbackLocale;
  }

  const savedLocale = window.localStorage.getItem('uss_locale');
  if (isLocaleCode(savedLocale)) {
    return savedLocale;
  }

  const browserLanguage = window.navigator?.language;
  if (browserLanguage?.startsWith('zh')) {
    return 'zh-CN';
  }

  if (isLocaleCode(browserLanguage ?? null)) {
    return browserLanguage;
  }

  return fallbackLocale;
}

export function translateByLocale(locale: LocaleCode, key: I18nKey, params?: TranslateParams): string {
  let text = resources[locale]?.[key] || resources[fallbackLocale]?.[key] || key;

  if (params) {
    for (const [paramKey, paramValue] of Object.entries(params)) {
      text = text.replace(new RegExp(`\\{${paramKey}\\}`, 'g'), String(paramValue));
    }
  }

  return text;
}

export function translateForCurrentLocale(key: I18nKey, params?: TranslateParams): string {
  return translateByLocale(resolveCurrentLocale(), key, params);
}
