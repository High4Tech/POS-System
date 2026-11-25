using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.Common;

internal static class Shortcuts
{
    private sealed record Entry(Key Key, ModifierKeys Modifiers, string Label, Action? Invoke, string? Requirement = null)
    {
        // Labels are resource identifiers. The key and modifiers distinguish separate bindings for the same command.
        public (Key, ModifierKeys, string) Identity => (Key, Modifiers, Label);
    }

    private static readonly (string Screen, string Label)[] Pages =
    [
        ("pos", "Nav_Pos"), ("customers", "Nav_Customers"), ("products", "Nav_Products"),
        ("purchases", "Nav_Purchases"), ("reports", "Nav_Reports"), ("settings", "Nav_Settings"), ("dashboard", "Nav_Dashboard")
    ];

    internal sealed record ReferenceRow(string Shortcut, string Action, string Page, string? Availability);

    internal static bool TryRun(Window window, MainViewModel shell, Key key, ModifierKeys modifiers)
    {
        if (modifiers==ModifierKeys.None && key is not (Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F12)) return false;
        if (modifiers!=ModifierKeys.None && key is not (Key.F or Key.S or Key.P or Key.H or Key.B or Key.G or Key.C or Key.T or Key.R or Key.I or Key.F1 or Key.F3 or Key.F12)) return false;
        if(shell.CurrentViewModel is PosViewModel { IsPaymentOpen:true }) return false;
        var entry = Entries(window, shell).FirstOrDefault(s => s.Key == key && s.Modifiers == modifiers);
        if (entry?.Invoke is null) return false;
        entry.Invoke();
        return true;
    }

    internal static (string Page, ReferenceRow[] Current, ReferenceRow[] Other) Describe(Window window, MainViewModel shell)
    {
        var text = LocalizationManager.Instance;
        var currentPage = Pages.First(page => page.Screen == shell.ActiveScreen);
        var current = Entries(window, shell, reference: true).DistinctBy(entry => entry.Identity).ToArray();
        var shown = current.Select(entry => entry.Identity).ToHashSet();
        var other = (from page in Pages
                     where page.Screen != shell.ActiveScreen
                     from entry in Entries(window, shell, reference: true, otherPage: page.Screen)
                     where !shown.Contains(entry.Identity)
                     group (entry, page.Label) by entry.Identity).Select(group =>
        {
            var entry = group.First().entry;
            return Row(entry, string.Join(text["Shortcut_PageSeparator"], group.Select(item => text[item.Label]).Distinct()),
                entry.Requirement is { } requirement ? text[requirement] : null);
        }).ToArray();
        return (text[currentPage.Label], current.Select(entry => Row(entry, "",
            entry.Invoke is null ? text[entry.Requirement ?? "Shortcut_Unavailable"] : null)).ToArray(), other);

        ReferenceRow Row(Entry entry, string page, string? availability) => new(
            (entry.Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl + " : "") +
            (entry.Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt + " : "") + entry.Key,
            text[entry.Label].Replace($" ({entry.Key})", "", StringComparison.Ordinal), page, availability);
    }

    private static IEnumerable<Entry> Entries(Window window, MainViewModel shell, bool reference = false, string? otherPage = null)
    {
        const ModifierKeys ctrl = ModifierKeys.Control;
        const ModifierKeys alt = ModifierKeys.Alt;
        static Action? Run(ICommand? command, object? arg = null) => command?.CanExecute(arg) == true ? () => command.Execute(arg) : null;
        var page = otherPage is null ? shell.CurrentViewModel : null;
        var screen = otherPage ?? shell.ActiveScreen;
        var pos = page as PosViewModel;
        if (pos is not null || reference && screen == "pos")
        {
            if (!reference || !shell.IsReadOnly)
            {
                yield return new(Key.F1, 0, "Pos_Cash", Run(pos?.OpenCashCommand), "Shortcut_CartRequired");
                yield return new(Key.F2, 0, "Pos_Card", Run(pos?.OpenCardCommand), "Shortcut_CartRequired");
                yield return new(Key.F3, 0, "Trade_Credit", Run(pos?.OpenCreditCommand), "Shortcut_CartRequired");
            }
            yield return new(Key.F4, 0, "Pos_Clear", Run(pos?.ClearCartCommand), "Shortcut_CartRequired");
            if (pos?.IsPaymentOpen != true || reference)
            {
                var select = pos?.IsPaymentOpen == true ? null : Run(pos?.SelectCustomerCommand);
                yield return new(Key.C, ctrl | alt, "Customer_SelectExisting", select);
                yield return new(Key.F12, 0, "Customer_Details", select);
                var selected = pos is { CustomerSummary: not null, IsPaymentOpen: false };
                if (selected || reference)
                {
                    if (!reference || !shell.IsReadOnly)
                    {
                        yield return new(Key.T, ctrl | alt, "Trade_Frequent", selected ? Run(pos?.PreviousPurchasesCommand, true) : null, "Shortcut_CustomerRequired");
                        yield return new(Key.R, ctrl | alt, "Trade_PreviousPurchases", selected ? Run(pos?.PreviousPurchasesCommand, false) : null, "Shortcut_CustomerRequired");
                    }
                    var ledger = selected ? Run(pos?.CustomerLedgerCommand) : null;
                    yield return new(Key.B, ctrl, "Trade_CustomerLedger", ledger, "Shortcut_CustomerRequired");
                    yield return new(Key.F1, alt, "Trade_CustomerLedger", ledger, "Shortcut_CustomerRequired");
                    yield return new(Key.F12, alt, "Trade_CustomerLedger", ledger, "Shortcut_CustomerRequired");
                }
            }
        }
        var search = otherPage is null ? FindSearch(window) : null;
        if (search is not null || reference && (screen is "pos" or "products" or "customers" or "reports" ||
            screen == "purchases" && shell.IsPharmacy))
            yield return new(Key.F, ctrl, "Shortcut_FocusSearch", search is null ? null : () => { search.Focus(); search.SelectAll(); });
        if (!shell.IsReadOnly && (page is ProductsViewModel { IsEditorOpen: true } || reference && screen == "products"))
            yield return new(Key.S, ctrl, "Scanner_SaveProduct", page is ProductsViewModel { IsEditorOpen: true } editor ? Run(editor.SaveCommand) : null, "Shortcut_EditorRequired");
        var reports = page as ReportsViewModel;
        if (reports?.SelectedSale is not null || reference && screen == "reports")
        {
            var sale = reports?.SelectedSale;
            yield return new(Key.P, ctrl, "Invoice_Print", sale is null ? null : Run(reports?.PrintInvoiceCommand), "Shortcut_InvoiceRequired");
            if (!reference || !shell.IsReadOnly)
                yield return new(Key.R, ctrl | alt, "Trade_RepeatSale", sale is null ? null : Run(shell.RepeatSaleCommand, sale.Sale), "Shortcut_InvoiceRequired");
        }
        yield return new(Key.G, ctrl, "Nav_Reports", Run(shell.ShowReportsCommand));
        if(page is not PosViewModel { CustomerSummary:not null }) yield return new(Key.B, ctrl, "Nav_Customers", Run(shell.ShowCustomersCommand));
        var customers = page as CustomersViewModel;
        if (customers?.Selected is not null || reference && screen == "customers")
        {
            var ledger = customers?.Selected is null ? null : Run(customers.ViewLedgerCommand);
            yield return new(Key.F1, alt, "Trade_CustomerLedger", ledger, "Shortcut_CustomerRequired");
            yield return new(Key.F12, alt, "Trade_CustomerLedger", ledger, "Shortcut_CustomerRequired");
        }
        var inventory = page as ProductsViewModel;
        if (inventory is not null || reference && screen == "products")
        {
            var row = otherPage is null ? (Keyboard.FocusedElement as FrameworkElement)?.DataContext as ProductRowViewModel : null;
            var editorOpen = inventory?.IsEditorOpen == true;
            if (!editorOpen || reference)
            {
                yield return new(Key.F3, alt, "Products_ShelfLabels",
                    editorOpen || reference && inventory?.Products.Count == 0 ? null : Run(inventory?.PrintLabelsCommand), "Shortcut_LabelsRequired");
                if (!editorOpen && row is not null || reference)
                    yield return new(Key.I, ctrl | alt, "Products_EditTitle", row is null || editorOpen ? null : Run(inventory?.EditCommand, row), "Shortcut_ProductRequired");
            }
            var hasProduct = row is not null || editorOpen && inventory?.Editor.Id is not null;
            if (hasProduct || reference)
                yield return new(Key.H, ctrl, "Trade_ProductSales", hasProduct ? Run(inventory?.ProductSalesCommand, row?.Product.Id) : null, "Shortcut_ProductRequired");
        }
    }

    private static TextBox? FindSearch(DependencyObject root)
    {
        var controls = Descendants(root).ToArray();
        if (controls.OfType<MedicineSearchBox>().FirstOrDefault(s => s.IsVisible && s.IsEnabled)?.Input is { } medicine) return medicine;
        return controls.OfType<TextBox>().FirstOrDefault(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly &&
            (t.Name == "SearchBox" || t.Name == "InvoiceSearchBox" ||
            t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path is "Search" or "Filter"));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
}
