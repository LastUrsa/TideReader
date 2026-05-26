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
        Bold = style.Bold,
        Italic = style.Italic,
        Underline = style.Underline
    };
}
