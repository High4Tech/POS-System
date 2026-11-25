using System.Windows;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Wpf.Views;

public partial class SerialEntryWindow : Window
{
    public IReadOnlyList<UnitIdentifiers> Units { get; private set; } = [];

    public SerialEntryWindow(string productName)
    {
        InitializeComponent();
        ProductTitle.Text = productName;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            Units = Parse(Identifiers.Text);
            DialogResult = true;
        }
        catch (Exception error) { ErrorText.Text = error.Message; }
    }

    public static IReadOnlyList<UnitIdentifiers> Parse(string text)
    {
        var units = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length > 3) throw new InvalidOperationException("Use Serial | IMEI 1 | IMEI 2 on each line.");
            return UnitIdentifiers.Create(cells[0], cells.ElementAtOrDefault(1), cells.ElementAtOrDefault(2));
        }).ToArray();
        if (units.Length == 0) throw new InvalidOperationException("Enter at least one serial or IMEI.");
        var identifiers = units.SelectMany(unit => unit.All).ToArray();
        if (identifiers.Distinct(StringComparer.Ordinal).Count() != identifiers.Length)
            throw new InvalidOperationException("A serial or IMEI occurs more than once.");
        return units;
    }
}
