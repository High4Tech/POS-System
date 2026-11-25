using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Entities;
namespace SupermarketPos.Wpf.ViewModels;
public sealed partial class ProductsViewModel
{
    private readonly MedicineCatalogService _medicineCatalog;
    private readonly PharmacyInventory _pharmacyInventory;
    private readonly StockLocationService _stockLocations;
    private readonly LayoutPreferences<InventoryViewPreference> _inventoryPreferences;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowMedicineCards),nameof(ShowInventoryTable))]private bool _medicineCards;
    public bool ShowMedicineCards=>IsPharmacy&&MedicineCards;
    public bool ShowInventoryTable=>!ShowMedicineCards;
    partial void OnMedicineCardsChanged(bool value)=>_inventoryPreferences.Write(new(value));
    private sealed record InventoryViewPreference(bool Cards);
    private bool _quickAddOpen;
    private int? _locationFilterId;
    private HashSet<int>? _locationProductIds;
    [ObservableProperty]private string? _locationFilterName;
    [ObservableProperty]private int _filterCategoryId;
    [ObservableProperty]private string _stockState="all";
    [ObservableProperty]private string _expiryState="all";
    [ObservableProperty]private bool _readyOnly;
    [ObservableProperty]private string _batchFilter="";
    [ObservableProperty]private string _manufacturerFilter="";
    [ObservableProperty]private string _formFilter="";
    [ObservableProperty]private string _barcodeState="all";
    [ObservableProperty]private int _selectedLocationFilter;
    [ObservableProperty]private IReadOnlyList<ProductEditViewModel.ProductLocationOption> _locationFilters=[];
    private Dictionary<int,long>? _locationQuantities;
    private Dictionary<int,long>? _locationBatchQuantities;
    private int _locationFilterVersion;
    private bool _applyingLocationFilter;
    public BusinessForm.Choice[] BarcodeStates=>new[]{("all","Pharmacy_AllBarcodes"),("has","Pharmacy_HasBarcode"),("missing","Pharmacy_MissingBarcode")}.Select(c=>new BusinessForm.Choice(c.Item1,Localization.LocalizationManager.Instance[c.Item2])).ToArray();
    public BusinessForm.Choice[] BatchFilters=>new[]{new BusinessForm.Choice("",Localization.LocalizationManager.Instance["Pharmacy_AllBatches"])}.Concat(Products.SelectMany(r=>r.Product.Batches).Select(b=>b.Lot).Distinct().Order().Select(l=>new BusinessForm.Choice(l,l))).ToArray();
    public BusinessForm.Choice[] ManufacturerFilters=>TextFilters(Products.Select(r=>r.Manufacturer),"Pharmacy_AllManufacturers");
    public BusinessForm.Choice[] FormFilters=>TextFilters(Products.Select(r=>r.Formulation),"Pharmacy_AllForms");
    private static BusinessForm.Choice[] TextFilters(IEnumerable<string> values,string title)=>new[]{new BusinessForm.Choice("",Localization.LocalizationManager.Instance[title])}.Concat(values.Where(v=>v.Length>0).Distinct().Order().Select(v=>new BusinessForm.Choice(v,v))).ToArray();
    public string FilterSummary
    {
        get
        {
            var rows=VisibleProducts.Cast<ProductRowViewModel>().ToArray();var groups=rows.Where(r=>r.Product.StockTracked).GroupBy(r=>r.UnitText).Select(g=>$"{g.Sum(r=>SupermarketPos.Domain.Pricing.MeasuredGoods.Quantity(ScopedQuantity(r.Product),r.Product.Unit)):0.###} {g.Key}");
            var batches=rows.SelectMany(r=>r.Product.Batches).Count(b=>MatchesBatch(b)&&ScopedBatchQuantity(b)>0);
            return (LocationFilterName is{} location?location+" · ":"")+Localization.LocalizationManager.Instance.Format("Pharmacy_FilterSummary",rows.Length,batches)+" · "+string.Join(" · ",groups);
        }
    }
    private bool MatchesBatch(InventoryBatch b)
    {
        var today=DateOnly.FromDateTime(DateTime.Today);
        return (BatchFilter.Length==0||b.Lot==BatchFilter)&&(ExpiryState switch{"expired"=>b.ExpiryDate<today,"soon"=>b.ExpiryDate>=today&&b.ExpiryDate<=today.AddDays(30),"valid"=>b.ExpiryDate>=today&&b.HoldReason.Length==0,_=>true});
    }
    private long ScopedBatchQuantity(InventoryBatch b)=>_locationBatchQuantities is null?b.BaseQuantity:_locationFilterId==-1?Math.Max(0,b.BaseQuantity-_locationBatchQuantities.GetValueOrDefault(b.Id)):_locationBatchQuantities.GetValueOrDefault(b.Id);
    private long ScopedQuantity(Product p)=>p.TrackExpiry&&(BatchFilter.Length>0||ExpiryState!="all")?p.Batches.Where(MatchesBatch).Sum(ScopedBatchQuantity):_locationQuantities is null?p.BaseStockQuantity:_locationFilterId==-1?Math.Max(0,p.BaseStockQuantity-_locationQuantities.GetValueOrDefault(p.Id)):_locationQuantities.GetValueOrDefault(p.Id);
    partial void OnReadyOnlyChanged(bool value){if(value)NeedsSetupOnly=false;RefreshPharmacyFilter();}
    partial void OnBatchFilterChanged(string value)=>RefreshPharmacyFilter();
    partial void OnManufacturerFilterChanged(string value)=>RefreshPharmacyFilter();
    partial void OnFormFilterChanged(string value)=>RefreshPharmacyFilter();
    partial void OnBarcodeStateChanged(string value)=>RefreshPharmacyFilter();
    partial void OnSelectedLocationFilterChanged(int value){if(!_applyingLocationFilter)_=ApplyLocationFilterAsync(value);}
    private void RefreshPharmacyFilter(){VisibleProducts.Refresh();OnPropertyChanged(nameof(FilterSummary));}
    public IReadOnlyList<Category> FilterCategories=>new[]{new Category(Localization.LocalizationManager.Instance["Pharmacy_AllCategories"])}.Concat(_categoryList).ToArray();
    public BusinessForm.Choice[] StockStates=>new[]{("all","Pharmacy_AllStock"),("in","Pharmacy_InStock"),("out","Pharmacy_OutOfStock"),("untracked","Pharmacy_StockUntracked")}.Select(c=>new BusinessForm.Choice(c.Item1,Localization.LocalizationManager.Instance[c.Item2])).ToArray();
    public BusinessForm.Choice[] ExpiryStates=>new[]{("all","Pharmacy_AllExpiry"),("expired","Batch_ExpiredStatus"),("soon","Batch_ExpiringSoon"),("valid","Batch_Ready")}.Select(c=>new BusinessForm.Choice(c.Item1,Localization.LocalizationManager.Instance[c.Item2])).ToArray();
    partial void OnFilterCategoryIdChanged(int value)=>RefreshPharmacyFilter();
    partial void OnStockStateChanged(string value)=>RefreshPharmacyFilter();
    partial void OnExpiryStateChanged(string value)=>RefreshPharmacyFilter();
    private bool MatchesPharmacyFilters(ProductRowViewModel row)
    {
        var p=row.Product;var today=DateOnly.FromDateTime(DateTime.Today);var available=p.AvailableForSale(today);
        if(ReadyOnly&&row.NeedsSetup||BatchFilter.Length>0&&!p.Batches.Any(b=>b.Lot==BatchFilter)||ManufacturerFilter.Length>0&&row.Manufacturer!=ManufacturerFilter||FormFilter.Length>0&&row.Formulation!=FormFilter)return false;
        var hasBarcode=!p.Barcode.StartsWith("INTERNAL-",StringComparison.Ordinal);
        if(BarcodeState=="has"&&!hasBarcode||BarcodeState=="missing"&&hasBarcode)return false;
        return (FilterCategoryId==0||p.CategoryId==FilterCategoryId)&&(_locationProductIds is null||_locationProductIds.Contains(p.Id))&&(StockState switch{"in"=>p.StockTracked&&available>0,"out"=>p.StockTracked&&available<=0,"untracked"=>!p.StockTracked,_=>true})&&(ExpiryState switch{"expired"=>p.Batches.Any(b=>b.Quantity>0&&b.ExpiryDate<today),"soon"=>p.Batches.Any(b=>b.Quantity>0&&b.ExpiryDate>=today&&b.ExpiryDate<=today.AddDays(30)),"valid"=>p.Batches.Any(b=>b.Quantity>0&&b.ExpiryDate>=today&&b.HoldReason.Length==0),_=>true});
    }
    private async Task<SupermarketPos.Infrastructure.Scanning.ScanOutcome> FilterLocationAsync(string code,Guid? epoch=null)
    {
        if(!IsPharmacy||await _stockLocations.FindAsync(code) is not{} location){ProductError=Localization.LocalizationManager.Instance["Pharmacy_SelectStockLocation"];return new("invalid");}
        if(!await ApplyLocationFilterAsync(location.Id,epoch,location.Name))return new("paused");return new("location_selected",location.Name);
    }
    private async Task<bool> ApplyLocationFilterAsync(int id,Guid? epoch=null,string? name=null)
    {
        var version=++_locationFilterVersion;
        try
        {
            var quantities=id==0?null:await _stockLocations.QuantitiesAtAsync(id>0?id:null);
            var batchQuantities=id==0?null:await _stockLocations.BatchQuantitiesAtAsync(id>0?id:null);
            var ids=id>0?(await _stockLocations.ProductIdsAtAsync(id)).ToHashSet():id<0?Products.Where(r=>r.Product.StockTracked?r.Product.BaseStockQuantity>quantities!.GetValueOrDefault(r.Id)||r.Product.DefaultLocationId is null&&quantities!.GetValueOrDefault(r.Id)==0:r.Product.DefaultLocationId is null).Select(r=>r.Id).ToHashSet():null;
            if(version!=_locationFilterVersion||epoch is{} active&&!_scanner.CanApply(this,active))return false;
            _locationFilterId=id==0?null:id;_locationProductIds=ids;_locationQuantities=quantities;_locationBatchQuantities=batchQuantities;LocationFilterName=id==0?null:name??LocationFilters.FirstOrDefault(l=>l.Id==id)?.Name;_applyingLocationFilter=true;try{SelectedLocationFilter=id;}finally{_applyingLocationFilter=false;}RefreshPharmacyFilter();return true;
        }
        catch{ProductError=Localization.LocalizationManager.Instance["Pharmacy_SearchError"];return false;}
    }
    [RelayCommand]private void ClearLocationFilter(){SelectedLocationFilter=0;_locationFilterVersion++;_locationFilterId=null;_locationProductIds=null;_locationQuantities=null;_locationBatchQuantities=null;LocationFilterName=null;RefreshPharmacyFilter();}
    [RelayCommand]private void ClearPharmacyFilters(){ClearLocationFilter();Filter="";FilterCategoryId=0;StockState=ExpiryState=BarcodeState="all";BatchFilter=ManufacturerFilter=FormFilter="";ReadyOnly=NeedsSetupOnly=LowStockOnly=false;RefreshPharmacyFilter();}
    [ObservableProperty]private FrameworkElement? _medicineNameSearch;
    [ObservableProperty]private FrameworkElement? _inventoryMedicineSearch;
    [ObservableProperty]private bool _needsSetupOnly;
    partial void OnNeedsSetupOnlyChanged(bool value){if(value)ReadyOnly=false;RefreshPharmacyFilter();}
    [RelayCommand]private async Task ManageStockLocationsAsync()
    {
        if(!IsPharmacy)return;_quickAddOpen=true;UpdateInventoryWorkflow();Action close=()=>{};
        try{await InAppDialog.ShowAsync(new StockLocationsPanel(_stockLocations,_products,_storage,()=>close()),c=>close=c,contentWidth:1000);}
        finally{_quickAddOpen=false;UpdateInventoryWorkflow();await RefreshAsync();}
    }
    [RelayCommand]private async Task VerifyMedicineStockAsync()
    {
        if(!IsPharmacy)return;_quickAddOpen=true;UpdateInventoryWorkflow();Action close=()=>{};
        try
        {
            var panel=new BusinessForm();var choices=(await _products.GetAllAsync()).Where(p=>!p.StockTracked).ToArray();var select=new ComboBox{ItemsSource=choices,DisplayMemberPath="Name",IsEditable=true};var quantity=new TextBox{FlowDirection=FlowDirection.LeftToRight};var note=new TextBox{MaxLength=1000};var error=new TextBlock{TextWrapping=TextWrapping.Wrap};
            panel.Add("Pharmacy_VerifyPhysicalStock",select,true);panel.Add("Products_Stock",quantity);panel.Add("Pharmacy_VerificationNote",note);panel.Add("",error,true);
            panel.Add("",BusinessForm.Button("Common_Save",async(sender,_)=>
            {
                ((Button)sender).IsEnabled=false;
                try{if(select.SelectedItem is not SupermarketPos.Domain.Entities.Product p||!decimal.TryParse(quantity.Text,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var count))throw new SupermarketPos.Domain.Common.DomainException("Pharmacy_InvalidProduct");await _pharmacyInventory.VerifyStockAsync(p.Id,count,note.Text);close();}
                catch(Exception ex){error.Text=Localization.LocalizationManager.Instance[ex is SupermarketPos.Domain.Common.DomainException?ex.Message:"Pharmacy_SaveError"];}
                finally{((Button)sender).IsEnabled=true;}
            }));panel.Add("",BusinessForm.Button("Common_Close",(_,_)=>close(),"GhostButton"));await InAppDialog.ShowAsync(panel,c=>close=c,contentWidth:1000);
        }
        finally{_quickAddOpen=false;UpdateInventoryWorkflow();await RefreshAsync();}
    }
    private void PrepareMedicineSearch()
    {
        if(!IsPharmacy||MedicineNameSearch is not null)return;
        var editor=new MedicineSearchBox(_medicineCatalog);editor.Input.SetBinding(TextBox.TextProperty,new System.Windows.Data.Binding(nameof(Editor.Name)){Source=Editor,Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged});
        editor.Selected+=async match=>{if(match.ProductId is{} id)await OpenProductAsync(id);else{Editor.MedicineReferenceId=match.ReferenceId;Editor.Name=match.Name;}};MedicineNameSearch=editor;
        var search=new MedicineSearchBox(_medicineCatalog);search.Input.SetBinding(TextBox.TextProperty,new System.Windows.Data.Binding(nameof(Filter)){Source=this,Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged});search.Selected+=async match=>{if(match.ProductId is not null)Filter=match.Name;else await QuickAddMedicineAsync("",match.Name,match.ReferenceId);};search.BarcodeSubmitted+=async code=>{await _scanner.DispatchPhysicalAsync(code);search.Input.Clear();};InventoryMedicineSearch=search;
    }
    public async Task<int?> CreateMedicineForPurchaseAsync(string barcode,string name,int? reference=null,int? supplierId=null)
    {
        await LoadEditorOptionsAsync(await _settings.GetAsync());ProductSaved=false;SavedProductId=null;await QuickAddMedicineAsync(barcode,name,reference,purchasing:true);
        if(IsEditorOpen){Editor.SupplierId=supplierId??0;new ScannedProductWindow(this){Owner=System.Windows.Application.Current.MainWindow}.ShowDialog();}
        return ProductSaved?SavedProductId:null;
    }
    private async Task QuickAddMedicineAsync(string barcode,string name="",int? reference=null,bool purchasing=false)
    {
        if(_quickAddOpen)return;_quickAddOpen=true;UpdateInventoryWorkflow();Action close=()=>{};bool details=false;string fullName=name,fullCode=barcode;int? fullReference=reference;PharmacyQuickAddPanel? panel=null;
        try
        {
            panel=new PharmacyQuickAddPanel(_medicineCatalog,_pharmacyInventory,barcode,
                p=>{SavedProductId=p.Id;ProductSaved=true;close();},(n,c,r)=>{details=true;fullName=n;fullCode=c;fullReference=r;close();},()=>close(),name,reference,purchasing,_scanner);
            await InAppDialog.ShowAsync(panel,c=>close=c,contentWidth:1000);
        }
        finally{_quickAddOpen=false;UpdateInventoryWorkflow();}
        if(details){OpenNewProduct(fullCode,scannerEntry:purchasing,purchasing:purchasing);Editor.Name=fullName;Editor.MedicineReferenceId=fullReference;if(panel?.ScannedTracking is{} gs1){Editor.TrackExpiry=true;Editor.BatchLot=gs1.Batch??"";Editor.BatchExpiry=gs1.ExpiryDate?.ToDateTime(TimeOnly.MinValue);}}
        else await RefreshAsync();
    }
}
