using System.Windows;
using System.Windows.Controls;
using YitDesktopFold.Native.Models;

namespace YitDesktopFold.Native.Controls;

/// <summary>
/// Reflows shortcut tiles without scaling them. The selected organizer layout
/// owns the exact icon/tile geometry; resizing changes only row/column count.
/// </summary>
public sealed class AdaptiveIconPanel : Panel
{
    public static readonly DependencyProperty ShowLabelsProperty = DependencyProperty.Register(
        nameof(ShowLabels),
        typeof(bool),
        typeof(AdaptiveIconPanel),
        new FrameworkPropertyMetadata(
            false,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty LayoutModeProperty = DependencyProperty.Register(
        nameof(LayoutMode),
        typeof(OrganizerIconLayoutMode),
        typeof(AdaptiveIconPanel),
        new FrameworkPropertyMetadata(
            OrganizerIconLayoutMode.Medium,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty SuppressLabelsProperty = DependencyProperty.Register(
        nameof(SuppressLabels),
        typeof(bool),
        typeof(AdaptiveIconPanel),
        new FrameworkPropertyMetadata(
            false,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    private static readonly DependencyPropertyKey AreLabelsVisiblePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(AreLabelsVisible),
            typeof(bool),
            typeof(AdaptiveIconPanel),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty AreLabelsVisibleProperty =
        AreLabelsVisiblePropertyKey.DependencyProperty;

    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    public OrganizerIconLayoutMode LayoutMode
    {
        get => (OrganizerIconLayoutMode)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    public bool SuppressLabels
    {
        get => (bool)GetValue(SuppressLabelsProperty);
        set => SetValue(SuppressLabelsProperty, value);
    }

    public bool AreLabelsVisible => (bool)GetValue(AreLabelsVisibleProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = NormalizeSize(availableSize);
        var layout = CalculateLayout(size, InternalChildren.Count, LayoutMode);
        UpdateLabelVisibility();

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(layout.TileWidth, layout.TileHeight));
        }

        return new Size(size.Width, double.IsFinite(availableSize.Height)
            ? size.Height
            : layout.ExtentHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        var layout = CalculateLayout(finalSize, InternalChildren.Count, LayoutMode);
        UpdateLabelVisibility();
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var row = index / layout.Columns;
            var column = index % layout.Columns;
            var x = layout.StartX + column * (layout.TileWidth + layout.HorizontalGap);
            var y = layout.StartY + row * (layout.TileHeight + layout.VerticalGap);
            InternalChildren[index].Arrange(new Rect(x, y, layout.TileWidth, layout.TileHeight));
        }

        return finalSize;
    }

    private void UpdateLabelVisibility()
    {
        var shouldShow = !SuppressLabels &&
                         (LayoutMode is OrganizerIconLayoutMode.List || ShowLabels);
        if (shouldShow != AreLabelsVisible)
        {
            SetValue(AreLabelsVisiblePropertyKey, shouldShow);
        }
    }

    private static Layout CalculateLayout(
        Size size,
        int count,
        OrganizerIconLayoutMode mode)
    {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        var specification = GetSpecification(mode);
        var usableWidth = Math.Max(1, width - specification.Padding * 2);

        if (mode is OrganizerIconLayoutMode.List)
        {
            var tileWidth = usableWidth;
            var listContentHeight = Math.Max(1, count) * specification.TileHeight +
                                    Math.Max(0, count - 1) * specification.VerticalGap;
            var listStartY = listContentHeight <= height - specification.Padding * 2
                ? (height - listContentHeight) / 2
                : specification.Padding;
            return new Layout(
                1,
                tileWidth,
                specification.TileHeight,
                0,
                specification.VerticalGap,
                specification.Padding,
                Math.Max(specification.Padding, listStartY),
                listContentHeight + specification.Padding * 2);
        }

        var columns = Math.Max(
            1,
            (int)Math.Floor(
                (usableWidth + specification.HorizontalGap) /
                (specification.TileWidth + specification.HorizontalGap)));
        columns = Math.Min(columns, Math.Max(1, count));
        var rows = Math.Max(1, (int)Math.Ceiling((double)Math.Max(1, count) / columns));
        var contentHeight = rows * specification.TileHeight +
                            Math.Max(0, rows - 1) * specification.VerticalGap;
        var startY = contentHeight <= height - specification.Padding * 2
            ? (height - contentHeight) / 2
            : specification.Padding;

        return new Layout(
            columns,
            specification.TileWidth,
            specification.TileHeight,
            specification.HorizontalGap,
            specification.VerticalGap,
            specification.Padding,
            Math.Max(specification.Padding, startY),
            contentHeight + specification.Padding * 2);
    }

    private static Specification GetSpecification(OrganizerIconLayoutMode mode) => mode switch
    {
        OrganizerIconLayoutMode.Large => new Specification(86, 98, 14, 12, 14),
        OrganizerIconLayoutMode.Small => new Specification(52, 64, 10, 8, 12),
        OrganizerIconLayoutMode.List => new Specification(0, 40, 0, 6, 12),
        _ => new Specification(70, 84, 12, 10, 12),
    };

    private static Size NormalizeSize(Size size) => new(
        double.IsFinite(size.Width) ? size.Width : 444,
        double.IsFinite(size.Height) ? size.Height : 340);

    private readonly record struct Specification(
        double TileWidth,
        double TileHeight,
        double HorizontalGap,
        double VerticalGap,
        double Padding);

    private readonly record struct Layout(
        int Columns,
        double TileWidth,
        double TileHeight,
        double HorizontalGap,
        double VerticalGap,
        double StartX,
        double StartY,
        double ExtentHeight);
}
