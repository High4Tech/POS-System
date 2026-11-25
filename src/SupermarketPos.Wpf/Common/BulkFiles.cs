using System.IO;
using System.Text;
using Microsoft.Win32;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Common;

internal static class BulkFiles
{
    public static void SaveTemplate(string fileName, string header)
    {
        var text = LocalizationManager.Instance;
        var picker = new SaveFileDialog
        {
            Title = text["Bulk_Template"],
            FileName = fileName,
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = ".csv"
        };
        if (picker.ShowDialog() == true)
            File.WriteAllText(picker.FileName, header + Environment.NewLine, new UTF8Encoding(true));
    }

    public static string? PickCsv()
    {
        var picker = new OpenFileDialog
        {
            Title = LocalizationManager.Instance["Bulk_Import"],
            Filter = "CSV (*.csv)|*.csv",
            CheckFileExists = true
        };
        return picker.ShowDialog() == true ? picker.FileName : null;
    }
}
