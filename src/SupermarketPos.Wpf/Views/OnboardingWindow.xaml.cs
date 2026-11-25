using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;
public partial class OnboardingWindow : Window
{
    private readonly OnboardingViewModel _vm;
    private readonly IBusinessSetup _setup;
    private readonly IAuthService _auth;
    private readonly LicenseClient _license;
    private readonly PosStorage _storage;
    private PasswordBox? _password, _confirm;
    private BusinessForm? _form;
    private bool _attempted;
    private readonly HashSet<string> _touched = [];
    private readonly Dictionary<string,string> _actionErrors = [];
    private bool _saved;
    public bool RecordsOnly { get; private set; }
    private void OpenRecords(object sender, RoutedEventArgs e) { RecordsOnly = true; _saved = true; DialogResult = true; }
    public OnboardingWindow(OnboardingViewModel vm, IBusinessSetup setup, IAuthService auth, LicenseClient license, PosStorage storage)
    {
        InitializeComponent(); _vm = vm; _setup = setup; _auth = auth; _license = license; _storage = storage; DataContext = vm;
        vm.DataFolder = Path.GetDirectoryName(storage.DatabasePath)!;
        SourceInitialized += (_,_) => { Width=Math.Min(1100,SystemParameters.WorkArea.Width); Height=Math.Min(820,SystemParameters.WorkArea.Height); };
        Loaded += async (_, _) => { await vm.LoadAsync(); Draw(); };
        Closing += (_, args) => { if (_vm.Busy && !_saved) args.Cancel = true; };
    }
    private static string T(string key) => LocalizationManager.Instance[key];
    private void Draw()
    {
        _vm.Error = null; _password = _confirm = null; _attempted=false; _touched.Clear(); _actionErrors.Clear();
        var form = new BusinessForm(); var p = _vm.Profile;
        _form=form;
        Heading.Text = T("Setup_Step" + _vm.Step);
        Description.Text = T("Setup_Help" + _vm.Step);
        BackButton.IsEnabled = _vm.Step > 0 && !_saved;
        RecordsButton.Visibility = _vm.Step == 1 && _vm.PreviouslyCompleted ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = T(_saved ? "Common_Close" : _vm.Step == 8 ? "Setup_Finish" : "Setup_Next");
        switch (_vm.Step)
        {
            case 0:
                var languages = form.Select("Settings_Language", p, nameof(p.Language), ("en", "Setup_English"), ("ur", "Setup_Urdu"));
                languages.SelectionChanged += (_, _) => { LocalizationManager.Instance.SetLanguage(p.Language); Heading.Text = T("Setup_Step0"); Description.Text = T("Setup_Help0");NextButton.Content=T("Setup_Next");BackButton.Content=T("Setup_Back"); };
                break;
            case 1:
                form.Text("License_Key", _vm, nameof(_vm.LicenseKey), true, 128);
                form.Text("License_DeviceLabel", _vm, nameof(_vm.DeviceLabel),limit:100);
                var status = new TextBlock { Text = LicenseStatus(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
                form.Add("License_Status", status, true);
                form.Add("", BusinessForm.Button("License_Online", async (_, _) => { if(ValidateActivation()) await Run(async () => { await _license.ActivateAsync(_vm.LicenseKey, _vm.DeviceLabel); status.Text = LicenseStatus(); },nameof(_vm.LicenseKey)); }), true);
                form.Add("", BusinessForm.Button("License_ExportRequest", (_, _) => { if(ValidateActivation()) ExportRequest(); }, "GhostButton"));
                form.Add("", BusinessForm.Button("License_ImportResponse", async (_, _) => await Run(() => { var file = new OpenFileDialog { Filter = "JSON|*.json" }; if (file.ShowDialog() == true) _license.Import(File.ReadAllText(file.FileName)); status.Text = LicenseStatus(); return Task.CompletedTask; }), "GhostButton"));
                form.Add("", BusinessForm.Button("Help_Support", (_, _) => NoticeDialog.Show(_license.Options.Support ?? T("License_ServicePending"), T("Help_Support")), "GhostButton"), true);
                break;
            case 2:
                if (_vm.HasUsers && !_auth.IsLoggedIn)
                { form.Text("Login_Username", _vm, nameof(_vm.Username), true, 64); _password = new(); form.Password("Login_Password","Password", _password); }
                else form.Add("", new TextBlock { Text = T("Setup_OwnerReady"), TextWrapping = TextWrapping.Wrap }, true);
                break;
            case 3:
                var choices = new StackPanel();
                var group="Registration-"+Guid.NewGuid().ToString("N");
                foreach (var registered in new[] { false, true })
                {
                    var text=new StackPanel();text.Children.Add(new TextBlock{Text=T(registered?"Setup_Registered":"Setup_Unregistered"),FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
                    text.Children.Add(new TextBlock{Text=T(registered?"Setup_RegisteredChoiceHelp":"Setup_UnregisteredChoiceHelp"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
                    var radio = new RadioButton { Content = text, GroupName=group, IsChecked = p.Registered == registered, Style=(Style)FindResource("RegistrationChoice") };
                    radio.Checked += (_, _) => { typeof(BusinessProfile).GetProperty(nameof(p.Registered))!.SetValue(p, registered);_touched.Add(nameof(p.Registered));RefreshValidation(); };
                    choices.Children.Add(radio);
                }
                form.Field("",nameof(p.Registered),choices,true);BusinessFeatureFields.Add(form,p);break;
            case 4:
                form.Text("Settings_StoreName", p, nameof(p.StoreName), limit:150);
                form.Text("Setup_Owner", p, nameof(p.Owner));
                form.Text("Customer_Phone", p, nameof(p.Phone), true,30);
                form.Text("Customer_Email", p, nameof(p.Email), true);
                form.Text("Settings_StoreAddress", p, nameof(p.Address), limit:300);
                form.Text("Setup_City", p, nameof(p.City));
                form.Text("Setup_Province", p, nameof(p.Province));
                form.Add("", BusinessForm.Button("Profile_ChooseLogo", async (_, _) => await Run(() => { var file = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg" }; if (file.ShowDialog() == true) { _vm.Profile = p with { Logo = _storage.ImportImage(file.FileName), SetupLogoChosen=true }; Draw(); } return Task.CompletedTask; }), "GhostButton"));
                var preview = new SetupInvoicePreview(p); form.Add("Setup_FullPreview",preview,true);
                form.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>preview.Refresh(p)));
                break;
            case 5:
                if (!p.Registered) { form.Add("", new TextBlock { Text = T("Setup_UnregisteredNotice"), TextWrapping = TextWrapping.Wrap }, true); break; }
                form.Text("Setup_LegalName", p, nameof(p.LegalName));
                form.Select("Setup_Entity", p, nameof(p.EntityType), ("sole", "Setup_Sole"), ("aop", "Setup_Aop"), ("private", "Setup_Private"), ("public", "Setup_Public"), ("other", "Payment_Other"));
                form.Text("Setup_Ntn", p, nameof(p.Ntn), true, 15);
                form.Check("Setup_SalesTaxRegistered", p, nameof(p.SalesTaxRegistered));
                form.Text("Setup_Strn", p, nameof(p.Strn), true, 13);
                form.Text("Setup_Jurisdiction", p, nameof(p.Jurisdiction));
                form.Text("Setup_Formation", p, nameof(p.Formation));
                form.Check("Setup_FbrRequired", p, nameof(p.FbrRequired));
                form.Select("Fbr_Route", p, nameof(p.FbrRoute), ("none", "Fbr_NotConfigured"), ("pos", "Fbr_Pos"), ("digital", "Fbr_Digital"));
                form.Text("Fbr_Provider",p,nameof(p.FbrProvider));
                form.Select("Fbr_Status", p, nameof(p.FbrStatus), ("not-configured", "Fbr_NotConfigured"), ("pending", "Fbr_Pending"), ("sandbox", "Fbr_Sandbox"));
                form.Add("", new TextBlock { Text = T("Fbr_Explanation"), TextWrapping = TextWrapping.Wrap }, true);
                break;
            case 6:
                form.Select("Setup_Paper", p, nameof(p.Paper), ("58", "Setup_58"), ("80", "Setup_80"), ("A4", "Setup_A4"));
                form.Select("Setup_InvoiceLanguage", p, nameof(p.InvoiceLanguage), ("en", "Setup_English"), ("ur", "Setup_Urdu"));
                form.Text("Setup_Printer", p, nameof(p.Printer));
                form.Text("Setup_BackupFolder", p, nameof(p.BackupFolder), true, 400);
                form.Text("Setup_Branch", p, nameof(p.Branch),true);
                form.Text("Setup_Terminal", p, nameof(p.Terminal),true);
                form.Text("Setup_InvoicePrefix", p, nameof(p.InvoicePrefix), true, 12);
                form.Text("Setup_InvoiceTitle", p, nameof(p.InvoiceTitle));
                form.Text("Setup_DataFolder", _vm, nameof(_vm.DataFolder), true, 400);
                form.Add("", new TextBlock { Text = T("Setup_StorageNote"), TextWrapping = TextWrapping.Wrap }, true);
                break;
            case 7:
                if (!_vm.HasUsers)
                {
                    form.Text("Login_Username", _vm, nameof(_vm.Username), true, 64);
                    _password = new(); _confirm = new(); form.Password("Profile_NewPassword","Password", _password); form.Password("Profile_ConfirmPassword","ConfirmPassword", _confirm);
                }
                else form.Add("", new TextBlock { Text = T("Setup_OwnerReady"), TextWrapping = TextWrapping.Wrap }, true);
                break;
            case 8:
                form.Add("Settings_StoreName", new TextBlock { Text = p.StoreName });
                form.Add("Setup_Registration", new TextBlock { Text = T(p.Registered ? "Setup_Registered" : "Setup_Unregistered") });
                form.Add("License_Status", new TextBlock { Text = LicenseStatus(), TextWrapping = TextWrapping.Wrap });
                form.Add("Fbr_Status", new TextBlock { Text = T(p.FbrRequired ? "Setup_RequiredIntegration" : "Fbr_NotConfigured"), TextWrapping = TextWrapping.Wrap });
                break;
        }
        Page.Content = form;
        form.FieldEdited += field => { _actionErrors.Clear(); _vm.Error=null; RefreshValidation(); };
        form.FieldLeft += field => { _touched.Add(field); RefreshValidation(); };
        RefreshValidation();
    }
    private string LicenseStatus()
    {
        try { return _license.Current is { } c ? T("License_Activated") + " · " + c.Edition : T(_license.Options.Commercial ? "License_Required" : "License_Development"); }
        catch { return T("License_Invalid"); }
    }
    private void ExportRequest()
    {
        try
        {
            var file = new SaveFileDialog { Filter = "JSON|*.json", FileName = "pos-activation-request.json" };
            if (file.ShowDialog() == true) File.WriteAllText(file.FileName, _license.OfflineRequest(_vm.LicenseKey, _vm.DeviceLabel));
        }
        catch { _vm.Error = T("Setup_SaveFailed"); }
    }
    private string? _ownerPassword;
    private Dictionary<string,string> StepErrors()
    {
        var errors=_vm.Profile.FieldErrors().Where(e=>_form?.Fields.ContainsKey(e.Key)==true).ToDictionary(e=>e.Key,e=>e.Value);
        void Required(string field,string? value) { if(string.IsNullOrWhiteSpace(value))errors[field]="Validation_Required"; }
        if(_vm.Step==1)
        {
            var key=_vm.LicenseKey.Trim();
            if(key.Length>0&&!Regex.IsMatch(key,"^POS-[a-fA-F0-9]{48}$"))errors[nameof(_vm.LicenseKey)]="Validation_LicenseKey";
            if(_license.Options.Commercial)
            {
                try { if(_license.Current is null&&!errors.ContainsKey(nameof(_vm.LicenseKey)))errors[nameof(_vm.LicenseKey)]=key.Length==0?"Validation_Required":"License_Required"; }
                catch { errors[nameof(_vm.LicenseKey)]="License_Invalid"; }
            }
            Required(nameof(_vm.DeviceLabel),_vm.DeviceLabel);
            if(_vm.DeviceLabel.Length>100||_vm.DeviceLabel.Any(char.IsControl))errors[nameof(_vm.DeviceLabel)]="Validation_Text";
        }
        if((_vm.Step==2&&_vm.HasUsers&&!_auth.IsLoggedIn)||(_vm.Step==7&&!_vm.HasUsers))
        {
            Required(nameof(_vm.Username),_vm.Username); Required("Password",_password?.Password);
            if(_vm.Username.Length>64||_vm.Username.Any(char.IsControl))errors[nameof(_vm.Username)]="Validation_Text";
            if(_vm.Step==7)
            {
                if(!string.IsNullOrEmpty(_password?.Password))
                    try { Infrastructure.Business.BusinessSetup.ValidatePassword(_password.Password); }
                    catch(BusinessValidationException ex){errors["Password"]=ex.Message;}
                Required("ConfirmPassword",_confirm?.Password);
                if(!string.IsNullOrEmpty(_confirm?.Password)&&_password?.Password!=_confirm.Password)errors["ConfirmPassword"]="Profile_PasswordMismatch";
            }
        }
        if(_vm.Step==5 && _vm.Profile.FbrStatus=="sandbox" && (_vm.Profile.FbrProvider!="mock"||_vm.Profile.FbrRoute=="none"))errors[nameof(BusinessProfile.FbrProvider)]="Validation_Mock";
        if(_vm.Step==3&&!_vm.Profile.Registered&&(_vm.Profile.SalesTaxRegistered||_vm.Profile.Strn.Length>0||_vm.Profile.FbrRequired))errors[nameof(BusinessProfile.Registered)]="Validation_RegistrationConflict";
        if(_vm.Step==6)
        {
            Required(nameof(_vm.DataFolder),_vm.DataFolder);
            if(_vm.DataFolder.Length>0&&!ValidFolder(_vm.DataFolder))errors[nameof(_vm.DataFolder)]="Storage_InvalidFolder";
            if(_vm.Profile.BackupFolder.Length>0&&!ValidFolder(_vm.Profile.BackupFolder))errors[nameof(BusinessProfile.BackupFolder)]="Storage_InvalidFolder";
        }
        foreach(var pair in _actionErrors)errors[pair.Key]=pair.Value;
        return errors;
    }
    private static bool ValidFolder(string value)
    {
        try { return value.Length<=400&&Path.IsPathFullyQualified(value)&&value.IndexOfAny(Path.GetInvalidPathChars())<0&&new DriveInfo(Path.GetPathRoot(value)!).DriveType==DriveType.Fixed; }
        catch { return false; }
    }
    private void RefreshValidation(bool focus=false)
    {
        if(_saved){NextButton.IsEnabled=!_vm.Busy;return;}
        var errors=StepErrors();
        var visible=errors.Where(e=>_attempted||_touched.Contains(e.Key)||_actionErrors.ContainsKey(e.Key)).ToDictionary(e=>e.Key,e=>e.Value);
        _form?.ShowErrors(visible,focus);
        NextButton.IsEnabled=!_vm.Busy&&visible.Count==0;
    }
    private bool ValidateActivation()
    {
        _attempted=true;_actionErrors.Clear();
        if(string.IsNullOrWhiteSpace(_vm.LicenseKey))_actionErrors[nameof(_vm.LicenseKey)]="Validation_Required";
        // A validly shaped key still requires a signed server response; dummy data never means activated.
        var errors=StepErrors();
        errors.Remove(nameof(_vm.LicenseKey),out var licenseError);
        if(licenseError is not null && licenseError!="License_Required")_actionErrors[nameof(_vm.LicenseKey)]=licenseError;
        RefreshValidation(true);
        return _actionErrors.Count==0&&errors.Count==0;
    }
    private bool RedirectInvalidSummary()
    {
        var errors=_vm.Profile.FieldErrors().ToDictionary(e=>e.Key,e=>e.Value);
        if(_vm.Profile.FbrStatus=="sandbox"&&(_vm.Profile.FbrProvider!="mock"||_vm.Profile.FbrRoute=="none"))errors[nameof(BusinessProfile.FbrProvider)]="Validation_Mock";
        if(errors.Count==0)return false;
        var field=errors.First().Key;
        _vm.Step=!_vm.Profile.Registered&&(_vm.Profile.SalesTaxRegistered||_vm.Profile.Strn.Length>0||_vm.Profile.FbrRequired)?3:field is nameof(BusinessProfile.StoreName) or nameof(BusinessProfile.Owner) or nameof(BusinessProfile.Phone) or nameof(BusinessProfile.Email) or nameof(BusinessProfile.Address) or nameof(BusinessProfile.City) or nameof(BusinessProfile.Province)?4:
            field is nameof(BusinessProfile.LegalName) or nameof(BusinessProfile.EntityType) or nameof(BusinessProfile.Ntn) or nameof(BusinessProfile.Strn) or nameof(BusinessProfile.SalesTaxRegistered) or nameof(BusinessProfile.FbrRequired) or nameof(BusinessProfile.FbrRoute) or nameof(BusinessProfile.FbrProvider) or nameof(BusinessProfile.FbrStatus)?5:6;
        Draw();_attempted=true;RefreshValidation(true);return true;
    }
    private async void Next(object sender, RoutedEventArgs e)
    {
        if (_saved) { DialogResult = true; return; }
        _attempted=true;
        if(_vm.Step==8&&RedirectInvalidSummary())return;
        if(StepErrors().Count>0){RefreshValidation(true);return;}
        await Run(async () =>
        {
            if (_vm.Step == 1 && _license.Options.Commercial && _license.Current is null) throw new CommercialException("License_Required");
            if (_vm.Step == 2 && _vm.HasUsers && !_auth.IsLoggedIn) { await _auth.LoginAsync(_vm.Username, _password?.Password ?? ""); _auth.RequireAdmin(); }
            if (_vm.Step == 4 && string.IsNullOrWhiteSpace(_vm.Profile.StoreName)) throw new BusinessValidationException("Setup_NameRequired");
            if (_vm.Step == 7 && !_vm.HasUsers)
            {
                if (_password?.Password != _confirm?.Password) throw new BusinessValidationException("Profile_PasswordMismatch");
                Infrastructure.Business.BusinessSetup.ValidatePassword(_password?.Password); _ownerPassword = _password!.Password;
            }
            if(_vm.Step==6)
            {
                try { DataLocation.ValidateChange(Path.GetDirectoryName(_storage.DatabasePath)!,_vm.DataFolder); }
                catch(CommercialException ex){_actionErrors[nameof(_vm.DataFolder)]=ex.Message;RefreshValidation(true);return;}
                if(_vm.Profile.BackupFolder.Length>0)
                {
                    try
                    {
                        var folder=Path.GetFullPath(_vm.Profile.BackupFolder);
                        Directory.CreateDirectory(folder);
                        var probe=Path.Combine(folder,".pos-write-check-"+Guid.NewGuid().ToString("N"));
                        File.WriteAllText(probe,"");File.Delete(probe);
                    }
                    catch { _actionErrors[nameof(BusinessProfile.BackupFolder)]="Storage_InvalidFolder";RefreshValidation(true);return; }
                }
            }
            if (_vm.Step == 8)
            {
                DataLocation.ValidateChange(Path.GetDirectoryName(_storage.DatabasePath)!, _vm.DataFolder);
                _vm.RecoveryCode = await _setup.CompleteAsync(_vm.Profile, _vm.Username, _ownerPassword);
                if (!_vm.HasUsers) await _auth.LoginAsync(_vm.Username, _ownerPassword!);
                // Account/profile commit is authoritative even if scheduling relocation fails.
                try { DataLocation.Schedule(Path.GetDirectoryName(_storage.DatabasePath)!, _vm.DataFolder); }
                catch { _vm.Error = T("Storage_FolderScheduleFailed"); }
                _ownerPassword = null;
                _saved = true;
                if (_vm.RecoveryCode is { } recovery)
                {
                    Heading.Text = T("Setup_RecoveryTitle"); Description.Text = T("Setup_RecoveryHelp");
                    var code = new TextBox { Text = recovery, IsReadOnly = true, FlowDirection = FlowDirection.LeftToRight };
                    Page.Content = code; BackButton.IsEnabled = false; NextButton.Content = T("Setup_Start");
                }
                else DialogResult = true;
                return;
            }
            var next = _vm.Step == 2 && _vm.ResumeStep > 3 ? Math.Min(_vm.ResumeStep, _vm.HasUsers ? 8 : 7) : _vm.Step + 1;
            await _vm.SaveAsync(next);
            _vm.Step = next;
            Draw();
        });
    }
    private async void Back(object sender, RoutedEventArgs e) => await Run(async () => { var previous=Math.Max(0,_vm.Step-1);await _vm.SaveAsync(previous);_vm.Step=previous;Draw(); });
    private async Task Run(Func<Task> action,string? errorField=null)
    {
        if (_vm.Busy) return;
        _vm.Busy = true; NextButton.IsEnabled = BackButton.IsEnabled = false; _vm.Error = null;
        try { await action(); }
        catch (Exception ex)
        {
            if(ex is SupermarketPos.Domain.Common.AuthException)
            { _actionErrors[nameof(_vm.Username)]="Validation_Credentials";_actionErrors["Password"]="Validation_Credentials"; }
            else if(errorField is not null)_actionErrors[errorField]=ex is BusinessValidationException or CommercialException?ex.Message:"Setup_SaveFailed";
            else _vm.Error=T(ex is BusinessValidationException or CommercialException?ex.Message:"Setup_SaveFailed");
        }
        finally { _vm.Busy = false; BackButton.IsEnabled = _vm.Step > 0 && !_saved; RefreshValidation(); }
    }
}
