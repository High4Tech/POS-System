using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SupermarketPos.Wpf.Common;

public enum ColumnResizeEdge { None, Leading, Trailing }

public static class TableLayout
{
    private static Dictionary<string, double[]> _widths = new(StringComparer.Ordinal);
    private static Dictionary<string, int[]> _orders = new(StringComparer.Ordinal);
    private static LayoutPreferences<Dictionary<string, double[]>>? _widthFile;
    private static LayoutPreferences<Dictionary<string, int[]>>? _orderFile;

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(TableLayout), new PropertyMetadata(null, KeyChanged));
    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);
    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    public static readonly DependencyProperty ResizeGripProperty = DependencyProperty.RegisterAttached(
        "ResizeGrip", typeof(ColumnResizeEdge), typeof(TableLayout), new PropertyMetadata(ColumnResizeEdge.None, GripChanged));
    public static ColumnResizeEdge GetResizeGrip(DependencyObject element) => (ColumnResizeEdge)element.GetValue(ResizeGripProperty);
    public static void SetResizeGrip(DependencyObject element, ColumnResizeEdge value) => element.SetValue(ResizeGripProperty, value);

    private static readonly DependencyProperty DragProperty = DependencyProperty.RegisterAttached(
        "Drag", typeof(Drag), typeof(TableLayout), new PropertyMetadata(null));

    private static readonly DependencyProperty DefaultWidthsProperty = DependencyProperty.RegisterAttached(
        "DefaultWidths", typeof(DataGridLength[]), typeof(TableLayout), new PropertyMetadata(null));

    public static RoutedUICommand FitColumnsCommand { get; } = new();

    static TableLayout()
    {
        CommandManager.RegisterClassCommandBinding(typeof(DataGrid),
            new CommandBinding(FitColumnsCommand, FitColumnsToTable, CanFitColumnsToTable));
    }

    public static void Load(PosStorage storage)
    {
        _widthFile = new(storage, "table-widths.json");
        _orderFile = new(storage, "table-order.json");
        _widths = _widthFile.Read() ?? new(StringComparer.Ordinal);
        _orders = _orderFile.Read() ?? new(StringComparer.Ordinal);
    }

    private static void KeyChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not DataGrid grid) return;
        grid.Loaded -= Restore;
        grid.ColumnReordered -= RememberOrder;
        if (e.NewValue is not string key || string.IsNullOrWhiteSpace(key)) return;
        grid.Loaded += Restore;
        grid.ColumnReordered += RememberOrder;
        if (grid.IsLoaded) Restore(grid, new RoutedEventArgs());
    }

    private static void Restore(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || GetKey(grid) is not { } key) return;
        var count = grid.Columns.Count;
        if (grid.GetValue(DefaultWidthsProperty) is not DataGridLength[] defaults || defaults.Length != count)
            grid.SetValue(DefaultWidthsProperty, grid.Columns
                .Select(column => new DataGridLength(column.Width.Value, column.Width.UnitType)).ToArray());
        if (_orders.TryGetValue(key, out var order) && order is not null &&
            order.Length == count && order.Order().SequenceEqual(Enumerable.Range(0, count)))
        {
            for (var position = 0; position < count; position++)
                grid.Columns[order[position]].DisplayIndex = position;
        }

        if (_widths.TryGetValue(key, out var widths) && widths?.Length == count)
        {
            for (var index = 0; index < count; index++)
                if (double.IsFinite(widths[index]) && widths[index] > 0 && widths[index] <= 10000)
                    grid.Columns[index].Width = new DataGridLength(widths[index]);
        }
    }

    private static void CanFitColumnsToTable(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = sender is DataGrid grid && GetKey(grid) is { } key && _widths.ContainsKey(key);
        e.Handled = true;
    }

    private static void FitColumnsToTable(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is not DataGrid grid || GetKey(grid) is not { } key ||
            grid.GetValue(DefaultWidthsProperty) is not DataGridLength[] defaults ||
            defaults.Length != grid.Columns.Count) return;
        for (var index = 0; index < defaults.Length; index++)
            grid.Columns[index].Width = defaults[index];
        _widths.Remove(key);
        _widthFile?.Write(_widths);
        e.Handled = true;
    }

    private static void RememberOrder(object? sender, DataGridColumnEventArgs e)
    {
        if (sender is not DataGrid grid || GetKey(grid) is not { } key) return;
        // DisplayIndex moves with the header. The collection index stays with the column and its saved width.
        _orders[key] = Enumerable.Range(0, grid.Columns.Count).OrderBy(index => grid.Columns[index].DisplayIndex).ToArray();
        _orderFile?.Write(_orders);
    }

    private static void Remember(DataGrid grid)
    {
        if (GetKey(grid) is not { } key) return;
        _widths[key] = grid.Columns.Select(column => column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth).ToArray();
        _widthFile?.Write(_widths);
    }

    private static void GripChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Thumb grip) return;
        if ((ColumnResizeEdge)e.OldValue == ColumnResizeEdge.None)
        {
            grip.DragStarted += Start;
            grip.DragDelta += Resize;
            grip.DragCompleted += Finish;
            grip.PreviewMouseLeftButtonDown += Fit;
        }
        else if ((ColumnResizeEdge)e.NewValue == ColumnResizeEdge.None)
        {
            grip.DragStarted -= Start;
            grip.DragDelta -= Resize;
            grip.DragCompleted -= Finish;
            grip.PreviewMouseLeftButtonDown -= Fit;
        }
    }

    private static Drag? Measure(Thumb grip)
    {
        if (grip.TemplatedParent is not DataGridColumnHeader { Column: { } selected } header ||
            !selected.CanUserResize) return null;
        DependencyObject? parent = header;
        while (parent is not null and not DataGrid) parent = VisualTreeHelper.GetParent(parent);
        if (parent is not DataGrid { CanUserResizeColumns: true } grid) return null;
        var columns = grid.Columns.Where(column => column.Visibility == Visibility.Visible)
            .OrderBy(column => column.DisplayIndex).ToArray();
        var index = Array.IndexOf(columns, selected);
        if (index < 0 || columns.Any(column => column.ActualWidth <= 0)) return null;
        return new Drag(grid, columns, columns.Select(column => column.Width).ToArray(),
            columns.Select(column => column.ActualWidth).ToArray(), index, Mouse.GetPosition(grid).X, GetResizeGrip(grip));
    }

    private static void Start(object sender, DragStartedEventArgs e)
    {
        var grip = (Thumb)sender;
        if (Measure(grip) is { } drag) grip.SetValue(DragProperty, drag);
    }

    private static void Resize(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb grip && grip.GetValue(DragProperty) is Drag drag)
            Apply(drag, drag.WidthChange(Mouse.GetPosition(drag.Grid).X));
    }

    private static void Finish(object sender, DragCompletedEventArgs e)
    {
        if (sender is not Thumb grip || grip.GetValue(DragProperty) is not Drag drag) return;
        grip.ClearValue(DragProperty);
        if (!drag.Changed) return;
        if (e.Canceled) RestoreWidths(drag);
        else Remember(drag.Grid);
    }

    private static void Fit(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not Thumb grip || Measure(grip) is not { } drag) return;
        var column = drag.Columns[drag.Index];
        column.Width = DataGridLength.Auto;
        drag.Grid.UpdateLayout();
        var wanted = column.Width.DesiredValue;
        column.Width = drag.Original[drag.Index];
        if (double.IsFinite(wanted) && wanted > 0)
        {
            Apply(drag, wanted - drag.Widths[drag.Index]);
            if (drag.Changed) Remember(drag.Grid);
        }
        e.Handled = true;
    }

    private static void Apply(Drag drag, double distance)
    {
        var column = drag.Columns[drag.Index];
        var originalWidth = drag.Widths[drag.Index];
        var min = Math.Max(drag.Grid.MinColumnWidth, column.MinWidth);
        var max = Math.Max(min, Math.Min(drag.Grid.MaxColumnWidth, column.MaxWidth));
        var width = Math.Clamp(originalWidth + distance, min, max);
        if (Math.Abs(width - originalWidth) < 0.01)
        {
            if (drag.Changed) RestoreWidths(drag);
            return;
        }

        if (!drag.Changed)
        {
            // Pin the measured widths before resizing so the grid cannot redistribute its star columns.
            for (var index = 0; index < drag.Columns.Length; index++)
                drag.Columns[index].Width = new DataGridLength(drag.Widths[index]);
            drag.Changed = true;
        }
        column.Width = new DataGridLength(width);
    }

    private static void RestoreWidths(Drag drag)
    {
        for (var index = 0; index < drag.Columns.Length; index++)
            drag.Columns[index].Width = drag.Original[index];
        drag.Changed = false;
    }

    private sealed record Drag(DataGrid Grid, DataGridColumn[] Columns, DataGridLength[] Original,
        double[] Widths, int Index, double StartX, ColumnResizeEdge Edge)
    {
        public bool Changed { get; set; }
        public double WidthChange(double pointerX) => Edge == ColumnResizeEdge.Leading ? StartX - pointerX : pointerX - StartX;
    }
}
