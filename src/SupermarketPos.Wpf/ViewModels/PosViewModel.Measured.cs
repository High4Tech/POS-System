using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Application.Services;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;
public sealed partial class PosViewModel
{
    private BusinessProfile _business=new();
    public bool IsPharmacy=>_business.IsPharmacy;
    public bool ShowProductImages=>_business.EnableCatalogImages;
    public bool EnableLooseGoods=>_business.LooseGoodsAvailable;
    public bool EnablePackagedUnits=>_business.PackagingAvailable;
    public bool EnableGoodsPanel=>!IsPharmacy&&(EnableLooseGoods||EnablePackagedUnits);
    public bool EnableDigitalServices=>_business.DigitalWalletAvailable||_business.MobileRechargeAvailable;
    public bool EnableMobileRecharge=>_business.MobileRechargeAvailable;
    [ObservableProperty] private System.Windows.FrameworkElement? _contextPanel;
    [RelayCommand] private void CloseContextPanel()=>ContextPanel=null;
    public void OpenDigitalPanel(DigitalServicesView view)=>ContextPanel=ReferenceEquals(ContextPanel,view)?null:view;
    [RelayCommand] private async Task LooseItemsAsync()
    {
        CatalogKind=CatalogKind=="loose"?"all":"loose";ApplyRegisterFilter();await Task.CompletedTask;
    }
    private async Task<bool> AddMeasuredAsync(Product selected,decimal quantity,bool packaged)
    {
        if(!CanReadBarcode())return false;
        _readingBarcode=true;NotifyPaymentAvailability();
        try{
        if(await _products.GetCurrentForRegisterAsync(selected.Id) is not{} current)return false;
        var expected=packaged?current.PackageSellingPrice??current.UnitPrice*current.UnitsPerPackage:current.UnitPrice;
        if(Lines.Any(l=>l.Line.Product.Id==current.Id&&l.Line.IsPackage==packaged&&l.Line.GrossUnitPrice!=expected)){Status=Text["Measure_ExistingPrice"];return false;}
        var result=await ApplyBarcodeAsync(new BarcodeMatch(current,quantity,packaged),current.Barcode);
        return result.Outcome=="added";
        }finally{_readingBarcode=false;NotifyPaymentAvailability();}
    }
}
