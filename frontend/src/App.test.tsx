import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { AppState } from './api';

const apiMocks = vi.hoisted(() => ({
  checkForUpdates: vi.fn(),
  chooseOutputFolder: vi.fn(),
  getState: vi.fn(),
  getSystemFonts: vi.fn(),
  openReleasePage: vi.fn(),
  openOutputFolder: vi.fn(),
  runDetectionNow: vi.fn(),
  saveSettings: vi.fn(),
  getArtworkUrl: vi.fn(),
}));

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api');
  return {
    ...actual,
    checkForUpdates: apiMocks.checkForUpdates,
    chooseOutputFolder: apiMocks.chooseOutputFolder,
    getState: apiMocks.getState,
    getSystemFonts: apiMocks.getSystemFonts,
    openReleasePage: apiMocks.openReleasePage,
    openOutputFolder: apiMocks.openOutputFolder,
    runDetectionNow: apiMocks.runDetectionNow,
    saveSettings: apiMocks.saveSettings,
    getArtworkUrl: apiMocks.getArtworkUrl,
  };
});

function createState(overrides?: Partial<AppState>): AppState {
  return {
    settings: {
      outputFolder: 'C:\\Output',
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
    },
    nowPlaying: {
      status: 'playing',
      title: 'Sample Track',
      artist: 'Sample Artist',
      album: 'Sample Album',
      durationMs: 123000,
      artworkPath: 'cover.jpg',
      source: 'TIDAL',
      method: 'media_session',
      confidence: 0.94,
      detectedText: 'Sample Artist - Sample Track',
      metadataSource: 'MusicBrainz',
      provider: 'tidal',
      browser: '',
      site: '',
      rawTitle: 'Sample Track',
      rawArtist: 'Sample Artist',
      rawAlbum: 'Sample Album',
      selectionReason: 'selected: highest priority active source',
    },
    outputFolder: 'C:\\Output',
    appVersion: '0.3.0',
    artworkRevision: 12,
    overlayUrl: 'http://127.0.0.1:17655/overlay',
    logPath: 'C:\\Logs\\bridge.log',
    lastError: '',
    manualInput: '',
    startupReady: true,
    statusMessage: 'Ready',
    browserDebug: {
      sessions: [],
    },
    ...overrides,
  };
}

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((nextResolve) => {
    resolve = nextResolve;
  });
  return { promise, resolve };
}

describe('App', () => {
  beforeEach(() => {
    delete (window as Window & { chrome?: unknown }).chrome;
    document.documentElement.removeAttribute('data-theme');
    apiMocks.chooseOutputFolder.mockReset();
    apiMocks.checkForUpdates.mockReset();
    apiMocks.getState.mockReset();
    apiMocks.getSystemFonts.mockReset();
    apiMocks.openReleasePage.mockReset();
    apiMocks.openOutputFolder.mockReset();
    apiMocks.runDetectionNow.mockReset();
    apiMocks.saveSettings.mockReset();
    apiMocks.getArtworkUrl.mockReset();
    apiMocks.getArtworkUrl.mockReturnValue('/api/artwork?v=12');
    apiMocks.getState.mockResolvedValue(createState());
    apiMocks.getSystemFonts.mockResolvedValue(['Segoe UI', 'Arial', 'Tahoma']);
    apiMocks.runDetectionNow.mockResolvedValue(createState({ statusMessage: 'Refreshed' }));
    apiMocks.openOutputFolder.mockResolvedValue(undefined);
    apiMocks.openReleasePage.mockResolvedValue(undefined);
    apiMocks.checkForUpdates.mockResolvedValue({
      currentVersion: '0.3.0',
      latestVersion: '0.2.1',
      updateAvailable: true,
      releaseUrl: 'https://github.com/LastUrsa/TideReader/releases',
      message: 'Version 0.2.1 is available.',
    });
    apiMocks.saveSettings.mockResolvedValue(createState());
    apiMocks.chooseOutputFolder.mockResolvedValue('C:\\Chosen');
  });

  it('renders current track state after load', async () => {
    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Sample Track' })).toBeInTheDocument();
    expect(screen.getByText('Sample Artist')).toBeInTheDocument();
    expect(screen.getByText('Sample Album')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Sample Track cover art' })).toHaveAttribute('src', '/api/artwork?v=12');
  });

  it('keeps album art square in the app shell', async () => {
    render(<App />);

    const image = await screen.findByRole('img', { name: 'Sample Track cover art' });

    expect(image).toHaveStyle({ borderRadius: '0px' });
    expect(image.parentElement).toHaveStyle({ borderRadius: '0px' });
    expect(image.parentElement).toHaveClass('has-artwork');
  });

  it('posts layout messages when the host bridge is available', async () => {
    const postMessage = vi.fn();
    (window as Window & {
      chrome?: {
        webview?: {
          postMessage: (message: unknown) => void;
        };
      };
    }).chrome = { webview: { postMessage } };

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    expect(postMessage).toHaveBeenCalledWith({ type: 'layout', mode: 'compact' });

    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    expect(postMessage).toHaveBeenCalledWith({ type: 'layout', mode: 'settings' });

    fireEvent.click(screen.getAllByRole('button', { name: 'Close' })[0]);
    expect(postMessage).toHaveBeenCalledWith({ type: 'layout', mode: 'compact' });
  });

  it('falls back to placeholders when artwork is unavailable', async () => {
    apiMocks.getState.mockResolvedValue(createState({
      nowPlaying: {
        ...createState().nowPlaying,
        title: '',
        artist: '',
        album: '',
        artworkPath: '',
      },
    }));

    render(<App />);

    expect(await screen.findByText('Waiting for playback')).toBeInTheDocument();
    expect(screen.getByText('Artist unavailable')).toBeInTheDocument();
    expect(screen.getByText('Album unavailable')).toBeInTheDocument();
    expect(screen.getByText('Idle')).toBeInTheDocument();
  });

  it('shows browser-aware fallback copy for low-quality browser sessions', async () => {
    apiMocks.getState.mockResolvedValue(createState({
      nowPlaying: {
        ...createState().nowPlaying,
        provider: 'browser',
        source: 'Bandcamp',
        site: 'bandcamp',
        title: "lastursa's collection | Bandcamp",
        artist: '',
        album: '',
        artworkPath: '',
      },
    }));

    render(<App />);

    expect(await screen.findByRole('heading', { name: "lastursa's collection | Bandcamp" })).toBeInTheDocument();
    expect(screen.getAllByText('Bandcamp').length).toBeGreaterThan(0);
    expect(screen.getByText('Metadata limited')).toBeInTheDocument();
    expect(screen.queryByText('Idle')).not.toBeInTheDocument();
  });

  it('opens the output folder from the main panel', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));

    fireEvent.click(screen.getByRole('button', { name: 'Open output folder' }));
    await waitFor(() => expect(apiMocks.openOutputFolder).toHaveBeenCalledOnce());
  });

  it('refreshes detection when asked', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

    await waitFor(() => expect(apiMocks.runDetectionNow).toHaveBeenCalledOnce());
  });

  it('checks for updates from the General tab and opens releases when available', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));

    expect(screen.getByText('Current version')).toBeInTheDocument();
    expect(screen.getByText('0.3.0')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Check for Updates' }));

    await screen.findByText('Version 0.2.1 is available.');
    expect(screen.getByText('Latest version: 0.2.1')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'View Releases' }));
    await waitFor(() => expect(apiMocks.openReleasePage).toHaveBeenCalledOnce());
  });

  it('shows an error when the update check fails', async () => {
    apiMocks.checkForUpdates.mockRejectedValue(new Error('network down'));

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('button', { name: 'Check for Updates' }));

    expect(await screen.findByText('Update check failed: Error: network down')).toBeInTheDocument();
  });

  it('saves edited settings through the backend', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));

    fireEvent.change(screen.getByDisplayValue('C:\\Output'), {
      target: { value: 'D:\\OBS' },
    });
    fireEvent.change(screen.getByDisplayValue('1000'), {
      target: { value: '750' },
    });
    fireEvent.change(screen.getAllByRole('combobox')[0], {
      target: { value: 'Light' },
    });
    fireEvent.change(screen.getAllByRole('combobox')[1], {
      target: { value: 'MusicBrainzOnly' },
    });
    fireEvent.click(screen.getByLabelText('Start minimized'));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));
    fireEvent.change(screen.getByDisplayValue('17655'), {
      target: { value: '19000' },
    });
    fireEvent.click(screen.getByLabelText('Enable local overlay'));
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => expect(apiMocks.saveSettings).toHaveBeenCalledWith(expect.objectContaining({
      outputFolder: 'D:\\OBS',
      overlayEnabled: false,
      overlayPort: 19000,
      pollIntervalMs: 750,
      startMinimized: true,
      metadataProviderMode: 'MusicBrainzOnly',
      themeMode: 'Light',
    })));
  });

  it('updates the output folder draft when browse returns a folder', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('button', { name: 'Browse' }));

    await waitFor(() => expect(apiMocks.chooseOutputFolder).toHaveBeenCalledOnce());
    expect(screen.getByDisplayValue('C:\\Chosen')).toBeInTheDocument();
  });

  it('saves overlay customization settings through the backend', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));
    fireEvent.click(screen.getByLabelText('Show app name'));
    fireEvent.click(screen.getByLabelText('Show playback state'));

    fireEvent.change(screen.getAllByLabelText('Font family')[0], {
      target: { value: 'Arial' },
    });
    fireEvent.change(screen.getAllByLabelText('Hex color')[0], {
      target: { value: '#112233' },
    });
    fireEvent.change(screen.getAllByLabelText('Font size (px)')[0], {
      target: { value: '30' },
    });
    fireEvent.change(screen.getAllByLabelText('Character limit')[0], {
      target: { value: '18' },
    });
    fireEvent.change(screen.getAllByLabelText('Font family')[1], {
      target: { value: 'Tahoma' },
    });
    fireEvent.change(screen.getAllByLabelText('Character limit')[1], {
      target: { value: '12' },
    });
    fireEvent.click(screen.getAllByLabelText('Italic')[1]);
    fireEvent.change(screen.getAllByLabelText('Character limit')[2], {
      target: { value: '8' },
    });
    fireEvent.click(screen.getAllByLabelText('Underline')[2]);
    fireEvent.change(screen.getByLabelText('Artwork image size (px)'), {
      target: { value: '92' },
    });
    fireEvent.change(screen.getAllByLabelText('Background color')[0], {
      target: { value: '#445566' },
    });
    fireEvent.change(screen.getByLabelText('Background Opacity'), {
      target: { value: '0.75' },
    });
    fireEvent.change(screen.getByLabelText('Artwork position'), {
      target: { value: 'Right' },
    });
    fireEvent.change(screen.getByLabelText('Text alignment'), {
      target: { value: 'Center' },
    });
    fireEvent.click(screen.getByLabelText('Border enabled'));
    fireEvent.change(screen.getAllByLabelText('Background color')[1], {
      target: { value: '#334455' },
    });
    fireEvent.change(screen.getByLabelText('Text color'), {
      target: { value: '#FFEEDD' },
    });
    fireEvent.change(screen.getByLabelText('Status Pill Opacity'), {
      target: { value: '0.9' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => expect(apiMocks.saveSettings).toHaveBeenCalledWith(expect.objectContaining({
      overlaySettings: expect.objectContaining({
        songTextStyle: expect.objectContaining({
          fontFamily: 'Arial',
          colorHex: '#112233',
          fontSizePx: 30,
          maxCharacters: 18,
        }),
        artistTextStyle: expect.objectContaining({
          fontFamily: 'Tahoma',
          maxCharacters: 12,
          italic: true,
        }),
        albumTextStyle: expect.objectContaining({
          maxCharacters: 8,
          underline: true,
        }),
        imageSizePx: 92,
        backgroundColorHex: '#445566',
        overlayContainerStyle: expect.objectContaining({
          backgroundColorHex: '#445566',
          opacity: 0.75,
          borderEnabled: false,
        }),
        statusPillStyle: expect.objectContaining({
          backgroundColorHex: '#334455',
          textColorHex: '#FFEEDD',
          opacity: 0.9,
        }),
        imagePosition: 'Right',
        textAlign: 'Center',
        showAppName: false,
        showPlaybackState: false,
      }),
    })));
  });

  it('shows a live preview for overlay styling', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));

    expect(screen.getByText('Live Preview')).toBeInTheDocument();
    expect(screen.getAllByText('Sample Track').length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Copy OBS Overlay URL' })).not.toBeInTheDocument();
  });

  it('copies the overlay url from overlay behavior', async () => {
    Object.assign(navigator, {
      clipboard: {
        writeText: vi.fn().mockResolvedValue(undefined),
      },
    });

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));

    fireEvent.click(screen.getByRole('button', { name: /http:\/\/127\.0\.0\.1:17655\/overlay/i }));

    await waitFor(() => expect(navigator.clipboard.writeText).toHaveBeenCalledWith('http://127.0.0.1:17655/overlay'));
  });

  it('copies the overlay url through the execCommand fallback and shows copied state', async () => {
    Object.defineProperty(document, 'execCommand', {
      configurable: true,
      value: vi.fn().mockReturnValue(true),
    });
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: undefined,
    });

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));

    const button = screen.getByRole('button', { name: /http:\/\/127\.0\.0\.1:17655\/overlay/i });
    fireEvent.click(button);
    fireEvent.click(button);

    await waitFor(() => expect(document.execCommand).toHaveBeenCalledWith('copy'));
    expect(screen.getByText('Copied')).toBeInTheDocument();
  });

  it('allows overlay sections to be collapsed and expanded', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));

    const artworkToggle = screen.getByRole('button', { name: 'Artwork' });
    expect(screen.getByLabelText('Artwork image size (px)')).toBeInTheDocument();

    fireEvent.click(artworkToggle);
    expect(screen.queryByLabelText('Artwork image size (px)')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Artwork' }));
    expect(screen.getByLabelText('Artwork image size (px)')).toBeInTheDocument();
  });

  it('collapses the container and status pill sections and toggles song text styling', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));

    expect(screen.getByLabelText('Background mode')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Container' }));
    expect(screen.queryByLabelText('Background mode')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Container' }));
    expect(screen.getByLabelText('Background mode')).toBeInTheDocument();

    expect(screen.getByLabelText('Text color')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Status Pill' }));
    expect(screen.queryByLabelText('Text color')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Status Pill' }));
    expect(screen.getByLabelText('Text color')).toBeInTheDocument();

    fireEvent.click(screen.getAllByLabelText('Bold')[0]);
    fireEvent.click(screen.getAllByLabelText('Italic')[0]);
    fireEvent.click(screen.getAllByLabelText('Underline')[0]);
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => expect(apiMocks.saveSettings).toHaveBeenCalledWith(expect.objectContaining({
      overlaySettings: expect.objectContaining({
        songTextStyle: expect.objectContaining({
          bold: false,
          italic: true,
          underline: true,
        }),
      }),
    })));
  });

  it('shows gradient-only controls, filters two-color presets, and resets overlay defaults', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));

    fireEvent.change(screen.getByLabelText('Background mode'), {
      target: { value: 'gradient' },
    });

    expect(screen.getByLabelText('Gradient colors')).toBeInTheDocument();
    expect(screen.getByLabelText('Gradient preset')).toBeInTheDocument();
    expect(screen.getByLabelText('Gradient Angle')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Gradient colors'), {
      target: { value: '2' },
    });

    expect(screen.queryByLabelText('Gradient Color 3')).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Stream Neon' })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Subtle Glass' })).not.toBeInTheDocument();

    fireEvent.change(screen.getAllByLabelText('Hex color')[0], {
      target: { value: 'bad-color' },
    });
    expect(screen.getByText(/Enter a valid font family, positive font size/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Reset Overlay Styling to Defaults' }));

    expect(screen.getAllByLabelText('Hex color')[0]).toHaveValue('#EBEBEB');
    expect(screen.getByLabelText('Background mode')).toHaveValue('solid');
  });

  it('edits the remaining container and status pill controls', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));

    fireEvent.change(screen.getByLabelText('Background mode'), {
      target: { value: 'gradient' },
    });
    fireEvent.change(screen.getByLabelText('Gradient colors'), {
      target: { value: '3' },
    });
    fireEvent.change(screen.getByLabelText('Gradient preset'), {
      target: { value: 'Stream Neon' },
    });
    fireEvent.change(screen.getByLabelText('Gradient Color 1'), {
      target: { value: '#101010' },
    });
    fireEvent.change(screen.getByLabelText('Gradient Color 2'), {
      target: { value: '#202020' },
    });
    fireEvent.change(screen.getByLabelText('Gradient Color 3'), {
      target: { value: '#303030' },
    });
    fireEvent.change(screen.getByLabelText('Gradient Angle'), {
      target: { value: '220' },
    });
    fireEvent.change(screen.getAllByLabelText('Corner radius (px)')[0], {
      target: { value: '24' },
    });
    fireEvent.change(screen.getByLabelText('Padding (px)'), {
      target: { value: '20' },
    });
    fireEvent.change(screen.getByLabelText('Gap (px)'), {
      target: { value: '18' },
    });
    fireEvent.change(screen.getByLabelText('Border color'), {
      target: { value: '#123456' },
    });
    fireEvent.change(screen.getByLabelText('Border width (px)'), {
      target: { value: '3' },
    });

    fireEvent.change(screen.getByLabelText('Text color'), {
      target: { value: '#C0FFEE' },
    });
    fireEvent.change(screen.getAllByLabelText('Font family')[3], {
      target: { value: 'Arial' },
    });
    fireEvent.change(screen.getAllByLabelText('Font size (px)')[3], {
      target: { value: '14' },
    });
    fireEvent.change(screen.getAllByLabelText('Corner radius (px)')[1], {
      target: { value: '12' },
    });
    fireEvent.change(screen.getByLabelText('Horizontal padding (px)'), {
      target: { value: '11' },
    });
    fireEvent.change(screen.getByLabelText('Vertical padding (px)'), {
      target: { value: '6' },
    });
    fireEvent.click(screen.getAllByLabelText('Bold')[3]);
    fireEvent.click(screen.getAllByLabelText('Italic')[3]);
    fireEvent.click(screen.getAllByLabelText('Underline')[3]);

    fireEvent.click(screen.getByRole('button', { name: 'Live Preview' }));
    expect(screen.queryByText('Sample Song')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Live Preview' }));
    expect(screen.getAllByText('Sample Track').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => expect(apiMocks.saveSettings).toHaveBeenCalledWith(expect.objectContaining({
      overlaySettings: expect.objectContaining({
        overlayContainerStyle: expect.objectContaining({
          backgroundMode: 'gradient',
          cornerRadiusPx: 24,
          paddingPx: 20,
          gapPx: 18,
          borderColorHex: '#123456',
          borderWidthPx: 3,
          gradient: expect.objectContaining({
            colorCount: 3,
            preset: 'Stream Neon',
            color1Hex: '#101010',
            color2Hex: '#202020',
            color3Hex: '#303030',
            angleDeg: 220,
          }),
        }),
        statusPillStyle: expect.objectContaining({
          textColorHex: '#C0FFEE',
          fontFamily: 'Arial',
          fontSizePx: 14,
          cornerRadiusPx: 12,
          paddingHorizontalPx: 11,
          paddingVerticalPx: 6,
          bold: true,
          italic: true,
          underline: true,
        }),
      }),
    })));
  });

  it('shows saving state while persisting settings', async () => {
    const deferred = createDeferred<AppState>();
    apiMocks.saveSettings.mockReturnValueOnce(deferred.promise);

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    expect(screen.getByRole('button', { name: 'Saving...' })).toBeDisabled();

    deferred.resolve(createState());
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Saving...' })).not.toBeInTheDocument());
  });

  it('disables save when overlay hex colors are invalid', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));
    fireEvent.change(screen.getAllByLabelText('Background color')[0], {
      target: { value: 'not-a-color' },
    });

    expect(screen.getAllByLabelText('Background color')[0]).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('button', { name: 'Save settings' })).toBeDisabled();
  });

  it('disables save when character limits are negative', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));
    fireEvent.change(screen.getAllByLabelText('Character limit')[0], {
      target: { value: '-1' },
    });

    expect(screen.getAllByLabelText('Character limit')[0]).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('button', { name: 'Save settings' })).toBeDisabled();
  });

  it('keeps the output folder unchanged when browse returns nothing', async () => {
    apiMocks.chooseOutputFolder.mockResolvedValue('');

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('button', { name: 'Browse' }));

    await waitFor(() => expect(apiMocks.chooseOutputFolder).toHaveBeenCalledOnce());
    expect(screen.getByDisplayValue('C:\\Output')).toBeInTheDocument();
  });

  it('shows overlay url and output folder tooltip inside settings', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));

    expect(screen.getByDisplayValue('C:\\Output')).toHaveAttribute('title', 'C:\\Output');
    expect(screen.queryByText('Detected text')).not.toBeInTheDocument();
    expect(screen.queryByText('Output folder')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));
    expect(screen.getAllByText('http://127.0.0.1:17655/overlay').length).toBeGreaterThan(0);
  });

  it('shows disabled when the overlay url is empty', async () => {
    apiMocks.getState.mockResolvedValue(createState({ overlayUrl: '' }));

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));

    expect(screen.getByText('Disabled')).toBeInTheDocument();
  });

  it('saves browser media settings and shows browser debug details', async () => {
    apiMocks.getState.mockResolvedValue(createState({
      browserDebug: {
        sessions: [{
          provider: 'browser',
          browser: 'chrome',
          site: 'youtube',
          playbackState: 'playing',
          sourceAppId: 'chrome.exe youtube',
          rawTitle: 'Artist - Track',
          rawArtist: '',
          rawAlbum: '',
          parsedTitle: 'Track',
          parsedArtist: 'Artist',
          parsedAlbum: '',
          confidence: 0.75,
          hasArtwork: false,
          isSelected: true,
          decisionReason: 'selected: highest priority active source',
          sessionId: 'browser-1',
          lastUpdatedUtc: '2026-05-27T15:00:00Z',
        }],
      },
    }));

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Browser' }));

    expect(screen.getAllByText('selected: highest priority active source').length).toBeGreaterThan(0);
    fireEvent.change(screen.getByDisplayValue('5000'), {
      target: { value: '2500' },
    });
    fireEvent.change(screen.getByDisplayValue('30'), {
      target: { value: '45' },
    });
    fireEvent.change(screen.getByRole('combobox', { name: 'Active source mode' }), {
      target: { value: 'browser' },
    });
    fireEvent.click(screen.getByLabelText('Enable browser media support'));
    fireEvent.click(screen.getByLabelText('Use browser video image when album art is unavailable'));
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => expect(apiMocks.saveSettings).toHaveBeenCalledWith(expect.objectContaining({
      browserSettings: expect.objectContaining({
        enabled: false,
        activeSourceMode: 'browser',
        sourceSwitchCooldownMs: 2500,
        staleSessionAfterSeconds: 45,
        youTubeVideoImageFallbackEnabled: false,
      }),
    })));
  });

  it('shows the empty browser debug state and can reveal raw browser metadata', async () => {
    apiMocks.getState.mockResolvedValueOnce(createState()).mockResolvedValue(createState({
      browserDebug: {
        sessions: [{
          provider: 'browser',
          browser: 'firefox',
          site: 'generic',
          playbackState: 'playing',
          sourceAppId: '308046B0AF4A39CB',
          rawTitle: 'Artist - Track',
          rawArtist: '',
          rawAlbum: '',
          parsedTitle: 'Track',
          parsedArtist: 'Artist',
          parsedAlbum: '',
          confidence: 0.72,
          hasArtwork: false,
          isSelected: false,
          decisionReason: 'ignored: not selected',
          sessionId: 'browser-raw',
          lastUpdatedUtc: '2026-05-27T15:00:00Z',
        }],
      },
    }));

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Browser' }));

    expect(screen.getByText('No browser sessions detected yet.')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Show raw browser metadata'));

    await waitFor(() => expect(screen.getByText(/Raw: n\/a \| Artist - Track \| n\/a/)).toBeInTheDocument());
  });

  it('saves the remaining browser toggle controls', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Browser' }));

    fireEvent.click(screen.getByLabelText('Chrome'));
    fireEvent.click(screen.getByLabelText('Edge'));
    fireEvent.click(screen.getByLabelText('Firefox'));
    fireEvent.click(screen.getByLabelText('Brave'));
    fireEvent.click(screen.getByLabelText('Opera'));
    fireEvent.click(screen.getByLabelText('Prefer TIDAL over browser playback'));
    fireEvent.click(screen.getByLabelText('Allow generic browser playback'));
    fireEvent.click(screen.getByLabelText('Enable metadata cleanup/parsing'));
    fireEvent.click(screen.getByLabelText('Enable browser artwork retrieval'));
    fireEvent.click(screen.getByLabelText('Ignore paused sessions'));
    fireEvent.click(screen.getByLabelText('Ignore stale sessions'));
    fireEvent.click(screen.getByLabelText('Enable browser detection logging'));
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => expect(apiMocks.saveSettings).toHaveBeenCalledWith(expect.objectContaining({
      browserSettings: expect.objectContaining({
        supportedBrowsers: expect.objectContaining({
          chromeEnabled: false,
          edgeEnabled: false,
          firefoxEnabled: false,
          braveEnabled: false,
          operaEnabled: true,
        }),
        preferTidalOverBrowser: false,
        allowGenericPlayback: false,
        metadataCleanupEnabled: false,
        browserArtworkEnabled: false,
        ignorePausedSessions: false,
        ignoreStaleSessions: false,
        debugLoggingEnabled: true,
      }),
    })));
  });

  it('uses the sample preview when no live playback is available', async () => {
    apiMocks.getState.mockResolvedValue(createState({
      nowPlaying: {
        ...createState().nowPlaying,
        title: '',
        artist: '',
        album: '',
        artworkPath: '',
      },
    }));

    render(<App />);

    await screen.findByText('Waiting for playback');
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Overlay' }));

    expect(screen.getByText('Sample Song')).toBeInTheDocument();
    expect(screen.getByText('Sample Artist')).toBeInTheDocument();
    expect(screen.getByText('Sample Album')).toBeInTheDocument();
  });

  it('falls back to text when the artwork image fails to load', async () => {
    render(<App />);

    const image = await screen.findByRole('img', { name: 'Sample Track cover art' });
    fireEvent.error(image);

    expect(await screen.findByText('TIDAL')).toBeInTheDocument();
  });

  it('saves the remaining toggle settings', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    fireEvent.click(screen.getByLabelText('Enable window title fallback'));
    fireEvent.click(screen.getByLabelText('Launch at Windows startup'));
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => expect(apiMocks.saveSettings).toHaveBeenCalledWith(expect.objectContaining({
      enableWindowTitleFallback: false,
      launchAtStartup: true,
    })));
  });

  it('ignores late state refreshes after unmount', async () => {
    const deferred = createDeferred<AppState>();
    apiMocks.getState.mockReturnValue(deferred.promise);

    const { unmount } = render(<App />);
    unmount();

    deferred.resolve(createState());

    await expect(deferred.promise).resolves.toEqual(createState());
  });
});
