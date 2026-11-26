using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Views;
namespace SupermarketPos.Wpf.ViewModels;
public sealed partial class ProductsViewModel
{
    private readonly RetailInventory _retailInventory;
    private readonly RetailReferenceCatalog _retailCatalog;
    [RelayCommand]private async Task BrowseRetailTemplateAsync()
    {
        if(IsPharmacy||!CanManageStock)return;_quickAddOpen=true;UpdateInventoryWorkflow();Action close=()=>{};
        try{await InAppDialog.ShowAsync(new RetailTemplatePanel(_retailCatalog,_retailInventory,async _=>{close();await Task.CompletedTask;}),c=>close=c,contentWidth:1000);}
        finally{_quickAddOpen=false;UpdateInventoryWorkflow();await RefreshAsync();}
    }
    [RelayCommand]private async Task LoadRetailCategoriesAsync()
    {
        if(IsPharmacy||!CanManageStock)return;
        try{var count=await _retailInventory.LoadCategoryTemplateAsync();ImportMessage=count+" · "+Localization.LocalizationManager.Instance["Settings_Saved"];await RefreshAsync();}
        catch(Exception e){ImportError=Localization.LocalizationManager.Instance[e.Message];}
    }
}
