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
    },
    outputFolder: 'C:\\Output',
    appVersion: '0.2.0',
    artworkRevision: 12,
    overlayUrl: 'http://127.0.0.1:17655/overlay',
    logPath: 'C:\\Logs\\bridge.log',
    lastError: '',
    manualInput: '',
    startupReady: true,
    statusMessage: 'Ready',
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
      currentVersion: '0.2.0',
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
    expect(screen.getByText('0.2.0')).toBeInTheDocument();

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
