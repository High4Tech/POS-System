using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

// The same fields/styles are shared by setup, tax settings, activation, recovery and support.
public sealed class BusinessForm : Grid
{
    private int _cells;
    private readonly int _columns;
    private readonly Dictionary<string, (FrameworkElement Control, TextBlock Error)> _fields = new();
    public event Action<string>? FieldEdited;
    public event Action<string>? FieldLeft;
    public IReadOnlyDictionary<string, FrameworkElement> Fields => _fields.ToDictionary(p => p.Key, p => p.Value.Control);
    public BusinessForm(int columns=2)
    {
        _columns=columns;
        SetIsSharedSizeScope(this,true);
        for(var i=0;i<columns;i++)ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
    }
    public static TextBlock Label(string key)
    {
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) };
        label.SetResourceReference(StyleProperty, "FormLabel");
        label.SetBinding(TextBlock.TextProperty, new Binding($"[{key}]") { Source = LocalizationManager.Instance });
        return label;
    }
    public void Add(string key, FrameworkElement control, bool full = false)
    {
        if (full && _cells % _columns != 0) _cells+=_columns-_cells%_columns;
        var row = _cells / _columns;
        while (RowDefinitions.Count <= row) RowDefinitions.Add(new() { Height = GridLength.Auto });
        var panel = new Grid { Margin = new Thickness(6, 4, 6, 9) };
        panel.RowDefinitions.Add(new(){Height=GridLength.Auto,SharedSizeGroup="Heading"+row});
        panel.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.RowDefinitions.Add(new(){Height=GridLength.Auto});
        if (key.Length > 0) panel.Children.Add(Label(key));
        SetRow(control,1);panel.Children.Add(control);
        SetRow(panel, row); SetColumn(panel, _cells % _columns); if (full) SetColumnSpan(panel, _columns);
        Children.Add(panel); _cells += full ? _columns : 1;
    }
    public TextBox Text(string key, object source, string property, bool ltr = false, int limit = 200)
    {
        var input = new TextBox { MaxLength = limit };
        if (ltr) input.FlowDirection = FlowDirection.LeftToRight;
        input.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Add(key, input); Register(property, input, key); return input;
    }
    public CheckBox Check(string key, object source, string property)
    {
        var box = new CheckBox { Margin = new Thickness(0, 8, 0, 6) };
        box.Content=Label(key);box.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        box.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay });
        Add("", box); Register(property, box, key); return box;
    }
    public ComboBox Select(string key, object source, string property, params (string Code, string Key)[] choices)
    {
        var input = new ComboBox { ItemsSource = choices.Select(c => new Choice(c.Code, LocalizationManager.Instance[c.Key])).ToArray(), DisplayMemberPath = "Name", SelectedValuePath = "Code" };
        input.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay });
        Add(key, input); Register(property, input, key); return input;
    }
    public void Password(string key, string field, PasswordBox input)
    {
        input.FlowDirection=FlowDirection.LeftToRight; input.MaxLength=72;
        Add(key,input); Register(field,input,key);
    }
    public void Field(string key,string field,FrameworkElement input,bool full=false)
    {
        Add(key,input,full);Register(field,input,key);
    }
    private void Register(string field, FrameworkElement input, string key)
    {
        var panel=(Grid)input.Parent;
        if (HelpKeys.TryGetValue(key,out var helpKey))
        {
            var help=Label(helpKey); help.MaxWidth=420;help.Margin=new Thickness(8);help.FontSize=15;
            // ToolTipService owns the entire hover lifetime. Never manually open a
            // non-StaysOpen tooltip: that captures the mouse and throws on click.
            var tip=new ToolTip{Content=help};
            var info=new Border{Width=24,Height=24,CornerRadius=new CornerRadius(12),
                Background=System.Windows.Media.Brushes.White,BorderThickness=new Thickness(1),
                Margin=new Thickness(8,0,0,4),ToolTip=tip,VerticalAlignment=VerticalAlignment.Top,
                Child=new TextBlock{Text="i",FontSize=14,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            info.SetResourceReference(Border.BorderBrushProperty,"FieldBorder");
            System.Windows.Automation.AutomationProperties.SetName(info,LocalizationManager.Instance[key]+": "+LocalizationManager.Instance[helpKey]);
            ToolTipService.SetInitialShowDelay(info,150);ToolTipService.SetShowDuration(info,60000);
            var heading=new DockPanel{LastChildFill=true};DockPanel.SetDock(info,Dock.Right);heading.Children.Add(info);
            if(input is CheckBox box)
            {
                var label=(FrameworkElement)box.Content;box.Content=null;heading.Children.Add(label);box.Content=heading;
            }
            else if(panel.Children[0] is TextBlock label)
            {
                panel.Children.RemoveAt(0);heading.Children.Add(label);panel.Children.Insert(0,heading);
            }
        }
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0),Visibility=Visibility.Collapsed};
        error.SetResourceReference(TextBlock.ForegroundProperty,"FieldError");
        SetRow(error,2);panel.Children.Add(error); _fields[field]=(input,error);
        input.LostKeyboardFocus+=(_,_)=>FieldLeft?.Invoke(field);
        switch(input)
        {
            case TextBox text: text.TextChanged+=(_,_)=>FieldEdited?.Invoke(field); break;
            case PasswordBox password: password.PasswordChanged+=(_,_)=>FieldEdited?.Invoke(field); break;
            case ComboBox select: select.SelectionChanged+=(_,_)=>FieldEdited?.Invoke(field); break;
            case CheckBox check: check.Checked+=(_,_)=>FieldEdited?.Invoke(field);check.Unchecked+=(_,_)=>FieldEdited?.Invoke(field);break;
            case DatePicker date: date.SelectedDateChanged+=(_,_)=>FieldEdited?.Invoke(field);break;
        }
    }
    public void ShowErrors(IReadOnlyDictionary<string,string> errors, bool focusFirst=false)
    {
        foreach(var (field,(control,error)) in _fields)
        {
            var invalid=errors.TryGetValue(field,out var key);
            error.Text=invalid?LocalizationManager.Instance[key!]:"";
            error.Visibility=invalid?Visibility.Visible:Visibility.Collapsed;
            if(control is Control input)
            {
                if(invalid){input.SetResourceReference(Control.BorderBrushProperty,"FieldError");input.BorderThickness=new Thickness(2);}
                else {input.ClearValue(Control.BorderBrushProperty);input.ClearValue(Control.BorderThicknessProperty);}
            }
        }
        if(focusFirst && _fields.FirstOrDefault(f=>errors.ContainsKey(f.Key)).Value.Control is{} first) first.Focus();
    }
    private static readonly Dictionary<string,string> HelpKeys = new()
    {
        ["Settings_StoreName"]="Help_StoreName",["Setup_LegalName"]="Help_LegalName",["Invoice_MessageTemplate"]="Help_InvoiceTemplate",
        ["License_Key"]="Help_LicenseKey",["License_DeviceLabel"]="Help_DeviceLabel",
        ["Setup_Ntn"]="Help_Ntn",["Setup_Strn"]="Help_Strn",["Setup_SalesTaxRegistered"]="Help_SalesTax",
        ["Setup_Entity"]="Help_Entity",["Setup_FbrRequired"]="Help_FbrRequired",
        ["Fbr_Route"]="Help_FbrRoute",["Fbr_Provider"]="Help_FbrProvider",["Fbr_Status"]="Help_FbrStatus",
        ["Setup_BackupFolder"]="Help_BackupFolder",["Setup_DataFolder"]="Help_DataFolder",
        ["Setup_Branch"]="Help_Branch",["Setup_Terminal"]="Help_Terminal",["Setup_InvoicePrefix"]="Help_Prefix",
        ["Setup_Printer"]="Help_Printer",["Setup_Jurisdiction"]="Help_Jurisdiction",["Setup_Formation"]="Help_Formation",
        ["Profile_NewPassword"]="Setup_PasswordStrength",["Setup_Postal"]="Help_Postal"
    };
    public static Button Button(string key, RoutedEventHandler click, string style = "PrimaryButton")
    {
        var button = new Button { Margin = new Thickness(4), Padding = new Thickness(16, 9, 16, 9) };
        button.SetResourceReference(StyleProperty, style);
        button.SetBinding(ContentControl.ContentProperty, new Binding($"[{key}]") { Source = LocalizationManager.Instance });
        button.Click += click; return button;
    }
    public record Choice(string Code, string Name);
}
