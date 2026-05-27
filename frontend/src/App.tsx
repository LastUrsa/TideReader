import { type ReactNode, useEffect, useRef, useState } from 'react';
import './App.css';
import NowPlayingOverlayView from './NowPlayingOverlayView';
import { AppState, DetectionResult, GradientSettings, OverlayTextStyle, Settings, UpdateInfo, checkForUpdates as checkForUpdatesApi, chooseOutputFolder as chooseOutputFolderApi, getArtworkUrl, getState, getSystemFonts, openOutputFolder as openOutputFolderApi, openReleasePage as openReleasePageApi, runDetectionNow as runDetectionNowApi, saveSettings as saveSettingsApi } from './api';
import { cloneOverlaySettings, createDefaultSettings, defaultOverlaySettings, formatPlaybackStatus, getGradientPresetOptions, gradientColorCountOptions, isGradientAngleValid, isOpacityValid, isPositiveNumber, isValidHexColor, isZeroOrPositiveNumber, overlayBackgroundModeOptions, overlaySettingsHaveErrors, overlayTextStyleHasErrors, sampleNowPlaying } from './overlay';

type SettingsTab = 'general' | 'overlay';

const emptyState: AppState = {
  settings: createDefaultSettings(),
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
  appVersion: '',
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

function buildFontOptions(systemFonts: string[], ...currentFonts: string[]): string[] {
  return Array.from(new Set([...systemFonts, ...currentFonts].filter(Boolean)))
    .sort((left, right) => left.localeCompare(right));
}

async function copyText(value: string) {
  if (!value.trim()) {
    return;
  }

  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(value);
    return;
  }

  const helper = document.createElement('textarea');
  helper.value = value;
  helper.setAttribute('readonly', '');
  helper.style.position = 'absolute';
  helper.style.left = '-9999px';
  document.body.appendChild(helper);
  helper.select();
  document.execCommand('copy');
  document.body.removeChild(helper);
}

function updateOverlayContainer(
  draft: Settings,
  patch: Partial<Settings['overlaySettings']['overlayContainerStyle']>,
): Settings {
  return {
    ...draft,
    overlaySettings: {
      ...draft.overlaySettings,
      overlayContainerStyle: {
        ...draft.overlaySettings.overlayContainerStyle,
        ...patch,
      },
    },
  };
}

function updateOverlayGradient(
  draft: Settings,
  patch: Partial<GradientSettings>,
): Settings {
  return updateOverlayContainer(draft, {
    gradient: {
      ...draft.overlaySettings.overlayContainerStyle.gradient,
      ...patch,
    },
  });
}

function updateGradientColorCount(
  draft: Settings,
  colorCount: GradientSettings['colorCount'],
): Settings {
  const presetOptions = getGradientPresetOptions(colorCount);
  const currentPreset = draft.overlaySettings.overlayContainerStyle.gradient.preset;
  const nextPreset = presetOptions.includes(currentPreset)
    ? currentPreset
    : presetOptions[0];

  return updateOverlayGradient(draft, {
    colorCount,
    preset: nextPreset,
  });
}

function App() {
  const [state, setState] = useState<AppState>(emptyState);
  const [draft, setDraft] = useState<Settings>(emptyState.settings);
  const [saving, setSaving] = useState(false);
  const [artworkFailed, setArtworkFailed] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<SettingsTab>('general');
  const [systemFonts, setSystemFonts] = useState<string[]>([]);
  const [copiedOverlayUrl, setCopiedOverlayUrl] = useState(false);
  const [updateInfo, setUpdateInfo] = useState<UpdateInfo | null>(null);
  const [updateBusy, setUpdateBusy] = useState(false);
  const [updateError, setUpdateError] = useState('');
  const [overlaySections, setOverlaySections] = useState({
    behavior: true,
    textStyling: true,
    artwork: true,
    container: true,
    statusPill: true,
    livePreview: true,
  });
  const settingsOpenRef = useRef(settingsOpen);
  const copiedUrlTimerRef = useRef<number | null>(null);
  const artworkUrl = state.nowPlaying.artworkPath ? getArtworkUrl(state.artworkRevision) : '';
  const hasArtwork = Boolean(artworkUrl) && !artworkFailed;
  const effectiveThemeMode = settingsOpen ? draft.themeMode : state.settings.themeMode;
  const hasOverlayErrors = overlaySettingsHaveErrors(draft);
  const previewNowPlaying: DetectionResult = state.nowPlaying.title || state.nowPlaying.artist || state.nowPlaying.album || state.nowPlaying.artworkPath
    ? state.nowPlaying
    : sampleNowPlaying;
  const previewArtworkUrl = state.nowPlaying.artworkPath && !artworkFailed ? artworkUrl : '';
  const gradientPresetOptions = getGradientPresetOptions(draft.overlaySettings.overlayContainerStyle.gradient.colorCount);

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

  useEffect(() => () => {
    if (copiedUrlTimerRef.current !== null) {
      window.clearTimeout(copiedUrlTimerRef.current);
    }
  }, []);

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

  const checkForUpdates = async () => {
    setUpdateBusy(true);
    setUpdateError('');
    try {
      setUpdateInfo(await checkForUpdatesApi());
    } catch (error) {
      setUpdateError(`Update check failed: ${String(error)}`);
    } finally {
      setUpdateBusy(false);
    }
  };

  const openReleasePage = async () => {
    await openReleasePageApi();
  };

  const copyOverlayUrl = async () => {
    await copyText(state.overlayUrl);
    setCopiedOverlayUrl(true);
    if (copiedUrlTimerRef.current !== null) {
      window.clearTimeout(copiedUrlTimerRef.current);
    }
    copiedUrlTimerRef.current = window.setTimeout(() => setCopiedOverlayUrl(false), 1600);
  };

  return (
    <div className="shell">
      <main className="overlay-shell">
        <section className="app-chrome">
          <div className="app-chrome-title">
            <p className="app-name">TideReader</p>
            <div className={`status-pill ${state.nowPlaying.status}`}>{formatPlaybackStatus(state.nowPlaying.status)}</div>
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
                <section className="update-panel-wrap">
                  <div className="field-grid update-grid">
                    <div className="field inline-field">
                      <span>Current version</span>
                      <strong>{state.appVersion || '0.0.0'}</strong>
                    </div>
                    <div className="update-actions">
                      <button type="button" className="ghost" onClick={checkForUpdates} disabled={updateBusy}>
                        {updateBusy ? 'Checking for Updates' : 'Check for Updates'}
                      </button>
                      {updateInfo?.updateAvailable ? (
                        <button type="button" className="ghost" onClick={openReleasePage}>
                          View Releases
                        </button>
                      ) : null}
                    </div>
                  </div>
                  {updateInfo ? (
                    <div className={`update-panel ${updateInfo.updateAvailable ? 'available' : 'current'}`}>
                      <strong>{updateInfo.message}</strong>
                      <span>Latest version: {updateInfo.latestVersion}</span>
                    </div>
                  ) : null}
                  {updateError ? <div className="update-panel error"><strong>{updateError}</strong></div> : null}
                </section>
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
                <CollapsibleSection
                  title="Overlay behavior"
                  isOpen={overlaySections.behavior}
                  onToggle={() => setOverlaySections((current) => ({ ...current, behavior: !current.behavior }))}
                  actions={(
                    <button
                      className="ghost compact-button"
                      onClick={() => setDraft({
                        ...draft,
                        overlaySettings: cloneOverlaySettings(defaultOverlaySettings),
                      })}
                      type="button"
                    >
                      Reset Overlay Styling to Defaults
                    </button>
                  )}
                >
                  <div className="toggle-list">
                    <Toggle label="Enable local overlay" checked={draft.overlayEnabled} onChange={(value) => setDraft({ ...draft, overlayEnabled: value })} />
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
                  <div className="overlay-behavior-divider" aria-hidden="true" />
                  <div className="field-grid overlay-dimensions overlay-behavior-grid">
                    <div className="field inline-field">
                      <span>Overlay URL</span>
                      <button
                        type="button"
                        className="overlay-url-button"
                        disabled={!state.overlayUrl}
                        onClick={copyOverlayUrl}
                        title={state.overlayUrl || 'Overlay disabled'}
                      >
                        <strong>{state.overlayUrl || 'Disabled'}</strong>
                        {state.overlayUrl ? <span>{copiedOverlayUrl ? 'Copied' : 'Click to copy'}</span> : null}
                      </button>
                    </div>
                    <label>
                      Overlay port
                      <input type="number" value={draft.overlayPort} onChange={(event) => setDraft({ ...draft, overlayPort: Number(event.target.value) })} />
                    </label>
                  </div>
                </CollapsibleSection>

                <CollapsibleSection
                  title="Text Styling"
                  isOpen={overlaySections.textStyling}
                  onToggle={() => setOverlaySections((current) => ({ ...current, textStyling: !current.textStyling }))}
                >
                  <div className="field-grid overlay-dimensions">
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
                  <TextStyleEditor
                    title="Song"
                    fontOptions={buildFontOptions(systemFonts, draft.overlaySettings.songTextStyle.fontFamily)}
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
                    title="Artist"
                    fontOptions={buildFontOptions(systemFonts, draft.overlaySettings.artistTextStyle.fontFamily)}
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
                    title="Album"
                    fontOptions={buildFontOptions(systemFonts, draft.overlaySettings.albumTextStyle.fontFamily)}
                    value={draft.overlaySettings.albumTextStyle}
                    onChange={(value) => setDraft({
                      ...draft,
                      overlaySettings: {
                        ...draft.overlaySettings,
                        albumTextStyle: value,
                      },
                    })}
                  />
                </CollapsibleSection>

                <CollapsibleSection
                  title="Artwork"
                  isOpen={overlaySections.artwork}
                  onToggle={() => setOverlaySections((current) => ({ ...current, artwork: !current.artwork }))}
                >
                  <div className="field-grid overlay-dimensions">
                    <NumberField
                      label="Artwork image size (px)"
                      value={draft.overlaySettings.imageSizePx}
                      min={1}
                      invalid={!isPositiveNumber(draft.overlaySettings.imageSizePx)}
                      onChange={(value) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          imageSizePx: value,
                        },
                      })}
                    />
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
                  </div>
                </CollapsibleSection>

                <CollapsibleSection
                  title="Container"
                  isOpen={overlaySections.container}
                  onToggle={() => setOverlaySections((current) => ({ ...current, container: !current.container }))}
                >
                  <div className="field-grid">
                    <label>
                      Background mode
                      <select
                        value={draft.overlaySettings.overlayContainerStyle.backgroundMode}
                        onChange={(event) => setDraft(updateOverlayContainer(draft, {
                          backgroundMode: event.target.value as Settings['overlaySettings']['overlayContainerStyle']['backgroundMode'],
                        }))}
                      >
                        {overlayBackgroundModeOptions.map((mode) => (
                          <option key={mode} value={mode}>
                            {mode === 'solid' ? 'Solid Color' : 'Gradient'}
                          </option>
                        ))}
                      </select>
                    </label>
                    <ColorField
                      label="Background color"
                      value={draft.overlaySettings.overlayContainerStyle.backgroundColorHex}
                      invalid={!isValidHexColor(draft.overlaySettings.overlayContainerStyle.backgroundColorHex)}
                      onChange={(value) => {
                        const nextDraft = updateOverlayContainer(draft, {
                          backgroundColorHex: value,
                        });
                        setDraft({
                          ...nextDraft,
                          overlaySettings: {
                            ...nextDraft.overlaySettings,
                            backgroundColorHex: value,
                          },
                        });
                      }}
                    />
                    {draft.overlaySettings.overlayContainerStyle.backgroundMode === 'gradient' ? (
                      <>
                        <label>
                          Gradient colors
                          <select
                            value={draft.overlaySettings.overlayContainerStyle.gradient.colorCount}
                            onChange={(event) => setDraft(updateGradientColorCount(
                              draft,
                              Number(event.target.value) as GradientSettings['colorCount'],
                            ))}
                          >
                            {gradientColorCountOptions.map((count) => (
                              <option key={count} value={count}>{count} colors</option>
                            ))}
                          </select>
                        </label>
                        <label>
                          Gradient preset
                          <select
                            value={draft.overlaySettings.overlayContainerStyle.gradient.preset}
                            onChange={(event) => setDraft(updateOverlayGradient(draft, {
                              preset: event.target.value,
                            }))}
                          >
                            {gradientPresetOptions.map((preset) => (
                              <option key={preset} value={preset}>{preset}</option>
                            ))}
                          </select>
                        </label>
                        <ColorField
                          label="Gradient Color 1"
                          value={draft.overlaySettings.overlayContainerStyle.gradient.color1Hex}
                          invalid={!isValidHexColor(draft.overlaySettings.overlayContainerStyle.gradient.color1Hex)}
                          onChange={(value) => setDraft(updateOverlayGradient(draft, {
                            color1Hex: value,
                          }))}
                        />
                        <ColorField
                          label="Gradient Color 2"
                          value={draft.overlaySettings.overlayContainerStyle.gradient.color2Hex}
                          invalid={!isValidHexColor(draft.overlaySettings.overlayContainerStyle.gradient.color2Hex)}
                          onChange={(value) => setDraft(updateOverlayGradient(draft, {
                            color2Hex: value,
                          }))}
                        />
                        {draft.overlaySettings.overlayContainerStyle.gradient.colorCount === 3 ? (
                          <ColorField
                            label="Gradient Color 3"
                            value={draft.overlaySettings.overlayContainerStyle.gradient.color3Hex}
                            invalid={!isValidHexColor(draft.overlaySettings.overlayContainerStyle.gradient.color3Hex)}
                            onChange={(value) => setDraft(updateOverlayGradient(draft, {
                              color3Hex: value,
                            }))}
                          />
                        ) : null}
                        <AngleField
                          label="Gradient Angle"
                          value={draft.overlaySettings.overlayContainerStyle.gradient.angleDeg}
                          onChange={(value) => setDraft(updateOverlayGradient(draft, {
                            angleDeg: value,
                          }))}
                        />
                      </>
                    ) : null}
                    <OpacityField
                      label="Background Opacity"
                      value={draft.overlaySettings.overlayContainerStyle.opacity}
                      onChange={(value) => setDraft(updateOverlayContainer(draft, {
                        opacity: value,
                      }))}
                    />
                    <NumberField
                      label="Corner radius (px)"
                      value={draft.overlaySettings.overlayContainerStyle.cornerRadiusPx}
                      min={0}
                      invalid={!isZeroOrPositiveNumber(draft.overlaySettings.overlayContainerStyle.cornerRadiusPx)}
                      onChange={(value) => setDraft(updateOverlayContainer(draft, {
                        cornerRadiusPx: value,
                      }))}
                    />
                    <NumberField
                      label="Padding (px)"
                      value={draft.overlaySettings.overlayContainerStyle.paddingPx}
                      min={0}
                      invalid={!isZeroOrPositiveNumber(draft.overlaySettings.overlayContainerStyle.paddingPx)}
                      onChange={(value) => setDraft(updateOverlayContainer(draft, {
                        paddingPx: value,
                      }))}
                    />
                    <NumberField
                      label="Gap (px)"
                      value={draft.overlaySettings.overlayContainerStyle.gapPx}
                      min={0}
                      invalid={!isZeroOrPositiveNumber(draft.overlaySettings.overlayContainerStyle.gapPx)}
                      onChange={(value) => setDraft(updateOverlayContainer(draft, {
                        gapPx: value,
                      }))}
                    />
                    <ToggleField
                      label="Border enabled"
                      checked={draft.overlaySettings.overlayContainerStyle.borderEnabled}
                      onChange={(value) => setDraft(updateOverlayContainer(draft, {
                        borderEnabled: value,
                      }))}
                    />
                    <ColorField
                      label="Border color"
                      value={draft.overlaySettings.overlayContainerStyle.borderColorHex}
                      invalid={!isValidHexColor(draft.overlaySettings.overlayContainerStyle.borderColorHex)}
                      onChange={(value) => setDraft(updateOverlayContainer(draft, {
                        borderColorHex: value,
                      }))}
                    />
                    <NumberField
                      label="Border width (px)"
                      value={draft.overlaySettings.overlayContainerStyle.borderWidthPx}
                      min={0}
                      invalid={!isZeroOrPositiveNumber(draft.overlaySettings.overlayContainerStyle.borderWidthPx)}
                      onChange={(value) => setDraft(updateOverlayContainer(draft, {
                        borderWidthPx: value,
                      }))}
                    />
                  </div>
                </CollapsibleSection>

                <CollapsibleSection
                  title="Status Pill"
                  isOpen={overlaySections.statusPill}
                  onToggle={() => setOverlaySections((current) => ({ ...current, statusPill: !current.statusPill }))}
                >
                  <div className="field-grid">
                    <ColorField
                      label="Background color"
                      value={draft.overlaySettings.statusPillStyle.backgroundColorHex}
                      invalid={!isValidHexColor(draft.overlaySettings.statusPillStyle.backgroundColorHex)}
                      onChange={(value) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          statusPillStyle: {
                            ...draft.overlaySettings.statusPillStyle,
                            backgroundColorHex: value,
                          },
                        },
                      })}
                    />
                    <ColorField
                      label="Text color"
                      value={draft.overlaySettings.statusPillStyle.textColorHex}
                      invalid={!isValidHexColor(draft.overlaySettings.statusPillStyle.textColorHex)}
                      onChange={(value) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          statusPillStyle: {
                            ...draft.overlaySettings.statusPillStyle,
                            textColorHex: value,
                          },
                        },
                      })}
                    />
                    <OpacityField
                      label="Status Pill Opacity"
                      value={draft.overlaySettings.statusPillStyle.opacity}
                      onChange={(value) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          statusPillStyle: {
                            ...draft.overlaySettings.statusPillStyle,
                            opacity: value,
                          },
                        },
                      })}
                    />
                    <label>
                      Font family
                      <select
                        aria-invalid={!draft.overlaySettings.statusPillStyle.fontFamily.trim()}
                        className={!draft.overlaySettings.statusPillStyle.fontFamily.trim() ? 'invalid-field' : ''}
                        value={draft.overlaySettings.statusPillStyle.fontFamily}
                        onChange={(event) => setDraft({
                          ...draft,
                          overlaySettings: {
                            ...draft.overlaySettings,
                            statusPillStyle: {
                              ...draft.overlaySettings.statusPillStyle,
                              fontFamily: event.target.value,
                            },
                          },
                        })}
                      >
                        {buildFontOptions(systemFonts, draft.overlaySettings.statusPillStyle.fontFamily).map((font) => (
                          <option key={font} value={font}>{font}</option>
                        ))}
                      </select>
                    </label>
                    <NumberField
                      label="Font size (px)"
                      value={draft.overlaySettings.statusPillStyle.fontSizePx}
                      min={1}
                      invalid={!isPositiveNumber(draft.overlaySettings.statusPillStyle.fontSizePx)}
                      onChange={(value) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          statusPillStyle: {
                            ...draft.overlaySettings.statusPillStyle,
                            fontSizePx: value,
                          },
                        },
                      })}
                    />
                    <NumberField
                      label="Corner radius (px)"
                      value={draft.overlaySettings.statusPillStyle.cornerRadiusPx}
                      min={0}
                      invalid={!isZeroOrPositiveNumber(draft.overlaySettings.statusPillStyle.cornerRadiusPx)}
                      onChange={(value) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          statusPillStyle: {
                            ...draft.overlaySettings.statusPillStyle,
                            cornerRadiusPx: value,
                          },
                        },
                      })}
                    />
                    <NumberField
                      label="Horizontal padding (px)"
                      value={draft.overlaySettings.statusPillStyle.paddingHorizontalPx}
                      min={0}
                      invalid={!isZeroOrPositiveNumber(draft.overlaySettings.statusPillStyle.paddingHorizontalPx)}
                      onChange={(value) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          statusPillStyle: {
                            ...draft.overlaySettings.statusPillStyle,
                            paddingHorizontalPx: value,
                          },
                        },
                      })}
                    />
                    <NumberField
                      label="Vertical padding (px)"
                      value={draft.overlaySettings.statusPillStyle.paddingVerticalPx}
                      min={0}
                      invalid={!isZeroOrPositiveNumber(draft.overlaySettings.statusPillStyle.paddingVerticalPx)}
                      onChange={(value) => setDraft({
                        ...draft,
                        overlaySettings: {
                          ...draft.overlaySettings,
                          statusPillStyle: {
                            ...draft.overlaySettings.statusPillStyle,
                            paddingVerticalPx: value,
                          },
                        },
                      })}
                    />
                  </div>
                  <div className="toggle-list compact-toggles">
                    <Toggle label="Bold" checked={draft.overlaySettings.statusPillStyle.bold} onChange={(value) => setDraft({
                      ...draft,
                      overlaySettings: {
                        ...draft.overlaySettings,
                        statusPillStyle: {
                          ...draft.overlaySettings.statusPillStyle,
                          bold: value,
                        },
                      },
                    })} />
                    <Toggle label="Italic" checked={draft.overlaySettings.statusPillStyle.italic} onChange={(value) => setDraft({
                      ...draft,
                      overlaySettings: {
                        ...draft.overlaySettings,
                        statusPillStyle: {
                          ...draft.overlaySettings.statusPillStyle,
                          italic: value,
                        },
                      },
                    })} />
                    <Toggle label="Underline" checked={draft.overlaySettings.statusPillStyle.underline} onChange={(value) => setDraft({
                      ...draft,
                      overlaySettings: {
                        ...draft.overlaySettings,
                        statusPillStyle: {
                          ...draft.overlaySettings.statusPillStyle,
                          underline: value,
                        },
                      },
                    })} />
                  </div>
                </CollapsibleSection>

                <CollapsibleSection
                  title="Live Preview"
                  isOpen={overlaySections.livePreview}
                  onToggle={() => setOverlaySections((current) => ({ ...current, livePreview: !current.livePreview }))}
                >
                  <div className="preview-panel">
                    <NowPlayingOverlayView
                      overlaySettings={draft.overlaySettings}
                      nowPlaying={previewNowPlaying}
                      artworkUrl={previewArtworkUrl}
                      artworkAlt={`${previewNowPlaying.title || 'Sample Song'} cover art`}
                      fallbackMode="preview"
                    />
                  </div>
                </CollapsibleSection>

                <p className="note">Hex colors must use `#RGB` or `#RRGGBB`. Save is disabled while any overlay field is invalid.</p>
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

function CollapsibleSection({
  title,
  isOpen,
  onToggle,
  children,
  actions,
}: {
  title: string;
  isOpen: boolean;
  onToggle: () => void;
  children: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <section className="text-style-card collapsible-section">
      <div className="section-head">
        <button className="section-toggle" type="button" onClick={onToggle} aria-expanded={isOpen}>
          <span>{title}</span>
          <span className={`section-chevron ${isOpen ? 'open' : ''}`} aria-hidden="true">▾</span>
        </button>
        {actions ? <div className="section-actions">{actions}</div> : null}
      </div>
      {isOpen ? <div className="section-body">{children}</div> : null}
    </section>
  );
}

function ToggleField({ label, checked, onChange }: { label: string; checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <div className="field toggle-field">
      <span>{label}</span>
      <Toggle label={label} checked={checked} onChange={onChange} />
    </div>
  );
}

function NumberField({ label, value, min, invalid, onChange }: { label: string; value: number; min: number; invalid: boolean; onChange: (value: number) => void }) {
  return (
    <label>
      {label}
      <input
        aria-invalid={invalid}
        className={invalid ? 'invalid-field' : ''}
        type="number"
        min={min}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
      />
    </label>
  );
}

function OpacityField({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return (
    <label>
      {label}
      <div className="slider-field">
        <input
          aria-label={label}
          aria-invalid={!isOpacityValid(value)}
          className={!isOpacityValid(value) ? 'invalid-field' : ''}
          type="range"
          min={0}
          max={1}
          step={0.05}
          value={value}
          onChange={(event) => onChange(Number(event.target.value))}
        />
        <span>{Math.round(value * 100)}%</span>
      </div>
    </label>
  );
}

function AngleField({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  const invalid = !isGradientAngleValid(value);

  return (
    <label>
      {label}
      <div className="slider-field">
        <input
          aria-label={label}
          aria-invalid={invalid}
          className={invalid ? 'invalid-field' : ''}
          type="range"
          min={0}
          max={360}
          step={1}
          value={value}
          onChange={(event) => onChange(Number(event.target.value))}
        />
        <span>{value}deg</span>
      </div>
    </label>
  );
}

function ColorField({ label, value, invalid, onChange }: { label: string; value: string; invalid: boolean; onChange: (value: string) => void }) {
  return (
    <label>
      {label}
      <div className="color-field">
        <span className={`color-swatch ${invalid ? 'invalid' : ''}`} style={{ backgroundColor: invalid ? 'transparent' : value }} aria-hidden="true" />
        <input
          aria-invalid={invalid}
          className={invalid ? 'invalid-field' : ''}
          type="text"
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      </div>
    </label>
  );
}

function TextStyleEditor({ title, fontOptions, value, onChange }: { title: string; fontOptions: string[]; value: OverlayTextStyle; onChange: (value: OverlayTextStyle) => void }) {
  const colorValid = isValidHexColor(value.colorHex);
  const fontSizeValid = isPositiveNumber(value.fontSizePx);
  const fontFamilyValid = Boolean(value.fontFamily.trim());
  const maxCharactersValid = isZeroOrPositiveNumber(value.maxCharacters);

  return (
    <section className="subsection-card">
      <h4>{title}</h4>
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
        <ColorField
          label="Hex color"
          value={value.colorHex}
          invalid={!colorValid}
          onChange={(next) => onChange({ ...value, colorHex: next })}
        />
        <NumberField
          label="Font size (px)"
          value={value.fontSizePx}
          min={1}
          invalid={!fontSizeValid}
          onChange={(next) => onChange({ ...value, fontSizePx: next })}
        />
        <NumberField
          label="Character limit"
          value={value.maxCharacters}
          min={0}
          invalid={!maxCharactersValid}
          onChange={(next) => onChange({ ...value, maxCharacters: next })}
        />
      </div>
      <div className="toggle-list compact-toggles">
        <Toggle label="Bold" checked={value.bold} onChange={(next) => onChange({ ...value, bold: next })} />
        <Toggle label="Italic" checked={value.italic} onChange={(next) => onChange({ ...value, italic: next })} />
        <Toggle label="Underline" checked={value.underline} onChange={(next) => onChange({ ...value, underline: next })} />
      </div>
      {overlayTextStyleHasErrors(value) ? <p className="note">Enter a valid font family, positive font size, non-negative character limit, and `#RGB` or `#RRGGBB` color.</p> : null}
    </section>
  );
}

export default App;
