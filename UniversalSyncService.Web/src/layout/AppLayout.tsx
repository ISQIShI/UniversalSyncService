import { useEffect, useState } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAppStore } from '../store/useAppStore.ts';
import { Sidebar, type NavigationItem } from '../components/layout/Sidebar.tsx';
import { StatusBadge } from '../components/common/StatusBadge.tsx';
import { useDocumentTheme } from '../hooks/useTheme.ts';

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
            <span className="eyebrow">控制台</span>
            <h1>Universal Sync 服务控制台</h1>
          </div>

          <div className="topbar-right">
            <StatusBadge tone={health === 'Healthy' ? 'healthy' : 'danger'} label={health === 'Healthy' ? '系统正常' : '系统离线'} />
            <label className="theme-select-row">
              <span>主题</span>
              <select value={themeMode} onChange={(event) => setThemeMode(event.target.value as 'light' | 'dark' | 'system')}>
                <option value="light">浅色</option>
                <option value="dark">深色</option>
                <option value="system">系统</option>
              </select>
            </label>
            <label className="toggle-row">
              <input type="checkbox" checked={liveUpdates} onChange={(event) => setLiveUpdates(event.target.checked)} />
              <span>实时刷新</span>
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
              placeholder="请输入管理 API Key..."
              onKeyDown={(e) => {
                if (e.key === 'Enter' && apiKeyInput) {
                  connect(apiKeyInput);
                }
              }}
            />
            <button type="button" className="primary" data-testid="connect-button" onClick={() => connect(apiKeyInput)} disabled={!apiKeyInput}>
              连接控制台
            </button>
            {isConnected && (
              <>
                <button type="button" onClick={() => fetchConsoleState()} disabled={isBusy}>
                  立即刷新
                </button>
                <button type="button" onClick={() => {
                  setApiKeyInput('');
                  disconnect();
                }}>断开连接</button>
              </>
            )}
          </section>
        ) : (
          <section className="auth-strip auth-strip-auto">
            <StatusBadge tone="healthy" label="本机免密访问已启用" />
            <span>当前控制台已启用免密访问。</span>
            <button type="button" onClick={() => fetchConsoleState()} disabled={!(canUseAnonymousApi || isConnected) || isBusy}>立即刷新</button>
          </section>
        )}

        {message ? <div className="message-banner error">{message}</div> : null}

        <Outlet />
      </main>
    </div>
  );
}
