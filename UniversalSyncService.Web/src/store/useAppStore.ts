import { create } from 'zustand';
import {
  ApiRequestError,
  executePlan,
  getConfigSummary,
  getGlobalHistory,
  getHealth,
  getNodes,
  getPlans,
  getPlugins,
  getPublicInterfaceProfile,
  getStatus,
  type ConfigSummary,
  type ExecutePlanResponse,
  type HistoryEntry,
  type NodeSummary,
  type PlanSummary,
  type PluginSummary,
  type PublicInterfaceProfile,
  type ServiceStatus,
} from '../api.ts';
import { translateForCurrentLocale } from '../i18n/translate.ts';

interface AppState {
  // Auth & Connection
  apiKey: string | null;
  isConnected: boolean;
  health: string;
  interfaceProfile: PublicInterfaceProfile | null;
  canUseAnonymousApi: boolean;
  
  // App Settings
  liveUpdates: boolean;
  themeMode: 'light' | 'dark' | 'system';
  
  // Data State
  status: ServiceStatus | null;
  plans: PlanSummary[];
  nodes: NodeSummary[];
  plugins: PluginSummary[];
  configSummary: ConfigSummary | null;
  globalHistory: HistoryEntry[];
  
  // UI State
  isBusy: boolean;
  message: string;
  
  // Actions
  setApiKey: (key: string | null) => void;
  setConnected: (connected: boolean) => void;
  setLiveUpdates: (enabled: boolean) => void;
  setThemeMode: (mode: 'light' | 'dark' | 'system') => void;
  setMessage: (msg: string) => void;
  
  // Async Thunks
  fetchHealthAndProfile: () => Promise<void>;
  fetchConsoleState: (options?: { silent?: boolean }) => Promise<void>;
  executePlanNow: (planId: string) => Promise<ExecutePlanResponse>;
  connect: (key?: string) => void;
  disconnect: () => void;
}

export const useAppStore = create<AppState>((set, get) => ({
  apiKey: localStorage.getItem('uss_api_key'),
  isConnected: false, // We'll verify this
  health: 'Checking',
  interfaceProfile: null,
  canUseAnonymousApi: false,
  
  liveUpdates: true,
  themeMode: (localStorage.getItem('uss_theme') as 'light' | 'dark' | 'system') || 'system',
  
  status: null,
  plans: [],
  nodes: [],
  plugins: [],
  configSummary: null,
  globalHistory: [],
  
  isBusy: false,
  message: '',
  
  setApiKey: (key) => {
    if (key) localStorage.setItem('uss_api_key', key);
    else localStorage.removeItem('uss_api_key');
    set({ apiKey: key });
  },
  setConnected: (connected) => set({ isConnected: connected }),
  setLiveUpdates: (enabled) => set({ liveUpdates: enabled }),
  setThemeMode: (mode) => {
    localStorage.setItem('uss_theme', mode);
    set({ themeMode: mode });
  },
  setMessage: (message) => set({ message }),
  
  fetchHealthAndProfile: async () => {
    try {
      const healthRes = await getHealth();
      set({ health: healthRes.status });
    } catch {
      set({ health: 'Unavailable' });
    }

    try {
      const profile = await getPublicInterfaceProfile();
      set({ interfaceProfile: profile, canUseAnonymousApi: !profile.requireManagementApiKey });
      if (!profile.requireManagementApiKey) {
        set({ isConnected: true });
      }
    } catch {
      set({ interfaceProfile: null, canUseAnonymousApi: false });
    }
  },
  
  fetchConsoleState: async (options) => {
    const { apiKey, canUseAnonymousApi } = get();
    if (!apiKey && !canUseAnonymousApi) return;
    
    const currentCredential = apiKey || '';
    
    try {
      if (!options?.silent) set({ isBusy: true });
      
      const [status, plans, nodes, plugins, configSummary, globalHistory] = await Promise.all([
        getStatus(currentCredential),
        getPlans(currentCredential),
        getNodes(currentCredential),
        getPlugins(currentCredential),
        getConfigSummary(currentCredential),
        getGlobalHistory(currentCredential, 12),
      ]);
      
      set({
        status,
        plans,
        nodes,
        plugins,
        configSummary,
        globalHistory,
        message: '',
        isConnected: true,
      });
    } catch (error) {
      set({ message: error instanceof Error ? error.message : translateForCurrentLocale('web.store.error.fetchConsoleStateFailed') });
      if (error instanceof ApiRequestError && error.status === 401) {
        set({ isConnected: false });
      }
    } finally {
      if (!options?.silent) set({ isBusy: false });
    }
  },

  executePlanNow: async (planId) => {
    const { apiKey, canUseAnonymousApi, fetchConsoleState } = get();
    if (!apiKey && !canUseAnonymousApi) {
      throw new Error(translateForCurrentLocale('web.store.error.notConnected'));
    }

    const currentCredential = apiKey || '';

    try {
      set({ isBusy: true, message: '' });
      const result = await executePlan(planId, currentCredential);
      await fetchConsoleState();
      return result;
    } catch (error) {
      const message = error instanceof Error ? error.message : translateForCurrentLocale('web.store.error.executePlanFailed');
      set({ message });
      throw error;
    } finally {
      set({ isBusy: false });
    }
  },
  
  connect: (key?: string) => {
    if (key !== undefined) {
      if (key) localStorage.setItem('uss_api_key', key);
      else localStorage.removeItem('uss_api_key');
      set({ apiKey: key || null });
    }
    set({ isConnected: true, message: '' });
    get().fetchConsoleState();
  },
  
  disconnect: () => {
    localStorage.removeItem('uss_api_key');
    set({
      apiKey: null,
      isConnected: false,
      plans: [],
      nodes: [],
      status: null,
      globalHistory: [],
      configSummary: null,
      plugins: [],
      message: '',
    });
  },
}));
