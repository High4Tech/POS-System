using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Application.Services;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PosViewModel
{
    private readonly InvoiceMarginService _margin;
    public bool ShowMargin => _auth.CurrentUser?.IsAdmin == true;
    [ObservableProperty] private CartLineViewModel? _selectedCartLine;

    [RelayCommand]
    private Task ViewCartProductAsync() => SelectedCartLine is { } line && !InAppDialog.IsOpen && !IsPaymentOpen
        ? ProductInformation.ShowAsync(line.Line.Product, IsPharmacy) : Task.CompletedTask;

    [RelayCommand]
    private async Task ViewCartProductHistoryAsync()
    {
        if (SelectedCartLine?.Line.Product.Id is not > 0 || InAppDialog.IsOpen || IsPaymentOpen) return;
        try { await HistoryTable.ShowProductSalesAsync(_customerHistory, SelectedCartLine.Line.Product.Id, IsPharmacy,
            _business.DeviceTrackingAvailable, ShowMargin); }
        catch (Exception) { Status = Text["Reports_LoadError"]; }
    }

    [RelayCommand]
    private Task ViewMarginAsync() =>
        ShowMargin && !_cart.IsEmpty && !IsPaymentOpen && !InAppDialog.IsOpen
            ? InvoiceMarginPanel.ShowAsync(_margin.ForCart(_cart)) : Task.CompletedTask;
}
