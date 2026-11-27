using System.Text.Json;
using System.Globalization;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.Views;
public static class ReferenceText
{
    public static string Format(string text)
    {
        try
        {
            using var json=JsonDocument.Parse(text);if(json.RootElement.ValueKind!=JsonValueKind.Object)return text;
            return string.Join("\n",json.RootElement.EnumerateObject().Where(p=>p.Value.ValueKind!=JsonValueKind.Null&&p.Value.ToString() is not ("" or "0" or "0.0"))
                .Select(p=>(p.Name.StartsWith("Digital_")||p.Name.StartsWith("Service_")?LocalizationManager.Instance[p.Name]:CultureInfo.CurrentCulture.TextInfo.ToTitleCase(p.Name.Replace('_',' ')))+": "+(p.Value.ValueKind==JsonValueKind.Object?"\n"+Format(p.Value.GetRawText()):p.Value.ToString())));
        }
        catch(JsonException){return string.Join("\n",text.Split('·').Select(s=>s.Trim()).Where(s=>s.Length>0&&!s.StartsWith("Official catalog name",StringComparison.OrdinalIgnoreCase)&&!s.StartsWith("http",StringComparison.OrdinalIgnoreCase)&&!s.StartsWith("historical_",StringComparison.OrdinalIgnoreCase)&&!System.Text.RegularExpressions.Regex.IsMatch(s,@"^(?:0(?:\.0)?\s|(?:Social\s)?GB$)")));}
    }
}
