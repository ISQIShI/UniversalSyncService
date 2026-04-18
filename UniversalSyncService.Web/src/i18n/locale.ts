import { LocaleCode, fallbackLocale, resources } from './generated/resources';

export function getInitialLocale(): LocaleCode {
  // 1. User override
  const saved = localStorage.getItem('uss_locale');
  if (saved && (saved === 'en' || saved === 'zh-CN')) {
    return saved as LocaleCode;
  }

  // 2. Browser language
  if (typeof window !== 'undefined' && window.navigator && window.navigator.language) {
    const browserLang = window.navigator.language;
    if (browserLang.startsWith('zh')) {
      return 'zh-CN';
    }
    if (resources[browserLang as LocaleCode]) {
      return browserLang as LocaleCode;
    }
  }

  // 3. Fallback
  return fallbackLocale;
}

export function saveLocale(locale: LocaleCode) {
  localStorage.setItem('uss_locale', locale);
}
