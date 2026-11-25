using System.Windows;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

// App-owned prompts avoid the Windows system sound attached to native message boxes.
public partial class NoticeDialog : Window
{
    private NoticeDialog(string message, string title, bool confirmDelete, Window? owner)
    {
        InitializeComponent();
        Owner = owner ?? (System.Windows.Application.Current.MainWindow is { IsVisible: true } main ? main : null);
        WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Title = title;
        Message.Text = message;
        AcceptButton.Content = LocalizationManager.Instance[confirmDelete ? "Common_Delete" : "Common_Ok"];
        if (confirmDelete) AcceptButton.SetResourceReference(StyleProperty, "DangerButton");
        AcceptButton.IsDefault = !confirmDelete;
        AcceptButton.IsCancel = !confirmDelete;
        CancelButton.Visibility = confirmDelete ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            if (confirmDelete) CancelButton.Focus();
            else AcceptButton.Focus();
        };
    }

    public static void Show(string message, string title, Window? owner = null) =>
        new NoticeDialog(message, title, confirmDelete: false, owner).ShowDialog();

    public static bool ConfirmDelete(string message, string title) =>
        new NoticeDialog(message, title, confirmDelete: true, owner: null).ShowDialog() == true;

    public static bool Confirm(string message,string title)
    {
        var dialog=new NoticeDialog(message,title,confirmDelete:true,owner:null);
        dialog.AcceptButton.Content=LocalizationManager.Instance["Common_Confirm"];
        dialog.AcceptButton.SetResourceReference(StyleProperty,"PrimaryButton");
        return dialog.ShowDialog()==true;
    }

    private void Accept(object sender, RoutedEventArgs e) => DialogResult = true;
}
