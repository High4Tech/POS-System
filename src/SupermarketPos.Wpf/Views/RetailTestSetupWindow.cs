using System.Windows;
using System.IO;
using System.Windows.Controls;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.Views;

public sealed class RetailTestSetupWindow:Window
{
    public RetailTestSetupWindow(IBusinessSetup setup,IAuthService auth,PosStorage storage)
    {
        Title="High4Tech POS Test";SetResourceReference(StyleProperty,"CaptionWindow");Width=820;Height=720;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new Grid{Margin=new Thickness(24)};SetResourceReference(BackgroundProperty,"BgBrush");Content=root;
        var form=new BusinessForm();var card=new Border{Child=form,MaxWidth=900,VerticalAlignment=VerticalAlignment.Center};card.SetResourceReference(StyleProperty,"Card");root.Children.Add(new ScrollViewer{Content=card,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        form.Add("",BusinessForm.Label("Trial_Title"),true);form.Add("",BusinessForm.Label("Trial_LocalOnly"),true);
        form.SetBinding(FlowDirectionProperty,new System.Windows.Data.Binding(nameof(LocalizationManager.FlowDirection)){Source=LocalizationManager.Instance});
        var name=new TextBox{Text="Tech Retail Test Store",MaxLength=150};var username=new TextBox{MaxLength=64,FlowDirection=FlowDirection.LeftToRight};var password=new PasswordBox{MaxLength=72,FlowDirection=FlowDirection.LeftToRight};var confirm=new PasswordBox{MaxLength=72,FlowDirection=FlowDirection.LeftToRight};
        var language=new ComboBox{ItemsSource=new[]{new LanguageChoice("en","English"),new LanguageChoice("ur","اردو")},DisplayMemberPath="Name",SelectedValuePath="Code",SelectedValue="en"};
        language.SelectionChanged+=(_,_)=>LocalizationManager.Instance.SetLanguage((string?)language.SelectedValue??"en");
        form.Field("Settings_StoreName","store",name);form.Field("Login_Username","username",username);form.Field("Login_Password","password",password);form.Field("Profile_ConfirmPassword","confirm",confirm);form.Field("Settings_Language","language",language);
        form.Add("",BusinessForm.Label("Setup_PasswordStrength"),true);
        var folder=new TextBlock{Text=Path.GetDirectoryName(storage.DatabasePath),TextWrapping=TextWrapping.Wrap};form.Add("Setup_DataFolder",folder,true);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};form.Add("",error,true);
        var create=BusinessForm.Button("Trial_Create",async(sender,_)=>
        {
            var button=(Button)sender;button.IsEnabled=false;
            try
            {
                var errors=new Dictionary<string,string>();
                if(string.IsNullOrWhiteSpace(name.Text))errors["store"]="Validation_Required";
                if(string.IsNullOrWhiteSpace(username.Text)||username.Text.Any(char.IsControl))errors["username"]="Setup_UsernameRequired";
                try{BusinessSetup.ValidatePassword(password.Password);}catch(BusinessValidationException){errors["password"]="Setup_PasswordStrength";}
                if(confirm.Password!=password.Password)errors["confirm"]="Profile_PasswordMismatch";
                form.ShowErrors(errors,true);if(errors.Count>0)return;
                var profile=new BusinessProfile{StoreName=name.Text.Trim(),Owner=username.Text.Trim(),BusinessType="electronics",Language=(string?)language.SelectedValue??"en",EnableCatalogImages=false};profile.ApplyBusinessDefaults();
                var recovery=await setup.CompleteAsync(profile,username.Text.Trim(),password.Password);
                await auth.LoginAsync(username.Text.Trim(),password.Password);password.Clear();confirm.Clear();
                LocalizationManager.Instance.SetLanguage(profile.Language);
                form.Children.Clear();form.RowDefinitions.Clear();
                var recoveryPanel=new StackPanel{Margin=new Thickness(12)};recoveryPanel.Children.Add(BusinessForm.Label("Setup_RecoveryTitle"));
                recoveryPanel.Children.Add(new TextBox{Text=recovery,IsReadOnly=true,FlowDirection=FlowDirection.LeftToRight});
                recoveryPanel.Children.Add(BusinessForm.Label("Setup_RecoveryHelp"));
                recoveryPanel.Children.Add(BusinessForm.Button("Trial_Start",(_,_)=>DialogResult=true));form.Children.Add(recoveryPanel);
            }
            catch(Exception e){error.Text=LocalizationManager.Instance[e.Message];}
            finally{button.IsEnabled=true;}
        });form.Add("",create,true);
    }
    private sealed record LanguageChoice(string Code,string Name);
}
