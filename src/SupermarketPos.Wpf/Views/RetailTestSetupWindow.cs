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
        Title=RetailTestEdition.Standalone?"Wijgandu":"Wijgandu Test";SetResourceReference(StyleProperty,"CaptionWindow");Width=820;Height=720;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new Grid{Margin=new Thickness(24)};SetResourceReference(BackgroundProperty,"BgBrush");Content=root;
        var form=new BusinessForm();var card=new Border{Child=form,MaxWidth=900,VerticalAlignment=VerticalAlignment.Center};card.SetResourceReference(StyleProperty,"Card");root.Children.Add(new ScrollViewer{Content=card,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var branding=new StackPanel{Orientation=Orientation.Horizontal};branding.Children.Add(new Image{Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/SupermarketPos;component/Assets/wijgandu.png")),Width=54,Height=54,Margin=new Thickness(0,0,12,12)});branding.Children.Add(new TextBlock{Text="Wijgandu",FontSize=30,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center});form.Add("",branding,true);form.Add("",BusinessForm.Label(RetailTestEdition.Standalone?"Retail_SetupTitle":"Trial_Title"),true);form.Add("",BusinessForm.Label(RetailTestEdition.Standalone?"Retail_SetupHelp":"Trial_LocalOnly"),true);
        form.SetBinding(FlowDirectionProperty,new System.Windows.Data.Binding(nameof(LocalizationManager.FlowDirection)){Source=LocalizationManager.Instance});
        var name=new TextBox{Text=RetailTestEdition.Standalone?"":"Tech Retail Test Store",MaxLength=150};var username=new TextBox{MaxLength=64,FlowDirection=FlowDirection.LeftToRight};var password=new PasswordBox{MaxLength=72,FlowDirection=FlowDirection.LeftToRight};var confirm=new PasswordBox{MaxLength=72,FlowDirection=FlowDirection.LeftToRight};
        var language=new ComboBox{ItemsSource=new[]{new LanguageChoice("en","English"),new LanguageChoice("ur","اردو")},DisplayMemberPath="Name",SelectedValuePath="Code",SelectedValue="en"};
        language.SelectionChanged+=(_,_)=>LocalizationManager.Instance.SetLanguage((string?)language.SelectedValue??"en");
        form.Field("Settings_StoreName","store",name);form.Field("Login_Username","username",username);form.Field("Login_Password","password",password);form.Field("Profile_ConfirmPassword","confirm",confirm);form.Field("Settings_Language","language",language);
        var owner=new TextBox{MaxLength=150};var phone=new TextBox{MaxLength=30,FlowDirection=FlowDirection.LeftToRight};var email=new TextBox{MaxLength=200,FlowDirection=FlowDirection.LeftToRight};var address=new TextBox{MaxLength=300};string? logo=null;
        if(RetailTestEdition.Standalone)
        {
            form.Field("Setup_Owner","owner",owner);form.Field("Customer_Phone","phone",phone);form.Field("Customer_Email","email",email);form.Field("Settings_StoreAddress","address",address,true);
            var logoLabel=new TextBlock{Text=LocalizationManager.Instance["Setup_YourLogo"]};form.Add("",logoLabel);
            form.Add("",BusinessForm.Button("Profile_ChooseLogo",(_,_)=>
            {
                var picker=new Microsoft.Win32.OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif"};
                if(picker.ShowDialog(this)!=true)return;
                try{logo=storage.ImportImage(picker.FileName);logoLabel.Text=Path.GetFileName(picker.FileName);}
                catch(Exception){form.ShowErrors(new Dictionary<string,string>{{"store","Validation_Text"}},false);}
            },"GhostButton"));
        }
        form.Add("",BusinessForm.Label("Setup_PasswordStrength"),true);
        var folder=new TextBlock{Text=Path.GetDirectoryName(storage.DatabasePath),TextWrapping=TextWrapping.Wrap};form.Add("Setup_DataFolder",folder,true);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};form.Add("",error,true);
        var create=BusinessForm.Button(RetailTestEdition.Standalone?"Setup_Finish":"Trial_Create",async(sender,_)=>
        {
            var button=(Button)sender;button.IsEnabled=false;
            try
            {
                var errors=new Dictionary<string,string>();
                if(string.IsNullOrWhiteSpace(name.Text))errors["store"]="Validation_Required";
                if(RetailTestEdition.Standalone&&string.IsNullOrWhiteSpace(owner.Text))errors["owner"]="Validation_Required";
                if(string.IsNullOrWhiteSpace(username.Text)||username.Text.Any(char.IsControl))errors["username"]="Setup_UsernameRequired";
                try{BusinessSetup.ValidatePassword(password.Password);}catch(BusinessValidationException){errors["password"]="Setup_PasswordStrength";}
                if(confirm.Password!=password.Password)errors["confirm"]="Profile_PasswordMismatch";
                form.ShowErrors(errors,true);if(errors.Count>0)return;
                var profile=new BusinessProfile{StoreName=name.Text.Trim(),Owner=RetailTestEdition.Standalone?owner.Text.Trim():username.Text.Trim(),Phone=phone.Text.Trim(),Email=email.Text.Trim(),Address=address.Text.Trim(),Logo=logo,SetupLogoChosen=true,BusinessType="electronics",Language=(string?)language.SelectedValue??"en",EnableCatalogImages=false};profile.ApplyBusinessDefaults();if(RetailTestEdition.Standalone)profile.EnablePackagedUnits=true;
                foreach(var issue in profile.FieldErrors().Where(e=>e.Key is nameof(BusinessProfile.Phone) or nameof(BusinessProfile.Email)))errors[issue.Key.ToLowerInvariant()]=issue.Value;form.ShowErrors(errors,true);if(errors.Count>0)return;
                var recovery=await setup.CompleteAsync(profile,username.Text.Trim(),password.Password);
                await auth.LoginAsync(username.Text.Trim(),password.Password);password.Clear();confirm.Clear();
                LocalizationManager.Instance.SetLanguage(profile.Language);
                form.Children.Clear();form.RowDefinitions.Clear();
                var recoveryPanel=new StackPanel{Margin=new Thickness(12)};recoveryPanel.Children.Add(BusinessForm.Label("Setup_RecoveryTitle"));
                recoveryPanel.Children.Add(new TextBox{Text=recovery,IsReadOnly=true,FlowDirection=FlowDirection.LeftToRight});
                recoveryPanel.Children.Add(BusinessForm.Label("Setup_RecoveryHelp"));
                recoveryPanel.Children.Add(BusinessForm.Button(RetailTestEdition.Standalone?"Common_Continue":"Trial_Start",(_,_)=>DialogResult=true));form.Children.Add(recoveryPanel);
            }
            catch(Exception e){error.Text=LocalizationManager.Instance[e.Message];}
            finally{button.IsEnabled=true;}
        });create.IsDefault=true;form.Add("",create,true);
    }
    private sealed record LanguageChoice(string Code,string Name);
}
