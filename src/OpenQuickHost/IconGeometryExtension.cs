using System;
using System.Windows.Markup;
using System.Windows.Media;

namespace OpenQuickHost;

[MarkupExtensionReturnType(typeof(Geometry))]
public sealed class IconGeometryExtension : MarkupExtension
{
    public IconGeometryExtension()
    {
    }

    public IconGeometryExtension(string reference)
    {
        Reference = reference;
    }

    public string? Reference { get; set; }

    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Reference))
        {
            return null;
        }

        var normalizedReference = Reference!.Contains(':', StringComparison.Ordinal)
            ? Reference
            : $"mdi:{Reference}";
        return ExtensionIconLibrary.ResolveVectorIcon(normalizedReference);
    }
}
