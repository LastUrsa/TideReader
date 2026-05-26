import { useEffect, useRef, useState } from 'react';
import './App.css';
import { AppState, Settings, chooseOutputFolder as chooseOutputFolderApi, getArtworkUrl, getState, openOutputFolder as openOutputFolderApi, runDetectionNow as runDetectionNowApi, saveSettings as saveSettingsApi } from './api';

const emptyState: AppState = {
  settings: {
    outputFolder: '',
    overlayEnabled: true,
    overlayPort: 17655,
    pollIntervalMs: 1000,
    enableWindowTitleFallback: true,
    enableDebugManualInput: false,
    startMinimized: false,
    launchAtStartup: false,
    metadataProviderMode: 'MusicBrainzWithFallbacks',
    themeMode: 'Dark',
  },
  nowPlaying: {
    status: 'not_running',
    title: '',
    artist: '',
    album: '',
    durationMs: 0,
    artworkPath: '',
    source: 'TIDAL',
    method: 'none',
    confidence: 0,
    detectedText: '',
    metadataSource: '',
  },
  outputFolder: '',
  artworkRevision: 0,
  overlayUrl: '',
  logPath: '',
  lastError: '',
  manualInput: '',
  startupReady: false,
  statusMessage: 'Loading...',
};

function notifyHostLayout(mode: 'compact' | 'settings') {
  const bridge = (window as Window & {
    chrome?: {
      webview?: {
        postMessage: (message: unknown) => void;
      };
    };
  }).chrome?.webview;

  if (!bridge) {
    return;
  }

  bridge.postMessage({ type: 'layout', mode });
}

function App() {
  const [state, setState] = useState<AppState>(emptyState);
  const [draft, setDraft] = useState<Settings>(emptyState.settings);
  const [saving, setSaving] = useState(false);
  const [artworkFailed, setArtworkFailed] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const settingsOpenRef = useRef(settingsOpen);
  const artworkUrl = state.nowPlaying.artworkPath ? getArtworkUrl(state.artworkRevision) : '';
  const effectiveThemeMode = settingsOpen ? draft.themeMode : state.settings.themeMode;

  useEffect(() => {
    settingsOpenRef.current = settingsOpen;
  }, [settingsOpen]);

  useEffect(() => {
    let cancelled = false;

    const refresh = async () => {
      const nextState = await getState();
      if (cancelled) {
        return;
      }
      setState(nextState);
      setDraft((current) => (settingsOpenRef.current ? current : nextState.settings));
    };

    refresh();
    const timer = window.setInterval(refresh, 1000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  useEffect(() => {
    setArtworkFailed(false);
  }, [state.artworkRevision, artworkUrl]);

  useEffect(() => {
    document.documentElement.dataset.theme = effectiveThemeMode.toLowerCase();
  }, [effectiveThemeMode]);

  useEffect(() => {
    notifyHostLayout(settingsOpen ? 'settings' : 'compact');
  }, [settingsOpen]);

  const saveSettings = async () => {
    setSaving(true);
    try {
      const nextState = await saveSettingsApi(draft);
      setState(nextState);
      setSettingsOpen(false);
    } finally {
      setSaving(false);
    }
  };

  const chooseOutputFolder = async () => {
    const folder = await chooseOutputFolderApi();
    if (!folder) {
      return;
    }

    setDraft((current) => ({
      ...current,
      outputFolder: folder,
    }));
  };

  const runNow = async () => {
    const nextState = await runDetectionNowApi();
    setState(nextState);
  };

  const openOutputFolder = async () => {
    await openOutputFolderApi();
  };

  return (
    <div className="shell">
      <main className="overlay-shell">
        <section className="app-chrome">
          <div className="app-chrome-title">
            <p className="app-name">TideReader</p>
            <div className={`status-pill ${state.nowPlaying.status}`}>{state.nowPlaying.status.replaceAll('_', ' ')}</div>
          </div>
          <button className="ghost chrome-button" onClick={() => setSettingsOpen(true)}>Settings</button>
        </section>
        <section className="overlay-panel">
          <div className="overlay-art">
            {artworkUrl && !artworkFailed ? (
              <img
                key={String(state.artworkRevision)}
                className="cover-image"
                src={artworkUrl}
                alt={`${state.nowPlaying.title || 'Current track'} cover art`}
                onError={() => setArtworkFailed(true)}
              />
            ) : (
              <span>{state.nowPlaying.title ? 'TIDAL' : 'Idle'}</span>
            )}
          </div>
          <div className="overlay-copy">
            <h1>{state.nowPlaying.title || 'Waiting for playback'}</h1>
            <p className="artist-line">{state.nowPlaying.artist || 'Artist unavailable'}</p>
            <p className="album-line">{state.nowPlaying.album || 'Album unavailable'}</p>
          </div>
          <div className="overlay-actions">
            <button className="ghost compact-button" onClick={runNow}>Refresh</button>
          </div>
        </section>
      </main>

      {settingsOpen ? (
        <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="settings-title">
          <section className="modal-panel">
            <div className="panel-head">
              <h2 id="settings-title">Settings</h2>
              <div className="row">
                <button className="ghost" onClick={() => setSettingsOpen(false)}>Close</button>
                <button className="solid" disabled={saving} onClick={saveSettings}>{saving ? 'Saving...' : 'Save settings'}</button>
              </div>
            </div>
            <div className="field">
              <label>Output folder</label>
              <div className="row">
                <input
                  title={draft.outputFolder || 'Not set'}
                  value={draft.outputFolder}
                  onChange={(event) => setDraft({ ...draft, outputFolder: event.target.value })}
                />
                <button className="ghost" onClick={chooseOutputFolder}>Browse</button>
              </div>
              <div className="row">
                <button className="ghost" onClick={openOutputFolder}>Open output folder</button>
              </div>
            </div>
            <div className="field-grid">
              <label>
                Overlay port
                <input type="number" value={draft.overlayPort} onChange={(event) => setDraft({ ...draft, overlayPort: Number(event.target.value) })} />
              </label>
              <label>
                Poll interval (ms)
                <input type="number" value={draft.pollIntervalMs} onChange={(event) => setDraft({ ...draft, pollIntervalMs: Number(event.target.value) })} />
              </label>
            </div>
            <label className="field">
              <span>Theme mode</span>
              <select value={draft.themeMode} onChange={(event) => setDraft({ ...draft, themeMode: event.target.value as Settings['themeMode'] })}>
                <option value="Dark">Dark</option>
                <option value="Light">Light</option>
              </select>
            </label>
            <label className="field">
              <span>Metadata provider mode</span>
              <select value={draft.metadataProviderMode} onChange={(event) => setDraft({ ...draft, metadataProviderMode: event.target.value as Settings['metadataProviderMode'] })}>
                <option value="Off">Off</option>
                <option value="MusicBrainzOnly">MusicBrainz only</option>
                <option value="MusicBrainzWithFallbacks">MusicBrainz + fallbacks</option>
              </select>
            </label>
            <div className="field">
              <span>Overlay URL</span>
              <strong>{state.overlayUrl || 'Disabled'}</strong>
            </div>
            <div className="toggle-list">
              <Toggle label="Enable local overlay" checked={draft.overlayEnabled} onChange={(value) => setDraft({ ...draft, overlayEnabled: value })} />
              <Toggle label="Enable window title fallback" checked={draft.enableWindowTitleFallback} onChange={(value) => setDraft({ ...draft, enableWindowTitleFallback: value })} />
              <Toggle label="Start minimized" checked={draft.startMinimized} onChange={(value) => setDraft({ ...draft, startMinimized: value })} />
              <Toggle label="Launch at Windows startup" checked={draft.launchAtStartup} onChange={(value) => setDraft({ ...draft, launchAtStartup: value })} />
            </div>
          </section>
        </div>
      ) : null}
    </div>
  );
}

function Toggle({ label, checked, onChange }: { label: string; checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <label className="toggle">
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

export default App;
