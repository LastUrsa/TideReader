using System.Drawing.Text;

namespace TideReader.Backend.Services;

public sealed class WindowsSystemFontCatalog : ISystemFontCatalog
{
    public IReadOnlyList<string> GetFontFamilies()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families
            .Select(family => family.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
