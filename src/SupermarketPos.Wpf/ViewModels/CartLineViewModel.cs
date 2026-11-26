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
    [ObservableProperty] private bool _hasInvalidQuantity;
    partial void OnHasInvalidQuantityChanged(bool value) => _onChanged();
    [ObservableProperty] private bool _hasInvalidTax;
    [ObservableProperty] private string _taxText="";
    [ObservableProperty] private string? _editError;
    [ObservableProperty] private string _nameText="";
    [ObservableProperty] private bool _hasInvalidName;
    private bool _synchronizing;
    public bool PendingSetup{get;private set;}
    public bool NeedsTax{get;}
    public bool CanEditPrice{get;}
    public bool NeedsName=>PendingSetup&&Line.Product.Id==0&&Line.Product.MedicineReferenceId is null;
    private bool CanChooseBaseUnit=>PendingSetup&&!Line.Product.StockTracked&&Line.Product.UnitPrice<=0&&Line.Product.UnitsPerStrip<=1&&Line.Product.UnitsPerPackage<=1;
    public decimal? LocationStock{get;private set;}

    public CartLine Line { get; }

    public CartLineViewModel(CartLine line, Action onChanged, Func<CartLineViewModel, decimal, bool> canSetQuantity,bool canEditPrice=true,bool needsTax=false)
    {
        Line = line;
        _onChanged = onChanged;
        _canSetQuantity = canSetQuantity;
        CanEditPrice=canEditPrice;PendingSetup=line.MedicineUnit is not null&&line.GrossUnitPrice<=0;NeedsTax=PendingSetup&&line.Product.Id==0&&needsTax;HasInvalidTax=NeedsTax;
        _nameText=line.Product.Id==0&&line.Product.Name==Localization.LocalizationManager.Instance["Pharmacy_Unlisted"]?"":line.Product.Name;HasInvalidName=NeedsName&&string.IsNullOrWhiteSpace(_nameText);
        _priceText = line.GrossUnitPrice.ToString("0.00", CultureInfo.InvariantCulture);
        HasInvalidPrice=line.MedicineUnit is not null&&line.GrossUnitPrice<=0;
        UpdateAvailability(line.Product);
    }

    public string Name => Line.BatchLot is { } lot ? $"{Line.Product.Name} · {lot}" : Line.Product.Name;
    public string UnitText => Localization.LocalizationManager.Instance["Measure_"+Line.UnitCode];
    public string LineTotalText => MoneyText.Format(Line.GrossTotal);
    public string? DiscountText => Line.LineDiscount?.Describe();
    public decimal AvailableStock { get; private set; }
    public bool IsAvailable { get; private set; }
    public record SellingUnitOption(string Code,string Name);
    public SellingUnitOption[] SellingUnits=>(CanChooseBaseUnit?new[]{"piece","tablet","capsule","bottle","tube","sachet","vial","other"}:new[]{Line.Product.SellingUnit}.Concat(Line.Product.UnitsPerStrip>1?["strip"]:Array.Empty<string>()).Concat(Line.Product.UnitsPerPackage>1?[Line.Product.PurchaseUnit]:Array.Empty<string>())).Select(u=>new SellingUnitOption(u,Localization.LocalizationManager.Instance["Measure_"+u])).ToArray();
    public bool HasUnitChoices=>Line.MedicineUnit is not null&&SellingUnits.Length>1;
    public string SelectedSellingUnit
    {
        get=>Line.UnitCode;
        set
        {
            if(value==Line.UnitCode)return;
            try
            {
                var enteredPrice=Line.GrossUnitPrice;var choosingBase=CanChooseBaseUnit;if(choosingBase)Line.Product.ConfigureMedicineUnits(value);
                var quantity=Line.DisplayQuantity*Line.Product.SellingUnitSize(value);
                if(!_canSetQuantity(this,quantity)){OnPropertyChanged();return;}
                Line.SetSellingUnit(value);if(choosingBase&&enteredPrice>0)Line.SetUnitPrice(enteredPrice);PendingSetup=Line.GrossUnitPrice<=0||PendingSetup;RefreshPrice();Refresh();OnPropertyChanged();OnPropertyChanged(nameof(UnitText));EditError=null;_onChanged();
            }
            catch(DomainException error){EditError=Localization.LocalizationManager.Instance[error.Message];OnPropertyChanged();}
        }
    }
    public void SetLocationStock(decimal? quantity){LocationStock=quantity;}
    public void CompleteSetup(){PendingSetup=false;OnPropertyChanged(nameof(NeedsName));OnPropertyChanged(nameof(Name));OnPropertyChanged(nameof(HasUnitChoices));OnPropertyChanged(nameof(SellingUnits));}
    private void RefreshPrice(){_synchronizing=true;try{PriceText=Line.GrossUnitPrice.ToString("0.00",CultureInfo.InvariantCulture);}finally{_synchronizing=false;}HasInvalidPrice=Line.MedicineUnit is not null&&Line.GrossUnitPrice<=0;}

    public void UpdateAvailability(Product? product)
    {
        var batch = Line.BatchId is { } id ? product?.Batches.SingleOrDefault(item => item.Id == id) : null;
        AvailableStock = Line.BatchId is null ? product?.StockQuantity ?? 0m : batch?.Quantity ?? 0m;
        IsAvailable = product?.IsActive == true && (Line.BatchId is null ? !product.TrackExpiry :
            batch is not null && batch.HoldReason.Length==0 && batch.ExpiryDate == Line.BatchExpiry && batch.ExpiryDate >= DateOnly.FromDateTime(DateTime.Today));
    }

    partial void OnPriceTextChanged(string value)
    {
        if(_synchronizing)return;
        if(!CanEditPrice){RefreshPrice();return;}
        HasInvalidPrice = true;
        if (decimal.TryParse(value, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var price))
        {
            try
            {
                if(Line.MedicineUnit is not null&&price<=0)throw new DomainException("Pharmacy_ConfirmUnitPrice");
                Line.SetUnitPrice(price);
                HasInvalidPrice = false;
                OnPropertyChanged(nameof(LineTotalText));
            }
            catch (Exception ex) when (ex is DomainException or OverflowException) { }
        }
        _onChanged();
    }
    partial void OnTaxTextChanged(string value)
    {
        if(!NeedsTax)return;HasInvalidTax=true;
        if(decimal.TryParse(value,System.Globalization.NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var rate)&&rate is >=0 and <=100){Line.Product.SetVatRate(rate/100);HasInvalidTax=false;}
        _onChanged();
    }
    partial void OnNameTextChanged(string value)
    {
        if(!NeedsName)return;HasInvalidName=string.IsNullOrWhiteSpace(value)||value.Trim().Length>200||value.Any(char.IsControl);
        if(!HasInvalidName){Line.Product.SetName(value);OnPropertyChanged(nameof(Name));}_onChanged();
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
