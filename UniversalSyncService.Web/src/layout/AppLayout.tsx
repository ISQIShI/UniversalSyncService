import { useEffect, useState } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAppStore } from '../store/useAppStore.ts';
import { Sidebar, type NavigationItem } from '../components/layout/Sidebar.tsx';
import { StatusBadge } from '../components/common/StatusBadge.tsx';
import { useDocumentTheme } from '../hooks/useTheme.ts';
import { useI18n } from '../i18n/useI18n.ts';

export function AppLayout() {
  const {
    apiKey,
    isConnected,
    health,
    interfaceProfile,
    canUseAnonymousApi,
    liveUpdates,
    themeMode,
    message,
    isBusy,
    setThemeMode,
    setLiveUpdates,
    connect,
    disconnect,
    fetchConsoleState,
    fetchHealthAndProfile,
  } = useAppStore();

  const [apiKeyInput, setApiKeyInput] = useState(apiKey || '');
  const location = useLocation();
  const navigate = useNavigate();
  const { locale, setLocale, t } = useI18n();

  useDocumentTheme(themeMode);

  // Map route to active view
  let activeView: NavigationItem = 'dashboard';
  if (location.pathname.startsWith('/plans')) activeView = 'plans';
  if (location.pathname.startsWith('/nodes')) activeView = 'nodes';
  if (location.pathname.startsWith('/system')) activeView = 'system';

  useEffect(() => {
    fetchHealthAndProfile();
  }, [fetchHealthAndProfile]);

  useEffect(() => {
    if (canUseAnonymousApi && !isConnected) {
      connect();
    }
  }, [canUseAnonymousApi, connect, isConnected]);

  useEffect(() => {
    if (!apiKey && !canUseAnonymousApi) return;

    fetchConsoleState();

    if (!liveUpdates) return;
    
    const timer = window.setInterval(() => {
      fetchConsoleState({ silent: true });
    }, 10_000);

    return () => window.clearInterval(timer);
  }, [apiKey, canUseAnonymousApi, fetchConsoleState, liveUpdates]);

  const handleSelectMenu = (view: NavigationItem) => {
    navigate(`/${view === 'dashboard' ? '' : view}`);
  };

  return (
    <div className="console-shell">
      <Sidebar activeItem={activeView} onSelect={handleSelectMenu} />

      <main className="console-main">
        <header className="topbar">
          <div>
            <span className="eyebrow">{t('web.app.console')}</span>
            <h1>{t('web.app.title')}</h1>
          </div>

          <div className="topbar-right">
            <StatusBadge tone={health === 'Healthy' ? 'healthy' : 'danger'} label={health === 'Healthy' ? t('web.app.status.healthy') : t('web.app.status.offline')} />
            <label className="theme-select-row">
              <span>{t('web.app.language')}</span>
              <select data-testid="locale-switcher" value={locale} onChange={(event) => setLocale(event.target.value as 'en' | 'zh-CN')}>
                <option value="en">English</option>
                <option value="zh-CN">中文</option>
              </select>
            </label>
            <label className="theme-select-row">
              <span>{t('web.app.theme')}</span>
              <select value={themeMode} onChange={(event) => setThemeMode(event.target.value as 'light' | 'dark' | 'system')}>
                <option value="light">{t('web.app.theme.light')}</option>
                <option value="dark">{t('web.app.theme.dark')}</option>
                <option value="system">{t('web.app.theme.system')}</option>
              </select>
            </label>
            <label className="toggle-row">
              <input type="checkbox" checked={liveUpdates} onChange={(event) => setLiveUpdates(event.target.checked)} />
              <span>{t('web.app.liveUpdates')}</span>
            </label>
          </div>
        </header>

        {/* 【注意】这里保留了 api-key-input 与 connect-button 的 data-testid 以维持 Playwright E2E 测试兼容性 */}
        {interfaceProfile?.requireManagementApiKey !== false ? (
          <section className="auth-strip">
            <input
              data-testid="api-key-input"
              type="password"
              value={apiKeyInput}
              onChange={(event) => setApiKeyInput(event.target.value)}
              placeholder={t('web.app.auth.placeholder')}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && apiKeyInput) {
                  connect(apiKeyInput);
                }
              }}
            />
            <button type="button" className="primary" data-testid="connect-button" onClick={() => connect(apiKeyInput)} disabled={!apiKeyInput}>
              {t('web.app.auth.connect')}
            </button>
            {isConnected && (
              <>
                <button type="button" onClick={() => fetchConsoleState()} disabled={isBusy}>
                  {t('web.app.auth.refresh')}
                </button>
                <button type="button" onClick={() => {
                  setApiKeyInput('');
                  disconnect();
                }}>{t('web.app.auth.disconnect')}</button>
              </>
            )}
          </section>
        ) : (
          <section className="auth-strip auth-strip-auto">
            <StatusBadge tone="healthy" label={t('web.app.auth.anonymous.title')} />
            <span>{t('web.app.auth.anonymous.message')}</span>
            <button type="button" onClick={() => fetchConsoleState()} disabled={!(canUseAnonymousApi || isConnected) || isBusy}>{t('web.app.auth.refresh')}</button>
          </section>
        )}

        {message ? <div className="message-banner error">{message}</div> : null}

        <Outlet />
      </main>
    </div>
  );
}
