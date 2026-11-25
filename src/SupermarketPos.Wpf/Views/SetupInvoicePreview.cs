using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SupermarketPos.Domain.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

// Illustrative receipt only: never writes a sale or invents fiscal identifiers.
public sealed class SetupInvoicePreview : Border
{
    private readonly StackPanel _body=new();
    public SetupInvoicePreview(BusinessProfile profile)
    {
        Width=440; HorizontalAlignment=HorizontalAlignment.Center; Padding=new Thickness(18);
        Background=Brushes.White; BorderBrush=Brushes.LightGray; BorderThickness=new Thickness(1);
        Child=_body; Refresh(profile);
    }
    private static string T(string key)=>LocalizationManager.Instance[key];
    private TextBlock Text(string value,bool bold=false,bool centered=false)
    {
        var text=new TextBlock{Text=value,FontSize=13,Foreground=Brushes.Black,TextWrapping=TextWrapping.Wrap,
            FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(0,2,0,2),
            TextAlignment=centered?TextAlignment.Center:TextAlignment.Left};
        _body.Children.Add(text);return text;
    }
    private void Line()=>_body.Children.Add(new Border{Height=1,Background=Brushes.Gray,Margin=new Thickness(0,6,0,6)});
    private void Pair(string label,string value,bool bold=false)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var left=new TextBlock{Text=label,FontSize=13,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Black};
        var right=new TextBlock{Text=value,FontSize=13,FlowDirection=FlowDirection.LeftToRight,Foreground=Brushes.Black,Margin=new Thickness(12,0,0,0)};
        if(bold){left.FontWeight=right.FontWeight=FontWeights.Bold;}
        Grid.SetColumn(right,1);grid.Children.Add(left);grid.Children.Add(right);_body.Children.Add(grid);
    }
    public void Refresh(BusinessProfile p)
    {
        _body.Children.Clear();
        if(p.SetupLogoChosen&&PosStorage.ResolveImagePath(p.Logo) is{} path&&File.Exists(path))
        {
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(path);bitmap.EndInit();
            var image=new FormatConvertedBitmap(bitmap,PixelFormats.Gray8,null,0);
            _body.Children.Add(new Image{Source=image,Height=64,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,0,8)});
        }
        else
        {
            var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            words.Children.Add(new TextBlock{Text=T("Setup_Your"),TextAlignment=TextAlignment.Center,FontSize=13,Foreground=Brushes.Gray});
            words.Children.Add(new TextBlock{Text=T("Setup_Logo"),TextAlignment=TextAlignment.Center,FontSize=13,Foreground=Brushes.Gray});
            _body.Children.Add(new Border{Width=72,Height=64,HorizontalAlignment=HorizontalAlignment.Center,BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(1),Child=words,Margin=new Thickness(0,0,0,8)});
        }
        Text(string.IsNullOrWhiteSpace(p.StoreName)?T("Settings_StoreName"):p.StoreName,true,true);
        if(p.Registered&&p.LegalName.Length>0)Text(p.LegalName,false,true);
        if(p.Address.Length>0)Text(p.Address,false,true);
        if(p.Phone.Length>0)Text(p.Phone,false,true);
        Text(T("Setup_PreviewOnly"),false,true);Line();
        Pair(T("Setup_PreviewReceipt"),(p.InvoicePrefix.Length>0?p.InvoicePrefix+"-":"")+"000001");
        Text(T("Setup_PreviewItem"),true); Pair("1 × PKR 1,000.00","PKR 1,000.00");Line();
        Pair(T("Pos_Total"),"PKR 1,000.00",true);
        Pair(T("Setup_PreviewPayment"),"PKR 1,000.00");
        Line();Text(T("Setup_PreviewThankYou"),false,true);
    }
}
