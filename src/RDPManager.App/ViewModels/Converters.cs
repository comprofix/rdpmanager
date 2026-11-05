using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RDPManager.App.ViewModels;

/// <summary>Small value converters used by the views.</summary>
public static class Converters
{
    /// <summary>True → semi-bold, false → normal (source rows in the tree are semi-bold).</summary>
    public static readonly IValueConverter BoolToSemiBold =
        new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);
}
