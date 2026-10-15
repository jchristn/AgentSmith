import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { AuthProvider, useAuth } from './context/AuthContext';
import { AppProvider } from './context/AppContext';
import { ToastProvider } from './context/ToastContext';
import Login from './components/Login';
import Dashboard from './components/Dashboard';
import LegacyRedirect from './components/LegacyRedirect';
import { TooltipLayer } from './components/Tooltip';
import { LEGACY_REDIRECTS, DASHBOARD_BASE } from './config/navConfig';

function AppLoading() {
  const { t } = useTranslation();
  return (
    <div className="app-loading" role="status">
      <div className="loading-spinner" />
      <p>{t('login.restoring')}</p>
    </div>
  );
}

function PrivateRoute({ children }) {
  const { isAuthenticated, isLoading } = useAuth();
  if (isLoading) return <AppLoading />;
  return isAuthenticated ? <AppProvider>{children}</AppProvider> : <Navigate to="/" replace />;
}

function PublicRoute({ children }) {
  const { isAuthenticated, isLoading } = useAuth();
  if (isLoading) return <AppLoading />;
  return !isAuthenticated ? children : <Navigate to="/dashboard/home" replace />;
}

function AppRoutes() {
  return (
    <Routes>
      <Route
        path="/"
        element={
          <PublicRoute>
            <Login />
          </PublicRoute>
        }
      />
      <Route path="/dashboard" element={<Navigate to="/dashboard/home" replace />} />
      {/* Pre-hub URLs replace-redirect to their hub and tab, keeping the query string. */}
      {LEGACY_REDIRECTS.map((r) => (
        <Route key={r.from} path={`${DASHBOARD_BASE}/${r.from}`} element={<LegacyRedirect to={r.to} tab={r.tab} />} />
      ))}
      <Route
        path="/dashboard/:section"
        element={
          <PrivateRoute>
            <Dashboard />
          </PrivateRoute>
        }
      />
      <Route
        path="/dashboard/:section/:id"
        element={
          <PrivateRoute>
            <Dashboard />
          </PrivateRoute>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

/** Root component. */
export default function App() {
  return (
    <AuthProvider>
      <ToastProvider>
        <BrowserRouter>
          <AppRoutes />
        </BrowserRouter>
        <TooltipLayer />
      </ToastProvider>
    </AuthProvider>
  );
}
