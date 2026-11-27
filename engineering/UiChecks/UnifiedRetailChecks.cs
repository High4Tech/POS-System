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
        var inventory=sp.GetRequiredService<RetailInventory>();var legacy=await inventory.QuickAddAsync("QA old digital service","digital",price:100);
        await pos.InitializeAsync();Assert(!pos.ProductTiles.Any(t=>t.Product.Kind=="digital"),"Physical Sales excludes all digital records");
        Assert(!pos.CatalogKinds.Any(k=>k.Code=="digital"),"Physical Sales has no digital kind or filter");
        var nav=(StackPanel)window.FindName("Destinations");Assert(nav.Children.OfType<Button>().Take(3).Select(b=>b.Tag).SequenceEqual(new object[]{"dashboard","pos","digital"}),"Header begins Home, Sales, Digital Services");Assert(!nav.Children.OfType<Button>().Any(b=>Equals(b.Tag,"settings")),"Settings is in the right-side actions");
        await shell.ShowDigitalServicesCommand.ExecuteAsync(null);Pump();
        var view=Descendants(window).OfType<DigitalServicesView>().Single();
        Assert(view.IsVisible&&shell.ActiveScreen=="digital","The header opens digital selling directly");
        Assert(!Descendants(view).OfType<Button>().Any(b=>Equals(b.Content,SupermarketPos.Wpf.Localization.LocalizationManager.Instance["Retail_Templates"])||Equals(b.Content,"⋯")),"Cashier page has no template gateway or hidden management menu");
        var package=Descendants(view).OfType<Button>().Single(b=>Equals(b.Content,SupermarketPos.Wpf.Localization.LocalizationManager.Instance["Service_PACKAGE"]));package.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        for(var i=0;i<100&&!Descendants(view).OfType<ListBox>().Any(l=>l.Name=="PackageChoices"&&l.Items.Count>0);i++){await Task.Delay(30);Pump();}
        Assert(Descendants(view).OfType<RadioButton>().Count(b=>b.IsVisible)==2,"Prepaid and Postpaid are radio filters");
        var list=Descendants(view).OfType<ListBox>().Single(l=>l.Name=="PackageChoices");Assert(list.Items.Count>0,"Bundled offers appear directly in digital selling");list.SelectedIndex=0;
        list.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(window)!,0,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});Pump();
        Assert(InAppDialog.IsOpen,"Package Enter opens a selling popup");
        var overlay=((Grid)window.Content).Children.OfType<Grid>().Last();var priceForm=Descendants(overlay).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("price"));
        Assert(priceForm.Fields.ContainsKey("cost")&&!Descendants(overlay).OfType<CheckBox>().Any(c=>c.Content?.ToString()?.Contains("I checked")==true),"Package has one selling price and optional cost without an import verification checkbox");
        Assert(nav.IsEnabled&&!nav.IsHitTestVisible,"Modal blocks background input without disabled styling");
        var digitalButton=(Button)window.FindName("DigitalDestination");Assert(digitalButton.Background is System.Windows.Media.SolidColorBrush brush&&brush.Color==System.Windows.Media.Colors.White,"Digital navigation remains selected behind the popup");
        foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200}){SupermarketPos.Wpf.Localization.LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);Pump();Capture(window,System.IO.Path.Combine(screenshots,$"digital-package-sell-{lang}-{size}.png"));}
        sp.GetRequiredService<DesktopTextSize>().Save(100);SupermarketPos.Wpf.Localization.LocalizationManager.Instance.SetLanguage("en");
        var cancel=Descendants(overlay).OfType<Button>().Single(b=>Equals(b.Content,SupermarketPos.Wpf.Localization.LocalizationManager.Instance["Common_Cancel"]));cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        for(var i=0;InAppDialog.IsOpen&&i<100;i++){await Task.Delay(20);Pump();}
        Assert(!InAppDialog.IsOpen&&!view.HasDraft,"Cancel returns directly to digital selling without a phantom draft");
        list=Descendants(view).OfType<ListBox>().Single(l=>l.Name=="PackageChoices");list.SelectedIndex=0;
        list.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent});Pump();
        Assert(InAppDialog.IsOpen,"Package double-click opens the same selling popup");
        overlay=((Grid)window.Content).Children.OfType<Grid>().Last();priceForm=Descendants(overlay).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("price"));
        ((TextBox)priceForm.Fields["price"]).Text="250";((TextBox)priceForm.Fields["cost"]).Text="";
        var confirmation=Descendants(overlay).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("Phone"));
        ((TextBox)confirmation.Fields["Phone"]).Text="03001234567";((TextBox)confirmation.Fields["Phone"]).GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        var result=(ComboBox)confirmation.Fields["Status"];result.SelectedValue="Pending";result.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)!.UpdateSource();
        var db=sp.GetRequiredService<SupermarketPos.Infrastructure.Persistence.PosDbContext>();var productsBefore=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Products);
        Descendants(confirmation).OfType<Button>().Single(b=>Equals(b.Content,SupermarketPos.Wpf.Localization.LocalizationManager.Instance["Digital_Sell"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var wait=0;InAppDialog.IsOpen&&wait<200;wait++){await Task.Delay(25);Pump();}
        Assert(!InAppDialog.IsOpen,"Selling a selected package returns to digital selling");
        var recorded=(await sp.GetRequiredService<DigitalServiceLedger>().HistoryAsync()).First(t=>t.Type=="PACKAGE");
        Assert(recorded.Status=="Pending"&&recorded.Collected==250&&!recorded.ProviderCostKnown,"Package sale stores one price and permits omitted cost");
        Assert(!(await sp.GetRequiredService<DigitalServiceLedger>().PostingsAsync()).Any(t=>t.TransactionId==recorded.Id)&&await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Products)==productsBefore,"Pending digital sale does not post finances or create physical stock");
        Assert(!view.HasDraft,"Completed package entry clears its draft state");
        list=Descendants(view).OfType<ListBox>().Single(l=>l.Name=="PackageChoices");list.SelectedIndex=0;
        list.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(window)!,0,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});Pump();
        overlay=((Grid)window.Content).Children.OfType<Grid>().Last();confirmation=Descendants(overlay).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("Phone"));
        ((TextBox)confirmation.Fields["Phone"]).Text="03001234567";((TextBox)confirmation.Fields["Phone"]).GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        ((TextBox)confirmation.Fields["Reference"]).Text="UI-PACKAGE-CONFIRMED";((TextBox)confirmation.Fields["Reference"]).GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        var external=(CheckBox)confirmation.Fields["External"];external.IsChecked=true;external.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)!.UpdateSource();
        Descendants(confirmation).OfType<Button>().Single(b=>Equals(b.Content,SupermarketPos.Wpf.Localization.LocalizationManager.Instance["Digital_Sell"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var wait=0;!Descendants(window).OfType<ReceiptPreviewWindow>().Any()&&wait<200;wait++){await Task.Delay(25);Pump();}
        var serviceReceipt=Descendants(window).OfType<ReceiptPreviewWindow>().Single();
        Assert(((Grid)window.Content).Children.OfType<Grid>().Count(g=>Panel.GetZIndex(g)==int.MaxValue)==1&&!nav.IsHitTestVisible,"Confirmed service replaces the package popup with one modal receipt");
        Descendants(serviceReceipt).OfType<Button>().Single(b=>b.Name=="CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var wait=0;InAppDialog.IsOpen&&wait<100;wait++){await Task.Delay(25);Pump();}
        Assert(!InAppDialog.IsOpen&&nav.IsHitTestVisible,"Closing the service receipt restores the selling screen");
        await shell.ShowDigitalServicesCommand.ExecuteAsync(null);Pump();Assert(Descendants(view).OfType<Button>().Any(b=>Equals(b.Content,SupermarketPos.Wpf.Localization.LocalizationManager.Instance["Service_CASHIN"])),"Clicking the selected header returns to the selling page");
        await shell.ShowReportsCommand.ExecuteAsync(null);Pump();
        var reports=Descendants(window).OfType<ReportsView>().Single();
        var reportTabs=Descendants(reports).OfType<TabControl>().First().Items.OfType<TabItem>().ToArray();
        var history=reportTabs.Single(t=>t.Name=="DigitalHistoryTab");var summary=reportTabs.Single(t=>t.Name=="DigitalReportTab");
        Assert(history.IsVisible&&summary.IsVisible,"Digital history and totals are available in Reports");
        history.IsSelected=true;Pump();for(var wait=0;!Descendants(reports).OfType<DataGrid>().Any(t=>t.Items.Count>0)&&wait<100;wait++){await Task.Delay(25);Pump();}
        Assert(Descendants(reports).OfType<DataGrid>().Any(t=>t.Items.OfType<ServiceHistoryRow>().Any()),"Reports displays the saved digital transaction history");
        summary.IsSelected=true;Pump();Capture(window,System.IO.Path.Combine(screenshots,"digital-reports.png"));
        await shell.ShowDigitalServicesCommand.ExecuteAsync(null);Pump();Capture(window,System.IO.Path.Combine(screenshots,"digital-selling-main.png"));
        await shell.ShowPosCommand.ExecuteAsync(null);Pump();
    }
}
