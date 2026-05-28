export type OverlayTextStyle = {
  fontFamily: string;
  colorHex: string;
  fontSizePx: number;
  maxCharacters: number;
  bold: boolean;
  italic: boolean;
  underline: boolean;
};

export type OverlayContainerStyle = {
  backgroundMode: 'solid' | 'gradient';
  backgroundColorHex: string;
  gradient: GradientSettings;
  opacity: number;
  cornerRadiusPx: number;
  paddingPx: number;
  gapPx: number;
  borderEnabled: boolean;
  borderColorHex: string;
  borderWidthPx: number;
};

export type GradientSettings = {
  colorCount: 2 | 3;
  preset: string;
  color1Hex: string;
  color2Hex: string;
  color3Hex: string;
  angleDeg: number;
};

export type StatusPillStyle = {
  backgroundColorHex: string;
  textColorHex: string;
  opacity: number;
  fontFamily: string;
  fontSizePx: number;
  bold: boolean;
  italic: boolean;
  underline: boolean;
  cornerRadiusPx: number;
  paddingHorizontalPx: number;
  paddingVerticalPx: number;
};

export type OverlaySettings = {
  songTextStyle: OverlayTextStyle;
  artistTextStyle: OverlayTextStyle;
  albumTextStyle: OverlayTextStyle;
  imageSizePx: number;
  backgroundColorHex: string;
  overlayContainerStyle: OverlayContainerStyle;
  statusPillStyle: StatusPillStyle;
  imagePosition: 'Left' | 'Right';
  textAlign: 'Left' | 'Center' | 'Right';
  showAppName: boolean;
  showPlaybackState: boolean;
  showPlaybackProvider: boolean;
};

export type BrowserSupportSettings = {
  chromeEnabled: boolean;
  edgeEnabled: boolean;
  firefoxEnabled: boolean;
  braveEnabled: boolean;
  operaEnabled: boolean;
};

export type BrowserSettings = {
  enabled: boolean;
  activeSourceMode: 'auto' | 'tidal' | 'browser';
  supportedBrowsers: BrowserSupportSettings;
  sourcePriority: string[];
  sourceSwitchCooldownMs: number;
  allowGenericPlayback: boolean;
  preferTidalOverBrowser: boolean;
  metadataCleanupEnabled: boolean;
  browserArtworkEnabled: boolean;
  youTubeVideoImageFallbackEnabled: boolean;
  debugLoggingEnabled: boolean;
  deepDiagnosticLoggingEnabled: boolean;
  ignorePausedSessions: boolean;
  ignoreStaleSessions: boolean;
  staleSessionAfterSeconds: number;
  showRawBrowserMetadata: boolean;
};

export type Settings = {
  outputFolder: string;
  overlayEnabled: boolean;
  overlayPort: number;
  pollIntervalMs: number;
  enableWindowTitleFallback: boolean;
  enableDebugManualInput: boolean;
  startMinimized: boolean;
  launchAtStartup: boolean;
  metadataProviderMode: 'Off' | 'MusicBrainzOnly' | 'MusicBrainzWithFallbacks';
  themeMode: 'Dark' | 'Light';
  overlaySettings: OverlaySettings;
  browserSettings: BrowserSettings;
};

export type DetectionResult = {
  status: string;
  title: string;
  artist: string;
  album: string;
  durationMs: number;
  artworkPath: string;
  source: string;
  method: string;
  confidence: number;
  detectedText: string;
  metadataSource: string;
  provider: string;
  browser: string;
  site: string;
  rawTitle: string;
  rawArtist: string;
  rawAlbum: string;
  selectionReason: string;
};

export type BrowserSessionDebugInfo = {
  provider: string;
  browser: string;
  site: string;
  playbackState: string;
  sourceAppId: string;
  rawTitle: string;
  rawArtist: string;
  rawAlbum: string;
  parsedTitle: string;
  parsedArtist: string;
  parsedAlbum: string;
  confidence: number;
  hasArtwork: boolean;
  isSelected: boolean;
  decisionReason: string;
  sessionId: string;
  lastUpdatedUtc: string;
};

export type BrowserDebugState = {
  sessions: BrowserSessionDebugInfo[];
};

export type AppState = {
  settings: Settings;
  nowPlaying: DetectionResult;
  appVersion: string;
  artworkRevision: number;
  outputFolder: string;
  overlayUrl: string;
  logPath: string;
  lastError: string;
  manualInput: string;
  startupReady: boolean;
  statusMessage: string;
  browserDebug: BrowserDebugState;
};

export type UpdateInfo = {
  currentVersion: string;
  latestVersion: string;
  updateAvailable: boolean;
  releaseUrl: string;
  message: string;
};

const localApiTokenSessionKey = 'tidereader.local_api_token';
const localApiTokenQueryKey = 'tr_token';
const localApiTokenHeader = 'X-TideReader-Token';
const configuredBaseUrl = import.meta.env.VITE_BACKEND_URL?.trim() ?? '';
const defaultBaseUrl = import.meta.env.DEV ? 'http://127.0.0.1:17656' : '';
const baseUrl = (configuredBaseUrl || defaultBaseUrl).replace(/\/$/, '');

function readLocalApiToken(): string {
  return window.sessionStorage.getItem(localApiTokenSessionKey)?.trim() ?? '';
}

export function syncLocalApiTokenFromLocation(locationHref: string = window.location.href): void {
  const url = new URL(locationHref);
  const token = url.searchParams.get(localApiTokenQueryKey)?.trim() ?? '';
  if (!token) {
    return;
  }

  window.sessionStorage.setItem(localApiTokenSessionKey, token);
  url.searchParams.delete(localApiTokenQueryKey);
  const nextLocation = `${url.pathname}${url.search}${url.hash}` || '/';
  window.history.replaceState(window.history.state, '', nextLocation);
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = readLocalApiToken();
  const response = await fetch(`${baseUrl}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { [localApiTokenHeader]: token } : {}),
      ...(init?.headers ?? {}),
    },
    ...init,
  });

  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }

  return response.json() as Promise<T>;
}

export function getState(): Promise<AppState> {
  return request<AppState>('/api/state');
}

export async function getSystemFonts(): Promise<string[]> {
  const result = await request<{ fonts: string[] }>('/api/system-fonts');
  return result.fonts ?? [];
}

export function saveSettings(settings: Settings): Promise<AppState> {
  return request<AppState>('/api/settings', {
    method: 'POST',
    body: JSON.stringify(settings),
  });
}

export function setManualInput(input: string): Promise<AppState> {
  return request<AppState>('/api/manual-input', {
    method: 'POST',
    body: JSON.stringify({ input }),
  });
}

export function runDetectionNow(): Promise<AppState> {
  return request<AppState>('/api/run-detection', {
    method: 'POST',
    body: '{}',
  });
}

export async function chooseOutputFolder(): Promise<string> {
  const result = await request<{ folder: string | null }>('/api/choose-output-folder', {
    method: 'POST',
    body: '{}',
  });
  return result.folder ?? '';
}

export async function openOutputFolder(): Promise<void> {
  await request<{ ok: boolean }>('/api/open-output-folder', {
    method: 'POST',
    body: '{}',
  });
}

export async function openLogsFolder(): Promise<void> {
  await request<{ ok: boolean }>('/api/open-logs-folder', {
    method: 'POST',
    body: '{}',
  });
}

export function checkForUpdates(): Promise<UpdateInfo> {
  return request<UpdateInfo>('/api/check-for-updates');
}

export async function openReleasePage(): Promise<void> {
  await request<{ ok: boolean }>('/api/open-releases-page', {
    method: 'POST',
    body: '{}',
  });
}

export function getArtworkUrl(revision: number): string {
  const suffix = revision > 0 ? `?v=${encodeURIComponent(String(revision))}` : '';
  const token = readLocalApiToken();
  const tokenPart = token ? `${suffix ? '&' : '?'}${localApiTokenQueryKey}=${encodeURIComponent(token)}` : '';
  return `${baseUrl}/api/artwork${suffix}${tokenPart}`;
}
