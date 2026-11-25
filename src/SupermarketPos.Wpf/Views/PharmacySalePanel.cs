using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

// Only product-specific traceability belongs here; ordinary selection and edits stay in the cart.
public sealed class PharmacySalePanel:UserControl
{
    public MedicineSearchBox Search{get;}
    public static readonly DependencyProperty HasPendingEntryProperty=DependencyProperty.Register(nameof(HasPendingEntry),typeof(bool),typeof(PharmacySalePanel),new PropertyMetadata(false,(d,e)=>((PharmacySalePanel)d).PendingChanged?.Invoke()));
    public event Action? PendingChanged;
    public bool HasPendingEntry{get=>(bool)GetValue(HasPendingEntryProperty);private set=>SetValue(HasPendingEntryProperty,value);}
    private readonly IProductRepository _products;
    private readonly StockLocationService _locations;
    private readonly Func<Product,decimal,string,InventoryBatch?,bool,string?,int?,string?,Task<bool>> _commit;
    private readonly ComboBox _batch=new(){DisplayMemberPath="Lot"},_location=new(){DisplayMemberPath="Name",SelectedValuePath="Id"};
    private readonly CheckBox _prescription=new();
    private readonly TextBox _reference=new(){MaxLength=150,FlowDirection=FlowDirection.LeftToRight};
    private readonly TextBlock _name=new(){TextWrapping=TextWrapping.Wrap},_error=new(){TextWrapping=TextWrapping.Wrap};
    private Product? _product;private string _unit="",_serial="";private bool _busy,_requiresTracking;
    private static string T(string key)=>LocalizationManager.Instance[key];
    public PharmacySalePanel(MedicineCatalogService catalog,PharmacyInventory inventory,IProductRepository products,StockLocationService locations,Func<Product,decimal,string,InventoryBatch?,bool,string?,int?,string?,Task<bool>> commit)
    {
        Search=new(catalog);_products=products;_locations=locations;_commit=commit;
        var form=new BusinessForm();form.Add("",_name,true);form.Add("Batch_Lot",_batch);form.Add("Pharmacy_StockLocation",_location);
        _prescription.Content=BusinessForm.Label("Pharmacy_PrescriptionChecked");form.Add("",_prescription);form.Add("Pharmacy_DispensingReference",_reference);
        form.Add("",_error,true);form.Add("",BusinessForm.Button("Common_Cancel",(_,_)=>CancelEntry(),"GhostButton"));form.Add("",BusinessForm.Button("Measure_Add",async(_,_)=>await AddAsync()));
        Content=form;_batch.SelectionChanged+=async(_,_)=>{if(!_busy)await ShowLocationsAsync();};
    }
    public async Task SelectAsync(MedicineMatch? match,string name,string barcode="",Gs1Barcode? gs1=null,bool packaged=false)
    {
        if(_busy)return;_busy=true;
        try
        {
            _product=match?.ProductId is{} id?await _products.GetCurrentForRegisterAsync(id):null;
            if(_product is not{} p){CancelEntry();return;}
            HasPendingEntry=true;_name.Text=p.Name;_error.Text="";_reference.Text="";_prescription.IsChecked=false;_serial=gs1?.SerialNumber??"";
            _unit=packaged?p.PurchaseUnit:gs1 is not null?p.SellingUnit:p.DefaultSellingUnit;_requiresTracking=gs1?.HasTrackingData()==true;
            Show(_prescription,p.RequiresPrescription);Show(_reference,p.RequiresDispensingReference);Show(_batch,p.TrackExpiry);
            _batch.ItemsSource=p.Batches.Where(b=>b.HoldReason.Length==0&&b.Quantity>0&&b.ExpiryDate>=DateOnly.FromDateTime(DateTime.Today)&&(gs1?.Batch is null||b.Lot==gs1.Batch)&&(gs1?.ExpiryDate is null||b.ExpiryDate==gs1.ExpiryDate)).OrderBy(b=>b.ExpiryDate).ToArray();_batch.SelectedIndex=-1;
            if(gs1?.Batch is not null&&_batch.Items.Count==1)_batch.SelectedIndex=0;
            if(gs1?.HasTrackingData()==true&&(!p.TrackExpiry||_batch.Items.Count==0))_error.Text=T("Pharmacy_TraceabilityRequired");
            await ShowLocationsAsync();
        }
        catch(Exception ex){_error.Text=T(ex is DomainException?ex.Message:"Pharmacy_SearchError");}
        finally{_busy=false;}
    }
    private async Task ShowLocationsAsync()
    {
        if(_product is not{} p||!p.StockTracked||p.TrackExpiry&&_batch.SelectedItem is not InventoryBatch){Show(_location,false);_location.ItemsSource=null;return;}
        var batch=_batch.SelectedItem as InventoryBatch;var places=await _locations.StockAsync(p.Id,batch?.Id);var remainder=(batch?.BaseQuantity??p.BaseStockQuantity)-places.Sum(l=>l.BaseQuantity);
        var choices=places.ToList();if(remainder>0)choices.Add(new(0,T("Pharmacy_Unassigned"),remainder));_location.ItemsSource=choices;_location.SelectedIndex=choices.Count==1?0:-1;Show(_location,choices.Count>1);
    }
    private async Task AddAsync()
    {
        if(_busy||_product is not{} p)return;_busy=true;
        try
        {
            if(_requiresTracking&&(!p.TrackExpiry||_batch.SelectedItem is not InventoryBatch)||p.RequiresPrescription&&_prescription.IsChecked!=true||p.RequiresDispensingReference&&string.IsNullOrWhiteSpace(_reference.Text)||p.TrackExpiry&&_batch.SelectedItem is not InventoryBatch||_location.Items.Count>1&&_location.SelectedItem is null)throw new DomainException("Pharmacy_TraceabilityRequired");
            if(await _commit(p,p.SellingUnitSize(_unit),_unit,_batch.SelectedItem as InventoryBatch,_prescription.IsChecked==true,_reference.Text,_location.SelectedValue is int id?id:null,_serial.Length>0?_serial:null))CancelEntry();
        }
        catch(Exception ex){_error.Text=T(ex is DomainException?ex.Message:"Pharmacy_SaveError");}
        finally{_busy=false;}
    }
    public void CancelEntry(){_product=null;HasPendingEntry=false;_error.Text="";Search.Input.Focus();}
    private static void Show(FrameworkElement field,bool visible)=>((FrameworkElement)field.Parent).Visibility=visible?Visibility.Visible:Visibility.Collapsed;
}
