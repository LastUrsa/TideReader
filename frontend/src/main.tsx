import React from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';
import App from './App';
import { syncLocalApiTokenFromLocation } from './api';

const container = document.getElementById('root');
syncLocalApiTokenFromLocation();

const root = createRoot(container!);

root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
