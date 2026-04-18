import { LocaleCode, I18nKey, LocaleDictionary } from './generated/resources';

export type { LocaleCode, I18nKey, LocaleDictionary };

export type I18nContextState = {
  locale: LocaleCode;
  setLocale: (locale: LocaleCode) => void;
  t: (key: I18nKey, params?: Record<string, string | number>) => string;
};