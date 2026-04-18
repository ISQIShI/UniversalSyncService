import { useMemo, useState } from 'react';

const storageKey = 'universal-sync-service-api-key';
const connectedStorageKey = 'universal-sync-service-api-connected';

export function useApiKey() {
  const [apiKeyInput, setApiKeyInput] = useState(() => sessionStorage.getItem(storageKey) ?? '');
  const [apiKey, setApiKey] = useState(() => sessionStorage.getItem(storageKey) ?? '');
  const [connected, setConnected] = useState(() => sessionStorage.getItem(connectedStorageKey) == 'true');

  const isConnected = useMemo(() => connected, [connected]);

  function connect() {
    sessionStorage.setItem(storageKey, apiKeyInput);
    sessionStorage.setItem(connectedStorageKey, 'true');
    setApiKey(apiKeyInput);
    setConnected(true);
  }

  function disconnect() {
    sessionStorage.removeItem(storageKey);
    sessionStorage.removeItem(connectedStorageKey);
    setApiKey('');
    setApiKeyInput('');
    setConnected(false);
  }

  return {
    apiKey,
    apiKeyInput,
    isConnected,
    setApiKeyInput,
    connect,
    disconnect,
  };
}



