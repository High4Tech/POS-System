using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;

namespace SupermarketPos.Wpf.Views;
public sealed partial class DigitalServicesView
{
    private sealed record PackageChoice(ServiceProviderAccount Provider,MobileServicePackage? Package,RetailReference? Reference,string Caption,string Details,decimal? Amount)
    { public override string ToString()=>Caption;public FlowDirection TextDirection=>Caption.Any(c=>c>=0x600&&c<=0x6ff)?FlowDirection.RightToLeft:FlowDirection.LeftToRight;public string Price=>Amount is{} amount?Money.Format(amount):T("Digital_PriceOnSale"); }
    private FrameworkElement PackageCatalog(Action<double> keepResultsVisible)
    {
        var panel=new Grid();panel.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});var filters=new StackPanel();panel.Children.Add(filters);
        filters.SizeChanged+=(_,_)=>keepResultsVisible(filters.ActualHeight+120);
        var providers=_providers.Where(p=>p.IsActive&&p.AllowedTypes.Split(',').Contains("PACKAGE")).ToArray();if(!providers.Any(p=>p.Name==_network))_network=providers.FirstOrDefault()?.Name??"";
        filters.Children.Add(BusinessForm.Label("Retail_SelectNetwork"));var network=new ComboBox{ItemsSource=providers,DisplayMemberPath="Name",SelectedValuePath="Name",SelectedValue=_network,Margin=new Thickness(4)};filters.Children.Add(network);
        network.SelectionChanged+=(_,_)=>{_network=(string?)network.SelectedValue??"";if(_cart.FirstOrDefault() is{} selected&&selected.ProviderName!=_network)DropRows();Record();};
        var search=new TextBox{Name="PackageSearch",MaxLength=150,Margin=new Thickness(4,4,4,8),ToolTip=T("Service_SearchPackages")};System.Windows.Automation.AutomationProperties.SetName(search,T("Service_SearchPackages"));filters.Children.Add(search);_search=search;
        var list=new ListBox{Name="PackageChoices",HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent};list.SetResourceReference(TagProperty,"ProductCardWidth");ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);Grid.SetRow(list,1);panel.Children.Add(list);
        var items=new FrameworkElementFactory(typeof(WrapPanel));list.ItemsPanel=new ItemsPanelTemplate(items);
        var card=new FrameworkElementFactory(typeof(Button));var cardStyle=new Style(typeof(Button),(Style)FindResource("CatalogChoiceCard"));var selected=new DataTrigger{Binding=new Binding("IsSelected"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)},Value=true};selected.Setters.Add(new Setter(Control.BackgroundProperty,new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(219,234,254))));cardStyle.Triggers.Add(selected);card.SetValue(StyleProperty,cardStyle);card.SetValue(MarginProperty,new Thickness(0,0,12,12));card.SetBinding(ToolTipProperty,new Binding("Details"));
        var width=new MultiBinding{Converter=new RegisterTileWidthConverter{StandardWidth=186,Gap=12}};width.Bindings.Add(new Binding("ActualWidth"){Source=list});width.Bindings.Add(new Binding("Tag"){Source=list});card.SetBinding(WidthProperty,width);
        var content=new FrameworkElementFactory(typeof(StackPanel));var name=new FrameworkElementFactory(typeof(TextBlock));name.SetResourceReference(TextBlock.FontSizeProperty,"RegisterTileTextSize");name.SetBinding(TextBlock.TextProperty,new Binding("Caption"));name.SetBinding(TextBlock.FontFamilyProperty,new Binding("Caption"){Converter=(IValueConverter)FindResource("ProductTextFont")});name.SetBinding(FlowDirectionProperty,new Binding("TextDirection"));name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);name.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);name.SetValue(MarginProperty,new Thickness(0,0,0,8));content.AppendChild(name);
        var details=new FrameworkElementFactory(typeof(TextBlock));details.SetResourceReference(TextBlock.FontSizeProperty,"RegisterTileTextSize");details.SetBinding(TextBlock.TextProperty,new Binding("Details"));details.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);details.SetValue(MarginProperty,new Thickness(0,0,0,10));content.AppendChild(details);
        var price=new FrameworkElementFactory(typeof(TextBlock));price.SetResourceReference(TextBlock.FontSizeProperty,"RegisterTileTextSize");price.SetBinding(TextBlock.TextProperty,new Binding("Price"));price.SetValue(TextBlock.FontWeightProperty,FontWeights.Bold);price.SetValue(TextBlock.ForegroundProperty,FindResource("AccentBrush"));price.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);content.AppendChild(price);card.AppendChild(content);
        card.AddHandler(Button.ClickEvent,new RoutedEventHandler((sender,e)=>{if(((FrameworkElement)sender).DataContext is PackageChoice choice)AddPackage(choice);e.Handled=true;}));list.ItemTemplate=new DataTemplate{VisualTree=card};
        var itemStyle=new Style(typeof(ListBoxItem));var shell=new FrameworkElementFactory(typeof(ContentPresenter));itemStyle.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(ListBoxItem)){VisualTree=shell}));list.ItemContainerStyle=itemStyle;
        void Choose(){if(list.SelectedItem is PackageChoice choice)AddPackage(choice);}
        list.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;Choose();}};search.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Down){list.SelectedIndex=0;list.Focus();e.Handled=true;}else if(e.Key==Key.Enter){list.SelectedIndex=Math.Max(0,list.SelectedIndex);e.Handled=true;Choose();}};
        var empty=BusinessForm.Label("Service_NoPackages");Grid.SetRow(empty,1);panel.Children.Add(empty);
        var rows=new List<PackageChoice>();var provider=providers.FirstOrDefault(p=>p.Name==_network);
        foreach(var pack in _packages.Where(p=>p.IsActive&&p.ProviderId==provider?.Id))
        {
            var description=ReferenceText.Format(pack.Benefits);if(pack.Validity.Length>0&&!description.Contains(pack.Validity,StringComparison.CurrentCultureIgnoreCase))description+="\n"+T("Service_Validity")+": "+pack.Validity;
            rows.Add(new(provider!,pack,null,pack.Name,description,pack.Price+pack.CustomerFee));
        }
        void Filter(){var filtered=rows.Where(p=>(p.Caption+" "+p.Details).Contains(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)).OrderBy(p=>p.Caption).ToArray();list.ItemsSource=filtered;empty.Visibility=filtered.Length==0?Visibility.Visible:Visibility.Collapsed;}
        search.TextChanged+=(_,_)=>Filter();Filter();_ = LoadReferences();
        async Task LoadReferences()
        {
            if(_references is null||provider is null)return;
            try{var references=await _references.SearchAsync("","package",1000);rows.AddRange(references.Where(r=>(provider.CatalogKey=="network:"+r.Model||r.Brand.Equals(provider.Name,StringComparison.OrdinalIgnoreCase))&&r.Category is not ("directory" or "directory_only")&&r.State!="directory_not_sellable"&&!_packages.Any(p=>p.CatalogKey==r.Key)).Select(r=>new PackageChoice(provider,null,r,r.Name,ReferenceText.Format(r.Details),decimal.TryParse(r.SourcePrice,NumberStyles.Number,CultureInfo.InvariantCulture,out var amount)?amount:null)));Filter();}catch(Exception){_status.Text=T("Service_Error");}
        }
        return panel;
    }
    private void AddPackage(PackageChoice choice)
    {
        AddRow(new DigitalCartLine{Type="PACKAGE",Provider=choice.Provider,Package=choice.Package,ReferencePackage=choice.Reference,Name=choice.Caption,AccountType=choice.Package?.AccountType??"Prepaid",AmountText=choice.Amount?.ToString("0.00",CultureInfo.InvariantCulture)??"",CostText=choice.Package?.ProviderCostKnown==true?choice.Package.ProviderCost.ToString("0.00",CultureInfo.InvariantCulture):""});
    }
}
