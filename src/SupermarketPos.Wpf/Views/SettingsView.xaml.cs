using System.Windows.Controls;
using System.Windows;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void ChangePassword(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel profile) return;
        ChangePasswordButton.IsEnabled = false;
        try
        {
            if (await profile.ChangePasswordAsync(CurrentPassword.Password, NewPassword.Password, ConfirmPassword.Password))
            {
                CurrentPassword.Clear();
                NewPassword.Clear();
                ConfirmPassword.Clear();
            }
        }
        finally
        {
            ChangePasswordButton.IsEnabled = true;
        }
    }
}
