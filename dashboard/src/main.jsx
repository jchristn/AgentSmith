import React from 'react';
import ReactDOM from 'react-dom/client';
import { i18nReady } from './i18n';
import App from './App';
import './index.css';
import './hugo.css';

// Render only after the active locale's catalog is loaded, so the first paint is already in the right language.
i18nReady.finally(() => {
  ReactDOM.createRoot(document.getElementById('root')).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
});
