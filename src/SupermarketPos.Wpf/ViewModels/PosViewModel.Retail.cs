using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Views;
namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PosViewModel
{
    private readonly RetailInventory _retailInventory;
    private readonly RetailReferenceCatalog _retailCatalog;
    private readonly RetailCheckout _retailCheckout;
    private readonly DigitalServiceLedger _digitalLedger;
    private IReadOnlyList<ServiceProviderAccount> _serviceProviders=[];
    private IReadOnlyList<MobileServicePackage> _servicePackages=[];
    [ObservableProperty]private string _catalogKind="all";
    [ObservableProperty]private string _registerSort="name";
    public CartLineViewModel.RetailChoice[] CatalogKinds=>new[]{"all","labeled","loose"}.Select(k=>new CartLineViewModel.RetailChoice(k,Text["Retail_"+k])).ToArray();
    public CartLineViewModel.RetailChoice[] RegisterSorts=>new[]{"name","recent","price"}.Select(k=>new CartLineViewModel.RetailChoice(k,Text["Retail_Sort_"+k])).ToArray();
    public bool CanQuickAddProduct=>!IsPharmacy&&_auth.CurrentUser?.IsAdmin==true&&SearchText.Trim().Length>0&&ProductTiles.Count==0;
    public string QuickAddCaption=>Text.Format("Retail_AddNamed",SearchText.Trim());
    public ObservableCollection<RetailReference> RetailReferenceMatches{get;}=[];
    private int _retailSearchVersion;
    partial void OnCatalogKindChanged(string value)=>ApplyRegisterFilter();
    partial void OnRegisterSortChanged(string value){_registerPreferences.Write(new(RegisterList,value));ApplyRegisterFilter();}
    private async Task RefreshRetailReferencesAsync(string value)
    {
        var version=++_retailSearchVersion;
        if(IsPharmacy||_business.RetailTemplate!="pakistan-tech"||value.Trim().Length<2||ProductTiles.Count>0){RetailReferenceMatches.Clear();return;}
        try
        {
            await Task.Delay(150);if(version!=_retailSearchVersion)return;
            var references=await _retailCatalog.SearchAsync(value,"model",8);

            if(version!=_retailSearchVersion)return;RetailReferenceMatches.Clear();
            foreach(var row in references)RetailReferenceMatches.Add(row);
        }
        catch(Exception e){System.Diagnostics.Trace.TraceWarning("Retail reference search: {0}",e.Message);}
    }
    [RelayCommand]private async Task QuickAddProductAsync()
    {
        if(!CanQuickAddProduct||!CanReadBarcode())return;
        try
        {
            var product=await _retailInventory.QuickAddAsync(SearchText.Trim(),CatalogKind=="loose"?"loose":"labeled");
            RegisterSort="recent";CatalogKind="all";SearchText="";await InitializeAsync();await AddFromResultsCommand.ExecuteAsync(product);
        }
        catch(Exception e){Status=Text[e.Message];}
    }
    [RelayCommand]private async Task UseRetailReferenceAsync(RetailReference? reference)
    {
        if(reference is null||!CanReadBarcode())return;
        try
        {
            Product? product;
            product=await _retailInventory.QuickAddAsync(reference.Name,"labeled",reference);
            if(product is null)return;RegisterSort="recent";CatalogKind="all";SearchText="";await InitializeAsync();await AddFromResultsCommand.ExecuteAsync(product);
        }
        catch(Exception e){Status=Text[e.Message];}
    }
}
