import React, { createContext, useState, useCallback, useMemo, ReactNode } from 'react';
import { I18nContextState, LocaleCode, I18nKey } from './types';
import { resources, fallbackLocale } from './generated/resources';
import { getInitialLocale, saveLocale } from './locale';

export const I18nContext = createContext<I18nContextState | null>(null);

interface I18nProviderProps {
  children: ReactNode;
}

export const I18nProvider: React.FC<I18nProviderProps> = ({ children }) => {
  const [locale, setLocaleState] = useState<LocaleCode>(getInitialLocale);

  const setLocale = useCallback((newLocale: LocaleCode) => {
    setLocaleState(newLocale);
    saveLocale(newLocale);
  }, []);

  const t = useCallback(
    (key: I18nKey, params?: Record<string, string | number>) => {
      let text = resources[locale]?.[key] || resources[fallbackLocale]?.[key] || key;

      if (params) {
        Object.entries(params).forEach(([k, v]) => {
          text = text.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v));
        });
      }

      return text;
    },
    [locale]
  );

  const value = useMemo(
    () => ({
      locale,
      setLocale,
      t,
    }),
    [locale, setLocale, t]
  );

  React.useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
};
