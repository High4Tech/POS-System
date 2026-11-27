using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;
public partial class BusinessSettingsWindow : Window
{
    private readonly IServiceProvider _provider;
    private readonly IBusinessSetup _setup;
    private readonly IAuthService _auth;
    private readonly LicenseClient _license;
    private readonly SoftwareUpdates _updates;
    private readonly PosStorage _storage;
    private readonly Diagnostics _diagnostics;
    private readonly ProtectedLocalStore _protected;
    private readonly ViewModels.MainViewModel _shell;
    private BusinessProfile _profile = new();
    private bool _busy;
    public BusinessSettingsWindow(IServiceProvider provider, IBusinessSetup setup, IAuthService auth, LicenseClient license,
        SoftwareUpdates updates, PosStorage storage, Diagnostics diagnostics, ProtectedLocalStore protectedStore, ViewModels.MainViewModel shell)
    {
        InitializeComponent();if(RetailTestEdition.Standalone){Title="Wijgandu";} _provider=provider;_setup=setup;_auth=auth;_license=license;_updates=updates;_storage=storage;_diagnostics=diagnostics;_protected=protectedStore;_shell=shell;
        SourceInitialized+=(_,_)=>{var growth=Math.Max(0,(double)FindResource("TextSize14")/14-1);Width=Math.Min(SystemParameters.WorkArea.Width,1220+400*growth);Height=Math.Min(SystemParameters.WorkArea.Height,900+280*growth);};
        Loaded+=async(_,_)=>await Run(LoadAsync);
        Closing+=(_,e)=>{if(_busy)e.Cancel=true;else _shell.RefreshLicense();};
    }
    public string Heading=>T(RetailTestEdition.Standalone?"Retail_Settings":"Business_Settings");
    private static string T(string key)=>LocalizationManager.Instance[key];
    private void Tab(string key,FrameworkElement content)=>Tabs.Items.Add(new TabItem{Header=T(key),Content=content,Padding=new Thickness(8)});
    private async Task LoadAsync()
    {
        _auth.RequireAdmin();_profile=(await _setup.LoadAsync()).Profile; Tabs.Items.Clear();
        ProfileTab();FeaturesTab(); if(!RetailTestEdition.Standalone&&_profile.Registered){FbrTab(); await TaxTab();} InvoiceTab(); if(!RetailTestEdition.Enabled){LicenseTab();UpdatesTab();} StorageTab(); SupportTab();
        if(_profile.IsPharmacy)await CatalogTab();
        if(_profile.DigitalWalletAvailable||_profile.MobileRechargeAvailable)
            Tab("Service_Config",new DigitalServicesView(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DigitalServiceLedger>(_provider),_setup,Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ReceiptPreviewDialog>(_provider),configuration:true,inventory:Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<RetailInventory>(_provider),references:Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<RetailReferenceCatalog>(_provider)));
    }
    private void ProfileTab()
    {
        var f=new BusinessForm(3);var p=_profile;
        f.Text("Settings_StoreName",p,nameof(p.StoreName),limit:150);f.Text("Settings_StoreAddress",p,nameof(p.Address),limit:300);
        if(RetailTestEdition.Standalone){f.Text("Setup_Owner",p,nameof(p.Owner));f.Text("Customer_Phone",p,nameof(p.Phone),true);f.Text("Customer_Email",p,nameof(p.Email),true);f.Text("Setup_City",p,nameof(p.City));f.Add("",BusinessForm.Button("Profile_Save",async(_,_)=>await Run(SaveProfile)),true);Tab("Retail_StoreDetails",f);return;}
        f.Check("Setup_Registered",p,nameof(p.Registered)); f.Text("Setup_LegalName",p,nameof(p.LegalName));
        f.Select("Setup_Entity",p,nameof(p.EntityType),("sole","Setup_Sole"),("aop","Setup_Aop"),("private","Setup_Private"),("public","Setup_Public"),("other","Payment_Other"));
        f.Text("Setup_Owner",p,nameof(p.Owner));f.Text("Customer_Phone",p,nameof(p.Phone),true);f.Text("Customer_Email",p,nameof(p.Email),true);
        f.Text("Setup_City",p,nameof(p.City));f.Text("Setup_Province",p,nameof(p.Province));f.Text("Setup_Postal",p,nameof(p.PostalCode));
        f.Text("Setup_Ntn",p,nameof(p.Ntn),true,15);f.Check("Setup_SalesTaxRegistered",p,nameof(p.SalesTaxRegistered));f.Text("Setup_Strn",p,nameof(p.Strn),true,13);
        var documents=new ComboBox{ItemsSource=p.Documents,DisplayMemberPath="Name",SelectedIndex=p.Documents.Count-1};f.Add("Documents_Attached",documents,true);
        f.Add("",BusinessForm.Button("Documents_Add",async(_,_)=>await Run(()=>
        {
            var file=new OpenFileDialog{Filter="Documents|*.pdf;*.png;*.jpg;*.jpeg"};
            if(file.ShowDialog()==true)
            {
                var document=new BusinessDocuments(_storage).Import(file.FileName);
                typeof(BusinessProfile).GetProperty(nameof(BusinessProfile.Documents))!.SetValue(_profile,_profile.Documents.Append(document).ToArray());
                documents.ItemsSource=_profile.Documents;documents.SelectedItem=document;
            }
            Status.Text=T("Documents_LocalOnly");return Task.CompletedTask;
        }),"GhostButton"));
        f.Add("",BusinessForm.Button("Documents_Export",async(_,_)=>await Run(()=>
        {
            if(documents.SelectedItem is LocalBusinessDocument doc){var file=new SaveFileDialog{FileName=doc.Name};if(file.ShowDialog()==true)new BusinessDocuments(_storage).Export(doc,file.FileName);}
            return Task.CompletedTask;
        }),"GhostButton"));
        f.Add("",BusinessForm.Button("Profile_Save",async(_,_)=>await Run(SaveProfile)));
        if(!RetailTestEdition.Enabled)f.Add("",BusinessForm.Button("Setup_Restart",async(_,_)=>await Run(async()=>
        {
            await _setup.RestartAsync();
            var wizard=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<OnboardingWindow>(_provider);
            wizard.Owner=this;wizard.ShowDialog(); await LoadAsync();
        }),"GhostButton"),true);
        Tab("Setup_Registration",f);
    }
    private async Task SaveProfile()
    {
        var previous=(await _setup.LoadAsync()).Profile;
        if(_shell.HasPendingWork&&(previous.BusinessType!=_profile.BusinessType||previous.EnableLooseGoods!=_profile.EnableLooseGoods||previous.EnablePackagedUnits!=_profile.EnablePackagedUnits||previous.EnableDigitalWalletServices!=_profile.EnableDigitalWalletServices||previous.EnableMobileRecharge!=_profile.EnableMobileRecharge))throw new CommercialException("Updates_FinishDrafts");
        var db=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PosDbContext>(_provider);
        var s=await db.Settings.SingleAsync();
        await _setup.CompleteAsync(_profile,null,null);
        await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<MedicineCatalogService>(_provider).EnsureSelectedAsync();
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<StoreProfile>(_provider).Apply(s);
        await LoadAsync();await _shell.RefreshFeaturesAsync();Status.Text=T("Settings_Saved");
    }
    private void FeaturesTab(){var f=new BusinessForm();if(RetailTestEdition.Standalone){f.Check("Features_Loose",_profile,nameof(_profile.EnableLooseGoods));f.Check("Features_Packaging",_profile,nameof(_profile.EnablePackagedUnits));f.Check("Features_Wallet",_profile,nameof(_profile.EnableDigitalWalletServices));f.Check("Features_Recharge",_profile,nameof(_profile.EnableMobileRecharge));f.Check("Features_CatalogImages",_profile,nameof(_profile.EnableCatalogImages));}else BusinessFeatureFields.Add(f,_profile);f.Add("",BusinessForm.Button("Profile_Save",async(_,_)=>await Run(SaveProfile)),true);Tab("Features_Title",f);}
    private async Task CatalogTab()
    {
        var catalog=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<MedicineCatalogService>(_provider);
        var f=new BusinessForm();var status=await catalog.StatusAsync();
        f.Add("Pharmacy_Template",BusinessForm.Label("Pharmacy_DrapTemplate"),true);
        f.Add("Pharmacy_CatalogCount",new TextBlock{Text=(status?.RowCount??0).ToString("N0"),TextWrapping=TextWrapping.Wrap});
        f.Add("",BusinessForm.Label("Pharmacy_CatalogReferenceOnly"),true);
        f.Add("",BusinessForm.Button("Pharmacy_LoadTemplate",async(_,_)=>await Run(async()=>{var count=await catalog.ImportBuiltinAsync();_profile.MedicineTemplate=MedicineCatalogService.TemplateId;await _setup.SaveDraftAsync(_profile,8);await LoadAsync();Status.Text=count.ToString("N0")+" · "+T("Pharmacy_CatalogReady");})),true);
        f.Add("",BusinessForm.Button("Pharmacy_ImportCatalog",async(_,_)=>await Run(async()=>{var file=new OpenFileDialog{Filter="CSV|*.csv"};if(file.ShowDialog()!=true)return;var count=await catalog.ImportFileAsync(await File.ReadAllBytesAsync(file.FileName),file.FileName);await LoadAsync();Status.Text=count.ToString("N0")+" · "+T("Pharmacy_CatalogReady");}),"GhostButton"),true);
        f.Add("",BusinessForm.Label("Pharmacy_CatalogRights"),true);Tab("Pharmacy_Catalog",f);
    }
    private void InvoiceTab()
    {
        var f=new BusinessForm();var p=_profile;
        f.Text("Setup_InvoiceTitle",p,nameof(p.InvoiceTitle));f.Text("Setup_InvoicePrefix",p,nameof(p.InvoicePrefix),true);
        f.Select("Setup_Paper",p,nameof(p.Paper),("58","Setup_Paper58"),("80","Setup_Paper80"),("A4","Setup_PaperA4"));
        f.Select("Setup_InvoiceLanguage",p,nameof(p.InvoiceLanguage),("en","Language_English"),("ur","Language_Urdu"));
        var printer=new ComboBox{IsEditable=true,IsTextSearchEnabled=true};printer.SetBinding(ComboBox.TextProperty,new System.Windows.Data.Binding(nameof(p.Printer)){Source=p,Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged});
        printer.DropDownOpened+=async(_,_)=>{try{printer.ItemsSource=await new ReceiptPrinter().PrintersAsync();}catch{Status.Text=T("Invoice_PrinterUnavailable");}};f.Field("Setup_Printer",nameof(p.Printer),printer);f.Text("Setup_BackupFolder",p,nameof(p.BackupFolder),true,400);
        f.Text("Invoice_ExportFolder",p,nameof(p.InvoiceExportFolder),true,400);f.Text("Invoice_MessageTemplate",p,nameof(p.WhatsAppTemplate),limit:2000);
        f.Text("Setup_Branch",p,nameof(p.Branch),true);f.Text("Setup_Terminal",p,nameof(p.Terminal),true);
        f.Text("Customer_Phone",p,nameof(p.Phone),true,30);f.Text("Invoice_SecondPhone",p,nameof(p.Phone2),true,30);f.Add("Invoice_Notes",new InvoiceNotesPanel(p),true);
        f.Add("",BusinessForm.Button("Profile_Save",async(_,_)=>await Run(SaveProfile)),true);Tab("Setup_InvoiceTitle",f);
    }
    private void FbrTab()
    {
        var f=new BusinessForm();var p=_profile;
        f.Add("",new TextBlock{Text=T("Fbr_Explanation"),TextWrapping=TextWrapping.Wrap},true);
        f.Check("Setup_FbrRequired",p,nameof(p.FbrRequired));
        f.Select("Fbr_Route",p,nameof(p.FbrRoute),("none","Fbr_NotConfigured"),("pos","Fbr_Pos"),("digital","Fbr_Digital"));
        f.Text("Fbr_Provider",p,nameof(p.FbrProvider));f.Text("Fbr_PosId",p,nameof(p.FbrPosId),true);
        f.Text("Fbr_Endpoint",p,nameof(p.FbrEndpoint),true,400);f.Text("Fbr_Schema",p,nameof(p.FbrSchema),true);
        f.Select("Fbr_Environment",p,nameof(p.FbrEnvironment),("sandbox","Fbr_Sandbox"),("production","Fbr_Production"));
        f.Select("Fbr_Status",p,nameof(p.FbrStatus),("not-configured","Fbr_NotConfigured"),("pending","Fbr_Pending"),("sandbox","Fbr_Sandbox"),("attention","Fbr_Attention"));
        var token=new PasswordBox();f.Add("Fbr_SandboxToken",token);
        f.Add("",BusinessForm.Button("Profile_Save",async(_,_)=>await Run(async()=>
        {
            if(p.FbrEnvironment=="production")throw new BusinessValidationException("Setup_ProductionPending");
            p.Validate(false);
            if(token.Password.Length>0){_protected.Write("fbr-sandbox-token",token.Password);token.Clear();}
            // Required-but-pending is saved and blocks checkout; it never becomes silently optional.
            await _setup.SaveDraftAsync(p,8);Status.Text=T("Settings_Saved");
        })),true);
        f.Add("",BusinessForm.Button("Fbr_TestMapping",async(_,_)=>await Run(async()=>
        {
            var db=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PosDbContext>(_provider);
            var invoice=await db.FiscalSubmissions.OrderByDescending(i=>i.Id).FirstOrDefaultAsync()??throw new BusinessValidationException("Fbr_NoInvoice");
            var result=await new MockFbrAdapter(p.FbrRoute).ValidateAsync(p,invoice.InvoiceJson);
            invoice.Status=result.Status;invoice.ValidationCodes=JsonSerializer.Serialize(result.Errors);
            await db.SaveChangesAsync();
            Status.Text=result.Valid?T("Fbr_TestPassed"):string.Join("\n",result.Errors.Select(T));
        }),"GhostButton"),true);
        Tab("Fbr_Title",f);
    }
    private async Task TaxTab()
    {
        var f=new BusinessForm();var rule=new ProductTaxRule{EffectiveFrom=DateTime.Today};
        var products=await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IProductRepository>(_provider).GetAllAsync();
        var select=new ComboBox{ItemsSource=products,DisplayMemberPath="Name",SelectedValuePath="Id",IsEditable=true,IsTextSearchEnabled=true};
        select.SelectionChanged+=(_,_)=>{if(select.SelectedValue is int id)rule.ProductId=id;};f.Add("Pos_Product",select,true);
        f.Text("Tax_HsCode",rule,nameof(rule.HsCode),true,12);f.Text("Tax_SaleType",rule,nameof(rule.SaleType));
        f.Select("Tax_Classification",rule,nameof(rule.Classification),("standard","Tax_Standard"),("exempt","Tax_Exempt"),("zero-rated","Tax_Zero"),("special","Tax_Special"));
        f.Text("Tax_RateFraction",rule,nameof(rule.Rate),true);f.Text("Tax_Sro",rule,nameof(rule.Sro));f.Text("Tax_Authority",rule,nameof(rule.AuthorityReference));
        var price=new TaxPriceInput();f.Check("Tax_ApplyCatalog",price,nameof(price.Apply));f.Check("Tax_Exclusive",price,nameof(price.Exclusive));f.Text("Tax_PriceInput",price,nameof(price.Price),true);
        var from=new DatePicker{SelectedDate=DateTime.Today};var to=new DatePicker();from.SelectedDateChanged+=(_,_)=>rule.EffectiveFrom=from.SelectedDate??DateTime.Today;to.SelectedDateChanged+=(_,_)=>rule.EffectiveTo=to.SelectedDate;
        f.Add("Reports_From",from);f.Add("Reports_To",to);
        f.Add("",new TextBlock{Text=T("Tax_RulesHelp"),TextWrapping=TextWrapping.Wrap},true);
        f.Add("",BusinessForm.Button("Profile_Save",async(_,_)=>await Run(async()=>{await _setup.SaveTaxRuleAsync(rule,priceInput:price.Apply?price.Price:null,taxExclusive:price.Exclusive);await LoadAsync();Status.Text=T("Settings_Saved");})),true);
        var rules=await _setup.TaxRulesAsync();
        var ending=new ComboBox{ItemsSource=rules.Select(r=>new{Id=r.Id,Name=$"#{r.Id} · {products.FirstOrDefault(p=>p.Id==r.ProductId)?.Name} · {r.Rate:P}"}),DisplayMemberPath="Name",SelectedValuePath="Id"};
        var endDate=new DatePicker{SelectedDate=DateTime.Today};f.Add("Tax_History",ending);f.Add("Reports_To",endDate);
        f.Add("",BusinessForm.Button("Tax_EndRule",async(_,_)=>await Run(async()=>{if(ending.SelectedValue is not int id||endDate.SelectedDate is not{} date)throw new BusinessValidationException("Tax_InvalidRule");await _setup.EndTaxRuleAsync(id,date);await LoadAsync();Status.Text=T("Settings_Saved");}),"GhostButton"),true);
        f.Add("Tax_History",new TextBlock{Text=T("Tax_RuleCount")+" "+rules.Count,TextWrapping=TextWrapping.Wrap},true);
        f.Add("",BusinessForm.Button("Tax_ViewRules",async(_,_)=>await Run(async()=>
        {var saved=await _setup.TaxRulesAsync();NoticeDialog.Show(string.Join("\n",saved.Select(r=>$"#{r.Id} · {products.FirstOrDefault(p=>p.Id==r.ProductId)?.Name} · {r.Rate:P} · {r.EffectiveFrom:d} – {r.EffectiveTo:d} · {r.AuthorityReference}")),T("Tax_History"));}),"GhostButton"),true);
        Tab("Tax_Title",f);
    }
    private void LicenseTab()
    {
        var f=new BusinessForm();var input=new ActivationInput();
        string summary;try{summary=_license.Current is{} c?T("License_Activated")+" · "+c.Edition+" · "+T("License_UpdatesUntil")+" "+c.UpdatesUntil.ToString("d"):T(_license.Options.Commercial?"License_Required":"License_Development");}catch{summary=T("License_Invalid");}
        f.Add("License_Status",new TextBlock{Text=summary,TextWrapping=TextWrapping.Wrap},true);
        f.Text("License_Key",input,nameof(input.Key),true,128);f.Text("License_DeviceLabel",input,nameof(input.Label));
        f.Add("",BusinessForm.Button("License_Online",async(_,_)=>await Run(async()=>{await _license.ActivateAsync(input.Key,input.Label);Status.Text=T("License_Activated");})),true);
        f.Add("",BusinessForm.Button("License_ExportRequest",(_,_)=>Export(_license.OfflineRequest(input.Key,input.Label),"pos-activation-request.json"),"GhostButton"));
        f.Add("",BusinessForm.Button("License_ImportResponse",async(_,_)=>await Run(()=>{var file=new OpenFileDialog{Filter="JSON|*.json"};if(file.ShowDialog()==true)_license.Import(File.ReadAllText(file.FileName));Status.Text=T("License_Activated");return Task.CompletedTask;}),"GhostButton"));
        f.Add("",BusinessForm.Button("License_Transfer",async(_,_)=>await Run(async()=>{await _license.RequestTransferAsync();Status.Text=T("License_TransferPending");}),"GhostButton"),true);
        f.Add("",new TextBlock{Text=T("License_PerpetualHelp"),TextWrapping=TextWrapping.Wrap},true);Tab("License_Title",f);
    }
    private void UpdatesTab()
    {
        _updates.ResumeDownloaded();
        var f=new BusinessForm();
        f.Add("Updates_Current",new TextBlock{Text=_updates.CurrentVersion.ToString(3)+" · "+_license.Options.Channel});
        f.Add("Updates_LastCheck",new TextBlock{Text=_updates.LastCheck?.ToLocalTime().ToString("g")??T("Updates_NotChecked")});
        f.Add("",BusinessForm.Button("Updates_Notes",(_,_)=>NoticeDialog.Show(_updates.Available?.Notes??T("Updates_None"),T("Updates_Notes")),"GhostButton"),true);
        f.Add("",BusinessForm.Button("Updates_Check",async(_,_)=>await Run(async()=>{await _updates.CheckAsync();Status.Text=_updates.Available is{} r?T("Updates_Available")+" "+r.Version:T("Updates_None");})),true);
        f.Add("",BusinessForm.Button("Updates_Download",async(_,_)=>await Run(async()=>{await _updates.DownloadAsync();Status.Text=T("Updates_Downloaded");})),true);
        f.Add("",BusinessForm.Button("Updates_Offline",async(_,_)=>await Run(async()=>{var file=new OpenFileDialog{Filter="Signed release|*.json"};if(file.ShowDialog()==true){await _updates.ImportAsync(file.FileName);Status.Text=T("Updates_Downloaded");}}),"GhostButton"),true);
        f.Add("",BusinessForm.Button("Updates_Install",async(_,_)=>await Run(async()=>await _updates.InstallAsync(()=>_shell.HasPendingWork,async()=>
        {
            await _shell.Scanner.StopAsync();
            var db=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PosDbContext>(_provider);
            await db.Database.CloseConnectionAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }))),true);
        f.Add("",BusinessForm.Button("Updates_History",(_,_)=>
        {
            var file=Path.Combine(Path.GetDirectoryName(_storage.DatabasePath)!,"Updates","history.jsonl");
            NoticeDialog.Show(File.Exists(file)?File.ReadAllText(file):T("Updates_NoHistory"),T("Updates_History"));
        },"GhostButton"),true);
        f.Add("",new TextBlock{Text=T("Updates_Safety"),TextWrapping=TextWrapping.Wrap},true);Tab("Updates_Title",f);
    }
    private void StorageTab()
    {
        var f=new BusinessForm();var root=Path.GetDirectoryName(_storage.DatabasePath)!;
        f.Check("Storage_DailyBackups",_profile,nameof(_profile.DailyBackups));f.Text("Setup_BackupFolder",_profile,nameof(_profile.BackupFolder),true,400);
        var schedule=new BackupSchedule(root);var last=schedule.ReadStatus();var backupStatus=new TextBlock{Text=last.Error.Length>0?T(last.Error):last.LastSuccess is{} at?T("Storage_LastBackup")+": "+at.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):T("Storage_NoAutoBackup"),TextWrapping=TextWrapping.Wrap};f.Add("",backupStatus,true);
        f.Add("",BusinessForm.Button("Profile_Save",async(_,_)=>await Run(SaveProfile)),true);
        var folder=new TextBox{Text=root,FlowDirection=FlowDirection.LeftToRight};f.Add("Setup_DataFolder",folder,true);
        f.Add("",BusinessForm.Button("Storage_ChangeFolder",async(_,_)=>await Run(()=>
        {
            if(_shell.HasPendingWork)throw new CommercialException("Updates_FinishDrafts");
            DataLocation.Schedule(root,folder.Text);Status.Text=T("Storage_FolderStaged");return Task.CompletedTask;
        }),"GhostButton"),true);
        f.Add("",BusinessForm.Button("Storage_Backup",async(_,_)=>await Run(async()=>
        {
            if(_shell.HasPendingWork)throw new CommercialException("Updates_FinishDrafts");
            var file=new SaveFileDialog{Filter="POS backup|*.zip",FileName="POS-backup-"+DateTime.Today.ToString("yyyyMMdd")+".zip",InitialDirectory=_profile.BackupFolder.Length>0?_profile.BackupFolder:root};
            if(file.ShowDialog()==true){await DataBackup.CreateAsync(root,file.FileName);Status.Text=T("Storage_BackupCreated");}
        })),true);
        f.Add("",BusinessForm.Button("Storage_Restore",async(_,_)=>await Run(async()=>
        {
            if(_shell.HasPendingWork)throw new CommercialException("Updates_FinishDrafts");
            var file=new OpenFileDialog{Filter="POS backup|*.zip"};if(file.ShowDialog()!=true)return;
            if(!NoticeDialog.Confirm(T("Storage_RestoreConfirm"),T("Storage_Restore")))return;
            await DataBackup.StageRestoreAsync(file.FileName,root,_protected);Status.Text=T("Storage_RestoreStaged");
        }),"GhostButton"),true);
        f.Add("",BusinessForm.Button("Business_Audit",async(_,_)=>await Run(async()=>
        {var events=await _setup.AuditAsync();NoticeDialog.Show(string.Join("\n",events.Take(30).Select(e=>$"{e.At:g} · {e.Action} · {e.Actor}")),T("Business_Audit"));}),"GhostButton"),true);
        f.Add("",new TextBlock{Text=T("Setup_StorageNote"),TextWrapping=TextWrapping.Wrap},true);Tab("Storage_Title",f);
    }
    private void SupportTab()
    {
        var f=new BusinessForm();var report=_diagnostics.Latest();var description=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=90,MaxLength=2000};
        var assembly=typeof(App).Assembly;
        var version=assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        var build=assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute),false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>().ToDictionary(item=>item.Key,item=>item.Value);
        var identity=new TextBox { IsReadOnly=true, TextWrapping=TextWrapping.Wrap, FlowDirection=FlowDirection.LeftToRight,
            Text=string.Join("\n",new[]{version,build.GetValueOrDefault("BuildConfiguration"),build.GetValueOrDefault("BuildTimestampUtc"),build.GetValueOrDefault("BuildSourceFingerprint")}.Where(value=>!string.IsNullOrEmpty(value))) };
        f.Add("Updates_InstalledVersion",identity,true);
        f.Add("Support_Description",description,true);
        var review=new TextBox{Text=JsonSerializer.Serialize(report,new JsonSerializerOptions(SignedDocuments.Json){WriteIndented=true}),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=120,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FlowDirection=FlowDirection.LeftToRight};
        f.Add("Support_Review",review,true);
        var consent=new CheckBox{Content=T("Support_Consent"),Margin=new Thickness(6)};f.Add("",consent,true);
        f.Add("",BusinessForm.Button("Support_Send",async(_,_)=>await Run(async()=>{await _diagnostics.SubmitAsync(_license,report,description.Text,consent.IsChecked==true);Status.Text=T("Support_Sent");})),true);
        f.Add("",BusinessForm.Button("Support_Export",(_,_)=>Export(JsonSerializer.Serialize(new{diagnostics=report,description=description.Text},SignedDocuments.Json),"pos-support.json"),"GhostButton"),true);
        var password=new PasswordBox();f.Add("Profile_CurrentPassword",password);
        f.Add("",BusinessForm.Button("Recovery_Generate",async(_,_)=>await Run(async()=>
        {var code=await _setup.RegenerateRecoveryAsync(password.Password);password.Clear();NoticeDialog.Show(code,T("Setup_RecoveryTitle"));}),"GhostButton"));
        Tab("Help_Support",f);
    }
    private static void Export(string json,string name)
    {var file=new SaveFileDialog{FileName=name,Filter="JSON|*.json"};if(file.ShowDialog()==true)File.WriteAllText(file.FileName,json);}
    private async Task Run(Func<Task> action)
    {
        if(_busy)return;_busy=true;Tabs.IsEnabled=false;
        try{await action();}catch(Exception e){Status.Text=T(e is BusinessValidationException or CommercialException?e.Message:"Setup_SaveFailed");}
        finally{_busy=false;Tabs.IsEnabled=true;}
    }
    private sealed class ActivationInput{public string Key{get;set;}="";public string Label{get;set;}=Environment.MachineName;}
    private sealed class TaxPriceInput{public bool Apply{get;set;}public bool Exclusive{get;set;}public decimal Price{get;set;}}
}
