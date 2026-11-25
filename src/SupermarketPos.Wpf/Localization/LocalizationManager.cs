using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using SupermarketPos.Domain.Localization;

namespace SupermarketPos.Wpf.Localization;

/// <summary>
/// Live UI string source for the English and Urdu resources.
/// Bindings via <see cref="TranslateExtension"/> target the indexer, so switching language at
/// runtime refreshes every translated control. No user-facing strings are hardcoded in XAML.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    public CultureInfo Culture { get; private set; } = PosText.Culture("en");

    private LocalizationManager() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage => Culture.TwoLetterISOLanguageName;
    public System.Windows.Media.FontFamily FontFamily=>CurrentLanguage=="ur"?new(new Uri("pack://application:,,,/SupermarketPos;component/"),"./Assets/Fonts/#Noto Nastaliq Urdu"):new("Segoe UI");
    public FlowDirection FlowDirection => Culture.TextInfo.IsRightToLeft
        ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    public XmlLanguage Language => XmlLanguage.GetLanguage(Culture.IetfLanguageTag);

    /// <summary>Indexer used by bindings: <c>{loc:T Pos_Total}</c> resolves to <c>this["Pos_Total"]</c>.</summary>
    public string this[string key] => PosText.Get(key, Culture);

    public string Format(string key, params object[] args) =>
        string.Format(Culture, this[key], args);

    public void SetLanguage(string code)
    {
        Culture = PosText.Culture(code);
        Thread.CurrentThread.CurrentUICulture = Culture;
        if(System.Windows.Application.Current is{} app)app.Resources["UiFont"]=FontFamily;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
