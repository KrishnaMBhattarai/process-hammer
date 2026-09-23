using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ProcessBooster.App.Behaviors;

/// <summary>
/// Attached behavior that builds a DataGrid's columns from a list of header strings. Each row is a
/// string[]; cell N binds to indexer [N]. Lets one generic grid render any table shape.
/// </summary>
public static class GridColumns
{
    public static readonly DependencyProperty HeadersProperty =
        DependencyProperty.RegisterAttached(
            "Headers", typeof(IEnumerable<string>), typeof(GridColumns),
            new PropertyMetadata(null, OnHeadersChanged));

    public static void SetHeaders(DependencyObject o, IEnumerable<string> v) => o.SetValue(HeadersProperty, v);
    public static IEnumerable<string> GetHeaders(DependencyObject o) => (IEnumerable<string>)o.GetValue(HeadersProperty);

    private static void OnHeadersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || e.NewValue is not IEnumerable<string> headers) return;

        grid.Columns.Clear();
        var i = 0;
        foreach (var header in headers)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"[{i}]"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = CellStyle(),
            });
            i++;
        }
    }

    private static Style? _cellStyle;

    // Single-line cells with ellipsis + a tooltip showing the full value (great for long paths/PATH).
    private static Style CellStyle()
    {
        if (_cellStyle is not null) return _cellStyle;
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2, 0, 8, 0)));
        s.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
            new Binding("Text") { RelativeSource = new RelativeSource(RelativeSourceMode.Self) }));
        s.Seal();
        _cellStyle = s;
        return s;
    }
}
