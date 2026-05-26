import { useEffect, useRef, useState } from 'react';
import './App.css';
import { AppState, OverlayTextStyle, Settings, chooseOutputFolder as chooseOutputFolderApi, getArtworkUrl, getState, getSystemFonts, openOutputFolder as openOutputFolderApi, runDetectionNow as runDetectionNowApi, saveSettings as saveSettingsApi } from './api';

type SettingsTab = 'general' | 'overlay';

const hexColorPattern = /^#[0-9A-Fa-f]{6}$/;

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
    overlaySettings: {
      songTextStyle: {
        fontFamily: 'Segoe UI',
        colorHex: '#EBEBEB',
        fontSizePx: 24,
        maxCharacters: 0,
        bold: true,
        italic: false,
        underline: false,
      },
      artistTextStyle: {
        fontFamily: 'Segoe UI',
        colorHex: '#929498',
        fontSizePx: 15,
        maxCharacters: 0,
        bold: false,
        italic: false,
        underline: false,
      },
      albumTextStyle: {
        fontFamily: 'Segoe UI',
        colorHex: '#929498',
        fontSizePx: 15,
        maxCharacters: 0,
        bold: false,
        italic: false,
        underline: false,
      },
      imageSizePx: 68,
      backgroundColorHex: '#32334F',
      imagePosition: 'Left',
      textAlign: 'Left',
      showAppName: true,
      showPlaybackState: true,
    },
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

function isValidHexColor(value: string): boolean {
  return hexColorPattern.test(value.trim());
}

function isPositiveNumber(value: number): boolean {
  return Number.isFinite(value) && value > 0;
}

function isZeroOrPositiveNumber(value: number): boolean {
  return Number.isFinite(value) && value >= 0;
}

function buildFontOptions(systemFonts: string[], ...currentFonts: string[]): string[] {
  return Array.from(new Set([...systemFonts, ...currentFonts].filter(Boolean)))
    .sort((left, right) => left.localeCompare(right));
}

function textAlignToCss(value: Settings['overlaySettings']['textAlign']): 'left' | 'center' | 'right' {
  switch (value) {
    case 'Center':
      return 'center';
    case 'Right':
      return 'right';
    default:
      return 'left';
  }
}

function overlayTextStyleHasErrors(style: OverlayTextStyle): boolean {
  return !isValidHexColor(style.colorHex) || !isPositiveNumber(style.fontSizePx) || !isZeroOrPositiveNumber(style.maxCharacters) || !style.fontFamily.trim();
}

function overlaySettingsHaveErrors(settings: Settings): boolean {
  return (
    overlayTextStyleHasErrors(settings.overlaySettings.songTextStyle) ||
    overlayTextStyleHasErrors(settings.overlaySettings.artistTextStyle) ||
    overlayTextStyleHasErrors(settings.overlaySettings.albumTextStyle) ||
    !isPositiveNumber(settings.overlaySettings.imageSizePx) ||
    !isValidHexColor(settings.overlaySettings.backgroundColorHex)
  );
}

function App() {
  const [state, setState] = useState<AppState>(emptyState);
  const [draft, setDraft] = useState<Settings>(emptyState.settings);
  const [saving, setSaving] = useState(false);
  const [artworkFailed, setArtworkFailed] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<SettingsTab>('general');
  const [systemFonts, setSystemFonts] = useState<string[]>([]);
  const settingsOpenRef = useRef(settingsOpen);
  const artworkUrl = state.nowPlaying.artworkPath ? getArtworkUrl(state.artworkRevision) : '';
  const hasArtwork = Boolean(artworkUrl) && !artworkFailed;
  const effectiveThemeMode = settingsOpen ? draft.themeMode : state.settings.themeMode;
  const hasOverlayErrors = overlaySettingsHaveErrors(draft);

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
    let cancelled = false;

    getSystemFonts()
      .then((fonts) => {
        if (!cancelled) {
          setSystemFonts(fonts);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setSystemFonts([]);
        }
      });

    return () => {
      cancelled = true;
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
      closeSettings();
    } finally {
      setSaving(false);
    }
  };

  const openSettings = () => {
    setActiveTab('general');
    setSettingsOpen(true);
  };

  const closeSettings = () => {
    setActiveTab('general');
    setSettingsOpen(false);
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
          <button className="ghost chrome-button" onClick={openSettings}>Settings</button>
        </section>
        <section className="overlay-panel">
          <div className={`overlay-art ${hasArtwork ? 'has-artwork' : ''}`} style={{ borderRadius: 0 }}>
            {hasArtwork ? (
              <img
                key={String(state.artworkRevision)}
                className="cover-image"
                src={artworkUrl}
                alt={`${state.nowPlaying.title || 'Current track'} cover art`}
                style={{ borderRadius: 0 }}
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
                <button className="ghost" onClick={closeSettings}>Close</button>
              </div>
            </div>
            <div className="settings-tabs" role="tablist" aria-label="Settings sections">
              <button className={`tab-button ${activeTab === 'general' ? 'active' : ''}`} role="tab" aria-selected={activeTab === 'general'} onClick={() => setActiveTab('general')}>General</button>
              <button className={`tab-button ${activeTab === 'overlay' ? 'active' : ''}`} role="tab" aria-selected={activeTab === 'overlay'} onClick={() => setActiveTab('overlay')}>Overlay</button>
            </div>
            {activeTab === 'general' ? (
              <>
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
                <div className="toggle-list">
                  <Toggle label="Enable window title fallback" checked={draft.enableWindowTitleFallback} onChange={(value) => setDraft({ ...draft, enableWindowTitleFallback: value })} />
                  <Toggle label="Start minimized" checked={draft.startMinimized} onChange={(value) => setDraft({ ...draft, startMinimized: value })} />
                  <Toggle label="Launch at Windows startup" checked={draft.launchAtStartup} onChange={(value) => setDraft({ ...draft, launchAtStartup: value })} />
                </div>
              </>
            ) : (
              <div className="overlay-settings">
                <div className="text-style-card">
                  <h3>Overlay behavior</h3>
                  <div className="toggle-list">
                    <Toggle label="Enable local overlay" checked={draft.overlayEnabled} onChange={(value) => setDraft({ ...draft, overlayEnabled: value })} />
                  </div>
                  <div className="field-grid overlay-dimensions">
                    <div className="field inline-field">
                      <span>Overlay URL</span>
                      <strong>{state.overlayUrl || 'Disabled'}</strong>
                    </div>
                    <label>
                      Overlay port
                      <input type="number" value={draft.overlayPort} onChange={(event) => setDraft({ ...draft, overlayPort: Number(event.target.value) })} />
                    </label>
                  </div>
                  <div className="toggle-list">
                    <Toggle label="Show app name" checked={draft.overlaySettings.showAppName} onChange={(value) => setDraft({
                      ...draft,
                      overlaySettings: {
                        ...draft.overlaySettings,
                        showAppName: value,
                      },
                    })} />
                    <Toggle label="Show playback state" checked={draft.overlaySettings.showPlaybackState} onChange={(value) => setDraft({
                      ...draft,
                      overlaySettings: {
                        ...draft.overlaySettings,
                        showPlaybackState: value,
                      },
                    })} />
                  </div>
                </div>
                <TextStyleEditor
                  title="Song text"
                  fontOptions={buildFontOptions(systemFonts, draft.overlaySettings.songTextStyle.fontFamily)}
                  textAlign={draft.overlaySettings.textAlign}
                  value={draft.overlaySettings.songTextStyle}
                  onChange={(value) => setDraft({
                    ...draft,
                    overlaySettings: {
                      ...draft.overlaySettings,
                      songTextStyle: value,
                    },
                  })}
                />
                <TextStyleEditor
                  title="Artist text"
                  fontOptions={buildFontOptions(systemFonts, draft.overlaySettings.artistTextStyle.fontFamily)}
                  textAlign={draft.overlaySettings.textAlign}
                  value={draft.overlaySettings.artistTextStyle}
                  onChange={(value) => setDraft({
                    ...draft,
                    overlaySettings: {
                      ...draft.overlaySettings,
                      artistTextStyle: value,
                    },
                  })}
                />
                <TextStyleEditor
                  title="Album text"
                  fontOptions={buildFontOptions(systemFonts, draft.overlaySettings.albumTextStyle.fontFamily)}
                  textAlign={draft.overlaySettings.textAlign}
                  value={draft.overlaySettings.albumTextStyle}
                  onChange={(value) => setDraft({
                    ...draft,
                    overlaySettings: {
                      ...draft.overlaySettings,
                      albumTextStyle: value,
                    },
                  })}
                />
                <div className="field-grid overlay-dimensions">
                  <label>
                    Artwork image size (px)
                    <input
                      aria-invalid={!isPositiveNumber(draft.overlaySettings.imageSizePx)}
                      className={!isPositiveNumber(draft.overlaySettings.imageSizePx) ? 'invalid-field' : ''}
                      type="number"
                      min={1}
                      value={draft.overlaySettings.imageSizePx}
                      onChange={(event) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          imageSizePx: Number(event.target.value),
                        },
                      })}
                    />
                  </label>
                  <label>
                    Overlay background color
                    <input
                      aria-invalid={!isValidHexColor(draft.overlaySettings.backgroundColorHex)}
                      className={!isValidHexColor(draft.overlaySettings.backgroundColorHex) ? 'invalid-field' : ''}
                      type="text"
                      value={draft.overlaySettings.backgroundColorHex}
                      onChange={(event) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          backgroundColorHex: event.target.value,
                        },
                      })}
                    />
                  </label>
                </div>
                <div className="field-grid overlay-dimensions">
                  <label>
                    Artwork position
                    <select
                      value={draft.overlaySettings.imagePosition}
                      onChange={(event) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          imagePosition: event.target.value as Settings['overlaySettings']['imagePosition'],
                        },
                      })}
                    >
                      <option value="Left">Left</option>
                      <option value="Right">Right</option>
                    </select>
                  </label>
                  <label>
                    Text alignment
                    <select
                      value={draft.overlaySettings.textAlign}
                      onChange={(event) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          textAlign: event.target.value as Settings['overlaySettings']['textAlign'],
                        },
                      })}
                    >
                      <option value="Left">Left</option>
                      <option value="Center">Center</option>
                      <option value="Right">Right</option>
                    </select>
                  </label>
                </div>
                <p className="note">Hex colors must use the `#RRGGBB` format. Save is disabled while any overlay field is invalid.</p>
              </div>
            )}
            <div className="panel-footer">
              <button className="ghost" onClick={closeSettings}>Close</button>
              <button className="solid" disabled={saving || hasOverlayErrors} onClick={saveSettings}>{saving ? 'Saving...' : 'Save settings'}</button>
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

function TextStyleEditor({ title, fontOptions, value, textAlign, onChange }: { title: string; fontOptions: string[]; value: OverlayTextStyle; textAlign: Settings['overlaySettings']['textAlign']; onChange: (value: OverlayTextStyle) => void }) {
  const colorValid = isValidHexColor(value.colorHex);
  const fontSizeValid = isPositiveNumber(value.fontSizePx);
  const fontFamilyValid = Boolean(value.fontFamily.trim());

  return (
    <section className="text-style-card">
      <h3>{title}</h3>
      <div className="field-grid">
        <label>
          Font family
          <select
            aria-invalid={!fontFamilyValid}
            className={!fontFamilyValid ? 'invalid-field' : ''}
            value={value.fontFamily}
            onChange={(event) => onChange({ ...value, fontFamily: event.target.value })}
          >
            {fontOptions.map((font) => (
              <option key={font} value={font}>{font}</option>
            ))}
          </select>
        </label>
        <label>
          Color hex
          <input
            aria-invalid={!colorValid}
            className={!colorValid ? 'invalid-field' : ''}
            type="text"
            value={value.colorHex}
            onChange={(event) => onChange({ ...value, colorHex: event.target.value })}
          />
        </label>
        <label>
          Font size (px)
          <input
            aria-invalid={!fontSizeValid}
            className={!fontSizeValid ? 'invalid-field' : ''}
            type="number"
            min={1}
            value={value.fontSizePx}
            onChange={(event) => onChange({ ...value, fontSizePx: Number(event.target.value) })}
          />
        </label>
        <label>
          <span title="When text exceeds this length, the overlay shows that many characters and then ... to indicate more. Use 0 for unlimited with no truncation.">
            Character limit
          </span>
          <input
            aria-invalid={!isZeroOrPositiveNumber(value.maxCharacters)}
            className={!isZeroOrPositiveNumber(value.maxCharacters) ? 'invalid-field' : ''}
            type="number"
            min={0}
            title="When text exceeds this length, the overlay shows that many characters and then ... to indicate more. Use 0 for unlimited with no truncation."
            value={value.maxCharacters}
            onChange={(event) => onChange({ ...value, maxCharacters: Number(event.target.value) })}
          />
        </label>
      </div>
      <div className="font-preview" style={{
        fontFamily: value.fontFamily,
        color: value.colorHex,
        fontSize: `${value.fontSizePx}px`,
        fontWeight: value.bold ? '800' : '400',
        fontStyle: value.italic ? 'italic' : 'normal',
        textDecoration: value.underline ? 'underline' : 'none',
        textAlign: textAlignToCss(textAlign),
      }}>
        Sphinx of black quartz, judge my vow
      </div>
      <div className="toggle-list compact-toggles">
        <Toggle label="Bold" checked={value.bold} onChange={(next) => onChange({ ...value, bold: next })} />
        <Toggle label="Italic" checked={value.italic} onChange={(next) => onChange({ ...value, italic: next })} />
        <Toggle label="Underline" checked={value.underline} onChange={(next) => onChange({ ...value, underline: next })} />
      </div>
    </section>
  );
}

export default App;
