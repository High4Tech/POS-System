using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task UnifiedRetailChecks(IServiceProvider sp,MainViewModel shell,MainWindow window,string screenshots)
    {
        var pos=(PosViewModel)shell.CurrentViewModel;
        pos.SearchText="QA loose retail cable";Pump();Assert(pos.CanQuickAddProduct,"An unmatched name offers Add Product under search");
        await pos.QuickAddProductCommand.ExecuteAsync(null);Pump();var row=pos.SelectedCartLine!;
        Assert(row.CanChooseKind&&row.HasInvalidPrice,"A quick product is added directly to the cart and needs a selling price");
        row.ProductKind="loose";row.SelectedSellingUnit="kg";row.Quantity=1.25m;row.PriceText="120";
        Assert(row.Line.Product.Unit==Unit.Kilogram&&row.Quantity==1.25m,"Quick loose goods use an exact measured quantity");
        pos.SaveDraftCommand.Execute(null);pos.RemoveLineCommand.Execute(row);await pos.ResumeDraftCommand.ExecuteAsync(null);Pump();row=pos.SelectedCartLine??pos.Lines.Single();
        Assert(row.ProductKind=="loose"&&row.Line.Product.Unit==Unit.Kilogram&&row.PriceText=="120.00","Saved quick drafts restore type, unit and selling price");
        foreach(var item in pos.Lines.ToArray())pos.RemoveLineCommand.Execute(item);
        pos.SearchText="QA cash withdrawal";await pos.QuickAddProductCommand.ExecuteAsync(null);Pump();row=pos.SelectedCartLine!;
        row.ProductKind="digital";row.SelectedServiceType="CASHOUT";row.PriceText="1000";
        row.SelectedServiceProviderId=row.ServiceProviders.First().Id;row.ServiceRecipient="QA account";row.ServiceReference="QA-CASH-OUT";row.ServiceFeeText="20";row.ServiceExternallyConfirmed=true;
        Assert(row.ServiceCash==-980,"Cash withdrawal settles a net payout while retaining its fee");
        row.ServiceFeeText="25";Assert(!row.ServiceExternallyConfirmed,"Editing settlement terms clears provider confirmation");row.ServiceExternallyConfirmed=true;
        pos.SaveDraftCommand.Execute(null);pos.RemoveLineCommand.Execute(row);await pos.ResumeDraftCommand.ExecuteAsync(null);Pump();row=pos.Lines.Single();
        Assert(row.SelectedServiceType=="CASHOUT"&&row.ServiceReference=="QA-CASH-OUT"&&!row.ServiceExternallyConfirmed,"Service drafts retain details and require provider confirmation again");
        var table=Descendants(window).OfType<DataGrid>().Single(c=>c.Name=="CartTable");table.ScrollIntoView(row);table.UpdateLayout();Pump();
        Assert(Descendants(table).OfType<TextBox>().Any(c=>System.Windows.Automation.AutomationProperties.GetName(c)==SupermarketPos.Wpf.Localization.LocalizationManager.Instance["Retail_Recipient"]&&c.IsVisible),"Financial service recipient is an actual visible cart editor");
        pos.OpenCashCommand.Execute(null);Pump();Assert(!pos.IsCashCollection&&pos.Tendered=="0.00","A payout does not ask for incoming cash");pos.CancelPaymentCommand.Execute(null);
        foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200})
        {
            SupermarketPos.Wpf.Localization.LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);Pump();
            window.UpdateLayout();var scroll=Descendants(table).OfType<ScrollViewer>().First();scroll.ScrollToBottom();Pump();window.UpdateLayout();
            Capture(window,System.IO.Path.Combine(screenshots,$"retail-service-row-{lang}-{size}.png"));
            Assert(table.ActualHeight>table.ColumnHeaderHeight+60,$"The cart keeps a usable body below its header at {size}% (table {table.ActualHeight}, header {table.ColumnHeaderHeight})");
            Assert(scroll.ScrollableHeight>0&&scroll.VerticalOffset>0,"Tall service details can scroll inside the cart at "+size+"%");
        }
        sp.GetRequiredService<DesktopTextSize>().Save(100);SupermarketPos.Wpf.Localization.LocalizationManager.Instance.SetLanguage("en");
        foreach(var item in pos.Lines.ToArray())pos.RemoveLineCommand.Execute(item);
        await shell.ShowDigitalServicesCommand.ExecuteAsync(null);Pump();
        var view=Descendants(window).OfType<DigitalServicesView>().Single();Assert(view.IsVisible&&shell.ActiveScreen=="digital","The header opens a dedicated Digital Services workspace");
        var record=Descendants(view).OfType<Button>().Single(b=>Equals(b.Content,SupermarketPos.Wpf.Localization.LocalizationManager.Instance["Service_Record"]));record.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        var package=Descendants(view).OfType<Button>().Single(b=>Equals(b.Content,SupermarketPos.Wpf.Localization.LocalizationManager.Instance["Service_PACKAGE"]));package.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Assert(Descendants(view).OfType<RadioButton>().Count(b=>b.IsVisible)==2,"Package account type uses exactly two radio choices");
        Assert(Descendants(view).OfType<ComboBox>().Any(c=>c.IsVisible&&c.Items.Count>=5),"Configured networks use a dropdown including supplied mobile brands");
        var radios=Descendants(view).OfType<RadioButton>().Where(b=>b.IsVisible).ToArray();radios[1].IsChecked=true;Pump();
        Assert(Descendants(view).OfType<RadioButton>().Count(b=>b.IsVisible&&b.IsChecked==true)==1,"Prepaid and Postpaid remain mutually exclusive");
        await shell.ShowPosCommand.ExecuteAsync(null);Pump();
    }
}
