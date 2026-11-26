using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;
public sealed partial class PosViewModel
{
    public string CollectionTitle => SelectedCategoryId != 0
        ? Categories.FirstOrDefault(tile => tile.Category.Id == SelectedCategoryId)?.Name ?? Text["Pos_AllProducts"]
        : string.IsNullOrWhiteSpace(SearchText) ? Text["Pos_AllProducts"] : Text["Pos_SearchResults"];
    public string CollectionCount => Text.Format("Pos_ProductCount", ProductTiles.Count);
    private const int CardPageSize=24;
    private IReadOnlyList<Product> _registerProducts=[];
    private readonly LayoutPreferences<RegisterPreference> _registerPreferences;
    private sealed record RegisterPreference(bool List);
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowRegisterCards),nameof(ShowRegisterList))]private bool _registerList;
    public bool ShowRegisterCards=>ShowResults&&!RegisterList&&ContextPanel is null;
    public bool ShowRegisterList=>ShowResults&&RegisterList&&ContextPanel is null;
    public bool ShowPendingTax=>Lines.Any(l=>l.NeedsTax&&l.PendingSetup);
    [ObservableProperty]private int _productPage;
    public RegisterProductTile[] CardTiles=>ProductTiles.Skip(ProductPage*CardPageSize).Take(CardPageSize).ToArray();
    public bool HasProductPages=>!RegisterList&&ProductTiles.Count>CardPageSize;
    public string ProductPageText=>$"{ProductPage+1} / {Math.Max(1,(ProductTiles.Count+CardPageSize-1)/CardPageSize)}";
    partial void OnProductPageChanged(int value){OnPropertyChanged(nameof(CardTiles));OnPropertyChanged(nameof(ProductPageText));}
    [RelayCommand]private void PreviousProducts(){if(ProductPage>0)ProductPage--;}
    [RelayCommand]private void NextProducts(){if((ProductPage+1)*CardPageSize<ProductTiles.Count)ProductPage++;}
    partial void OnRegisterListChanged(bool value){_registerPreferences.Write(new(value));OnPropertyChanged(nameof(HasProductPages));}
    [RelayCommand]private void ShowCards()=>RegisterList=false;
    [RelayCommand]private void ShowList()=>RegisterList=true;
    [RelayCommand]private void SelectAllCategory(){SelectedCategoryId=0;ApplyRegisterFilter();}
    partial void OnShowResultsChanged(bool value){OnPropertyChanged(nameof(ShowRegisterCards));OnPropertyChanged(nameof(ShowRegisterList));}
    private void ApplyRegisterFilter()
    {
        var term=SearchText.Trim();ShowProductTiles(_registerProducts.Where(p=>(SelectedCategoryId==0||p.CategoryId==SelectedCategoryId)&&(term.Length==0||p.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase)||p.Brand.Contains(term,StringComparison.CurrentCultureIgnoreCase)||p.Model.Contains(term,StringComparison.CurrentCultureIgnoreCase)||p.Sku.Contains(term,StringComparison.OrdinalIgnoreCase)||p.MedicineGeneric.Contains(term,StringComparison.CurrentCultureIgnoreCase)||p.Barcode.Contains(term,StringComparison.OrdinalIgnoreCase))).ToArray());
    }
    partial void OnSearchTextChanged(string value)=>ApplyRegisterFilter();
    [RelayCommand]private async Task SelectMedicineAsync(MedicineMatch match)
    {
        if(!IsPharmacy||!CanReadBarcode())return;
        if(match.ProductId is{} id){if(await _products.GetCurrentForRegisterAsync(id) is{} existing)await AddFromResultsAsync(existing);return;}
        if(!CanReadBarcode())return;
        AddProvisionalMedicine(match,match.Name,"");
        if(SelectedCartLine is { } selected)ManualProductAdded?.Invoke(selected);
        await Task.CompletedTask;
    }
    private void AddProvisionalMedicine(MedicineMatch? match,string name,string barcode)
    {
        var existing=Lines.FirstOrDefault(l=>l.Line.Product.Id==0&&(match?.ReferenceId is{} id?l.Line.Product.MedicineReferenceId==id:barcode.Length>0&&l.Line.Product.Barcode==barcode));
        if(existing is not null){existing.Quantity++;SelectedCartLine=existing;return;}
        var p=new Product(barcode,string.IsNullOrWhiteSpace(name)?Text["Pharmacy_Unlisted"]:name,0,0,Unit.Piece,0,Categories.FirstOrDefault()?.Category.Id??1);p.MarkStockUntracked();
        if(match?.ReferenceId is{} reference)p.LinkMedicine(reference);
        if(match is not null)p.SetMedicineDetails(match.GenericName,match.Strength,match.Formulation,match.Manufacturer);
        var line=_cart.AddOrIncrement(p,1,medicineUnit:p.SellingUnit);SelectedCartLine=CreateCartLine(line);Lines.Add(SelectedCartLine);Recalculate();Status=Text["Pharmacy_EnterCartDetails"];
    }
    [RelayCommand]private void UseMedicineName()
    {
        if(!IsPharmacy||!CanReadBarcode()||string.IsNullOrWhiteSpace(SearchText))return;
        AddProvisionalMedicine(null,SearchText,"");
        if(SelectedCartLine is { } selected)ManualProductAdded?.Invoke(selected);
    }
    private CartLineViewModel CreateCartLine(CartLine line)=>new(line,Recalculate,ApproveCartQuantity,_auth.CurrentUser?.IsAdmin==true,_business.SalesTaxRegistered);
    private async Task CompletePendingMedicineSetupAsync()
    {
        foreach(var line in Lines.Where(l=>l.PendingSetup).ToArray())
        {
            if(line.HasInvalidName||line.HasInvalidPrice||line.HasInvalidTax)throw new DomainException("Pharmacy_EnterCartDetails");
            var product=line.Line.Product;
            if(product.Id==0)
            {
                var saved=await _pharmacyInventory.QuickAddAsync(new(product.MedicineReferenceId,product.Name,product.Barcode.StartsWith("INTERNAL-",StringComparison.Ordinal)?"":product.Barcode,line.Line.UnitCode,line.Line.GrossUnitPrice,line.Line.VatRate,false));
                line.Line.BindSavedProduct(await _products.GetCurrentForRegisterAsync(saved.Id)??throw new DomainException("Pharmacy_InvalidProduct"));
            }
            else await _pharmacyInventory.ConfirmSaleSetupAsync(product.Id,line.Line.UnitCode,line.Line.GrossUnitPrice,line.Line.VatRate);
            line.CompleteSetup();
        }
    }
    private async Task RefreshLocationLimitsAsync(int productId)
    {
        foreach(var group in Lines.Where(l=>l.Line.Product.Id==productId&&l.Line.LocationId is not null).GroupBy(l=>l.Line.BatchId))
        {
            var places=await _stockLocations.StockAsync(productId,group.Key);
            foreach(var line in group)line.SetLocationStock(line.Line.LocationId==0?Math.Max(0,line.AvailableStock-places.Sum(p=>(decimal)p.BaseQuantity)):places.Where(p=>p.Id==line.Line.LocationId).Sum(p=>(decimal)p.BaseQuantity));
        }
    }
    partial void OnContextPanelChanged(System.Windows.FrameworkElement? value){OnPropertyChanged(nameof(ShowRegisterCards));OnPropertyChanged(nameof(ShowRegisterList));}
}
