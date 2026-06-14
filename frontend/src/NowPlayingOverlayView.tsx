import { useLayoutEffect, useRef, useState } from 'react';
import type { CSSProperties, ElementType } from 'react';
import type { DetectionResult, OverlaySettings } from './api';
import { formatPlaybackStatus, getAlbumDisplayText, getArtistDisplayText, getOverlayContainerBackground, truncateOverlayText, withAlpha } from './overlay';

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

type SmartOverlayTextProps = {
  as: ElementType;
  className: string;
  style: OverlaySettings['songTextStyle'];
  children: string;
};

function SmartOverlayText({ as: Component, className, style, children }: SmartOverlayTextProps) {
  const containerRef = useRef<HTMLElement | null>(null);
  const measureRef = useRef<HTMLSpanElement | null>(null);
  const [isOverflowing, setIsOverflowing] = useState(false);
  const [autoSizePx, setAutoSizePx] = useState(style.fontSizePx);
  const mode = style.textOverflowMode ?? 'Default';

  useLayoutEffect(() => {
    const container = containerRef.current;
    const measure = measureRef.current;
    if (!container || !measure || mode === 'Default' || mode === 'TwoLines') {
      setIsOverflowing(false);
      setAutoSizePx(style.fontSizePx);
      return undefined;
    }

    const update = () => {
      const availableWidth = container.clientWidth;
      if (availableWidth <= 0) {
        setIsOverflowing(false);
        setAutoSizePx(style.fontSizePx);
        return;
      }

      measure.style.fontSize = `${style.fontSizePx}px`;
      const fullWidth = measure.scrollWidth;
      const overflowing = fullWidth > availableWidth + 1;
      setIsOverflowing(overflowing);

      if (mode === 'AutoSize' && overflowing) {
        const minimumSize = Math.max(1, Math.round(style.fontSizePx * 0.6));
        const fittedSize = Math.max(minimumSize, Math.floor((availableWidth / fullWidth) * style.fontSizePx));
        setAutoSizePx(fittedSize);
      } else {
        setAutoSizePx(style.fontSizePx);
      }
    };

    update();
    if (typeof ResizeObserver === 'undefined') {
      return undefined;
    }

    const resizeObserver = new ResizeObserver(update);
    resizeObserver.observe(container);
    return () => resizeObserver.disconnect();
  }, [children, mode, style.fontFamily, style.fontSizePx, style.bold, style.italic, style.underline]);

  const cssStyle = textStyleToCss(style);
  const smartStyle = {
    ...cssStyle,
    ...(mode === 'AutoSize' ? { fontSize: `${autoSizePx}px` } : {}),
  };
  const shouldScroll = mode === 'Scroll' && isOverflowing;

  if (mode === 'Default') {
    return (
      <Component
        className={`${className} smart-text smart-text-default`.trim()}
        style={cssStyle}
        data-overflow-mode={mode}
      >
        {children}
      </Component>
    );
  }

  return (
    <Component
      ref={containerRef}
      className={`${className} smart-text smart-text-${mode.toLowerCase()} ${shouldScroll ? 'is-scrolling' : ''}`.trim()}
      style={smartStyle}
      data-overflow-mode={mode}
    >
      <span className="smart-text-measure" ref={measureRef} aria-hidden="true">{children}</span>
      {shouldScroll ? (
        <span className="smart-text-scroll-track">
          <span>{children}</span>
          <span aria-hidden="true">{children}</span>
        </span>
      ) : (
        <span className="smart-text-content">{children}</span>
      )}
    </Component>
  );
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
    ? (nowPlaying.title ? nowPlaying.source || 'Browser' : 'Idle')
    : 'ART';
  const hideArtworkFallback = !hasArtwork && fallbackMode === 'app';

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
      {!hideArtworkFallback ? (
        <div className={`np-overlay-art ${hasArtwork ? 'has-artwork' : ''}`} style={{ borderRadius: 0 }}>
          {hasArtwork ? (
            <img className="np-cover-image" src={artworkUrl} alt={artworkAlt} style={{ borderRadius: 0 }} onError={onArtworkError} />
          ) : (
            <span>{artworkFallbackLabel}</span>
          )}
        </div>
      ) : null}
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
        <SmartOverlayText as="h1" className="np-overlay-title" style={overlaySettings.songTextStyle}>
          {truncateOverlayText(nowPlaying.title, overlaySettings.songTextStyle.maxCharacters, titleFallback)}
        </SmartOverlayText>
        <SmartOverlayText as="p" className="np-artist-line" style={overlaySettings.artistTextStyle}>
          {truncateOverlayText(getArtistDisplayText(nowPlaying, artistFallback), overlaySettings.artistTextStyle.maxCharacters, artistFallback)}
        </SmartOverlayText>
        <SmartOverlayText as="p" className="np-album-line" style={overlaySettings.albumTextStyle}>
          {truncateOverlayText(getAlbumDisplayText(nowPlaying, albumFallback), overlaySettings.albumTextStyle.maxCharacters, albumFallback)}
        </SmartOverlayText>
      </div>
    </section>
  );
}
