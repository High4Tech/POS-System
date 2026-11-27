using System.Globalization;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settings;
    private readonly IAuthService _auth;
    private readonly PosStorage _storage;
    private readonly StoreProfile _profile;
    private readonly DesktopTextSize _textSize;
    private readonly IServiceProvider _provider;

    public SettingsViewModel(ISettingsRepository settings, IAuthService auth, PosStorage storage,
        StoreProfile profile, ScannerFeedback sounds, DesktopTextSize textSize, IServiceProvider provider)
    {
        _settings = settings;
        _auth = auth;
        _storage = storage;
        _profile = profile;
        _textSize = textSize;
        _provider = provider;
        Sounds = sounds;
    }

    [ObservableProperty] private string _storeName = string.Empty;
    [ObservableProperty] private string _storeAddress = string.Empty;
    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private string _lowStockThreshold = "5";
    [ObservableProperty] private string _vatRatesCsv = "0.01,0.10,0.20";
    [ObservableProperty] private string? _logoPath;
    [ObservableProperty] private string? _profileStatus;
    [ObservableProperty] private string? _passwordStatus;
    [ObservableProperty] private string? _passwordError;
    [ObservableProperty] private string _operatorName = string.Empty;
    [ObservableProperty] private string? _operatorStatus;
    [ObservableProperty] private int _textSizePercent = 100;
    [ObservableProperty] private string? _textSizeStatus;
    public string BusinessSettingsTitle=>LocalizationManager.Instance[Common.RetailTestEdition.Standalone?"Retail_Settings":"Business_Settings"];
    public string BusinessSettingsHelp=>LocalizationManager.Instance[Common.RetailTestEdition.Standalone?"Retail_SettingsHelp":"Business_SettingsHelp"];
    public string Username => _auth.CurrentUser?.Username ?? string.Empty;
    public ScannerFeedback Sounds { get; }
    public TextSizeOption[] TextSizes { get; } =
        DesktopTextSize.Options.Select(percent => new TextSizeOption(percent, $"{percent}%")).ToArray();

    public LanguageOption[] Languages { get; } = { new("en", "English"), new("ur", "اردو") };

    [RelayCommand]
    private async Task OpenBusinessSettingsAsync()
    {
        _auth.RequireAdmin();
        var window = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Views.BusinessSettingsWindow>(_provider);
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ShowDialog();
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        var s = await _settings.GetAsync();
        StoreName = s.StoreName;
        StoreAddress = s.StoreAddress;
        Language = Domain.Localization.PosText.NormalizeLanguage(s.Language);
        LowStockThreshold = s.LowStockThreshold.ToString("0.###", CultureInfo.InvariantCulture);
        VatRatesCsv = s.VatRatesCsv;
        LogoPath = s.LogoPath;
        OperatorName = _auth.CurrentUser?.DisplayName ?? string.Empty;
        TextSizePercent = _textSize.Percent;
        TextSizeStatus = null;
        ProfileStatus = PasswordStatus = PasswordError = OperatorStatus = null;
    }

    [RelayCommand]
    private void SaveTextSize()
    {
        var text = LocalizationManager.Instance;
        try
        {
            _textSize.Save(TextSizePercent);
            TextSizeStatus = text["Settings_TextSizeSaved"];
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            Sounds.PlayWarning();
            Views.NoticeDialog.Show(error.Message, text["Common_Error"]);
        }
    }

    [RelayCommand]
    private async Task SaveOperatorAsync()
    {
        var text = LocalizationManager.Instance;
        OperatorStatus = null;
        if (string.IsNullOrWhiteSpace(OperatorName))
        {
            Sounds.PlayWarning();
            Views.NoticeDialog.Show(text["Profile_OperatorRequired"], text["Common_Error"]);
            return;
        }
        try
        {
            await _auth.UpdateOperatorNameAsync(OperatorName);
            OperatorName = _auth.CurrentUser!.DisplayName;
            OperatorStatus = text["Profile_OperatorSaved"];
        }
        catch (Exception ex)
        {
            Sounds.PlayWarning();
            Views.NoticeDialog.Show(ex.Message, text["Common_Error"]);
        }
    }

    [RelayCommand]
    private void ChooseLogo()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.Instance["Profile_ChooseLogo"],
            Filter = "PNG, JPG|*.png;*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog() == true)
            LogoPath = dialog.FileName;
    }

    [RelayCommand]
    private void RemoveLogo() => LogoPath = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var threshold = decimal.Parse(LowStockThreshold.Replace(',', '.'), CultureInfo.InvariantCulture);
            var rates = VatRatesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => decimal.Parse(r, CultureInfo.InvariantCulture))
                .ToList();

            var logo = _storage.ImportImage(LogoPath);
            var s = await _settings.GetAsync();
            s.Update(StoreName, StoreAddress, Language, threshold, rates);
            s.SetLogoPath(logo);
            await _settings.SaveAsync(s);

            LocalizationManager.Instance.SetLanguage(Language);
            _profile.Apply(s);
            LogoPath = logo;

            ProfileStatus = LocalizationManager.Instance["Settings_Saved"];
        }
        catch (Exception ex)
        {
            Sounds.PlayWarning();
            Views.NoticeDialog.Show(ex.Message, LocalizationManager.Instance["Common_Error"]);
        }
    }

    public async Task<bool> ChangePasswordAsync(string current, string next, string confirm)
    {
        var text = LocalizationManager.Instance;
        PasswordStatus = PasswordError = null;
        if (string.IsNullOrWhiteSpace(next) || Encoding.UTF8.GetByteCount(next) > 72)
            PasswordError = text["Profile_PasswordLength"];
        else if (next != confirm)
            PasswordError = text["Profile_PasswordMismatch"];
        if (PasswordError is not null) return false;

        try
        {
            SupermarketPos.Infrastructure.Business.BusinessSetup.ValidatePassword(next);
            await _auth.ChangePasswordAsync(current, next);
            PasswordStatus = text["Profile_PasswordChanged"];
            return true;
        }
        catch (SupermarketPos.Domain.Business.BusinessValidationException ex)
        {
            PasswordError=text[ex.Message];
        }
        catch (AuthException)
        {
            PasswordError = text["Profile_CurrentInvalid"];
        }
        catch (Exception)
        {
            PasswordError = text["Profile_PasswordError"];
        }
        return false;
    }
}

public record LanguageOption(string Code, string Label);
public record TextSizeOption(int Percent, string Label);
