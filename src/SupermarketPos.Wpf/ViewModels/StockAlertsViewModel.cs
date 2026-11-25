using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class StockAlertsViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IInventoryService _inventory;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(12) };
    private bool _refreshing;
    private bool _active;

    public StockAlertsViewModel(IServiceScopeFactory scopes, IInventoryService inventory)
    {
        _scopes = scopes;
        _inventory = inventory;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlerts), nameof(HasMoreAlerts))]
    private IReadOnlyList<Product> _items = [];

    [ObservableProperty] private bool _showAlertCards = true;
    [ObservableProperty] private Product? _selected;
    [ObservableProperty] private string _quantity = "1";
    [ObservableProperty] private string? _restockMessage;
    [ObservableProperty] private string? _restockError;

    public bool HasAlerts => Items.Count > 0;
    public bool HasMoreAlerts => Items.Count > 3;
    partial void OnSelectedChanged(Product? value) => RestockCommand.NotifyCanExecuteChanged();

    public async Task StartAsync()
    {
        _active = true;
        ShowAlertCards = true;
        _timer.Start();
        await RefreshAsync();
    }

    public void Stop()
    {
        _active = false;
        _timer.Stop();
        Items = [];
        Selected = null;
        RestockMessage = RestockError = null;
    }

    public async Task RefreshAsync()
    {
        if (_refreshing || !_active) return;
        _refreshing = true;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var current = (await inventory.GetLowStockAsync()).Where(product => product.IsActive).ToArray();
            if (!_active) return;
            if (current.Length == Items.Count && current.Zip(Items).All(pair => pair.First.Id == pair.Second.Id &&
                    pair.First.StockQuantity == pair.Second.StockQuantity && pair.First.Name == pair.Second.Name &&
                    pair.First.Barcode == pair.Second.Barcode && pair.First.Unit == pair.Second.Unit &&
                    pair.First.ImagePath == pair.Second.ImagePath)) return;
            var id = Selected?.Id;
            Items = current;
            Selected = current.FirstOrDefault(product => product.Id == id) ?? current.FirstOrDefault();
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Could not refresh low stock: {0}", error.Message);
        }
        finally { _refreshing = false; }
    }

    private bool CanRestock() => Selected is not null;

    [RelayCommand(CanExecute = nameof(CanRestock))]
    private async Task RestockAsync()
    {
        RestockError = RestockMessage = null;
        var text = LocalizationManager.Instance;
        if (Selected is not { } product) return;
        if (!PurchasesViewModel.TryNumber(Quantity, out var quantity) ||
            !PurchaseDraftRow.ValidQuantity(product, quantity))
        {
            RestockError = text["Purchase_QuantityInvalid"];
            return;
        }
        try
        {
            await _inventory.RestockAsync(product.Id, quantity);
            Quantity = "1";
            RestockMessage = text.Format("Stock_Restocked", product.Name);
            await RefreshAsync();
        }
        catch (Exception error) { RestockError = error.Message; }
    }
}
