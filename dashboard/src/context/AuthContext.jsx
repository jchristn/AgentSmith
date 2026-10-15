import { createContext, useContext, useState, useEffect, useCallback } from 'react';
import ApiClient from '../utils/api';
import { STORAGE_KEYS } from '../utils/constants';

const AuthContext = createContext(null);

/**
 * Authentication and session provider. Owns the ApiClient, server URL, token, the resolved principal (from
 * GET /v1.0/token/details), the forced password change flag, and the theme. Restores a saved session on load before
 * any private route renders, so protected pages never flicker. The locale is not part of the session: logout keeps it.
 */
export function AuthProvider({ children }) {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [apiClient, setApiClient] = useState(null);
  const [serverUrl, setServerUrl] = useState('');
  const [token, setToken] = useState('');
  const [principal, setPrincipal] = useState(null);
  // Set when the server says the password is still the seeded or reset default. The dashboard prompts for a change but
  // never blocks: Skip dismisses the prompt for the rest of this browser session (D-048).
  const [mustChangePassword, setMustChangePassword] = useState(false);
  const [passwordPromptSkipped, setPasswordPromptSkipped] = useState(() => {
    try {
      return sessionStorage.getItem('hugo_password_prompt_skipped') === 'true';
    } catch {
      return false;
    }
  });
  const [theme, setTheme] = useState(() => localStorage.getItem(STORAGE_KEYS.theme) || 'light');

  useEffect(() => {
    document.body.setAttribute('data-theme', theme);
  }, [theme]);

  useEffect(() => {
    const savedUrl = localStorage.getItem(STORAGE_KEYS.serverUrl);
    const savedToken = localStorage.getItem(STORAGE_KEYS.token);
    if (!savedUrl || !savedToken) {
      setIsLoading(false);
      return;
    }
    const client = new ApiClient(savedUrl, savedToken);
    client
      .getTokenDetails()
      .then((p) => {
        if (!p) throw new Error('invalid');
        setServerUrl(savedUrl);
        setToken(savedToken);
        setApiClient(client);
        setPrincipal(p);
        setMustChangePassword(!!p?.mustChangePassword);
        setIsAuthenticated(true);
      })
      .catch(() => {
        localStorage.removeItem(STORAGE_KEYS.token);
      })
      .finally(() => setIsLoading(false));
  }, []);

  const logout = useCallback(() => {
    try {
      sessionStorage.removeItem('hugo_password_prompt_skipped');
    } catch {
      // Storage may be unavailable.
    }
    setPasswordPromptSkipped(false);
    if (apiClient) apiClient.revokeToken().catch(() => {});
    localStorage.removeItem(STORAGE_KEYS.token);
    setToken('');
    setApiClient(null);
    setPrincipal(null);
    setMustChangePassword(false);
    setIsAuthenticated(false);
  }, [apiClient]);

  useEffect(() => {
    const handler = () => logout();
    window.addEventListener('auth:unauthorized', handler);
    return () => window.removeEventListener('auth:unauthorized', handler);
  }, [logout]);

  /**
   * Log in. mode 'password' uses { tenantId, email, password }; mode 'key' uses { accessKey, secretKey }.
   */
  const login = useCallback(async (url, mode, creds) => {
    const base = (url || '').replace(/\/$/, '');
    const temp = new ApiClient(base, null);
    const result =
      mode === 'password'
        ? await temp.loginWithPassword(creds.tenantId, creds.email, creds.password)
        : await temp.loginWithKey(creds.accessKey, creds.secretKey);
    const newToken = result?.token;
    if (!newToken) throw new Error('No token returned');

    const client = new ApiClient(base, newToken);
    const p = await client.getTokenDetails();

    localStorage.setItem(STORAGE_KEYS.serverUrl, base);
    localStorage.setItem(STORAGE_KEYS.token, newToken);
    setServerUrl(base);
    setToken(newToken);
    setApiClient(client);
    setPrincipal(p);
    setMustChangePassword(!!(result?.mustChangePassword || result?.passwordChangeRequired || p?.mustChangePassword));
    setIsAuthenticated(true);
  }, []);

  /** Called after the forced password change succeeds. */
  const passwordChanged = useCallback(() => setMustChangePassword(false), []);
  const skipPasswordChange = useCallback(() => {
    setPasswordPromptSkipped(true);
    try {
      sessionStorage.setItem('hugo_password_prompt_skipped', 'true');
    } catch {
      // Storage may be unavailable (private mode); the skip still applies until reload.
    }
  }, []);
  const passwordPromptOpen = mustChangePassword && !passwordPromptSkipped;

  const toggleTheme = useCallback(() => {
    setTheme((prev) => {
      const next = prev === 'light' ? 'dark' : 'light';
      localStorage.setItem(STORAGE_KEYS.theme, next);
      return next;
    });
  }, []);

  const value = { isAuthenticated, isLoading, apiClient, serverUrl, token, principal, theme, login, logout, toggleTheme, mustChangePassword, passwordChanged, skipPasswordChange, passwordPromptOpen };
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within an AuthProvider');
  return context;
}

export default AuthContext;
