import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import NowPlayingOverlayView from './NowPlayingOverlayView';
import type { DetectionResult, OverlaySettings } from './api';
import {
  cloneOverlaySettings,
  createDefaultSettings,
  defaultOverlaySettings,
  formatPlaybackStatus,
  getGradientPresetOptions,
  getOverlayContainerBackground,
  gradientSettingsHaveErrors,
  isGradientAngleValid,
  isGradientColorCount,
  isGradientPreset,
  isOpacityValid,
  isOverlayBackgroundMode,
  isPositiveNumber,
  isTextOverflowMode,
  isValidHexColor,
  isZeroOrPositiveNumber,
  overlayContainerStyleHasErrors,
  overlaySettingsHaveErrors,
  overlayTextStyleHasErrors,
  statusPillStyleHasErrors,
  textAlignToCss,
  truncateOverlayText,
  withAlpha,
  getArtistDisplayText,
  getAlbumDisplayText,
  isMetadataLimitedBrowserSession,
  shouldHideArtworkFallback,
} from './overlay';

function createOverlaySettings(overrides?: Partial<OverlaySettings>): OverlaySettings {
  return {
    ...cloneOverlaySettings(defaultOverlaySettings),
    ...overrides,
  };
}

function createNowPlaying(overrides?: Partial<DetectionResult>): DetectionResult {
  return {
    status: 'playing',
    title: 'Sample Song',
    artist: 'Sample Artist',
    album: 'Sample Album',
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
    ...overrides,
  };
}

describe('overlay helpers', () => {
  it('clones overlay settings deeply', () => {
    const cloned = cloneOverlaySettings(defaultOverlaySettings);
    cloned.overlayContainerStyle.gradient.color1Hex = '#000000';

    expect(defaultOverlaySettings.overlayContainerStyle.gradient.color1Hex).toBe('#1F1F2E');
  });

  it('validates common primitives and choices', () => {
    expect(isValidHexColor('#ABC')).toBe(true);
    expect(isValidHexColor('#AABBCC')).toBe(true);
    expect(isValidHexColor('AABBCC')).toBe(false);
    expect(isPositiveNumber(1)).toBe(true);
    expect(isPositiveNumber(0)).toBe(false);
    expect(isZeroOrPositiveNumber(0)).toBe(true);
    expect(isZeroOrPositiveNumber(-1)).toBe(false);
    expect(isOpacityValid(0.5)).toBe(true);
    expect(isOpacityValid(2)).toBe(false);
    expect(isGradientAngleValid(360)).toBe(true);
    expect(isGradientAngleValid(361)).toBe(false);
    expect(isOverlayBackgroundMode('solid')).toBe(true);
    expect(isOverlayBackgroundMode('noise')).toBe(false);
    expect(isGradientPreset('Diagonal')).toBe(true);
    expect(isGradientPreset('Unknown')).toBe(false);
    expect(isGradientColorCount(2)).toBe(true);
    expect(isGradientColorCount(4)).toBe(false);
    expect(isTextOverflowMode('Scroll')).toBe(true);
    expect(isTextOverflowMode('Fade')).toBe(false);
  });

  it('formats alignment, status, truncation, and alpha colors', () => {
    expect(textAlignToCss('Left')).toBe('left');
    expect(textAlignToCss('Center')).toBe('center');
    expect(textAlignToCss('Right')).toBe('right');
    expect(formatPlaybackStatus('not_running')).toBe('Not running');
    expect(truncateOverlayText('abcdef', 3, 'fallback')).toBe('abc...');
    expect(truncateOverlayText('', 3, 'fallback')).toBe('fal...');
    expect(truncateOverlayText('abc', 0, 'fallback')).toBe('abc');
    expect(withAlpha('#ABC', 0.5)).toBe('rgba(170, 187, 204, 0.5)');
    expect(withAlpha('bad', 0.5)).toBe('bad');
  });

  it('validates overlay style objects', () => {
    const badTextStyle = {
      ...defaultOverlaySettings.songTextStyle,
      colorHex: 'nope',
      fontSizePx: 0,
      fontFamily: '',
      maxCharacters: -1,
      textOverflowMode: 'Fade' as never,
    };
    const badContainerStyle = {
      ...defaultOverlaySettings.overlayContainerStyle,
      backgroundMode: 'gradient' as const,
      gradient: {
        ...defaultOverlaySettings.overlayContainerStyle.gradient,
        colorCount: 3 as const,
        color3Hex: 'oops',
      },
      opacity: 9,
    };
    const badPillStyle = {
      ...defaultOverlaySettings.statusPillStyle,
      textColorHex: 'oops',
      opacity: -1,
      fontSizePx: 0,
      fontFamily: '',
    };

    expect(overlayTextStyleHasErrors(badTextStyle)).toBe(true);
    expect(overlayContainerStyleHasErrors(badContainerStyle)).toBe(true);
    expect(gradientSettingsHaveErrors(badContainerStyle.gradient)).toBe(true);
    expect(statusPillStyleHasErrors(badPillStyle)).toBe(true);

    const badSettings = {
      ...createDefaultSettings(),
      outputFolder: '',
      overlayEnabled: true,
      overlayPort: 17655,
      pollIntervalMs: 1000,
      enableWindowTitleFallback: true,
      enableDebugManualInput: false,
      startMinimized: false,
      launchAtStartup: false,
      metadataProviderMode: 'MusicBrainzWithFallbacks' as const,
      themeMode: 'Dark' as const,
      overlaySettings: {
        ...createOverlaySettings(),
        imageSizePx: 0,
      },
      browserSettings: {
        enabled: true,
        activeSourceMode: 'auto' as const,
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
        deepDiagnosticLoggingEnabled: false,
        ignorePausedSessions: true,
        ignoreStaleSessions: true,
        staleSessionAfterSeconds: 30,
        showRawBrowserMetadata: false,
      },
    };

    expect(overlaySettingsHaveErrors(badSettings)).toBe(true);
  });

  it('builds solid, two-color, and three-color gradient backgrounds', () => {
    expect(getOverlayContainerBackground(defaultOverlaySettings.overlayContainerStyle)).toBe('rgba(50, 51, 79, 0.86)');

    const twoColor = getOverlayContainerBackground({
      ...defaultOverlaySettings.overlayContainerStyle,
      backgroundMode: 'gradient',
      gradient: {
        ...defaultOverlaySettings.overlayContainerStyle.gradient,
        colorCount: 2,
        preset: 'Soft Radial',
      },
    });
    expect(twoColor).toContain('radial-gradient(circle');
    expect(twoColor).toContain('100%)');

    const threeColor = getOverlayContainerBackground({
      ...defaultOverlaySettings.overlayContainerStyle,
      backgroundMode: 'gradient',
      gradient: {
        ...defaultOverlaySettings.overlayContainerStyle.gradient,
        colorCount: 3,
        preset: 'Stream Neon',
      },
    });
    expect(threeColor).toContain('linear-gradient(120deg');
    expect(threeColor).toContain('50%');

    const fallbackPreset = getOverlayContainerBackground({
      ...defaultOverlaySettings.overlayContainerStyle,
      backgroundMode: 'gradient',
      gradient: {
        ...defaultOverlaySettings.overlayContainerStyle.gradient,
        preset: 'Unknown' as never,
      },
    });
    expect(fallbackPreset).toContain('135deg');
  });

  it('builds the remaining gradient presets', () => {
    const reverseDiagonal = getOverlayContainerBackground({
      ...defaultOverlaySettings.overlayContainerStyle,
      backgroundMode: 'gradient',
      gradient: {
        ...defaultOverlaySettings.overlayContainerStyle.gradient,
        colorCount: 2,
        preset: 'Reverse Diagonal',
      },
    });
    expect(reverseDiagonal).toContain('linear-gradient(45deg');

    const spotlight = getOverlayContainerBackground({
      ...defaultOverlaySettings.overlayContainerStyle,
      backgroundMode: 'gradient',
      gradient: {
        ...defaultOverlaySettings.overlayContainerStyle.gradient,
        colorCount: 3,
        preset: 'Spotlight',
      },
    });
    expect(spotlight).toContain('circle at top left');
    expect(spotlight).toContain('45%');

    const subtleGlass = getOverlayContainerBackground({
      ...defaultOverlaySettings.overlayContainerStyle,
      backgroundMode: 'gradient',
      gradient: {
        ...defaultOverlaySettings.overlayContainerStyle.gradient,
        colorCount: 2,
        preset: 'Subtle Glass',
      },
    });
    expect(subtleGlass).toContain('linear-gradient(135deg');
  });

  it('returns the allowed preset subsets for two and three colors', () => {
    expect(getGradientPresetOptions(2)).toEqual([
      'Linear Left to Right',
      'Linear Top to Bottom',
      'Diagonal',
      'Reverse Diagonal',
      'Soft Radial',
      'Spotlight',
    ]);
    expect(getGradientPresetOptions(3)).toContain('Stream Neon');
    expect(getGradientPresetOptions(3)).toContain('Subtle Glass');
  });

  it('uses browser-aware artist and album fallback labels', () => {
    const browserNowPlaying = createNowPlaying({
      provider: 'browser',
      source: 'Bandcamp',
      site: 'bandcamp',
      title: 'lastursa collection',
      artist: '',
      album: '',
    });

    expect(getArtistDisplayText(browserNowPlaying, 'Artist unavailable')).toBe('Bandcamp');
    expect(getAlbumDisplayText(browserNowPlaying, 'Album unavailable')).toBe('Metadata limited');
    expect(isMetadataLimitedBrowserSession(browserNowPlaying)).toBe(true);
    expect(shouldHideArtworkFallback(browserNowPlaying)).toBe(true);
  });

  it('handles the remaining browser label branches', () => {
    expect(getArtistDisplayText(createNowPlaying({
      provider: 'browser',
      source: '',
      site: 'youtubeMusic',
      title: 'Track',
      artist: '',
    }), 'Artist unavailable')).toBe('YouTube Music');
    expect(getArtistDisplayText(createNowPlaying({
      provider: 'browser',
      source: '',
      site: 'youtube',
      title: 'Track',
      artist: '',
    }), 'Artist unavailable')).toBe('YouTube');
    expect(getArtistDisplayText(createNowPlaying({
      provider: 'browser',
      source: '',
      site: 'soundcloud',
      title: 'Track',
      artist: '',
    }), 'Artist unavailable')).toBe('SoundCloud');
    expect(getArtistDisplayText(createNowPlaying({
      provider: 'browser',
      source: '',
      site: 'generic',
      title: 'Track',
      artist: '',
    }), 'Artist unavailable')).toBe('Browser');

    expect(getAlbumDisplayText(createNowPlaying({
      provider: 'browser',
      site: 'youtubeMusic',
      title: 'Track',
      album: '',
    }), 'Album unavailable')).toBe('Music playback');
    expect(getAlbumDisplayText(createNowPlaying({
      provider: 'browser',
      site: 'youtube',
      title: 'Track',
      album: '',
    }), 'Album unavailable')).toBe('Video playback');
    expect(getAlbumDisplayText(createNowPlaying({
      provider: 'browser',
      site: 'soundcloud',
      title: 'Track',
      album: '',
    }), 'Album unavailable')).toBe('Stream playback');
    expect(getAlbumDisplayText(createNowPlaying({
      provider: 'browser',
      site: 'bandcamp',
      title: 'Track',
      album: 'Album',
    }), 'Album unavailable')).toBe('Album');
    expect(shouldHideArtworkFallback(createNowPlaying({
      provider: 'browser',
      site: 'youtube',
      title: 'Track',
      album: '',
    }))).toBe(false);
  });
});

describe('NowPlayingOverlayView', () => {
  it('renders placeholder copy and hides the topline when disabled', () => {
    const settings = createOverlaySettings({
      showAppName: false,
      showPlaybackState: false,
      textAlign: 'Right',
      imagePosition: 'Right',
    });

    render(
      <NowPlayingOverlayView
        overlaySettings={settings}
        nowPlaying={createNowPlaying({ status: 'not_running', title: '', artist: '', album: '' })}
        artworkAlt="placeholder cover art"
        fallbackMode="app"
      />,
    );

    expect(screen.getByText('Waiting for playback')).toBeInTheDocument();
    expect(screen.getByText('Artist unavailable')).toBeInTheDocument();
    expect(screen.getByText('Album unavailable')).toBeInTheDocument();
    expect(screen.queryByText('Idle')).not.toBeInTheDocument();
    expect(screen.queryByText('TideReader')).not.toBeInTheDocument();
    expect(screen.queryByText('Not running')).not.toBeInTheDocument();
  });

  it('shows browser fallback labels when metadata is missing for generic browser playback', () => {
    render(
      <NowPlayingOverlayView
        overlaySettings={createOverlaySettings()}
        nowPlaying={createNowPlaying({ provider: 'browser', source: 'Browser', site: 'generic', artist: '', album: '', title: 'Video Title' })}
        artworkAlt="youtube cover art"
        fallbackMode="app"
      />,
    );

    expect(screen.getAllByText('Browser').length).toBeGreaterThan(0);
    expect(screen.getByText('Browser playback')).toBeInTheDocument();
  });

  it('hides the artwork block for metadata-limited browser sessions with no artwork', () => {
    render(
      <NowPlayingOverlayView
        overlaySettings={createOverlaySettings()}
        nowPlaying={createNowPlaying({
          provider: 'browser',
          source: 'Bandcamp',
          site: 'bandcamp',
          title: "lastursa's collection | Bandcamp",
          artist: '',
          album: '',
        })}
        artworkAlt="bandcamp cover art"
        fallbackMode="app"
      />,
    );

    expect(screen.queryByText('Bandcamp')).toBeInTheDocument();
    expect(screen.queryByText('Metadata limited')).toBeInTheDocument();
    expect(screen.queryByText('ART')).not.toBeInTheDocument();
  });

  it('renders artwork, brand, status, and styled text when live data exists', () => {
    const settings = createOverlaySettings({
      overlayContainerStyle: {
        ...defaultOverlaySettings.overlayContainerStyle,
        backgroundMode: 'gradient',
        gradient: {
          ...defaultOverlaySettings.overlayContainerStyle.gradient,
          colorCount: 2,
          preset: 'Linear Left to Right',
        },
      },
    });
    const onArtworkError = vi.fn();

    render(
      <NowPlayingOverlayView
        overlaySettings={settings}
        nowPlaying={createNowPlaying()}
        artworkUrl="/cover.jpg"
        artworkAlt="Sample Song cover art"
        fallbackMode="preview"
        onArtworkError={onArtworkError}
      />,
    );

    expect(screen.getByText('TideReader')).toBeInTheDocument();
    expect(screen.getByText('Playing')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Sample Song cover art' })).toHaveAttribute('src', '/cover.jpg');
    expect(screen.getByText('Sample Song')).toHaveStyle({ fontWeight: '700' });
    expect(screen.getByText('Sample Song').closest('.smart-text')).toHaveAttribute('data-overflow-mode', 'Default');
  });

  it('marks smart text modes for rendering', () => {
    const settings = createOverlaySettings({
      songTextStyle: {
        ...defaultOverlaySettings.songTextStyle,
        textOverflowMode: 'Scroll',
      },
      artistTextStyle: {
        ...defaultOverlaySettings.artistTextStyle,
        textOverflowMode: 'TwoLines',
      },
      albumTextStyle: {
        ...defaultOverlaySettings.albumTextStyle,
        textOverflowMode: 'AutoSize',
      },
    });

    render(
      <NowPlayingOverlayView
        overlaySettings={settings}
        nowPlaying={createNowPlaying({ title: 'A Very Long Sample Song Title', artist: 'A Very Long Artist Name', album: 'A Very Long Album Name' })}
        artworkAlt="Sample Song cover art"
        fallbackMode="preview"
      />,
    );

    expect(screen.getAllByText('A Very Long Sample Song Title')[0].closest('.smart-text')).toHaveAttribute('data-overflow-mode', 'Scroll');
    expect(screen.getAllByText('A Very Long Artist Name')[0].closest('.smart-text')).toHaveAttribute('data-overflow-mode', 'TwoLines');
    expect(screen.getAllByText('A Very Long Album Name')[0].closest('.smart-text')).toHaveAttribute('data-overflow-mode', 'AutoSize');
  });
});
