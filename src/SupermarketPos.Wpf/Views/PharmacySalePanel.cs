using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

public sealed class PharmacySalePanel:UserControl
{
    public MedicineSearchBox Search{get;}
    public bool HasPendingEntry=>_entry.Visibility==Visibility.Visible&&(_name.Text.Length>0||_barcode.Length>0);
    private readonly BusinessForm _entry=new();
    private readonly TextBlock _name=new(){TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _error=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBox _price=new(){FlowDirection=FlowDirection.LeftToRight};
    private readonly TextBox _vat=new(){FlowDirection=FlowDirection.LeftToRight};
    private readonly TextBox _quantity=new(){Text="1",FlowDirection=FlowDirection.LeftToRight};
    private readonly TextBox _reference=new(){FlowDirection=FlowDirection.LeftToRight};
    private readonly ComboBox _unit=new(){DisplayMemberPath="Name",SelectedValuePath="Code"};
    private readonly ComboBox _batch=new(){DisplayMemberPath="Lot"};
    private readonly ComboBox _location=new(){DisplayMemberPath="Name",SelectedValuePath="Id"};
    private readonly CheckBox _reviewed=new(),_prescription=new(),_controlled=new(),_checked=new();
    private readonly IProductRepository _products;private readonly PharmacyInventory _inventory;
    private readonly Func<Product,decimal,string,InventoryBatch?,bool,string?,int?,string?,Task<bool>> _commit;
    private string? _gs1Serial;
    private readonly StockLocationService _locations;
    private MedicineMatch? _match;private Product? _product;private string _barcode="";private bool _busy;private bool _strictBatch;
    private static string T(string key)=>LocalizationManager.Instance[key];
    public PharmacySalePanel(MedicineCatalogService catalog,PharmacyInventory inventory,IProductRepository products,StockLocationService locations,Func<Product,decimal,string,InventoryBatch?,bool,string?,int?,string?,Task<bool>> commit)
    {
        _products=products;_inventory=inventory;_locations=locations;_commit=commit;Search=new(catalog);var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var searchRow=new DockPanel();var entered=BusinessForm.Button("Pharmacy_UseEnteredName",async(_,_)=>await SelectAsync(null,Search.Input.Text,_barcode),"GhostButton");DockPanel.SetDock(entered,Dock.Right);searchRow.Children.Add(entered);searchRow.Children.Add(Search);root.Children.Add(searchRow);
        var heading=new StackPanel{Margin=new Thickness(6)};heading.Children.Add(_name);heading.Children.Add(_status);var body=new StackPanel();body.Children.Add(heading);body.Children.Add(_entry);
        Search.Selected+=async m=>await SelectAsync(m,m.Name,_barcode);var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);root.Children.Add(scroll);Content=root;_entry.Visibility=Visibility.Collapsed;
        _entry.Add("Pharmacy_SellingUnit",_unit);_entry.Add("Pos_Qty",_quantity);_entry.Add("Products_Price",_price);_entry.Add("Products_Vat",_vat);
        _reviewed.Content=BusinessForm.Label("Pharmacy_RulesReviewed");_prescription.Content=BusinessForm.Label("Pharmacy_RequiresPrescription");_controlled.Content=BusinessForm.Label("Pharmacy_RequiresReference");_checked.Content=BusinessForm.Label("Pharmacy_PrescriptionChecked");
        _entry.Add("",_reviewed,true);_entry.Add("",_prescription);_entry.Add("",_controlled);_entry.Add("Batch_Lot",_batch);_entry.Add("",_checked);_entry.Add("Pharmacy_DispensingReference",_reference,true);
        _entry.Add("Pharmacy_StockLocation",_location);var actions=new StackPanel();actions.Children.Add(_error);var buttons=new WrapPanel();buttons.Children.Add(BusinessForm.Button("Common_Cancel",(_,_)=>CancelEntry(),"GhostButton"));buttons.Children.Add(BusinessForm.Button("Measure_Add",async(_,_)=>await AddAsync()));actions.Children.Add(buttons);Grid.SetRow(actions,2);root.Children.Add(actions);actions.Visibility=Visibility.Collapsed;
        _entry.IsVisibleChanged+=(_,_)=>{actions.Visibility=_entry.Visibility;heading.Visibility=_entry.Visibility;Height=_entry.Visibility==Visibility.Visible?Math.Clamp((Window.GetWindow(this)?.ActualHeight??900)*.58,360,600):double.NaN;};
        _batch.SelectionChanged+=async(_,_)=>{if(!_busy)await ShowLocationsAsync();};
        _prescription.Checked+=(_,_)=>Show(_checked,true);_controlled.Checked+=(_,_)=>Show(_reference,true);
        _unit.SelectionChanged+=(_,_)=>{if(_product is{} p&&_unit.SelectedValue is string u)try{_price.Text=p.ResolveSellingUnit(u).Price.ToString("0.00",CultureInfo.InvariantCulture);}catch(DomainException){_price.Text="";}};
    }
    public async Task SelectAsync(MedicineMatch? match,string name,string barcode="",Gs1Barcode? gs1=null,bool packaged=false)
    {
        if(_busy)return;_busy=true;
        try
        {
            _match=match;_product=match?.ProductId is{} id?await _products.GetCurrentForRegisterAsync(id):null;_barcode=barcode;_gs1Serial=gs1?.SerialNumber;_name.Text=name;_quantity.Text="1";_error.Text="";_reference.Text="";_checked.IsChecked=false;
            var p=_product;_entry.Visibility=Visibility.Visible;_status.Text=T("Pharmacy_"+(match?.Status??"NeedsSetup"))+(p?.StockTracked==true?$" · {p.StockQuantity:0.###}":"");
            if(p is not null&&p.Unit!=Unit.Piece)throw new DomainException("Pharmacy_PackedOnly");
            var units=new List<BusinessForm.Choice>();
            if(p is null)foreach(var u in new[]{"piece","tablet","capsule","bottle","tube","sachet","vial","other"})units.Add(new(u,T("Measure_"+u)));
            else{units.Add(new(p.SellingUnit,T("Measure_"+p.SellingUnit)));if(p.UnitsPerStrip>1)units.Add(new("strip",T("Measure_strip")));if(p.UnitsPerPackage>1)units.Add(new(p.PurchaseUnit,T("Measure_"+p.PurchaseUnit)));}
            _unit.ItemsSource=units;_unit.SelectedIndex=p is null?-1:0;Show(_unit,units.Count>1);Grid.SetColumn((FrameworkElement)_quantity.Parent,units.Count>1?1:0);Grid.SetColumnSpan((FrameworkElement)_quantity.Parent,units.Count>1?1:2);_price.Text=p?.UnitPrice>0?p.UnitPrice.ToString("0.00",CultureInfo.InvariantCulture):"";
            if(packaged&&p is not null)_unit.SelectedValue=p.PurchaseUnit;
            _vat.Text=p is null?"":(p.VatRate*100).ToString("0.####",CultureInfo.InvariantCulture);_vat.IsEnabled=p is null;Show(_vat,p is null);Grid.SetColumnSpan((FrameworkElement)_price.Parent,p is null?1:2);
            var reviewed=p?.DispensingRulesReviewed==true;_reviewed.IsChecked=reviewed;_prescription.IsChecked=p?.RequiresPrescription==true;_controlled.IsChecked=p?.RequiresDispensingReference==true;
            Show(_reviewed,!reviewed);Show(_prescription,!reviewed);Show(_controlled,!reviewed);Show(_checked,p?.RequiresPrescription==true);Show(_reference,p?.RequiresDispensingReference==true);
            _strictBatch=gs1?.HasTrackingData()==true;var batches=p?.Batches.Where(b=>b.ExpiryDate>=DateOnly.FromDateTime(DateTime.Today)&&b.HoldReason.Length==0&&b.Quantity>0).OrderBy(b=>b.ExpiryDate).ToArray()??[];
            if(gs1 is not null)batches=batches.Where(b=>(gs1.Batch is null||b.Lot==gs1.Batch)&&(gs1.ExpiryDate is null||b.ExpiryDate==gs1.ExpiryDate)).ToArray();
            _batch.ItemsSource=batches;_batch.SelectedIndex=-1;Show(_batch,p?.TrackExpiry==true);
            if(_strictBatch){if(batches.Length==1)_batch.SelectedIndex=0;else _error.Text=T("Pharmacy_TraceabilityRequired");}
            await ShowLocationsAsync();var focus=name.Length==0?Search.Input:p?.UnitPrice>0?_quantity:_price;focus.Focus();_=focus.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>{if(focus==Search.Input)focus.BringIntoView();else((FrameworkElement)focus.Parent).BringIntoView();});
        }
        catch(Exception ex){_error.Text=T(ex is DomainException?ex.Message:"Pharmacy_SearchError");}
        finally{_busy=false;}
    }
    private async Task AddAsync()
    {
        if(_busy)return;_busy=true;_error.Text="";
        try
        {
            decimal Number(TextBox input)=>decimal.TryParse(input.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)?n:throw new DomainException("Common_InvalidNumber");
            var price=Number(_price);var quantity=Number(_quantity);var unit=_unit.SelectedValue as string??throw new DomainException("Pharmacy_InvalidUnits");
            if(quantity<=0||quantity!=decimal.Truncate(quantity)||price<=0)throw new DomainException("Pharmacy_InvalidProduct");
            if(_reviewed.IsChecked!=true)throw new DomainException("Pharmacy_TraceabilityRequired");
            if(_strictBatch&&(_product?.TrackExpiry!=true||_batch.SelectedItem is not InventoryBatch))throw new DomainException("Pharmacy_TraceabilityRequired");
            var p=_product??await _inventory.QuickAddAsync(new(_match?.ReferenceId,_name.Text,_barcode,unit,price,Number(_vat)/100,true,_prescription.IsChecked==true,_controlled.IsChecked==true));
            await _inventory.RememberAsync(p.Id,unit,price,true,_prescription.IsChecked==true,_controlled.IsChecked==true);p=await _products.GetCurrentForRegisterAsync(p.Id)??throw new DomainException("Pharmacy_InvalidProduct");_product=p;
            if(p.TrackExpiry&&_batch.SelectedItem is not InventoryBatch)throw new DomainException("Pharmacy_TraceabilityRequired");
            if(await _commit(p,quantity*p.ResolveSellingUnit(unit).Size,unit,_batch.SelectedItem as InventoryBatch,_checked.IsChecked==true,_reference.Text,_location.SelectedValue is int location?location:null,_gs1Serial))CancelEntry();
        }
        catch(Exception ex){_error.Text=T(ex is DomainException?ex.Message:"Pharmacy_SaveError");}
        finally{_busy=false;}
    }
    public void CancelEntry(){_entry.Visibility=Visibility.Collapsed;_barcode="";_gs1Serial=null;_match=null;_product=null;_name.Text=_status.Text="";Search.Input.Clear();Search.Input.Focus();}
    private async Task ShowLocationsAsync()
    {
        try
        {
            if(_product is not{} p||!p.StockTracked||p.TrackExpiry&&_batch.SelectedItem is not InventoryBatch){Show(_location,false);_location.ItemsSource=null;return;}
            var batch=_batch.SelectedItem as InventoryBatch;var places=await _locations.StockAsync(p.Id,batch?.Id);var remaining=(batch?.BaseQuantity??p.BaseStockQuantity)-places.Sum(l=>l.BaseQuantity);
            var options=places.Select(l=>new LocationStock(l.Id,l.Name+$" · {l.BaseQuantity}",l.BaseQuantity)).ToList();if(remaining>0)options.Add(new(0,T("Pharmacy_Unassigned")+$" · {remaining}",remaining));
            _location.ItemsSource=options;_location.SelectedIndex=options.Count==1?0:-1;Show(_location,options.Count>1);
        }
        catch(Exception){_error.Text=T("Pharmacy_SearchError");}
    }
    private static void Show(FrameworkElement field,bool visible)=>((FrameworkElement)field.Parent).Visibility=visible?Visibility.Visible:Visibility.Collapsed;
}
