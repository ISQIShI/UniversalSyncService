import { LocaleCode, fallbackLocale, resources } from './generated/resources';

function isLocaleCode(value: string | null): value is LocaleCode {
  return value !== null && Object.prototype.hasOwnProperty.call(resources, value);
}

export function getInitialLocale(): LocaleCode {
  // 1. User override
  const saved = localStorage.getItem('uss_locale');
  if (isLocaleCode(saved)) {
    return saved;
  }

  // 2. Browser language
  if (typeof window !== 'undefined' && window.navigator && window.navigator.language) {
    const browserLang = window.navigator.language;
    if (isLocaleCode(browserLang)) {
      return browserLang;
    }

    if (browserLang.startsWith('zh') && isLocaleCode('zh-CN')) {
      return 'zh-CN';
    }
  }

  // 3. Fallback
  return fallbackLocale;
}

export function saveLocale(locale: LocaleCode) {
  localStorage.setItem('uss_locale', locale);
}
