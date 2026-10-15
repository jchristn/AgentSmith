import { createContext, useContext, useState, useCallback, useMemo } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { formatNumber } from '../i18n/formatters';

const ToastContext = createContext(null);

let nextId = 1;

/**
 * Toasts and a progress panel for long operations (bulk deletes, imports).
 *
 *  - addToast({ message, type, duration }) shows a toast; click dismisses it.
 *  - startProgress(label, total) returns an id; updateProgress(id, done) advances it; endProgress(id) removes it.
 */
export function ToastProvider({ children }) {
  const { t, i18n } = useTranslation();
  const [toasts, setToasts] = useState([]);
  const [tasks, setTasks] = useState([]);

  const removeToast = useCallback((id) => {
    setToasts((prev) => prev.filter((x) => x.id !== id));
  }, []);

  const addToast = useCallback(
    ({ message, type = 'info', duration = 4000 }) => {
      const id = nextId++;
      setToasts((prev) => [...prev, { id, message, type }]);
      if (duration > 0) setTimeout(() => removeToast(id), duration);
      return id;
    },
    [removeToast]
  );

  const startProgress = useCallback((label, total = 0) => {
    const id = nextId++;
    setTasks((prev) => [...prev, { id, label, total, done: 0 }]);
    return id;
  }, []);

  const updateProgress = useCallback((id, done) => {
    setTasks((prev) => prev.map((task) => (task.id === id ? { ...task, done } : task)));
  }, []);

  const endProgress = useCallback((id) => {
    setTasks((prev) => prev.filter((task) => task.id !== id));
  }, []);

  const value = useMemo(
    () => ({ addToast, removeToast, startProgress, updateProgress, endProgress }),
    [addToast, removeToast, startProgress, updateProgress, endProgress]
  );

  return (
    <ToastContext.Provider value={value}>
      {children}
      {createPortal(
        <div className="toast-stack" role="status" aria-live="polite">
          {tasks.map((task) => (
            <div key={task.id} className="toast toast-progress" data-testid="progress-panel">
              <div>{task.label}</div>
              {task.total > 0 && (
                <>
                  <progress max={task.total} value={task.done} aria-label={task.label} />
                  <div className="toast-progress-count">
                    {t('components.progress.count', { done: formatNumber(task.done, {}, i18n.language), total: formatNumber(task.total, {}, i18n.language) })}
                  </div>
                </>
              )}
            </div>
          ))}
          {toasts.map((x) => (
            <div key={x.id} className={`toast toast-${x.type}`} onClick={() => removeToast(x.id)}>
              {x.message}
            </div>
          ))}
        </div>,
        document.body
      )}
    </ToastContext.Provider>
  );
}

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be used within a ToastProvider');
  return ctx;
}

export default ToastContext;
