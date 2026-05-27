import { beforeEach, describe, expect, it, vi } from 'vitest';
import { checkForUpdates, chooseOutputFolder, getArtworkUrl, getState, getSystemFonts, openLogsFolder, openOutputFolder, openReleasePage, runDetectionNow, saveSettings, setManualInput, syncLocalApiTokenFromLocation, type Settings } from './api';

const fetchMock = vi.fn();

const baseUrl = 'http://127.0.0.1:17656';

describe('api', () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    window.sessionStorage.clear();
    window.history.replaceState({}, '', '/');
  });

  it('requests current state from the backend', async () => {
    const responseBody = { statusMessage: 'ok' };
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => responseBody,
    });
    window.sessionStorage.setItem('tidereader.local_api_token', 'abc123');

    const result = await getState();

    expect(fetchMock).toHaveBeenCalledWith(`${baseUrl}/api/state`, expect.objectContaining({
      headers: { 'Content-Type': 'application/json', 'X-TideReader-Token': 'abc123' },
    }));
    expect(result).toEqual(responseBody);
  });

  it('stores the local api token from the URL and removes it from the address bar', () => {
    window.history.replaceState({}, '', '/?tr_token=secret-token#debug');

    syncLocalApiTokenFromLocation();

    expect(window.sessionStorage.getItem('tidereader.local_api_token')).toBe('secret-token');
    expect(window.location.search).toBe('');
    expect(window.location.hash).toBe('#debug');
  });

  it('posts settings updates as JSON', async () => {
    const settings: Settings = {
      outputFolder: 'C:\\Temp',
      overlayEnabled: true,
      overlayPort: 17655,
      pollIntervalMs: 1000,
      enableWindowTitleFallback: true,
      enableDebugManualInput: false,
      startMinimized: false,
      launchAtStartup: true,
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
        overlayContainerStyle: {
          backgroundMode: 'solid',
          backgroundColorHex: '#32334F',
          gradient: {
            colorCount: 3,
            preset: 'Diagonal',
            color1Hex: '#1F1F2E',
            color2Hex: '#6B46C1',
            color3Hex: '#111827',
            angleDeg: 135,
          },
          opacity: 0.86,
          cornerRadiusPx: 18,
          paddingPx: 14,
          gapPx: 14,
          borderEnabled: true,
          borderColorHex: '#929498',
          borderWidthPx: 1,
        },
        statusPillStyle: {
          backgroundColorHex: '#45475D',
          textColorHex: '#787B80',
          opacity: 1,
          fontFamily: 'Segoe UI',
          fontSizePx: 11,
          bold: false,
          italic: false,
          underline: false,
          cornerRadiusPx: 999,
          paddingHorizontalPx: 9,
          paddingVerticalPx: 4,
        },
        imagePosition: 'Left',
        textAlign: 'Left',
        showAppName: true,
        showPlaybackState: true,
        showPlaybackProvider: false,
      },
      browserSettings: {
        enabled: true,
        activeSourceMode: 'auto',
        supportedBrowsers: {
          chromeEnabled: true,
          edgeEnabled: true,
          firefoxEnabled: true,
          braveEnabled: true,
          operaEnabled: false,
        },
        sourcePriority: ['tidal', 'youtubeMusic', 'bandcamp', 'soundcloud', 'youtube', 'genericBrowser'],
        sourceSwitchCooldownMs: 5000,
        allowGenericPlayback: true,
        preferTidalOverBrowser: true,
        metadataCleanupEnabled: true,
        browserArtworkEnabled: true,
        youTubeVideoImageFallbackEnabled: true,
        debugLoggingEnabled: false,
        ignorePausedSessions: true,
        ignoreStaleSessions: true,
        staleSessionAfterSeconds: 30,
        showRawBrowserMetadata: false,
      },
    };

    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({ settings }),
    });

    await saveSettings(settings);

    expect(fetchMock).toHaveBeenCalledWith(`${baseUrl}/api/settings`, expect.objectContaining({
      method: 'POST',
      body: JSON.stringify(settings),
    }));
  });

  it('posts manual input and run-now actions', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({ statusMessage: 'ok' }),
    });

    await setManualInput('Artist - Track | Album');
    await runDetectionNow();

    expect(fetchMock).toHaveBeenNthCalledWith(1, `${baseUrl}/api/manual-input`, expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ input: 'Artist - Track | Album' }),
    }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, `${baseUrl}/api/run-detection`, expect.objectContaining({
      method: 'POST',
      body: '{}',
    }));
  });

  it('handles folder helper endpoints', async () => {
    fetchMock
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ folder: 'C:\\Output' }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ ok: true }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ ok: true }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ fonts: ['Segoe UI', 'Arial'] }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          currentVersion: '0.3.0',
          latestVersion: '0.2.1',
          updateAvailable: true,
          releaseUrl: 'https://github.com/LastUrsa/TideReader/releases',
          message: 'Version 0.2.1 is available.',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ ok: true }),
      });

    const folder = await chooseOutputFolder();
    await openOutputFolder();
    await openLogsFolder();
    const fonts = await getSystemFonts();
    const updateInfo = await checkForUpdates();
    await openReleasePage();

    expect(folder).toBe('C:\\Output');
    expect(fonts).toEqual(['Segoe UI', 'Arial']);
    expect(updateInfo.latestVersion).toBe('0.2.1');
    expect(fetchMock).toHaveBeenNthCalledWith(1, `${baseUrl}/api/choose-output-folder`, expect.objectContaining({ method: 'POST' }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, `${baseUrl}/api/open-output-folder`, expect.objectContaining({ method: 'POST' }));
    expect(fetchMock).toHaveBeenNthCalledWith(3, `${baseUrl}/api/open-logs-folder`, expect.objectContaining({ method: 'POST' }));
    expect(fetchMock).toHaveBeenNthCalledWith(4, `${baseUrl}/api/system-fonts`, expect.objectContaining({
      headers: { 'Content-Type': 'application/json' },
    }));
    expect(fetchMock).toHaveBeenNthCalledWith(5, `${baseUrl}/api/check-for-updates`, expect.objectContaining({
      headers: { 'Content-Type': 'application/json' },
    }));
    expect(fetchMock).toHaveBeenNthCalledWith(6, `${baseUrl}/api/open-releases-page`, expect.objectContaining({ method: 'POST' }));
  });

  it('builds the artwork URL with revision cache busting', () => {
    window.sessionStorage.setItem('tidereader.local_api_token', 'art-token');

    expect(getArtworkUrl(0)).toBe(`${baseUrl}/api/artwork?tr_token=art-token`);
    expect(getArtworkUrl(42)).toBe(`${baseUrl}/api/artwork?v=42&tr_token=art-token`);
  });

  it('builds the artwork URL without a token when none is present', () => {
    expect(getArtworkUrl(0)).toBe(`${baseUrl}/api/artwork`);
    expect(getArtworkUrl(42)).toBe(`${baseUrl}/api/artwork?v=42`);
  });

  it('throws on failed requests', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 503,
      json: async () => ({}),
    });

    await expect(getState()).rejects.toThrow('Request failed: 503');
  });
});
