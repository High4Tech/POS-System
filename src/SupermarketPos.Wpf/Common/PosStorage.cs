using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SupermarketPos.Domain.Common;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.Common;

public sealed class PosStorage
{
    private readonly string _pdfDirectory;
    private readonly PosMediaStore _media;

    public PosStorage()
    {
        var directory = DataDirectory();
        DatabasePath = Path.Combine(directory, "pos.db");
        _pdfDirectory = Directory.CreateDirectory(Path.Combine(directory, "PDFs")).FullName;
        _media = new PosMediaStore(directory);
    }

    private static string DataDirectory()
    {
        var configuredDirectory = AppContext.GetData("SupermarketPos.DataDirectory") as string;
        var directory = configuredDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SupermarketPos");
        if (!Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("The POS data folder must be an absolute path.");

        return AppContext.GetData("SupermarketPos.IsolatedDataDirectory") is true ? Path.GetFullPath(directory) : DataLocation.Resolve(directory);
    }

    public string DatabasePath { get; }

    public string PdfPath(string fileName) => Path.Combine(_pdfDirectory, fileName);

    public string? ChoosePdfPath(string fileName, string? title = null)
    {
        var dialog = new SaveFileDialog
        {
            InitialDirectory = _pdfDirectory,
            FileName = fileName,
            DefaultExt = ".pdf",
            Filter = "PDF (*.pdf)|*.pdf"
        };
        if (title is not null)
            dialog.Title = title;

        dialog.FileOk += (_, args) =>
        {
            if (IsInDirectory(dialog.FileName, _pdfDirectory))
                return;

            args.Cancel = true;
            NoticeDialog.Show(
                LocalizationManager.Instance.Format("Storage_PdfFolder", _pdfDirectory),
                LocalizationManager.Instance["Common_Error"]);
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ImportImage(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || PosMediaStore.IsToken(source)) return source;
        ReadThumbnail(source, 320);
        return _media.Import(source);
    }

    public string ImportTemplatePhoto(string path, string csvPath)
    {
        try
        {
            var source = Path.GetFullPath(path, Path.GetDirectoryName(csvPath)!);
            var root = Path.GetPathRoot(source)!;
            if (root.Length != 3 || root[1] != ':' || new DriveInfo(root).DriveType == DriveType.Network ||
                Path.GetExtension(source).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"))
                throw new DomainException("PhotoPath must name a local PNG, JPG, BMP or GIF file.");
            return ImportImage(source)!;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or FileFormatException or InvalidOperationException)
        {
            throw new DomainException("Could not open the product photo. Check PhotoPath and make sure the file is an image.");
        }
    }

    public string? CategoryImagePath(string? reference) => _media.ResolveCategoryPath(reference);

    public static string? ResolveImagePath(string? reference) => new PosMediaStore(DataDirectory()).ResolvePath(reference);

    public static BitmapSource ReadCategoryThumbnail(string path) => ReadThumbnail(path, 320);

    private static BitmapSource ReadThumbnail(string path, int maximumDimension)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        bool landscape = frame.PixelWidth >= frame.PixelHeight;
        using var input = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (landscape && frame.PixelWidth > maximumDimension) image.DecodePixelWidth = maximumDimension;
        if (!landscape && frame.PixelHeight > maximumDimension) image.DecodePixelHeight = maximumDimension;
        image.StreamSource = input;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static bool IsInDirectory(string path, string directory) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory, StringComparison.OrdinalIgnoreCase);
}
