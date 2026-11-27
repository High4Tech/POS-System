using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;
using SupermarketPos.Wpf.ViewModels;

internal static partial class Program
{
    private static async Task RetailTestSetupChecks(IServiceProvider sp,string screenshots)
    {
        var setup=sp.GetRequiredService<IBusinessSetup>();var auth=sp.GetRequiredService<IAuthService>();
        var window=new RetailTestSetupWindow(setup,auth,sp.GetRequiredService<PosStorage>());window.Show();Pump();
        var form=Descendants(window).OfType<BusinessForm>().Single();
        Assert(window.Icon is not null,"Setup uses the official app icon");
        if(RetailTestEdition.Standalone)
        {
            Assert(((TextBox)form.Fields["store"]).Text==""&&form.Fields.ContainsKey("owner")&&!form.Fields.Keys.Any(k=>k.Contains("license",StringComparison.OrdinalIgnoreCase)),"Standalone setup asks for store and owner without licensing");
            ((TextBox)form.Fields["store"]).Text="QA Wijgandu shop";((TextBox)form.Fields["owner"]).Text="QA owner";
        }
        var create=Descendants(window).OfType<Button>().Single(b=>Equals(b.Content,LocalizationManager.Instance[RetailTestEdition.Standalone?"Setup_Finish":"Trial_Create"]));
        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Assert(!(await setup.LoadAsync()).HasUsers&&((Control)form.Fields["username"]).BorderThickness==new Thickness(2),"Empty local credentials are rejected on the same fields");
        ((TextBox)form.Fields["username"]).Text="test-owner";((PasswordBox)form.Fields["password"]).Password="TrialRetail123!";((PasswordBox)form.Fields["confirm"]).Password="Mismatch1234";
        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Assert(!(await setup.LoadAsync()).HasUsers,"Mismatched local passwords do not create an account");
        ((PasswordBox)form.Fields["confirm"]).Password="TrialRetail123!";
        foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200})
        {
            ((ComboBox)form.Fields["language"]).SelectedValue=lang;sp.GetRequiredService<DesktopTextSize>().Save(size);Pump();
            Assert(form.FlowDirection==LocalizationManager.Instance.FlowDirection,"Local setup follows selected language direction");
            Capture(window,System.IO.Path.Combine(screenshots,$"trial-setup-{lang}-{size}.png"));
        }
        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var i=0;!create.IsEnabled&&i<100;i++){await Task.Delay(50);Pump();}
        var state=await setup.LoadAsync();Assert(state.Completed&&state.HasUsers&&auth.IsLoggedIn,"Local setup creates and signs in the owner without external activation");
        Assert(state.Profile.BusinessType=="electronics"&&state.Profile.LooseGoodsAvailable&&state.Profile.DigitalWalletAvailable&&state.Profile.MobileRechargeAvailable&&!state.Profile.EnableCatalogImages,"The separate store enables the requested Tech Retail modules with images off");
        Assert(state.Profile.Logo is null&&state.Profile.SetupLogoChosen,"The app image never becomes the business logo");
        Assert(Descendants(form).OfType<TextBox>().Any(t=>t.IsReadOnly&&t.Text.Length==48),"Owner recovery code is shown after local setup");
        window.Close();sp.GetRequiredService<DesktopTextSize>().Save(100);LocalizationManager.Instance.SetLanguage("en");
        if(RetailTestEdition.Standalone)
        {
            var products=sp.GetRequiredService<ProductsViewModel>();await products.RefreshAsync();products.NewCommand.Execute(null);
            var editor=new ProductEditorView{DataContext=products};var host=new Window{Content=new ScrollViewer{Content=editor},Width=1000,Height=850};host.Show();Pump();
            Assert(!products.IsPharmacy&&!products.Editor.AllowExpiry&&!Descendants(editor).OfType<CheckBox>().Any(c=>c.IsVisible&&Equals(c.Content,LocalizationManager.Instance["Batch_Track"])),"Standalone add product has no pharmacy or expiry controls");
            var types=Descendants(editor).OfType<ComboBox>().Single(c=>c.Name=="RetailProductType");Assert(types.Items.Count==3,"Standalone offers labeled, loose and packaged product types");types.SelectedValue="loose";Pump();Assert(!Descendants(editor).OfType<TextBox>().Single(t=>t.Name=="BarcodeField").IsVisible,"Loose selection hides the retail barcode field");
            Assert(Descendants(editor).OfType<CheckBox>().Any(c=>c.IsVisible&&Equals(c.Content,LocalizationManager.Instance["Trade_SerialTrack"]))&&Descendants(editor).OfType<CheckBox>().Any(c=>c.IsVisible&&Equals(c.Content,LocalizationManager.Instance["Retail_Warranty"])),"Serial/IMEI and warranty remain available as checkboxes");
            Capture(host,System.IO.Path.Combine(screenshots,"wijgandu-retail-product.png"));host.Close();products.CancelEditCommand.Execute(null);
        }
    }
}
