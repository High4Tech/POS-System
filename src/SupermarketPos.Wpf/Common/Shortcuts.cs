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
        ("purchases", "Nav_Purchases"), ("reports", "Nav_Reports"), ("settings", "Nav_Settings"), ("dashboard", "Nav_Dashboard"), ("digital","Service_Title")
    ];

    internal sealed record ReferenceRow(string Shortcut, string Action, string Page, string? Availability);

    internal static bool TryRun(Window window, MainViewModel shell, Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None && key is < Key.F1 or > Key.F24 && key != Key.OemQuestion) return false;
        if (key == Key.OemQuestion && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or ComboBox)
            return false;
        if (key == Key.V && modifiers == ModifierKeys.Shift && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox)
            return false;
        if(shell.CurrentViewModel is PosViewModel { IsPaymentOpen:true }) return false;
        var entry = Entries(window, shell, routedKey: key).FirstOrDefault(s => s.Key == key && s.Modifiers == modifiers);
        if (entry is null) return false;
        if (entry.Invoke is null)
        {
            if (entry.Requirement is null || key == Key.V && modifiers == ModifierKeys.Shift) return false;
            shell.ShortcutHint = LocalizationManager.Instance[entry.Requirement];
            return true;
        }
        shell.ShortcutHint = null;
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
                     where page.Screen != "purchases" || shell.CanManageStock
                     from entry in Entries(window, shell, reference: true, otherPage: page.Screen)
                     where !shown.Contains(entry.Identity)
                     group (entry, page.Label) by entry.Identity).Select(group =>
        {
            var entry = group.First().entry;
            return Row(entry, string.Join(text["Shortcut_PageSeparator"], group.Select(item => text[item.Label]).Distinct()),
                entry.Requirement is { } requirement ? text[requirement] : null);
        }).ToArray();
        return (text[currentPage.Label], current.Select(entry => Row(entry, "",
            entry.Modifiers==ModifierKeys.Windows&&window is MainWindow main&&main.WindowsKeyConflict(entry.Key)?text["Shortcut_WindowsConflict"]:entry.Invoke is null ? text[entry.Requirement ?? "Shortcut_Unavailable"] : null)).ToArray(), other);

        ReferenceRow Row(Entry entry, string page, string? availability) => new(
            (entry.Modifiers.HasFlag(ModifierKeys.Windows) ? "Win + " : "") +
            (entry.Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl + " : "") +
            (entry.Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt + " : "") +
            (entry.Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift + " : "") + (entry.Key==Key.OemQuestion?"/":entry.Key.ToString()),
            text[entry.Label].Replace($" ({entry.Key})", "", StringComparison.Ordinal), page, availability);
    }

    private static IEnumerable<Entry> Entries(Window window, MainViewModel shell, bool reference = false, string? otherPage = null, Key routedKey = Key.None)
    {
        const ModifierKeys ctrl = ModifierKeys.Control;
        const ModifierKeys alt = ModifierKeys.Alt;
        const ModifierKeys shift = ModifierKeys.Shift;
        static Action? Run(ICommand? command, object? arg = null) => command?.CanExecute(arg) == true ? () => command.Execute(arg) : null;
        var page = otherPage is null ? shell.CurrentViewModel : null;
        var screen = otherPage ?? shell.ActiveScreen;
        var pos = page as PosViewModel;
        if (pos is not null || reference && screen == "pos")
        {
            if (!shell.IsReadOnly)
            {
                yield return new(Key.F1, 0, "Pos_Cash", Run(pos?.OpenCashCommand), "Shortcut_CartRequired");
                yield return new(Key.F2, 0, "Pos_Card", Run(pos?.OpenCardCommand), "Shortcut_CartRequired");
                yield return new(Key.F3, 0, "Trade_Credit", Run(pos?.OpenCreditCommand), "Shortcut_CartRequired");
                yield return new(Key.S, ctrl, "Trade_SaveDraft", pos?.Lines.Count > 0 ? Run(pos.SaveDraftCommand) : null, "Shortcut_CartRequired");
                yield return new(Key.O, ctrl | shift, "Trade_ResumeDraft", pos?.Lines.Count == 0 ? Run(pos.ResumeDraftCommand) : null, "Trade_DraftEmptyCart");
            }
            yield return new(Key.F4, 0, "Pos_Clear", Run(pos?.ClearCartCommand), "Shortcut_CartRequired");
            if (shell.CanManageStock)
                yield return new(Key.V, shift, "Trade_ViewMargin", pos?.Lines.Count > 0 ? Run(pos.ViewMarginCommand) : null, "Shortcut_CartRequired");
            yield return new(Key.L, ctrl | alt, "Trade_ToggleRegisterView", pos is null ? null : () => pos.RegisterList = !pos.RegisterList);
            var selectedLine = pos?.SelectedCartLine;
            yield return new(Key.H, ctrl, "Trade_ProductSales", selectedLine?.Line.Product.Id > 0 ? Run(pos?.ViewCartProductHistoryCommand) : null, "Shortcut_ProductRequired");
            yield return new(Key.I, ctrl | alt, "Trade_ProductInfo", selectedLine is null ? null : Run(pos?.ViewCartProductCommand), "Shortcut_ProductRequired");
            yield return new(Key.Delete, ctrl | shift, "Trade_RemoveCartLine", selectedLine is null ? null : Run(pos?.RemoveLineCommand, selectedLine), "Shortcut_ProductRequired");
            if (pos?.IsPaymentOpen != true || reference)
            {
                var select = pos?.IsPaymentOpen == true ? null : Run(pos?.SelectCustomerCommand);
                yield return new(Key.C, ctrl | alt, "Customer_SelectExisting", select);
                yield return new(Key.F12, 0, "Customer_Details", select);
                var selected = pos is { HasCustomerAccount: true, IsPaymentOpen: false };
                if (!shell.IsReadOnly)
                {
                    yield return new(Key.T, ctrl | alt, "Trade_Frequent", selected ? Run(pos?.PreviousPurchasesCommand, true) : null, "Shortcut_CustomerAccountRequired");
                    yield return new(Key.R, ctrl | alt, "Trade_PreviousPurchases", selected ? Run(pos?.PreviousPurchasesCommand, false) : null, "Shortcut_CustomerAccountRequired");
                }
                else if (!reference && routedKey is Key.T or Key.R)
                {
                    yield return new(Key.T, ctrl | alt, "Trade_Frequent", null, "License_RecordsOnly");
                    yield return new(Key.R, ctrl | alt, "Trade_PreviousPurchases", null, "License_RecordsOnly");
                }
                var ledger = selected ? Run(pos?.CustomerLedgerCommand) : null;
                yield return new(Key.B, ctrl, "Trade_CustomerLedger", ledger, "Shortcut_CustomerAccountRequired");
                yield return new(Key.F1, alt, "Trade_CustomerLedger", ledger, "Shortcut_CustomerAccountRequired");
                yield return new(Key.F12, alt, "Trade_CustomerLedger", ledger, "Shortcut_CustomerAccountRequired");
            }
        }
        var digital=page as DigitalServicesView;
        if(digital is not null||reference&&screen=="digital")
        {
            if(!shell.IsReadOnly){yield return new(Key.F1,0,"Pos_Cash",Run(digital?.OpenCashCommand),"Digital_TransactionRequired");yield return new(Key.F2,0,"Pos_Card",Run(digital?.OpenCardCommand),digital?.Cart.Any(r=>r.Type=="CASHOUT")==true?"Digital_CashOutCashOnly":"Digital_TransactionRequired");
                yield return new(Key.S,ctrl,"Digital_SaveDraft",Run(digital?.SaveDraftCommand),"Digital_TransactionRequired");
                yield return new(Key.O,ctrl|shift,"Digital_ResumeDraft",Run(digital?.ResumeDraftCommand),"Digital_ClearBeforeResume");}
            yield return new(Key.F4,0,"Digital_ClearTransaction",Run(digital?.ClearCartCommand),"Digital_TransactionRequired");
            yield return new(Key.Delete,ctrl|shift,"Digital_ClearTransaction",Run(digital?.RemoveSelectedCommand),"Shortcut_ProductRequired");
        }
        var search = otherPage is null && (reference || routedKey is Key.F or Key.OemQuestion) ? FindSearch(window) : null;
        if (search is not null || reference && (screen is "pos" or "products" or "customers" or "reports" or "digital" ||
            screen == "purchases" && shell.CanManageStock))
        {
            yield return new(Key.F, ctrl, "Shortcut_FocusSearch", search is null ? null : () => { search.Focus(); search.SelectAll(); });
            yield return new(Key.OemQuestion, 0, "Shortcut_FocusSearch", search is null ? null : () => { search.Focus(); search.SelectAll(); });
        }
        foreach(var modifier in new[]{ModifierKeys.Windows,alt})
        {
            yield return new(Key.H,modifier,"Nav_Dashboard",Run(shell.ShowDashboardCommand));
            yield return new(Key.S,modifier,"Nav_Pos",Run(shell.ShowPosCommand));
            if(shell.DigitalServicesVisible)yield return new(Key.D,modifier,"Service_Title",Run(shell.ShowDigitalServicesCommand));
            if(shell.CanManageStock){yield return new(Key.P,modifier,"Nav_Purchases",Run(shell.ShowPurchasesCommand));yield return new(Key.T,modifier,"Nav_Products",Run(shell.ShowProductsCommand));}
            yield return new(Key.R,modifier,"Nav_Reports",Run(shell.ShowReportsCommand));yield return new(Key.C,modifier,"Nav_Customers",Run(shell.ShowCustomersCommand));yield return new(Key.Z,modifier,"Nav_Settings",Run(shell.ShowSettingsCommand));
            yield return new(Key.X,modifier,"Scanner_PairTitle",shell.Scanner.OpenPanelCommand.CanExecute(null)?()=>shell.Scanner.OpenPanelCommand.Execute(null):null);
        }
        yield return new(Key.D1,alt,"Nav_Dashboard",Run(shell.ShowDashboardCommand));
        yield return new(Key.D2,alt,"Nav_Pos",Run(shell.ShowPosCommand));
        yield return new(Key.D3,alt,"Nav_Products",Run(shell.ShowProductsCommand));
        if(shell.CanManageStock)yield return new(Key.D4,alt,"Nav_Purchases",Run(shell.ShowPurchasesCommand));
        yield return new(Key.D5,alt,"Nav_Reports",Run(shell.ShowReportsCommand));
        yield return new(Key.D6,alt,"Nav_Customers",Run(shell.ShowCustomersCommand));
        yield return new(Key.D7,alt,"Nav_Settings",Run(shell.ShowSettingsCommand));
        if(shell.DigitalServicesVisible)yield return new(Key.D8,alt,"Service_Title",Run(shell.ShowDigitalServicesCommand));
        if(window.FindName("MoreButton") is Button more)
            yield return new(Key.M,alt,"Common_More",()=>more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        if (shell.CanManageStock && !shell.IsReadOnly && (page is ProductsViewModel { IsEditorOpen: true } || reference && screen == "products"))
            yield return new(Key.S, ctrl, "Scanner_SaveProduct", page is ProductsViewModel { IsEditorOpen: true } editor ? Run(editor.SaveCommand) : null, "Shortcut_EditorRequired");
        var reports = page as ReportsViewModel;
        if (reports?.SelectedSale is not null || reference && screen == "reports")
        {
            var sale = reports?.SelectedSale;
            yield return new(Key.P, ctrl, "Invoice_Print", sale is null ? null : Run(reports?.PrintInvoiceCommand), "Shortcut_InvoiceRequired");
            yield return new(Key.O, ctrl | shift, "Reports_OpenReceipt", sale is null ? null : Run(reports?.OpenReceiptCommand), "Shortcut_InvoiceRequired");
            if (shell.CanManageStock)
                yield return new(Key.V, shift, "Trade_ViewMargin", sale is null ? null : Run(reports?.ViewMarginCommand), "Shortcut_InvoiceRequired");
            if (shell.CanManageStock && !shell.IsReadOnly)
                yield return new(Key.R, ctrl | shift, "Trade_ReturnItems", sale is null ? null : Run(reports?.ReturnItemsCommand), "Shortcut_InvoiceRequired");
            if (!reference || !shell.IsReadOnly)
                yield return new(Key.R, ctrl | alt, "Trade_RepeatSale", sale is null ? null : Run(shell.RepeatSaleCommand, sale.Sale), "Shortcut_InvoiceRequired");
        }
        if (reports is { HasReport: true, SelectedSale: null } || reference && screen == "reports")
            yield return new(Key.P, ctrl, "Trade_PrintReport",
                reports is { HasReport: true, SelectedSale: null } ? Run(reports.PrintReportCommand) : null, "Trade_PrintReportRequired");
        yield return new(Key.G, ctrl, "Nav_Reports", Run(shell.ShowReportsCommand));
        if (window is MainWindow main)
            yield return new(Key.H, ctrl | shift, "Nav_Shortcuts", () => _ = main.ShowShortcutsAsync());
        var customers = page as CustomersViewModel;
        if (customers is not null || reference && screen == "customers")
        {
            var ledger = customers is not null && (customers.CustomerTab==1?customers.SelectedDigitalCustomer is not null:customers.Selected is not null) ? Run(customers.ViewLedgerCommand) : null;
            yield return new(Key.B, ctrl, "Trade_CustomerLedger", ledger, "Shortcut_CustomerRequired");
            yield return new(Key.F1, alt, "Trade_CustomerLedger", ledger, "Shortcut_CustomerRequired");
            yield return new(Key.F12, alt, "Trade_CustomerLedger", ledger, "Shortcut_CustomerRequired");
        }
        else if (screen != "pos") yield return new(Key.B, ctrl, "Nav_Customers", Run(shell.ShowCustomersCommand));
        var inventory = page as ProductsViewModel;
        if (inventory is not null || reference && screen == "products")
        {
            if (shell.CanManageStock && !shell.IsReadOnly)
                yield return new(Key.N, ctrl | shift, "Products_New", inventory is { IsEditorOpen: false } ? Run(inventory.NewCommand) : null,
                    "Trade_CloseEditorRequired");
            var editorOpen = inventory?.IsEditorOpen == true;
            var row = inventory is not null && !editorOpen && (reference || routedKey is Key.H or Key.I)
                ? Descendants(window).OfType<DataGrid>().FirstOrDefault(grid => grid.Name == "InventoryTable" && grid.IsVisible)?.SelectedItem as ProductRowViewModel
                : null;
            if (!editorOpen || reference)
            {
                yield return new(Key.F3, alt, "Products_ShelfLabels",
                    editorOpen || reference && inventory?.Products.Count == 0 ? null : Run(inventory?.PrintLabelsCommand), "Shortcut_LabelsRequired");
                if (!editorOpen || reference)
                    yield return new(Key.I, ctrl | alt, "Trade_ProductInfo", row is null || editorOpen ? null : Run(inventory?.ProductInformationCommand, row.Product), "Shortcut_ProductRequired");
            }
            var hasProduct = row is not null || editorOpen && inventory?.Editor.Id is not null;
            yield return new(Key.H, ctrl, "Trade_ProductSales", hasProduct ? Run(inventory?.ProductSalesCommand, row?.Product.Id) : null, "Shortcut_ProductRequired");
        }
    }

    private static TextBox? FindSearch(DependencyObject root)
    {
        var controls = Descendants(root).ToArray();
        if (controls.OfType<MedicineSearchBox>().FirstOrDefault(s => s.IsVisible && s.IsEnabled)?.Input is { } medicine) return medicine;
        return controls.OfType<TextBox>().FirstOrDefault(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly &&
            (t.Name == "SearchBox" || t.Name == "InvoiceSearchBox" || t.Name == "PackageSearch" || t.Name == "DigitalAmount" || t.Name == "DigitalRecipient" || t.Name == "DigitalCustomerSearch" || t.Name == "DigitalHistorySearch" ||
             t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path is "Search" or "Filter" or "ProductSearch"));
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
