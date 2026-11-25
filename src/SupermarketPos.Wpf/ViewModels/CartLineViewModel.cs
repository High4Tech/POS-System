using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Common;

namespace SupermarketPos.Wpf.ViewModels;

/// <summary>View wrapper over a domain <see cref="CartLine"/>. Editing the quantity flows back into
/// the domain line and triggers a recalculation of the cart totals.</summary>
public sealed partial class CartLineViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private readonly Func<CartLineViewModel, decimal, bool> _canSetQuantity;
    [ObservableProperty] private string _priceText;
    [ObservableProperty] private bool _hasInvalidPrice;

    public CartLine Line { get; }

    public CartLineViewModel(CartLine line, Action onChanged, Func<CartLineViewModel, decimal, bool> canSetQuantity)
    {
        Line = line;
        _onChanged = onChanged;
        _canSetQuantity = canSetQuantity;
        _priceText = line.GrossUnitPrice.ToString("0.00", CultureInfo.InvariantCulture);
        UpdateAvailability(line.Product);
    }

    public string Name => Line.BatchLot is { } lot ? $"{Line.Product.Name} · {lot}" : Line.Product.Name;
    public string UnitText => Localization.LocalizationManager.Instance["Measure_"+Line.UnitCode];
    public string LineTotalText => MoneyText.Format(Line.GrossTotal);
    public string? DiscountText => Line.LineDiscount?.Describe();
    public decimal AvailableStock { get; private set; }
    public bool IsAvailable { get; private set; }

    public void UpdateAvailability(Product? product)
    {
        var batch = Line.BatchId is { } id ? product?.Batches.SingleOrDefault(item => item.Id == id) : null;
        AvailableStock = Line.BatchId is null ? product?.StockQuantity ?? 0m : batch?.Quantity ?? 0m;
        IsAvailable = product?.IsActive == true && (Line.BatchId is null ? !product.TrackExpiry :
            batch is not null && batch.HoldReason.Length==0 && batch.ExpiryDate == Line.BatchExpiry && batch.ExpiryDate >= DateOnly.FromDateTime(DateTime.Today));
    }

    partial void OnPriceTextChanged(string value)
    {
        HasInvalidPrice = true;
        if (decimal.TryParse(value, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var price))
        {
            try
            {
                Line.SetUnitPrice(price);
                HasInvalidPrice = false;
                OnPropertyChanged(nameof(LineTotalText));
            }
            catch (Exception ex) when (ex is DomainException or OverflowException) { }
        }
        _onChanged();
    }

    public decimal Quantity
    {
        get => Line.DisplayQuantity;
        set
        {
            try
            {
                value*=Line.IsPackage?Line.PackageSize:1;
                if (value == Line.Quantity) return;
                if (!_canSetQuantity(this, value))
                {
                    OnPropertyChanged();
                    return;
                }
                Line.SetQuantity(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(LineTotalText));
                _onChanged();
            }
            catch
            {
                OnPropertyChanged(); // revert display on invalid entry
            }
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Quantity));
        OnPropertyChanged(nameof(LineTotalText));
        OnPropertyChanged(nameof(DiscountText));
    }
}
