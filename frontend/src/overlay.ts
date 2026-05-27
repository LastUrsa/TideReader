import type { DetectionResult, GradientSettings, OverlayContainerStyle, OverlaySettings, OverlayTextStyle, Settings, StatusPillStyle } from './api';

export const hexColorPattern = /^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/;
export const gradientPresetOptions = [
  'Linear Left to Right',
  'Linear Top to Bottom',
  'Diagonal',
  'Reverse Diagonal',
  'Soft Radial',
  'Spotlight',
  'Stream Neon',
  'Subtle Glass',
] as const;

export const gradientPresetsByColorCount: Record<2 | 3, readonly GradientSettings['preset'][]> = {
  2: [
    'Linear Left to Right',
    'Linear Top to Bottom',
    'Diagonal',
    'Reverse Diagonal',
    'Soft Radial',
    'Spotlight',
  ],
  3: gradientPresetOptions,
};

export const overlayBackgroundModeOptions = ['solid', 'gradient'] as const;
export const gradientColorCountOptions = [2, 3] as const;

export const defaultOverlaySettings: OverlaySettings = {
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
};

export const sampleNowPlaying: DetectionResult = {
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
};

export function createDefaultSettings(): Settings {
  return {
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
    overlaySettings: cloneOverlaySettings(defaultOverlaySettings),
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
}

export function cloneOverlaySettings(settings: OverlaySettings): OverlaySettings {
  return {
    ...settings,
    songTextStyle: { ...settings.songTextStyle },
    artistTextStyle: { ...settings.artistTextStyle },
    albumTextStyle: { ...settings.albumTextStyle },
    overlayContainerStyle: {
      ...settings.overlayContainerStyle,
      gradient: { ...defaultOverlaySettings.overlayContainerStyle.gradient, ...settings.overlayContainerStyle.gradient },
    },
    statusPillStyle: { ...settings.statusPillStyle },
  };
}

export function isValidHexColor(value: string): boolean {
  return hexColorPattern.test(value.trim());
}

export function isPositiveNumber(value: number): boolean {
  return Number.isFinite(value) && value > 0;
}

export function isZeroOrPositiveNumber(value: number): boolean {
  return Number.isFinite(value) && value >= 0;
}

export function isOpacityValid(value: number): boolean {
  return Number.isFinite(value) && value >= 0 && value <= 1;
}

export function isGradientAngleValid(value: number): boolean {
  return Number.isFinite(value) && value >= 0 && value <= 360;
}

export function isOverlayBackgroundMode(value: string): value is OverlayContainerStyle['backgroundMode'] {
  return (overlayBackgroundModeOptions as readonly string[]).includes(value);
}

export function isGradientPreset(value: string): value is GradientSettings['preset'] {
  return (gradientPresetOptions as readonly string[]).includes(value);
}

export function isGradientColorCount(value: number): value is GradientSettings['colorCount'] {
  return (gradientColorCountOptions as readonly number[]).includes(value);
}

export function getGradientPresetOptions(colorCount: GradientSettings['colorCount']): readonly GradientSettings['preset'][] {
  return gradientPresetsByColorCount[colorCount];
}

export function textAlignToCss(value: Settings['overlaySettings']['textAlign']): 'left' | 'center' | 'right' {
  switch (value) {
    case 'Center':
      return 'center';
    case 'Right':
      return 'right';
    default:
      return 'left';
  }
}

export function overlayTextStyleHasErrors(style: OverlayTextStyle): boolean {
  return !isValidHexColor(style.colorHex) || !isPositiveNumber(style.fontSizePx) || !isZeroOrPositiveNumber(style.maxCharacters) || !style.fontFamily.trim();
}

export function overlayContainerStyleHasErrors(style: OverlayContainerStyle): boolean {
  const gradient = style.gradient ?? defaultOverlaySettings.overlayContainerStyle.gradient;
  return !isOverlayBackgroundMode(style.backgroundMode)
    || !isValidHexColor(style.backgroundColorHex)
    || gradientSettingsHaveErrors(gradient)
    || !isOpacityValid(style.opacity)
    || !isZeroOrPositiveNumber(style.cornerRadiusPx)
    || !isZeroOrPositiveNumber(style.paddingPx)
    || !isZeroOrPositiveNumber(style.gapPx)
    || !isValidHexColor(style.borderColorHex)
    || !isZeroOrPositiveNumber(style.borderWidthPx);
}

export function gradientSettingsHaveErrors(settings: GradientSettings): boolean {
  return !isGradientColorCount(settings.colorCount)
    || !isGradientPreset(settings.preset)
    || !isValidHexColor(settings.color1Hex)
    || !isValidHexColor(settings.color2Hex)
    || (settings.colorCount === 3 && !isValidHexColor(settings.color3Hex))
    || !isGradientAngleValid(settings.angleDeg);
}

export function statusPillStyleHasErrors(style: StatusPillStyle): boolean {
  return !isValidHexColor(style.backgroundColorHex)
    || !isValidHexColor(style.textColorHex)
    || !isOpacityValid(style.opacity)
    || !style.fontFamily.trim()
    || !isPositiveNumber(style.fontSizePx)
    || !isZeroOrPositiveNumber(style.cornerRadiusPx)
    || !isZeroOrPositiveNumber(style.paddingHorizontalPx)
    || !isZeroOrPositiveNumber(style.paddingVerticalPx);
}

export function overlaySettingsHaveErrors(settings: Settings): boolean {
  return (
    overlayTextStyleHasErrors(settings.overlaySettings.songTextStyle) ||
    overlayTextStyleHasErrors(settings.overlaySettings.artistTextStyle) ||
    overlayTextStyleHasErrors(settings.overlaySettings.albumTextStyle) ||
    !isPositiveNumber(settings.overlaySettings.imageSizePx) ||
    overlayContainerStyleHasErrors(settings.overlaySettings.overlayContainerStyle) ||
    statusPillStyleHasErrors(settings.overlaySettings.statusPillStyle)
  );
}

export function truncateOverlayText(value: string, maxCharacters: number, fallback: string): string {
  const next = String(value || fallback);
  const limit = Number(maxCharacters || 0);
  if (!Number.isFinite(limit) || limit <= 0 || next.length <= limit) {
    return next;
  }

  return `${next.slice(0, limit)}...`;
}

export function formatPlaybackStatus(status: string): string {
  const normalized = String(status || 'not_running').replaceAll('_', ' ');
  return normalized.charAt(0).toUpperCase() + normalized.slice(1);
}

function getBrowserSourceLabel(nowPlaying: DetectionResult): string {
  const source = nowPlaying.source.trim();
  if (source) {
    return source;
  }

  switch (nowPlaying.site) {
    case 'youtubeMusic':
      return 'YouTube Music';
    case 'youtube':
      return 'YouTube';
    case 'bandcamp':
      return 'Bandcamp';
    case 'soundcloud':
      return 'SoundCloud';
    default:
      return 'Browser';
  }
}

export function isMetadataLimitedBrowserSession(nowPlaying: DetectionResult): boolean {
  return nowPlaying.provider === 'browser'
    && nowPlaying.site === 'bandcamp'
    && nowPlaying.title.trim().length > 0
    && !nowPlaying.album.trim();
}

export function getArtistDisplayText(nowPlaying: DetectionResult, fallback: string): string {
  if (nowPlaying.artist.trim()) {
    return nowPlaying.artist;
  }

  if (nowPlaying.provider === 'browser' && nowPlaying.title.trim()) {
    return getBrowserSourceLabel(nowPlaying);
  }

  return fallback;
}

export function getAlbumDisplayText(nowPlaying: DetectionResult, fallback: string): string {
  if (nowPlaying.album.trim()) {
    return nowPlaying.album;
  }

  if (nowPlaying.provider === 'browser' && nowPlaying.title.trim()) {
    if (isMetadataLimitedBrowserSession(nowPlaying)) {
      return 'Metadata limited';
    }

    switch (nowPlaying.site) {
      case 'youtubeMusic':
        return 'Music playback';
      case 'youtube':
        return 'Video playback';
      case 'soundcloud':
        return 'Stream playback';
      default:
        return 'Browser playback';
    }
  }

  return fallback;
}

export function shouldHideArtworkFallback(nowPlaying: DetectionResult): boolean {
  return isMetadataLimitedBrowserSession(nowPlaying);
}

export function withAlpha(hexColor: string, opacity: number): string {
  const trimmed = hexColor.trim();
  if (!isValidHexColor(trimmed) || !isOpacityValid(opacity)) {
    return trimmed;
  }

  const normalized = trimmed.length === 4
    ? `#${trimmed[1]}${trimmed[1]}${trimmed[2]}${trimmed[2]}${trimmed[3]}${trimmed[3]}`
    : trimmed;
  const red = Number.parseInt(normalized.slice(1, 3), 16);
  const green = Number.parseInt(normalized.slice(3, 5), 16);
  const blue = Number.parseInt(normalized.slice(5, 7), 16);
  return `rgba(${red}, ${green}, ${blue}, ${opacity})`;
}

export function getOverlayContainerBackground(style: OverlayContainerStyle): string {
  if (style.backgroundMode !== 'gradient') {
    return withAlpha(style.backgroundColorHex, style.opacity);
  }

  const gradient = style.gradient ?? defaultOverlaySettings.overlayContainerStyle.gradient;
  const color1 = withAlpha(gradient.color1Hex, style.opacity);
  const color2 = withAlpha(gradient.color2Hex, style.opacity);
  const color3 = withAlpha(gradient.color3Hex, style.opacity);
  const angle = gradient.angleDeg;
  const stops = gradient.colorCount === 2
    ? [color1, color2]
    : [color1, color2, color3];
  const joinedStops = stops.join(', ');

  switch (gradient.preset) {
    case 'Linear Left to Right':
      return `linear-gradient(90deg, ${joinedStops})`;
    case 'Linear Top to Bottom':
      return `linear-gradient(180deg, ${joinedStops})`;
    case 'Reverse Diagonal':
      return `linear-gradient(45deg, ${joinedStops})`;
    case 'Soft Radial':
      return gradient.colorCount === 2
        ? `radial-gradient(circle, ${color1} 0%, ${color2} 100%)`
        : `radial-gradient(circle, ${color1} 0%, ${color2} 50%, ${color3} 100%)`;
    case 'Spotlight':
      return gradient.colorCount === 2
        ? `radial-gradient(circle at top left, ${color1} 0%, ${color2} 100%)`
        : `radial-gradient(circle at top left, ${color1} 0%, ${color2} 45%, ${color3} 100%)`;
    case 'Stream Neon':
      return gradient.colorCount === 2
        ? `linear-gradient(120deg, ${color1} 0%, ${color2} 100%)`
        : `linear-gradient(120deg, ${color1} 0%, ${color2} 50%, ${color3} 100%)`;
    case 'Subtle Glass':
      return gradient.colorCount === 2
        ? `linear-gradient(135deg, ${color1} 0%, ${color2} 100%)`
        : `linear-gradient(135deg, ${color1} 0%, ${color2} 60%, ${color3} 100%)`;
    case 'Diagonal':
      return `linear-gradient(${angle}deg, ${joinedStops})`;
    default:
      return `linear-gradient(${angle}deg, ${joinedStops})`;
  }
}
