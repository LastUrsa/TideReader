import type { CSSProperties } from 'react';
import type { DetectionResult, OverlaySettings } from './api';
import { formatPlaybackStatus, getOverlayContainerBackground, truncateOverlayText, withAlpha } from './overlay';

type NowPlayingOverlayViewProps = {
  overlaySettings: OverlaySettings;
  nowPlaying: DetectionResult;
  artworkUrl?: string;
  artworkAlt: string;
  fallbackMode?: 'app' | 'preview';
  className?: string;
  onArtworkError?: () => void;
};

function textStyleToCss(style: OverlaySettings['songTextStyle']): CSSProperties {
  return {
    fontFamily: style.fontFamily,
    color: style.colorHex,
    fontSize: `${style.fontSizePx}px`,
    fontWeight: style.bold ? '700' : '400',
    fontStyle: style.italic ? 'italic' : 'normal',
    textDecoration: style.underline ? 'underline' : 'none',
  };
}

export default function NowPlayingOverlayView({
  overlaySettings,
  nowPlaying,
  artworkUrl = '',
  artworkAlt,
  fallbackMode = 'preview',
  className = '',
  onArtworkError,
}: NowPlayingOverlayViewProps) {
  const hasArtwork = Boolean(artworkUrl);
  const status = nowPlaying.status || 'not_running';
  const containerStyle = overlaySettings.overlayContainerStyle;
  const pillStyle = overlaySettings.statusPillStyle;
  const topLineVisible = overlaySettings.showAppName || overlaySettings.showPlaybackState;
  const titleFallback = fallbackMode === 'app' ? 'Waiting for playback' : 'Sample Song';
  const artistFallback = fallbackMode === 'app' ? 'Artist unavailable' : 'Sample Artist';
  const albumFallback = fallbackMode === 'app' ? 'Album unavailable' : 'Sample Album';
  const artworkFallbackLabel = fallbackMode === 'app'
    ? (nowPlaying.title ? 'TIDAL' : 'Idle')
    : 'ART';

  return (
    <section
      className={`now-playing-overlay ${className}`.trim()}
      style={{
        '--overlay-gap': `${containerStyle.gapPx}px`,
        '--overlay-padding': `${containerStyle.paddingPx}px`,
        '--overlay-radius': `${containerStyle.cornerRadiusPx}px`,
        '--overlay-border-width': containerStyle.borderEnabled ? `${containerStyle.borderWidthPx}px` : '0px',
        '--overlay-border-color': withAlpha(containerStyle.borderColorHex, 1),
        '--overlay-bg': getOverlayContainerBackground(containerStyle),
        '--overlay-image-size': `${overlaySettings.imageSizePx}px`,
        '--overlay-pill-radius': `${pillStyle.cornerRadiusPx}px`,
        '--overlay-pill-padding-x': `${pillStyle.paddingHorizontalPx}px`,
        '--overlay-pill-padding-y': `${pillStyle.paddingVerticalPx}px`,
        '--overlay-pill-bg': withAlpha(pillStyle.backgroundColorHex, pillStyle.opacity),
        '--overlay-pill-text': pillStyle.textColorHex,
        '--overlay-pill-font': pillStyle.fontFamily,
        '--overlay-pill-size': `${pillStyle.fontSizePx}px`,
      } as CSSProperties}
      data-image-position={overlaySettings.imagePosition.toLowerCase()}
      data-text-align={overlaySettings.textAlign.toLowerCase()}
    >
      <div className={`np-overlay-art ${hasArtwork ? 'has-artwork' : ''}`} style={{ borderRadius: 0 }}>
        {hasArtwork ? (
          <img className="np-cover-image" src={artworkUrl} alt={artworkAlt} style={{ borderRadius: 0 }} onError={onArtworkError} />
        ) : (
          <span>{artworkFallbackLabel}</span>
        )}
      </div>
      <div className="np-overlay-copy">
        {topLineVisible ? (
          <div className="np-overlay-topline">
            {overlaySettings.showAppName ? <div className="np-overlay-brand">TideReader</div> : null}
            {overlaySettings.showPlaybackState ? (
              <div
                className={`np-status-pill ${status}`}
                style={{
                  fontFamily: pillStyle.fontFamily,
                  fontSize: `${pillStyle.fontSizePx}px`,
                  fontWeight: pillStyle.bold ? '700' : '400',
                  fontStyle: pillStyle.italic ? 'italic' : 'normal',
                  textDecoration: pillStyle.underline ? 'underline' : 'none',
                }}
              >
                {formatPlaybackStatus(status)}
              </div>
            ) : null}
          </div>
        ) : null}
        <h1 className="np-overlay-title" style={textStyleToCss(overlaySettings.songTextStyle)}>
          {truncateOverlayText(nowPlaying.title, overlaySettings.songTextStyle.maxCharacters, titleFallback)}
        </h1>
        <p className="np-artist-line" style={textStyleToCss(overlaySettings.artistTextStyle)}>
          {truncateOverlayText(nowPlaying.artist, overlaySettings.artistTextStyle.maxCharacters, artistFallback)}
        </p>
        <p className="np-album-line" style={textStyleToCss(overlaySettings.albumTextStyle)}>
          {truncateOverlayText(nowPlaying.album, overlaySettings.albumTextStyle.maxCharacters, albumFallback)}
        </p>
      </div>
    </section>
  );
}
