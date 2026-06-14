import { type KeyboardEvent, type ReactNode, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { HexColorPicker } from 'react-colorful';
import './App.css';
import tideReaderIcon from './assets/images/TideReaderIcon.png';
import NowPlayingOverlayView from './NowPlayingOverlayView';
import { AppState, DetectionResult, GradientSettings, OverlayProfile, OverlayTextStyle, Settings, UpdateInfo, checkForUpdates as checkForUpdatesApi, chooseOutputFolder as chooseOutputFolderApi, getArtworkUrl, getState, getSystemFonts, openOutputFolder as openOutputFolderApi, openReleasePage as openReleasePageApi, runDetectionNow as runDetectionNowApi, saveSettings as saveSettingsApi } from './api';
import { cloneOverlayProfile, cloneOverlayProfiles, cloneOverlaySettings, createDefaultOverlayProfiles, createDefaultSettings, defaultOverlaySettings, formatPlaybackStatus, getAlbumDisplayText, getArtistDisplayText, getGradientPresetOptions, gradientColorCountOptions, isGradientAngleValid, isOpacityValid, isPositiveNumber, isTextOverflowMode, isValidHexColor, isZeroOrPositiveNumber, overlayBackgroundModeOptions, overlaySettingsHaveErrors, overlayTextStyleHasErrors, shouldHideArtworkFallback, textOverflowModeOptions } from './overlay';

type SettingsTab = 'general' | 'browser' | 'overlay';
type OverlayTextTarget = 'song' | 'artist' | 'album';
type FeedbackTone = 'success' | 'warning' | 'danger' | 'info';

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
      provider: 'tidal',
      browser: '',
      site: '',
      rawTitle: '',
      rawArtist: '',
      rawAlbum: '',
      selectionReason: '',
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
    browserDebug: {
      sessions: [],
    },
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

function settingsEqual(left: Settings, right: Settings): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

function normalizeColorHex(value: string): string | null {
  const trimmed = value.trim();
  const fullMatch = /^#[0-9A-Fa-f]{6}$/.test(trimmed);
  if (fullMatch) {
    return trimmed.toLowerCase();
  }

  const shortMatch = /^#[0-9A-Fa-f]{3}$/.test(trimmed);
  if (!shortMatch) {
    return null;
  }

  const [, red, green, blue] = trimmed.toLowerCase();
  return `#${red}${red}${green}${green}${blue}${blue}`;
}

function isColorPickerHex(value: string): boolean {
  return normalizeColorHex(value) !== null;
}

function createOverlayProfileId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `profile-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function ensureOverlayProfiles(settings: Settings): OverlayProfile[] {
  return settings.overlayProfiles?.length > 0
    ? settings.overlayProfiles
    : createDefaultOverlayProfiles();
}

function getActiveOverlayProfile(settings: Settings): OverlayProfile {
  const profiles = ensureOverlayProfiles(settings);
  return profiles.find((profile) => profile.id === settings.activeOverlayProfileId) ?? profiles[0];
}

function getUniqueOverlayProfileName(baseName: string, profiles: OverlayProfile[]): string {
  const trimmedBase = baseName.trim() || 'Overlay Profile';
  const existing = new Set(profiles.map((profile) => profile.name.trim().toLowerCase()));
  if (!existing.has(trimmedBase.toLowerCase())) {
    return trimmedBase;
  }

  let suffix = 2;
  while (existing.has(`${trimmedBase} ${suffix}`.toLowerCase())) {
    suffix += 1;
  }
  return `${trimmedBase} ${suffix}`;
}

function normalizeSettingsForFrontend(settings: Settings): Settings {
  return {
    ...settings,
    overlaySettings: cloneOverlaySettings(settings.overlaySettings),
    overlayProfiles: cloneOverlayProfiles(ensureOverlayProfiles(settings)),
  };
}

function normalizeAppStateForFrontend(nextState: AppState): AppState {
  return {
    ...nextState,
    settings: normalizeSettingsForFrontend(nextState.settings),
  };
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
  const [saveError, setSaveError] = useState('');
  const [shellFeedback, setShellFeedback] = useState<{ tone: FeedbackTone; message: string } | null>(null);
  const [activeTextStyleTarget, setActiveTextStyleTarget] = useState<OverlayTextTarget>('song');
  const [overlaySections, setOverlaySections] = useState({
    behavior: true,
    textStyling: true,
    artwork: true,
    container: true,
    statusPill: true,
  });
  const settingsOpenRef = useRef(settingsOpen);
  const copiedUrlTimerRef = useRef<number | null>(null);
  const shellFeedbackTimerRef = useRef<number | null>(null);
  const artworkUrl = state.nowPlaying.artworkPath ? getArtworkUrl(state.artworkRevision) : '';
  const hasArtwork = Boolean(artworkUrl) && !artworkFailed;
  const hideArtworkFallback = !hasArtwork && shouldHideArtworkFallback(state.nowPlaying);
  const effectiveThemeMode = settingsOpen ? draft.themeMode : state.settings.themeMode;
  const hasOverlayErrors = overlaySettingsHaveErrors(draft);
  const hasUnsavedChanges = !settingsEqual(draft, state.settings);
  const previewNowPlaying: DetectionResult = state.nowPlaying;
  const previewArtworkUrl = state.nowPlaying.artworkPath && !artworkFailed ? artworkUrl : '';
  const gradientPresetOptions = getGradientPresetOptions(draft.overlaySettings.overlayContainerStyle.gradient.colorCount);
  const overlayProfiles = ensureOverlayProfiles(draft);
  const activeOverlayProfile = getActiveOverlayProfile(draft);
  const activeTextStyleLabel = activeTextStyleTarget === 'song'
    ? 'Song'
    : activeTextStyleTarget === 'artist'
      ? 'Artist'
      : 'Album';
  const activeTextStyleValue = activeTextStyleTarget === 'song'
    ? draft.overlaySettings.songTextStyle
    : activeTextStyleTarget === 'artist'
      ? draft.overlaySettings.artistTextStyle
      : draft.overlaySettings.albumTextStyle;
  const updateActiveTextStyle = (value: OverlayTextStyle) => {
    setDraft({
      ...draft,
      overlaySettings: {
        ...draft.overlaySettings,
        songTextStyle: activeTextStyleTarget === 'song' ? value : draft.overlaySettings.songTextStyle,
        artistTextStyle: activeTextStyleTarget === 'artist' ? value : draft.overlaySettings.artistTextStyle,
        albumTextStyle: activeTextStyleTarget === 'album' ? value : draft.overlaySettings.albumTextStyle,
      },
    });
  };

  const showShellFeedback = (tone: FeedbackTone, message: string) => {
    setShellFeedback({ tone, message });
    if (shellFeedbackTimerRef.current !== null) {
      window.clearTimeout(shellFeedbackTimerRef.current);
    }
    shellFeedbackTimerRef.current = window.setTimeout(() => {
      setShellFeedback(null);
      shellFeedbackTimerRef.current = null;
    }, 2800);
  };

  useEffect(() => {
    settingsOpenRef.current = settingsOpen;
  }, [settingsOpen]);

  useEffect(() => {
    let cancelled = false;

    const refresh = async () => {
      const nextState = normalizeAppStateForFrontend(await getState());
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
    if (shellFeedbackTimerRef.current !== null) {
      window.clearTimeout(shellFeedbackTimerRef.current);
    }
  }, []);

  const persistSettings = async (settings: Settings, successMessage: string, closeAfterSave = false) => {
    setSaving(true);
    setSaveError('');
    try {
      const nextState = normalizeAppStateForFrontend(await saveSettingsApi(settings));
      setState(nextState);
      setDraft(nextState.settings);
      showShellFeedback('success', successMessage);
      if (closeAfterSave) {
        closeSettings();
      }
    } catch (error) {
      setSaveError(`Settings could not be saved: ${String(error)}`);
    } finally {
      setSaving(false);
    }
  };

  const saveSettings = async () => {
    const profiles = overlayProfiles.map((profile) => profile.id === activeOverlayProfile.id
      ? {
          ...cloneOverlayProfile(profile),
          overlaySettings: cloneOverlaySettings(draft.overlaySettings),
        }
      : cloneOverlayProfile(profile));

    await persistSettings({
      ...draft,
      overlayProfiles: profiles,
      activeOverlayProfileId: activeOverlayProfile.id,
    }, 'Settings saved.', true);
  };

  const selectOverlayProfile = async (profileId: string) => {
    const profile = overlayProfiles.find((item) => item.id === profileId);
    if (!profile) {
      return;
    }

    await persistSettings({
      ...draft,
      activeOverlayProfileId: profile.id,
      overlayProfiles: cloneOverlayProfiles(overlayProfiles),
      overlaySettings: cloneOverlaySettings(profile.overlaySettings),
    }, `Applied ${profile.name}.`);
  };

  const saveOverlayProfile = async () => {
    const profiles = overlayProfiles.map((profile) => profile.id === activeOverlayProfile.id
      ? {
          ...cloneOverlayProfile(profile),
          overlaySettings: cloneOverlaySettings(draft.overlaySettings),
        }
      : cloneOverlayProfile(profile));

    await persistSettings({
      ...draft,
      overlayProfiles: profiles,
      activeOverlayProfileId: activeOverlayProfile.id,
    }, `Saved ${activeOverlayProfile.name}.`);
  };

  const saveOverlayProfileAs = async () => {
    const name = window.prompt('Overlay profile name', getUniqueOverlayProfileName('New Overlay Profile', overlayProfiles))?.trim();
    if (!name) {
      return;
    }

    const profile: OverlayProfile = {
      id: createOverlayProfileId(),
      name: getUniqueOverlayProfileName(name, overlayProfiles),
      overlaySettings: cloneOverlaySettings(draft.overlaySettings),
    };

    await persistSettings({
      ...draft,
      overlayProfiles: [...cloneOverlayProfiles(overlayProfiles), profile],
      activeOverlayProfileId: profile.id,
    }, `Created ${profile.name}.`);
  };

  const openSettings = () => {
    setActiveTab('general');
    setActiveTextStyleTarget('song');
    setSaveError('');
    setSettingsOpen(true);
  };

  const closeSettings = () => {
    setActiveTab('general');
    setActiveTextStyleTarget('song');
    setSaveError('');
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
    showShellFeedback('info', 'Overlay URL copied.');
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
            <img className="product-logo" src={tideReaderIcon} alt="" aria-hidden="true" />
            <div className="product-copy">
              <p className="suite-label">Starsong Tools</p>
              <div className="product-line">
                <p className="app-name">TideReader</p>
              </div>
            </div>
          </div>
          <div className="app-chrome-actions">
            <div className="shell-status-group" aria-label="Playback status">
              <div className={`status-pill ${state.nowPlaying.status}`}>{formatPlaybackStatus(state.nowPlaying.status)}</div>
            </div>
            <button className="button-secondary chrome-button" onClick={openSettings}>Settings</button>
          </div>
        </section>
        {shellFeedback ? <div className={`shell-banner ${shellFeedback.tone}`}>{shellFeedback.message}</div> : null}
        <section className="overlay-panel">
          {!hideArtworkFallback ? (
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
                <span>{state.nowPlaying.title ? state.nowPlaying.source : 'Idle'}</span>
              )}
            </div>
          ) : null}
          <div className="overlay-copy">
            <h1>{state.nowPlaying.title || 'Waiting for playback'}</h1>
            <p className="artist-line">{getArtistDisplayText(state.nowPlaying, 'Artist unavailable')}</p>
            <p className="album-line">{getAlbumDisplayText(state.nowPlaying, 'Album unavailable')}</p>
            {state.nowPlaying.selectionReason ? <p className="album-line">{state.nowPlaying.selectionReason}</p> : null}
          </div>
          <div className="overlay-actions">
            <button className="button-secondary compact-button" onClick={runNow}>Refresh</button>
          </div>
        </section>
        <footer className="shell-footer">
          <span className="shell-footer-item">{state.statusMessage || 'Waiting for status'}</span>
          <span className="shell-footer-item">{state.outputFolder ? `Output: ${state.outputFolder}` : 'Output folder not set'}</span>
        </footer>
      </main>

      {settingsOpen ? (
        <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="settings-title">
          <section className="modal-panel">
            <div className="panel-head">
              <div>
                <h2 id="settings-title">Settings</h2>
                <p className="panel-subtitle">Tune playback detection, outputs, and overlay presentation for this TideReader workspace.</p>
              </div>
              <button
                className="button-ghost modal-close-button"
                type="button"
                aria-label="Close settings"
                title="Close settings"
                onClick={closeSettings}
              >
                X
              </button>
            </div>
            <div className="settings-tabs" role="tablist" aria-label="Settings sections">
              <button className={`tab-button ${activeTab === 'general' ? 'active' : ''}`} role="tab" aria-selected={activeTab === 'general'} onClick={() => setActiveTab('general')}>General</button>
              <button className={`tab-button ${activeTab === 'browser' ? 'active' : ''}`} role="tab" aria-selected={activeTab === 'browser'} onClick={() => setActiveTab('browser')}>Browser</button>
              <button className={`tab-button ${activeTab === 'overlay' ? 'active' : ''}`} role="tab" aria-selected={activeTab === 'overlay'} onClick={() => setActiveTab('overlay')}>Overlay</button>
            </div>
            {activeTab === 'general' ? (
              <div className="settings-stack settings-tab-panel">
                <p className="tab-intro">App behavior, folders, updates, and launch preferences.</p>
                <div className="field field-span-full">
                  <label>Output folder</label>
                  <div className="row row-wrap">
                    <input
                      className="field-control field-control-full"
                      title={draft.outputFolder || 'Not set'}
                      value={draft.outputFolder}
                      onChange={(event) => setDraft({ ...draft, outputFolder: event.target.value })}
                    />
                    <button className="button-secondary" onClick={chooseOutputFolder}>Browse</button>
                    <button className="button-ghost" onClick={openOutputFolder}>Open output folder</button>
                  </div>
                </div>
                <div className="field-grid settings-grid settings-grid-general">
                  <label className="field-span-compact">
                    Poll interval (ms)
                    <input className="field-control field-control-compact" type="number" value={draft.pollIntervalMs} onChange={(event) => setDraft({ ...draft, pollIntervalMs: Number(event.target.value) })} />
                  </label>
                  <label className="field-span-medium">
                    <span>Theme mode</span>
                    <select className="field-control field-control-medium" value={draft.themeMode} onChange={(event) => setDraft({ ...draft, themeMode: event.target.value as Settings['themeMode'] })}>
                      <option value="Dark">Dark</option>
                      <option value="Light">Light</option>
                    </select>
                  </label>
                  <label className="field-span-large">
                    <span>Metadata provider mode</span>
                    <select className="field-control field-control-large" value={draft.metadataProviderMode} onChange={(event) => setDraft({ ...draft, metadataProviderMode: event.target.value as Settings['metadataProviderMode'] })}>
                      <option value="Off">Off</option>
                      <option value="MusicBrainzOnly">MusicBrainz only</option>
                      <option value="MusicBrainzWithFallbacks">MusicBrainz + fallbacks</option>
                    </select>
                  </label>
                </div>
                <section className="update-panel-wrap">
                  <div className="field-grid update-grid">
                    <div className="field inline-field">
                      <span>Current version</span>
                      <strong>{state.appVersion || '0.0.0'}</strong>
                    </div>
                    <div className="update-actions">
                      <button type="button" className="button-secondary" onClick={checkForUpdates} disabled={updateBusy}>
                        {updateBusy ? 'Checking for Updates' : 'Check for Updates'}
                      </button>
                      {updateInfo?.updateAvailable ? (
                        <button type="button" className="button-ghost" onClick={openReleasePage}>
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
                <div className="toggle-list toggle-grid toggle-grid-wide">
                  <Toggle label="Enable window title fallback" checked={draft.enableWindowTitleFallback} onChange={(value) => setDraft({ ...draft, enableWindowTitleFallback: value })} />
                  <Toggle label="Start minimized" checked={draft.startMinimized} onChange={(value) => setDraft({ ...draft, startMinimized: value })} />
                  <Toggle label="Launch at Windows startup" checked={draft.launchAtStartup} onChange={(value) => setDraft({ ...draft, launchAtStartup: value })} />
                </div>
              </div>
            ) : activeTab === 'browser' ? (
              <div className="overlay-settings settings-tab-panel">
                <p className="tab-intro">Browser source detection, filtering, and diagnostics.</p>
                <CollapsibleSection title="Enable Browser Media Support" isOpen={true} onToggle={() => undefined}>
                  <div className="toggle-list toggle-grid">
                    <Toggle label="Enable browser media support" checked={draft.browserSettings.enabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        enabled: value,
                      },
                    })} />
                  </div>
                </CollapsibleSection>

                <CollapsibleSection title="Supported Browsers" isOpen={true} onToggle={() => undefined}>
                  <div className="toggle-list toggle-grid toggle-grid-compact">
                    <Toggle label="Chrome" checked={draft.browserSettings.supportedBrowsers.chromeEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        supportedBrowsers: { ...draft.browserSettings.supportedBrowsers, chromeEnabled: value },
                      },
                    })} />
                    <Toggle label="Edge" checked={draft.browserSettings.supportedBrowsers.edgeEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        supportedBrowsers: { ...draft.browserSettings.supportedBrowsers, edgeEnabled: value },
                      },
                    })} />
                    <Toggle label="Firefox" checked={draft.browserSettings.supportedBrowsers.firefoxEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        supportedBrowsers: { ...draft.browserSettings.supportedBrowsers, firefoxEnabled: value },
                      },
                    })} />
                    <Toggle label="Brave" checked={draft.browserSettings.supportedBrowsers.braveEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        supportedBrowsers: { ...draft.browserSettings.supportedBrowsers, braveEnabled: value },
                      },
                    })} />
                    <Toggle label="Opera" checked={draft.browserSettings.supportedBrowsers.operaEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        supportedBrowsers: { ...draft.browserSettings.supportedBrowsers, operaEnabled: value },
                      },
                    })} />
                  </div>
                </CollapsibleSection>

                <CollapsibleSection title="Source Selection" isOpen={true} onToggle={() => undefined}>
                  <div className="field-grid settings-grid">
                    <label className="field-span-medium">
                      Active source mode
                      <select className="field-control field-control-medium" value={draft.browserSettings.activeSourceMode} onChange={(event) => setDraft({
                        ...draft,
                        browserSettings: {
                          ...draft.browserSettings,
                          activeSourceMode: event.target.value as Settings['browserSettings']['activeSourceMode'],
                        },
                      })}>
                        <option value="auto">Auto</option>
                        <option value="tidal">TIDAL only</option>
                        <option value="browser">Browser only</option>
                      </select>
                    </label>
                    <label className="field-span-compact">
                      Source switch cooldown (ms)
                      <input className="field-control field-control-compact" type="number" value={draft.browserSettings.sourceSwitchCooldownMs} onChange={(event) => setDraft({
                        ...draft,
                        browserSettings: {
                          ...draft.browserSettings,
                          sourceSwitchCooldownMs: Number(event.target.value),
                        },
                      })} />
                    </label>
                  </div>
                  <div className="toggle-list toggle-grid">
                    <Toggle label="Prefer TIDAL over browser playback" checked={draft.browserSettings.preferTidalOverBrowser} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        preferTidalOverBrowser: value,
                      },
                    })} />
                    <Toggle label="Allow generic browser playback" checked={draft.browserSettings.allowGenericPlayback} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        allowGenericPlayback: value,
                      },
                    })} />
                  </div>
                </CollapsibleSection>

                <CollapsibleSection title="Metadata Cleanup" isOpen={true} onToggle={() => undefined}>
                  <div className="toggle-list toggle-grid">
                    <Toggle label="Enable metadata cleanup/parsing" checked={draft.browserSettings.metadataCleanupEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        metadataCleanupEnabled: value,
                      },
                    })} />
                    <Toggle label="Enable browser artwork retrieval" checked={draft.browserSettings.browserArtworkEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        browserArtworkEnabled: value,
                      },
                    })} />
                  </div>
                </CollapsibleSection>

                <CollapsibleSection title="YouTube Artwork" isOpen={true} onToggle={() => undefined}>
                  <div className="toggle-list toggle-grid">
                    <Toggle label="Use browser video image when album art is unavailable" checked={draft.browserSettings.youTubeVideoImageFallbackEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        youTubeVideoImageFallbackEnabled: value,
                      },
                    })} />
                  </div>
                  <p className="note">Priority stays: album art, then browser video image, then removed.</p>
                </CollapsibleSection>

                <CollapsibleSection title="Session Filtering" isOpen={true} onToggle={() => undefined}>
                  <div className="field-grid settings-grid">
                    <label className="field-span-compact">
                      Stale session timeout (seconds)
                      <input className="field-control field-control-compact" type="number" value={draft.browserSettings.staleSessionAfterSeconds} onChange={(event) => setDraft({
                        ...draft,
                        browserSettings: {
                          ...draft.browserSettings,
                          staleSessionAfterSeconds: Number(event.target.value),
                        },
                      })} />
                    </label>
                  </div>
                  <div className="toggle-list toggle-grid">
                    <Toggle label="Ignore paused sessions" checked={draft.browserSettings.ignorePausedSessions} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        ignorePausedSessions: value,
                      },
                    })} />
                    <Toggle label="Ignore stale sessions" checked={draft.browserSettings.ignoreStaleSessions} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        ignoreStaleSessions: value,
                      },
                    })} />
                  </div>
                </CollapsibleSection>

                <CollapsibleSection title="Debug" isOpen={true} onToggle={() => undefined}>
                  <div className="toggle-list toggle-grid">
                    <Toggle label="Enable browser detection logging" checked={draft.browserSettings.debugLoggingEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        debugLoggingEnabled: value,
                      },
                    })} />
                    <Toggle label="Enable deep diagnostic logging" checked={draft.browserSettings.deepDiagnosticLoggingEnabled} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        deepDiagnosticLoggingEnabled: value,
                      },
                    })} />
                    <Toggle label="Show raw browser metadata" checked={draft.browserSettings.showRawBrowserMetadata} onChange={(value) => setDraft({
                      ...draft,
                      browserSettings: {
                        ...draft.browserSettings,
                        showRawBrowserMetadata: value,
                      },
                    })} />
                  </div>
                  <div className="browser-debug-list">
                    {state.browserDebug.sessions.length === 0 ? <p className="note">No browser sessions detected yet.</p> : state.browserDebug.sessions.map((session) => (
                      <section key={session.sessionId} className="subsection-card">
                        <h4>{session.site || 'generic'} · {session.browser || session.provider}</h4>
                        <p className="note">{session.decisionReason}</p>
                        <p className="note">State: {session.playbackState} · Confidence: {session.confidence.toFixed(2)} · Artwork: {session.hasArtwork ? 'yes' : 'no'}</p>
                        <p className="note">Parsed: {session.parsedArtist || 'Unknown artist'} - {session.parsedTitle || 'Unknown title'}</p>
                        {draft.browserSettings.showRawBrowserMetadata ? (
                          <p className="note">Raw: {session.rawArtist || 'n/a'} | {session.rawTitle || 'n/a'} | {session.rawAlbum || 'n/a'}</p>
                        ) : null}
                      </section>
                    ))}
                  </div>
                </CollapsibleSection>
              </div>
            ) : (
              <div className="overlay-workspace">
                <div className="overlay-settings overlay-control-pane settings-tab-panel">
                  <section className="overlay-profile-panel">
                    <label className="overlay-profile-select">
                      <span>Overlay Profile</span>
                      <select
                        value={activeOverlayProfile.id}
                        disabled={saving}
                        onChange={(event) => {
                          void selectOverlayProfile(event.target.value);
                        }}
                      >
                        {overlayProfiles.map((profile) => (
                          <option key={profile.id} value={profile.id}>{profile.name}</option>
                        ))}
                      </select>
                    </label>
                    <div className="overlay-profile-actions">
                      <button className="icon-button button-secondary" type="button" disabled={saving || hasOverlayErrors} onClick={() => void saveOverlayProfile()} aria-label="Save overlay profile" title="Save">
                        <svg aria-hidden="true" viewBox="0 0 24 24" focusable="false">
                          <path d="M5 3h12l2 2v16H5z" />
                          <path d="M8 3v6h8V3" />
                          <path d="M8 15h8v6H8z" />
                        </svg>
                      </button>
                      <button className="icon-button button-secondary" type="button" disabled={saving || hasOverlayErrors} onClick={() => void saveOverlayProfileAs()} aria-label="Save overlay profile as" title="Save As">
                        <svg aria-hidden="true" viewBox="0 0 24 24" focusable="false">
                          <path d="M5 3h10l4 4v14H5z" />
                          <path d="M8 3v6h7" />
                          <path d="M8 15h5v6H8z" />
                          <path d="M17 13v6" />
                          <path d="M14 16h6" />
                        </svg>
                      </button>
                    </div>
                  </section>
                  <CollapsibleSection
                    title="Overlay behavior"
                    isOpen={overlaySections.behavior}
                    onToggle={() => setOverlaySections((current) => ({ ...current, behavior: !current.behavior }))}
                    actions={(
                      <button
                        className="button-ghost compact-button"
                        onClick={() => {
                          setDraft({
                            ...draft,
                            overlaySettings: cloneOverlaySettings(defaultOverlaySettings),
                          });
                          window.dispatchEvent(new CustomEvent('tidereader-color-fields-reset'));
                        }}
                        type="button"
                      >
                        Reset Overlay Styling to Defaults
                      </button>
                    )}
                  >
                    <div className="toggle-list toggle-grid toggle-grid-wide">
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
                    <div className="field-grid settings-grid overlay-behavior-grid">
                      <div className="field inline-field field-span-full">
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
                      <label className="field-span-compact">
                        Overlay port
                        <input className="field-control field-control-compact" type="number" value={draft.overlayPort} onChange={(event) => setDraft({ ...draft, overlayPort: Number(event.target.value) })} />
                      </label>
                    </div>
                  </CollapsibleSection>

                  <CollapsibleSection
                    title="Text styling"
                    isOpen={overlaySections.textStyling}
                    onToggle={() => setOverlaySections((current) => ({ ...current, textStyling: !current.textStyling }))}
                  >
                    <div className="field-grid settings-grid overlay-behavior-grid">
                      <label className="field-span-medium">
                        Text alignment
                        <select
                          className="field-control field-control-medium"
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
                    <div className="inline-tabs" role="tablist" aria-label="Overlay text type">
                      {(['song', 'artist', 'album'] as const).map((target) => (
                        <button
                          key={target}
                          type="button"
                          className={`tab-button inline-tab-button ${activeTextStyleTarget === target ? 'active' : ''}`}
                          role="tab"
                          aria-selected={activeTextStyleTarget === target}
                          onClick={() => setActiveTextStyleTarget(target)}
                        >
                          {target === 'song' ? 'Song' : target === 'artist' ? 'Artist' : 'Album'}
                        </button>
                      ))}
                    </div>
                    <TextStyleEditor
                      title={`${activeTextStyleLabel} text`}
                      fontOptions={buildFontOptions(systemFonts, activeTextStyleValue.fontFamily)}
                      value={activeTextStyleValue}
                      onChange={updateActiveTextStyle}
                    />
                  </CollapsibleSection>

                  <CollapsibleSection
                    title="Artwork"
                    isOpen={overlaySections.artwork}
                    onToggle={() => setOverlaySections((current) => ({ ...current, artwork: !current.artwork }))}
                  >
                    <div className="field-grid settings-grid">
                      <NumberField
                        className="field-span-compact"
                        inputClassName="field-control field-control-compact"
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
                      <label className="field-span-medium">
                        Artwork position
                        <select
                          className="field-control field-control-medium"
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
                    <div className="mini-section">
                      <h4 className="mini-section-title">Background</h4>
                      <div className="field-grid settings-grid mini-section-grid">
                        <label className="field-span-medium">
                          Background mode
                          <select
                            className="field-control field-control-medium"
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
                          className="field-span-medium"
                          inputClassName="field-control field-control-compact"
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
                        <OpacityField
                          className="field-span-large"
                          label="Background Opacity"
                          value={draft.overlaySettings.overlayContainerStyle.opacity}
                          onChange={(value) => setDraft(updateOverlayContainer(draft, {
                            opacity: value,
                          }))}
                        />
                      </div>
                    </div>
                    {draft.overlaySettings.overlayContainerStyle.backgroundMode === 'gradient' ? (
                      <div className="mini-section">
                        <h4 className="mini-section-title">Gradient</h4>
                        <div className="field-grid settings-grid mini-section-grid">
                          <label className="field-span-compact">
                            Gradient colors
                            <select
                              className="field-control field-control-compact"
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
                          <label className="field-span-medium">
                            Gradient preset
                            <select
                              className="field-control field-control-medium"
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
                          <ColorField className="field-span-medium" inputClassName="field-control field-control-compact" label="Gradient Color 1" value={draft.overlaySettings.overlayContainerStyle.gradient.color1Hex} invalid={!isValidHexColor(draft.overlaySettings.overlayContainerStyle.gradient.color1Hex)} onChange={(value) => setDraft(updateOverlayGradient(draft, { color1Hex: value }))} />
                          <ColorField className="field-span-medium" inputClassName="field-control field-control-compact" label="Gradient Color 2" value={draft.overlaySettings.overlayContainerStyle.gradient.color2Hex} invalid={!isValidHexColor(draft.overlaySettings.overlayContainerStyle.gradient.color2Hex)} onChange={(value) => setDraft(updateOverlayGradient(draft, { color2Hex: value }))} />
                          {draft.overlaySettings.overlayContainerStyle.gradient.colorCount === 3 ? (
                            <ColorField className="field-span-medium" inputClassName="field-control field-control-compact" label="Gradient Color 3" value={draft.overlaySettings.overlayContainerStyle.gradient.color3Hex} invalid={!isValidHexColor(draft.overlaySettings.overlayContainerStyle.gradient.color3Hex)} onChange={(value) => setDraft(updateOverlayGradient(draft, { color3Hex: value }))} />
                          ) : null}
                          <AngleField className="field-span-large" label="Gradient Angle" value={draft.overlaySettings.overlayContainerStyle.gradient.angleDeg} onChange={(value) => setDraft(updateOverlayGradient(draft, { angleDeg: value }))} />
                        </div>
                      </div>
                    ) : null}
                    <div className="mini-section">
                      <h4 className="mini-section-title">Shape & Spacing</h4>
                      <div className="field-grid settings-grid mini-section-grid">
                        <NumberField className="field-span-compact" inputClassName="field-control field-control-compact" label="Corner radius (px)" value={draft.overlaySettings.overlayContainerStyle.cornerRadiusPx} min={0} invalid={!isZeroOrPositiveNumber(draft.overlaySettings.overlayContainerStyle.cornerRadiusPx)} onChange={(value) => setDraft(updateOverlayContainer(draft, { cornerRadiusPx: value }))} />
                        <NumberField className="field-span-compact" inputClassName="field-control field-control-compact" label="Padding (px)" value={draft.overlaySettings.overlayContainerStyle.paddingPx} min={0} invalid={!isZeroOrPositiveNumber(draft.overlaySettings.overlayContainerStyle.paddingPx)} onChange={(value) => setDraft(updateOverlayContainer(draft, { paddingPx: value }))} />
                        <NumberField className="field-span-compact" inputClassName="field-control field-control-compact" label="Gap (px)" value={draft.overlaySettings.overlayContainerStyle.gapPx} min={0} invalid={!isZeroOrPositiveNumber(draft.overlaySettings.overlayContainerStyle.gapPx)} onChange={(value) => setDraft(updateOverlayContainer(draft, { gapPx: value }))} />
                      </div>
                    </div>
                    <div className="mini-section">
                      <h4 className="mini-section-title">Border</h4>
                      <div className="field-grid settings-grid mini-section-grid">
                        <ToggleField
                          className="field-span-compact"
                          label="Border enabled"
                          checked={draft.overlaySettings.overlayContainerStyle.borderEnabled}
                          onChange={(value) => setDraft(updateOverlayContainer(draft, {
                            borderEnabled: value,
                          }))}
                        />
                        {draft.overlaySettings.overlayContainerStyle.borderEnabled ? (
                          <>
                            <ColorField className="field-span-medium" inputClassName="field-control field-control-compact" label="Border color" value={draft.overlaySettings.overlayContainerStyle.borderColorHex} invalid={!isValidHexColor(draft.overlaySettings.overlayContainerStyle.borderColorHex)} onChange={(value) => setDraft(updateOverlayContainer(draft, { borderColorHex: value }))} />
                            <NumberField className="field-span-compact" inputClassName="field-control field-control-compact" label="Border width (px)" value={draft.overlaySettings.overlayContainerStyle.borderWidthPx} min={0} invalid={!isZeroOrPositiveNumber(draft.overlaySettings.overlayContainerStyle.borderWidthPx)} onChange={(value) => setDraft(updateOverlayContainer(draft, { borderWidthPx: value }))} />
                          </>
                        ) : null}
                      </div>
                    </div>
                  </CollapsibleSection>

                  <CollapsibleSection
                    title="Status Pill"
                    isOpen={overlaySections.statusPill}
                    onToggle={() => setOverlaySections((current) => ({ ...current, statusPill: !current.statusPill }))}
                  >
                    <div className="mini-section">
                      <h4 className="mini-section-title">Colors</h4>
                      <div className="field-grid settings-grid mini-section-grid">
                        <ColorField className="field-span-medium" inputClassName="field-control field-control-compact" label="Background color" value={draft.overlaySettings.statusPillStyle.backgroundColorHex} invalid={!isValidHexColor(draft.overlaySettings.statusPillStyle.backgroundColorHex)} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, backgroundColorHex: value } } })} />
                        <ColorField className="field-span-medium" inputClassName="field-control field-control-compact" label="Text color" value={draft.overlaySettings.statusPillStyle.textColorHex} invalid={!isValidHexColor(draft.overlaySettings.statusPillStyle.textColorHex)} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, textColorHex: value } } })} />
                        <OpacityField className="field-span-large" label="Status Pill Opacity" value={draft.overlaySettings.statusPillStyle.opacity} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, opacity: value } } })} />
                      </div>
                    </div>
                    <div className="mini-section">
                      <h4 className="mini-section-title">Typography</h4>
                      <div className="field-grid settings-grid mini-section-grid">
                        <label className="field-span-large">
                          Font family
                          <select
                            aria-invalid={!draft.overlaySettings.statusPillStyle.fontFamily.trim()}
                            className={`field-control field-control-large ${!draft.overlaySettings.statusPillStyle.fontFamily.trim() ? 'invalid-field' : ''}`}
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
                        <NumberField className="field-span-compact" inputClassName="field-control field-control-compact" label="Font size (px)" value={draft.overlaySettings.statusPillStyle.fontSizePx} min={1} invalid={!isPositiveNumber(draft.overlaySettings.statusPillStyle.fontSizePx)} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, fontSizePx: value } } })} />
                      </div>
                      <div className="toggle-list toggle-grid toggle-grid-compact mini-toggle-group">
                        <Toggle label="Bold" checked={draft.overlaySettings.statusPillStyle.bold} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, bold: value } } })} />
                        <Toggle label="Italic" checked={draft.overlaySettings.statusPillStyle.italic} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, italic: value } } })} />
                        <Toggle label="Underline" checked={draft.overlaySettings.statusPillStyle.underline} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, underline: value } } })} />
                      </div>
                    </div>
                    <div className="mini-section">
                      <h4 className="mini-section-title">Shape & Padding</h4>
                      <div className="field-grid settings-grid mini-section-grid">
                        <NumberField className="field-span-compact" inputClassName="field-control field-control-compact" label="Corner radius (px)" value={draft.overlaySettings.statusPillStyle.cornerRadiusPx} min={0} invalid={!isZeroOrPositiveNumber(draft.overlaySettings.statusPillStyle.cornerRadiusPx)} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, cornerRadiusPx: value } } })} />
                        <NumberField className="field-span-compact" inputClassName="field-control field-control-compact" label="Horizontal padding (px)" value={draft.overlaySettings.statusPillStyle.paddingHorizontalPx} min={0} invalid={!isZeroOrPositiveNumber(draft.overlaySettings.statusPillStyle.paddingHorizontalPx)} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, paddingHorizontalPx: value } } })} />
                        <NumberField className="field-span-compact" inputClassName="field-control field-control-compact" label="Vertical padding (px)" value={draft.overlaySettings.statusPillStyle.paddingVerticalPx} min={0} invalid={!isZeroOrPositiveNumber(draft.overlaySettings.statusPillStyle.paddingVerticalPx)} onChange={(value) => setDraft({ ...draft, overlaySettings: { ...draft.overlaySettings, statusPillStyle: { ...draft.overlaySettings.statusPillStyle, paddingVerticalPx: value } } })} />
                      </div>
                    </div>
                  </CollapsibleSection>

                  <p className="note">Hex colors must use `#RGB` or `#RRGGBB`. Save is disabled while any overlay field is invalid.</p>
                </div>

                <aside className="overlay-preview-pane">
                  <section className="text-style-card preview-card">
                    <div className="section-head preview-card-head">
                      <div>
                        <h3>Live Preview</h3>
                        <p className="preview-note">Updates as you edit.</p>
                      </div>
                    </div>
                    <div className="preview-panel">
                      <NowPlayingOverlayView
                        overlaySettings={draft.overlaySettings}
                        nowPlaying={previewNowPlaying}
                        artworkUrl={previewArtworkUrl}
                        artworkAlt={`${previewNowPlaying.title || 'Waiting for playback'} cover art`}
                        fallbackMode="preview"
                      />
                    </div>
                  </section>
                </aside>
              </div>
            )}
            <div className="panel-footer">
              <div className="panel-footer-status">
                {hasUnsavedChanges ? <p className="settings-dirty-indicator">Unsaved changes</p> : <span className="settings-dirty-spacer" aria-hidden="true" />}
                {saveError ? <p className="inline-feedback danger">{saveError}</p> : null}
              </div>
              <button className="button-ghost" onClick={closeSettings}>Close</button>
              <button className="button-primary" disabled={saving || hasOverlayErrors} onClick={saveSettings}>{saving ? 'Saving...' : 'Save settings'}</button>
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

function ToggleField({ label, checked, onChange, className = '' }: { label: string; checked: boolean; onChange: (value: boolean) => void; className?: string }) {
  return (
    <label className={`toggle-field ${className}`.trim()}>
      <span>{label}</span>
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
    </label>
  );
}

function NumberField({ label, value, min, invalid, onChange, className = '', inputClassName = '' }: { label: string; value: number; min: number; invalid: boolean; onChange: (value: number) => void; className?: string; inputClassName?: string }) {
  return (
    <label className={className}>
      {label}
      <input
        aria-invalid={invalid}
        className={`${inputClassName} ${invalid ? 'invalid-field' : ''}`.trim()}
        type="number"
        min={min}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
      />
    </label>
  );
}

function OpacityField({ label, value, onChange, className = '' }: { label: string; value: number; onChange: (value: number) => void; className?: string }) {
  return (
    <label className={className}>
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

function AngleField({ label, value, onChange, className = '' }: { label: string; value: number; onChange: (value: number) => void; className?: string }) {
  const invalid = !isGradientAngleValid(value);

  return (
    <label className={className}>
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

function ColorField({ label, value, invalid, onChange, className = '', inputClassName = '' }: { label: string; value: string; invalid: boolean; onChange: (value: string) => void; className?: string; inputClassName?: string }) {
  const fieldId = useRef(`color-field-${Math.random().toString(16).slice(2)}`);
  const fieldRef = useRef<HTMLDivElement | null>(null);
  const swatchRef = useRef<HTMLButtonElement | null>(null);
  const popoverRef = useRef<HTMLDivElement | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [inputValue, setInputValue] = useState(value);
  const [popoverPosition, setPopoverPosition] = useState({ left: 0, top: 0 });
  const normalizedValue = normalizeColorHex(value);
  const pickerColor = normalizedValue ?? '#000000';
  const inputInvalid = !isColorPickerHex(inputValue);
  const labelId = `${fieldId.current}-label`;

  const updatePopoverPosition = () => {
    const swatch = swatchRef.current;
    if (!swatch) {
      return;
    }

    const popoverWidth = 242;
    const popoverHeight = 210;
    const margin = 12;
    const gap = 8;
    const rect = swatch.getBoundingClientRect();
    const availableBelow = window.innerHeight - rect.bottom - margin;
    const fitsBelow = availableBelow >= popoverHeight;
    const top = fitsBelow
      ? rect.bottom + gap
      : Math.max(margin, rect.top - popoverHeight - gap);
    const left = Math.min(
      Math.max(margin, rect.left),
      Math.max(margin, window.innerWidth - popoverWidth - margin),
    );

    setPopoverPosition({ left, top });
  };

  useEffect(() => {
    setInputValue(value);
  }, [value]);

  useEffect(() => {
    if (!pickerOpen) {
      return;
    }

    const closeOnOutsideClick = (event: MouseEvent) => {
      const target = event.target as Node;
      if (!fieldRef.current?.contains(target) && !popoverRef.current?.contains(target)) {
        setPickerOpen(false);
      }
    };
    const closeOnEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        setPickerOpen(false);
      }
    };
    const closeWhenAnotherPickerOpens = (event: Event) => {
      if (event instanceof CustomEvent && event.detail !== fieldId.current) {
        setPickerOpen(false);
      }
    };

    document.addEventListener('mousedown', closeOnOutsideClick);
    document.addEventListener('keydown', closeOnEscape);
    window.addEventListener('resize', updatePopoverPosition);
    window.addEventListener('scroll', updatePopoverPosition, true);
    window.addEventListener('tidereader-color-picker-open', closeWhenAnotherPickerOpens);
    updatePopoverPosition();
    return () => {
      document.removeEventListener('mousedown', closeOnOutsideClick);
      document.removeEventListener('keydown', closeOnEscape);
      window.removeEventListener('resize', updatePopoverPosition);
      window.removeEventListener('scroll', updatePopoverPosition, true);
      window.removeEventListener('tidereader-color-picker-open', closeWhenAnotherPickerOpens);
    };
  }, [pickerOpen]);

  useEffect(() => {
    const resetInputValue = () => setInputValue(value);
    window.addEventListener('tidereader-color-fields-reset', resetInputValue);
    return () => {
      window.removeEventListener('tidereader-color-fields-reset', resetInputValue);
    };
  }, [value]);

  const openPicker = () => {
    window.dispatchEvent(new CustomEvent('tidereader-color-picker-open', { detail: fieldId.current }));
    setPickerOpen(true);
  };

  const handleSwatchKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      openPicker();
    }
  };

  const handleTextChange = (nextValue: string) => {
    setInputValue(nextValue);
    const normalized = normalizeColorHex(nextValue);
    if (normalized !== null && normalized !== value) {
      onChange(normalized);
    }
  };

  const handlePickerChange = (nextValue: string) => {
    const normalized = normalizeColorHex(nextValue);
    if (normalized === null) {
      return;
    }

    setInputValue(normalized);
    onChange(normalized);
  };

  return (
    <div ref={fieldRef} className={`color-picker-field ${className}`.trim()}>
      <span id={labelId} className="color-picker-label">{label}</span>
      <div className="color-field">
        <button
          ref={swatchRef}
          type="button"
          className={`color-swatch ${invalid || inputInvalid ? 'invalid' : ''}`}
          style={{ backgroundColor: normalizedValue ?? 'transparent' }}
          aria-label={`Open ${label} color picker`}
          aria-expanded={pickerOpen}
          onClick={openPicker}
          onKeyDown={handleSwatchKeyDown}
        >
          <span id={`${fieldId.current}-swatch-label`} className="sr-only">color picker</span>
        </button>
        <input
          aria-label={label}
          aria-invalid={inputInvalid}
          className={`${inputClassName} ${inputInvalid ? 'invalid-field' : ''}`.trim()}
          type="text"
          value={inputValue}
          spellCheck={false}
          onChange={(event) => handleTextChange(event.target.value)}
          onBlur={() => {
            const normalized = normalizeColorHex(inputValue);
            if (normalized !== null) {
              setInputValue(normalized);
            }
          }}
        />
      </div>
      {pickerOpen ? createPortal((
        <div
          ref={popoverRef}
          className="color-picker-popover"
          role="dialog"
          aria-label={`${label} color picker`}
          style={{ left: popoverPosition.left, top: popoverPosition.top }}
        >
          <HexColorPicker color={pickerColor} onChange={handlePickerChange} />
        </div>
      ), document.body) : null}
    </div>
  );
}

function TextStyleEditor({ title, fontOptions, value, onChange }: { title: string; fontOptions: string[]; value: OverlayTextStyle; onChange: (value: OverlayTextStyle) => void }) {
  const colorValid = isValidHexColor(value.colorHex);
  const fontSizeValid = isPositiveNumber(value.fontSizePx);
  const fontFamilyValid = Boolean(value.fontFamily.trim());
  const maxCharactersValid = isZeroOrPositiveNumber(value.maxCharacters);
  const textOverflowModeValid = isTextOverflowMode(value.textOverflowMode);

  return (
    <section className="subsection-card">
      <h4>{title}</h4>
      <div className="field-grid settings-grid">
        <label className="field-span-large">
          Font family
          <select
            aria-invalid={!fontFamilyValid}
            className={`field-control field-control-large ${!fontFamilyValid ? 'invalid-field' : ''}`}
            value={value.fontFamily}
            onChange={(event) => onChange({ ...value, fontFamily: event.target.value })}
          >
            {fontOptions.map((font) => (
              <option key={font} value={font}>{font}</option>
            ))}
          </select>
        </label>
        <ColorField
          className="field-span-medium"
          inputClassName="field-control field-control-compact"
          label="Hex color"
          value={value.colorHex}
          invalid={!colorValid}
          onChange={(next) => onChange({ ...value, colorHex: next })}
        />
        <NumberField
          className="field-span-compact"
          inputClassName="field-control field-control-compact"
          label="Font size (px)"
          value={value.fontSizePx}
          min={1}
          invalid={!fontSizeValid}
          onChange={(next) => onChange({ ...value, fontSizePx: next })}
        />
        <NumberField
          className="field-span-compact"
          inputClassName="field-control field-control-compact"
          label="Character limit"
          value={value.maxCharacters}
          min={0}
          invalid={!maxCharactersValid}
          onChange={(next) => onChange({ ...value, maxCharacters: next })}
        />
        <label className="field-span-medium">
          Text overflow mode
          <select
            aria-invalid={!textOverflowModeValid}
            className={`field-control field-control-medium ${!textOverflowModeValid ? 'invalid-field' : ''}`}
            value={value.textOverflowMode}
            onChange={(event) => onChange({ ...value, textOverflowMode: event.target.value as OverlayTextStyle['textOverflowMode'] })}
          >
            {textOverflowModeOptions.map((mode) => (
              <option key={mode} value={mode}>{mode === 'TwoLines' ? 'Two Lines' : mode === 'AutoSize' ? 'Auto Size' : mode}</option>
            ))}
          </select>
        </label>
      </div>
      <div className="toggle-list toggle-grid toggle-grid-compact">
        <Toggle label="Bold" checked={value.bold} onChange={(next) => onChange({ ...value, bold: next })} />
        <Toggle label="Italic" checked={value.italic} onChange={(next) => onChange({ ...value, italic: next })} />
        <Toggle label="Underline" checked={value.underline} onChange={(next) => onChange({ ...value, underline: next })} />
      </div>
      {value.textOverflowMode !== 'Default' && value.maxCharacters > 0 ? (
        <p className="note">Smart Text Handling works best when character limits are disabled. Consider setting this field's character limit to 0 if you want the full text displayed.</p>
      ) : null}
      {overlayTextStyleHasErrors(value) ? <p className="note">Enter a valid font family, positive font size, non-negative character limit, text overflow mode, and `#RGB` or `#RRGGBB` color.</p> : null}
    </section>
  );
}

export default App;
