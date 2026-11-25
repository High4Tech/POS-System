using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;
public sealed class OwnerRecoveryWindow : Window
{
    public OwnerRecoveryWindow(IBusinessSetup setup)
    {
        Title=LocalizationManager.Instance["Setup_RecoveryTitle"];Style=(Style)FindResource("FormDialogWindow");WindowStartupLocation=WindowStartupLocation.CenterOwner;
        FlowDirection=LocalizationManager.Instance.FlowDirection;
        var model=new RecoveryInput();var form=new BusinessForm();var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)};
        form.Text("Login_Username",model,nameof(model.Username),true,64);form.Text("Recovery_Code",model,nameof(model.Code),true,64);
        var password=new PasswordBox();var confirm=new PasswordBox();form.Add("Profile_NewPassword",password);form.Add("Profile_ConfirmPassword",confirm);
        var save=BusinessForm.Button("Recovery_Reset",async(sender,_)=>
        {
            ((Button)sender).IsEnabled=false;
            try
            {
                if(password.Password!=confirm.Password)throw new BusinessValidationException("Profile_PasswordMismatch");
                await setup.RecoverAsync(model.Username,model.Code,password.Password);password.Clear();confirm.Clear();status.Text=LocalizationManager.Instance["Recovery_Done"];
            }
            catch(Exception ex){status.Text=LocalizationManager.Instance[ex is BusinessValidationException?ex.Message:"Setup_SaveFailed"];}
            finally{((Button)sender).IsEnabled=true;}
        });
        form.Add("",save,true);form.Add("",status,true);form.Add("",BusinessForm.Button("Common_Close",(_,_)=>Close(),"GhostButton"),true);
        Content=new Viewbox{Stretch=System.Windows.Media.Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Child=new Border{Width=800,Padding=new Thickness(20),Background=System.Windows.Media.Brushes.White,Child=form}};
    }
    private sealed class RecoveryInput{public string Username{get;set;}="";public string Code{get;set;}="";}
}
