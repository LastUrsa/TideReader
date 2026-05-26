import { beforeEach, describe, expect, it, vi } from 'vitest';
import { chooseOutputFolder, getArtworkUrl, getState, openLogsFolder, openOutputFolder, runDetectionNow, saveSettings, setManualInput, syncLocalApiTokenFromLocation, type Settings } from './api';

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
      });

    const folder = await chooseOutputFolder();
    await openOutputFolder();
    await openLogsFolder();

    expect(folder).toBe('C:\\Output');
    expect(fetchMock).toHaveBeenNthCalledWith(1, `${baseUrl}/api/choose-output-folder`, expect.objectContaining({ method: 'POST' }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, `${baseUrl}/api/open-output-folder`, expect.objectContaining({ method: 'POST' }));
    expect(fetchMock).toHaveBeenNthCalledWith(3, `${baseUrl}/api/open-logs-folder`, expect.objectContaining({ method: 'POST' }));
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
