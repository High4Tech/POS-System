using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

public sealed class LooseGoodsPanel : UserControl
{
    private readonly ComboBox _product;private readonly ComboBox _mode=new(){DisplayMemberPath="Name",SelectedValuePath="Code"};
    private readonly TextBox _value=new(){Text="1",FlowDirection=FlowDirection.LeftToRight};
    private readonly TextBlock _details=new(){TextWrapping=TextWrapping.Wrap};private readonly TextBlock _result=new(){TextWrapping=TextWrapping.Wrap};
    private readonly WrapPanel _presets=new();private readonly Button _add;private readonly bool _loose;private readonly bool _packs;
    private readonly WrapPanel _frequent=new();
    private readonly BusinessForm _body=new(){Visibility=Visibility.Collapsed};
    private readonly Func<Product,decimal,bool,Task<bool>> _commit;private decimal _quantity;private bool _packaged;
    private static string T(string k)=>LocalizationManager.Instance[k];
    public LooseGoodsPanel(IReadOnlyList<Product> products,bool loose,bool packs,Func<Product,decimal,bool,Task<bool>> commit)
    {
        _loose=loose;_packs=packs;_commit=commit;
        var selection=new BusinessForm();var form=_body;
        _product=new(){ItemsSource=products,DisplayMemberPath="Name",IsEditable=true,IsTextSearchEnabled=false};selection.Add("Pos_Product",_product,true);
        foreach(var p in products.Take(4))
        {
            var quick=new Button{Content=new TextBlock{Text=p.Name,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=160},ToolTip=p.Name,Margin=new Thickness(3)};quick.SetResourceReference(StyleProperty,"GhostButton");quick.Click+=(_,_)=>_product.SelectedItem=p;_frequent.Children.Add(quick);
        }
        selection.Add("",_frequent,true);
        form.Add("",_details,true);form.Add("Measure_Method",_mode);form.Add("Measure_QuantityOrAmount",_value);form.Add("",_presets,true);form.Add("",_result,true);
        _add=BusinessForm.Button("Measure_Add",async(_,_)=>await AddAsync());form.Add("",_add,true);var root=new StackPanel();root.Children.Add(selection);root.Children.Add(form);Content=root;
        _product.SelectionChanged+=(_,_)=>SelectProduct();_mode.SelectionChanged+=(_,_)=>{Calculate();_value.Focus();_value.SelectAll();};_value.TextChanged+=(_,_)=>Calculate();
        _value.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await AddAsync();}};
        Loaded+=(_,_)=>_product.Focus();
    }
    private void SelectProduct()
    {
        if(_product.SelectedItem is not Product p)return;
        _body.Visibility=Visibility.Visible;
        _details.Text=$"{p.Name} · {Money.Format(p.UnitPrice)} / {T(p.Unit==Unit.Kilogram?"Unit_Kg":p.Unit==Unit.Litre?"Measure_litre":"Unit_Piece")} · {T("Products_Stock")}: {p.StockQuantity:0.###}";
        var modes=new List<BusinessForm.Choice>();
        if(_loose){modes.Add(new("quantity",T(p.Unit==Unit.Piece?"Pos_Qty":"Measure_ByWeight")));if(p.Unit!=Unit.Piece){modes.Add(new("base",T(p.Unit==Unit.Kilogram?"Measure_Grams":"Measure_Millilitres")));modes.Add(new("amount",T("Measure_ByAmount")));}}
        if(_packs&&p.Unit==Unit.Piece&&p.UnitsPerPackage>1)modes.Add(new("package",p.PurchaseUnit));
        _frequent.Visibility=Visibility.Collapsed;
        _mode.ItemsSource=modes;_mode.SelectedIndex=0;((FrameworkElement)_mode.Parent).Visibility=modes.Count>1?Visibility.Visible:Visibility.Collapsed;
        Grid.SetColumn((FrameworkElement)_value.Parent,modes.Count>1?1:0);Grid.SetColumnSpan((FrameworkElement)_value.Parent,modes.Count>1?1:2);
        _presets.Children.Clear();if(p.Unit!=Unit.Piece)foreach(var pair in new[]{("Measure_Kilo",1m),("Measure_Half",.5m),("Measure_Pao",.25m)})
            _presets.Children.Add(BusinessForm.Button(pair.Item1,(_,_)=>{_mode.SelectedValue="quantity";_value.Text=pair.Item2.ToString(CultureInfo.InvariantCulture);},"GhostButton"));
        _value.Text="1";Calculate();_value.Focus();_value.SelectAll();
    }
    private bool Calculate()
    {
        try
        {
            if(_product.SelectedItem is not Product p)throw new DomainException("Purchase_ProductRequired");
            var mode=_mode.SelectedValue as string;
            if(((Grid)_value.Parent).Children[0] is TextBlock label)label.Text=mode=="amount"?T("Measure_Amount"):T("Pos_Qty")+" ("+(mode=="package"?p.PurchaseUnit:T(mode=="base"?(p.Unit==Unit.Kilogram?"Measure_Grams":"Measure_Millilitres"):p.Unit==Unit.Kilogram?"Unit_Kg":p.Unit==Unit.Litre?"Measure_litre":"Unit_Piece"))+")";
            if(!decimal.TryParse(_value.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var entered)||entered<=0)throw new DomainException("Measure_InvalidQuantity");
            _packaged=mode=="package";decimal charge;
            if(_packaged){if(entered!=decimal.Truncate(entered)||p.UnitsPerPackage<=1)throw new DomainException("Measure_InvalidPackage");_quantity=checked(entered*p.UnitsPerPackage);charge=Money.Round2(entered*(p.PackageSellingPrice??p.UnitPrice*p.UnitsPerPackage));}
            else if(mode=="amount"){var converted=MeasuredGoods.ForAmount(entered,p.UnitPrice,p.Unit);_quantity=converted.Quantity;charge=converted.Charge;}
            else{_quantity=mode=="base"?entered/1000:entered;_=MeasuredGoods.BaseQuantity(_quantity,p.Unit);charge=Money.Round2(_quantity*p.UnitPrice);}
            if(_quantity>p.StockQuantity)throw new DomainException("Measure_InvalidQuantity");
            var display=p.Unit!=Unit.Piece&&_quantity<1?$"{_quantity*1000:0} {T(p.Unit==Unit.Kilogram?"Measure_Grams":"Measure_Millilitres")}":$"{_quantity:0.###} {T(p.Unit==Unit.Kilogram?"Unit_Kg":p.Unit==Unit.Litre?"Measure_litre":"Unit_Piece")}";
            _result.Text=$"{p.Name} — {display} — {Money.Format(charge)}";_result.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");_add.IsEnabled=true;return true;
        }
        catch(Exception ex){_result.Text=T(ex is DomainException?ex.Message:"Common_InvalidNumber");_result.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");_add.IsEnabled=false;return false;}
    }
    private async Task AddAsync()
    {
        if(!Calculate()||_product.SelectedItem is not Product p)return;
        _add.IsEnabled=false;try{if(await _commit(p,_quantity,_packaged)){_value.Focus();_value.SelectAll();}}finally{_add.IsEnabled=true;}
    }
}
