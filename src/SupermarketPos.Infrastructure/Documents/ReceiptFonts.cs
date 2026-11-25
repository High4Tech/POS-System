using QuestPDF.Drawing;
namespace SupermarketPos.Infrastructure.Documents;
public static class ReceiptFonts
{
    private static readonly Lazy<bool> Urdu=new(()=>
    {
        foreach(var weight in new[]{"Regular","Bold"})
        {
            using var stream=typeof(ReceiptFonts).Assembly.GetManifestResourceStream($"SupermarketPos.Fonts.NotoNastaliqUrdu-{weight}.ttf")??throw new InvalidOperationException("Bundled Urdu font is missing.");
            FontManager.RegisterFont(stream);
        }
        return true;
    });
    public static string Family(string language){if(language=="ur"){_=Urdu.Value;return "Noto Nastaliq Urdu";}return "Segoe UI";}
}
