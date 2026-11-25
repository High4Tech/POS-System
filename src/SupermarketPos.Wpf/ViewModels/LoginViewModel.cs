using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;

    private readonly IBusinessSetup? _setup;
    public LoginViewModel(IAuthService auth, IBusinessSetup? setup = null) { _auth = auth; _setup = setup; }
    [RelayCommand]
    private void RecoverOwner()
    {
        if (_setup is not null) new Views.OwnerRecoveryWindow(_setup) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
    }

    /// <summary>Raised when authentication succeeds; the shell listens to switch screens.</summary>
    public event Action<User>? LoggedIn;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string? _error;

    [RelayCommand]
    private async Task LoginAsync()
    {
        Error = null;
        try
        {
            var user = await _auth.LoginAsync(Username, Password);
            Password = string.Empty;
            LoggedIn?.Invoke(user);
        }
        catch (Exception)
        {
            Error = LocalizationManager.Instance["Login_Invalid"];
        }
    }
}
