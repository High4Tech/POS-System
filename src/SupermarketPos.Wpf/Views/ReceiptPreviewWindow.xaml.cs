using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using Microsoft.Win32;
using SupermarketPos.Domain.Common;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

public partial class ReceiptPreviewWindow : UserControl
{
    private readonly ReceiptPreview _preview;
    private readonly string _receiptNumber;
    private readonly PosStorage _storage;
    private readonly BitmapSource[] _pages;
    private int _current;
    private readonly SavedInvoiceActions _actions;
    private readonly IReceiptPrinter _printer;
    private readonly WhatsAppShareService _whatsApp;
    private string? _recipient;
    private string _printerName;
    public Action? RequestClose {get;set;}
    private TaskCompletionSource<bool>? _prompt;
    private void ClosePreview(object sender,RoutedEventArgs e){CancelPrompt();RequestClose?.Invoke();}
    public void CancelPrompt()=>_prompt?.TrySetResult(false);

    public ReceiptPreviewWindow(ReceiptPreview preview, string receiptNumber, PosStorage storage,InvoiceActionInfo? info=null,IReceiptPrinter? printer=null,WhatsAppShareService? whatsApp=null)
    {
        InitializeComponent();
        _preview = preview;
        _receiptNumber = receiptNumber;
        _storage = storage;
        info??=new(receiptNumber,DateTime.Now,0,"",null,null,"en","80","","","");
        _actions=new(preview,info,storage);_printer=printer??new ReceiptPrinter();_whatsApp=whatsApp??new();_recipient=info.Phone;_printerName=info.Printer;
        Unloaded+=(_,_)=>CancelPrompt();
        _pages = preview.Pages.SelectMany(SplitPage).ToArray();
        PageControls.Visibility = _pages.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        ShowPage(0);
    }

    private void PreviousPage(object sender, RoutedEventArgs e) => ShowPage(_current - 1);
    private void NextPage(object sender, RoutedEventArgs e) => ShowPage(_current + 1);

    private void ShowPage(int page)
    {
        if (page < 0 || page >= _pages.Length) return;
        _current = page;
        PagePreview.Source = _pages[page];
        PageNumber.Text = LocalizationManager.Instance.Format("Receipt_PageNumber", page + 1, _pages.Length);
        PreviousButton.IsEnabled = page > 0;
        NextButton.IsEnabled = page < _pages.Length - 1;
    }

    private async void SavePdf(object sender, RoutedEventArgs e)
    {
        var folder=_actions.Info.ExportFolder;
        if(!Directory.Exists(folder))folder=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dialog=new SaveFileDialog{FileName=_actions.FileName,Filter="PDF|*.pdf",DefaultExt=".pdf",AddExtension=true,InitialDirectory=folder};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;

        SaveButton.IsEnabled = false;
        try
        {
            await _actions.DownloadAsync(dialog.FileName);ShowStatus("Msg_ReceiptSaved");
        }
        catch (Exception)
        {
            ShowStatus("Invoice_DownloadFailed",true);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
    private void ShowStatus(string key,bool error=false)
    {
        SavedMessage.Text=LocalizationManager.Instance[key];SavedMessage.Visibility=Visibility.Visible;SavedMessage.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,error?"DangerBrush":"SuccessBrush");
    }
    private async void PrintReceipt(object sender,RoutedEventArgs e)
    {
        if(!PrintButton.IsEnabled)return;
        if(string.IsNullOrWhiteSpace(_printerName)&&!await SelectPrinter())return;
        PrintButton.IsEnabled=RetryPrintButton.IsEnabled=false;
        try{await _printer.PrintAsync(_preview.Pages,_printerName,_actions.Info.Paper,_receiptNumber);RetryPrintButton.Visibility=Visibility.Collapsed;ShowStatus("Invoice_PrintSubmitted");}
        catch(Exception ex){RetryPrintButton.Visibility=Visibility.Visible;ShowStatus(ex is DomainException?ex.Message:"Invoice_PrintFailed",true);}
        finally{PrintButton.IsEnabled=RetryPrintButton.IsEnabled=true;}
    }
    private async Task<bool> SelectPrinter()
    {
        try
        {
            var select=new ComboBox{ItemsSource=await _printer.PrintersAsync(),SelectedItem=_printerName};
            if(!await PromptAsync("Setup_Printer",select,()=>!string.IsNullOrWhiteSpace(select.SelectedItem as string)))return false;
            _printerName=(string)select.SelectedItem;if(_actions.Info.SavePrinter is{} save)await save(_printerName);ShowStatus("Settings_Saved");return true;
        }
        catch{ShowStatus("Invoice_PrinterUnavailable",true);return false;}
    }
    private async void PrinterSettings(object sender,RoutedEventArgs e)=>await SelectPrinter();
    private async Task<bool> ChooseNumber()
    {
        var phone=new TextBox{Text=_recipient??"",MaxLength=60,FlowDirection=FlowDirection.LeftToRight};
        if(!await PromptAsync("Customer_Phone",phone,()=>{try{_=InvoicePhone.Normalize(phone.Text);return true;}catch(DomainException){return false;}}))return false;
        _recipient=InvoicePhone.Normalize(phone.Text);ShowStatus("Invoice_NumberReady");return true;
    }
    private async Task<bool> PromptAsync(string key,Control input,Func<bool> valid)
    {
        if(_prompt is not null)return false;
        var pending=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);_prompt=pending;
        PromptPanel.Children.Clear();PromptPanel.Children.Add(BusinessForm.Label(key));PromptPanel.Children.Add(input);
        var error=BusinessForm.Label(key=="Customer_Phone"?"Validation_Phone":"Validation_Required");error.Visibility=Visibility.Collapsed;error.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");PromptPanel.Children.Add(error);
        var actions=new WrapPanel();actions.Children.Add(BusinessForm.Button("Common_Continue",(_,_)=>{if(valid())pending.TrySetResult(true);else error.Visibility=Visibility.Visible;}));
        input.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;if(valid())pending.TrySetResult(true);else error.Visibility=Visibility.Visible;}};
        actions.Children.Add(BusinessForm.Button("Common_Cancel",(_,_)=>pending.TrySetResult(false),"GhostButton"));PromptPanel.Children.Add(actions);PromptPanel.Visibility=Visibility.Visible;input.Focus();
        try{return await pending.Task;}finally{PromptPanel.Visibility=Visibility.Collapsed;PromptPanel.Children.Clear();_prompt=null;}
    }
    private async void AddNumber(object sender,RoutedEventArgs e)=>await ChooseNumber();
    private async Task ShareAsync(bool browser)
    {
        try
        {
            try{_=InvoicePhone.Normalize(_recipient);}catch(DomainException){if(!await ChooseNumber())return;}
            await _actions.EnsurePdfAsync();ShowStatus(_whatsApp.Open(InvoicePhone.Normalize(_recipient),_actions.Message(),browser));AttachmentNote.Visibility=Visibility.Visible;
        }
        catch(Exception ex){ShowStatus(ex is DomainException?ex.Message:"Invoice_OpenFailed",true);}
    }
    private async void OpenWhatsApp(object sender,RoutedEventArgs e)=>await ShareAsync(false);
    private async void OpenBrowser(object sender,RoutedEventArgs e)=>await ShareAsync(true);
    private async void ViewPdf(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo(await _actions.EnsurePdfAsync()){UseShellExecute=true});}catch{ShowStatus("Invoice_OpenFailed",true);}}
    private async void OpenPdfLocation(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{await _actions.EnsurePdfAsync()}\""){UseShellExecute=true});}catch{ShowStatus("Invoice_OpenFailed",true);}}
    private void CopyDetails(object sender,RoutedEventArgs e){try{Clipboard.SetText(_actions.Message());ShowStatus("Invoice_Copied");}catch{ShowStatus("Invoice_OpenFailed",true);}}
    public void InvokeAction(string action)
    {
        if(action=="print")PrintReceipt(this,new RoutedEventArgs());else if(action=="whatsapp")OpenWhatsApp(this,new RoutedEventArgs());else if(action=="download")SavePdf(this,new RoutedEventArgs());
    }

    private static IEnumerable<BitmapSource> SplitPage(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var width = image.PixelWidth;
        var limit = width * 2;
        if (image.PixelHeight <= limit)
        {
            yield return image;
            yield break;
        }
        var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var row = new byte[width * 4];
        for (var top = 0; top < source.PixelHeight;)
        {
            var bottom = Math.Min(source.PixelHeight, top + limit);
            if (bottom < source.PixelHeight)
            {
                for (var line = bottom - 1; line >= Math.Max(top + 1, bottom - 68); line--)
                {
                    source.CopyPixels(new Int32Rect(0, line, width, 1), row, width * 4, 0);
                    if (!IsBlank(row)) continue;
                    bottom = line + 1;
                    break;
                }
            }
            yield return new CroppedBitmap(source, new Int32Rect(0, top, width, bottom - top));
            top = bottom;
        }
    }

    private static bool IsBlank(byte[] row)
    {
        for (var i = 0; i < row.Length; i += 4)
            if (row[i + 3] > 16 && (row[i] < 246 || row[i + 1] < 246 || row[i + 2] < 246))
                return false;
        return true;
    }
}
