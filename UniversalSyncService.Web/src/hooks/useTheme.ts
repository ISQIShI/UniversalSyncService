import { useEffect } from 'react';

export type ThemeMode = 'light' | 'dark' | 'system';

export function resolveThemeMode(themeMode: ThemeMode) {
  return themeMode === 'system'
    ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
    : themeMode;
}

export function useDocumentTheme(themeMode: ThemeMode) {
  useEffect(() => {
    // 【主题】由此 hook 作为唯一 DOM theme owner，store 仅保存状态与持久化。
    const root = document.documentElement;
    const applyTheme = () => {
      const resolvedTheme = resolveThemeMode(themeMode);

      root.dataset.themeMode = themeMode;
      root.dataset.theme = resolvedTheme;
    };

    applyTheme();

    if (themeMode !== 'system') {
      return;
    }

    const media = window.matchMedia('(prefers-color-scheme: dark)');
    media.addEventListener('change', applyTheme);
    return () => media.removeEventListener('change', applyTheme);
  }, [themeMode]);
}
