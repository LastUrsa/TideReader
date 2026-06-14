using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public sealed class OverlaySettingsSnapshotStore : IOverlaySettingsSnapshotStore
{
    private readonly Lock _lock = new();
    private OverlaySettings _settings = Clone(new OverlaySettings());

    public void Update(OverlaySettings settings)
    {
        lock (_lock)
        {
            _settings = Clone(settings);
        }
    }

    public OverlaySettings Get()
    {
        lock (_lock)
        {
            return Clone(_settings);
        }
    }

    private static OverlaySettings Clone(OverlaySettings settings) => new()
    {
        SongTextStyle = Clone(settings.SongTextStyle),
        ArtistTextStyle = Clone(settings.ArtistTextStyle),
        AlbumTextStyle = Clone(settings.AlbumTextStyle),
        ImageSizePx = settings.ImageSizePx,
        BackgroundColorHex = settings.BackgroundColorHex,
        OverlayContainerStyle = Clone(settings.OverlayContainerStyle),
        StatusPillStyle = Clone(settings.StatusPillStyle),
        ImagePosition = settings.ImagePosition,
        TextAlign = settings.TextAlign,
        ShowAppName = settings.ShowAppName,
        ShowPlaybackState = settings.ShowPlaybackState
    };

    private static OverlayTextStyle Clone(OverlayTextStyle style) => new()
    {
        FontFamily = style.FontFamily,
        ColorHex = style.ColorHex,
        FontSizePx = style.FontSizePx,
        MaxCharacters = style.MaxCharacters,
        TextOverflowMode = style.TextOverflowMode,
        Bold = style.Bold,
        Italic = style.Italic,
        Underline = style.Underline
    };

    private static OverlayContainerStyle Clone(OverlayContainerStyle style) => new()
    {
        BackgroundMode = style.BackgroundMode,
        BackgroundColorHex = style.BackgroundColorHex,
        Gradient = Clone(style.Gradient),
        Opacity = style.Opacity,
        CornerRadiusPx = style.CornerRadiusPx,
        PaddingPx = style.PaddingPx,
        GapPx = style.GapPx,
        BorderEnabled = style.BorderEnabled,
        BorderColorHex = style.BorderColorHex,
        BorderWidthPx = style.BorderWidthPx
    };

    private static GradientSettings Clone(GradientSettings settings) => new()
    {
        ColorCount = settings.ColorCount,
        Preset = settings.Preset,
        Color1Hex = settings.Color1Hex,
        Color2Hex = settings.Color2Hex,
        Color3Hex = settings.Color3Hex,
        AngleDeg = settings.AngleDeg
    };

    private static StatusPillStyle Clone(StatusPillStyle style) => new()
    {
        BackgroundColorHex = style.BackgroundColorHex,
        TextColorHex = style.TextColorHex,
        Opacity = style.Opacity,
        FontFamily = style.FontFamily,
        FontSizePx = style.FontSizePx,
        Bold = style.Bold,
        Italic = style.Italic,
        Underline = style.Underline,
        CornerRadiusPx = style.CornerRadiusPx,
        PaddingHorizontalPx = style.PaddingHorizontalPx,
        PaddingVerticalPx = style.PaddingVerticalPx
    };
}
