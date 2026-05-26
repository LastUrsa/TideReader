import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { AppState } from './api';

const apiMocks = vi.hoisted(() => ({
  chooseOutputFolder: vi.fn(),
  getState: vi.fn(),
  openOutputFolder: vi.fn(),
  runDetectionNow: vi.fn(),
  saveSettings: vi.fn(),
  getArtworkUrl: vi.fn(),
}));

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api');
  return {
    ...actual,
    chooseOutputFolder: apiMocks.chooseOutputFolder,
    getState: apiMocks.getState,
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
    apiMocks.getState.mockReset();
    apiMocks.openOutputFolder.mockReset();
    apiMocks.runDetectionNow.mockReset();
    apiMocks.saveSettings.mockReset();
    apiMocks.getArtworkUrl.mockReset();
    apiMocks.getArtworkUrl.mockReturnValue('/api/artwork?v=12');
    apiMocks.getState.mockResolvedValue(createState());
    apiMocks.runDetectionNow.mockResolvedValue(createState({ statusMessage: 'Refreshed' }));
    apiMocks.openOutputFolder.mockResolvedValue(undefined);
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

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
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

  it('saves edited settings through the backend', async () => {
    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));

    fireEvent.change(screen.getByDisplayValue('C:\\Output'), {
      target: { value: 'D:\\OBS' },
    });
    fireEvent.change(screen.getByDisplayValue('17655'), {
      target: { value: '19000' },
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
    fireEvent.click(screen.getByLabelText('Enable local overlay'));
    fireEvent.click(screen.getByLabelText('Start minimized'));
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

    expect(screen.getByText('http://127.0.0.1:17655/overlay')).toBeInTheDocument();
    expect(screen.getByDisplayValue('C:\\Output')).toHaveAttribute('title', 'C:\\Output');
    expect(screen.queryByText('Detected text')).not.toBeInTheDocument();
    expect(screen.queryByText('Output folder')).toBeInTheDocument();
  });

  it('shows disabled when the overlay url is empty', async () => {
    apiMocks.getState.mockResolvedValue(createState({ overlayUrl: '' }));

    render(<App />);

    await screen.findByRole('heading', { name: 'Sample Track' });
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));

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
