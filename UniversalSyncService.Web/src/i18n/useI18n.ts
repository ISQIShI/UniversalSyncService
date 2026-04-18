import { useContext } from 'react';
import { I18nContext } from './I18nProvider';
import { I18nContextState } from './types';

export function useI18n(): I18nContextState {
  const context = useContext(I18nContext);
  if (!context) {
    throw new Error('useI18n must be used within an I18nProvider');
  }
  return context;
}
