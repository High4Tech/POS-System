using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Dtos;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Business;

namespace SupermarketPos.Infrastructure.Documents;

public sealed class ReceiptService(ISettingsRepository settingsRepository, PosMediaStore media, PosDbContext? db = null) : IReceiptService
{
    private async Task<BusinessProfile?> BusinessAsync(Sale sale,CancellationToken ct)
    {
        if(sale.InvoiceProfileJson is{} json)return JsonSerializer.Deserialize<BusinessProfile>(json);
        if (db is null) return null;
        var fiscal=await db.FiscalSubmissions.AsNoTracking().SingleOrDefaultAsync(f=>f.SaleId==sale.Id,ct);
        if(fiscal is not null)
        {
            using var snapshot=JsonDocument.Parse(fiscal.InvoiceJson);
            if(snapshot.RootElement.TryGetProperty("businessProfile",out var business))return business.Deserialize<BusinessProfile>();
        }
        var row = await db.BusinessConfigurations.AsNoTracking().SingleOrDefaultAsync(ct);
        return row?.Completed == true ? JsonSerializer.Deserialize<BusinessProfile>(row.ProfileJson) : null;
    }
    public async Task<byte[]> RenderPdfAsync(Sale sale, CancellationToken ct = default)
    {
        var settings = await settingsRepository.GetAsync(ct);
        var business=await BusinessAsync(sale,ct);
        return new ReceiptDocument(sale, settings, media.ResolvePath(business?.Logo??settings.LogoPath), business).GeneratePdf();
    }

    public async Task SavePdfAsync(Sale sale, string path, CancellationToken ct = default)
    {
        var bytes = await RenderPdfAsync(sale, ct);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    public async Task<ReceiptPreview> RenderPreviewAsync(Sale sale, CancellationToken ct = default)
    {
        var settings = await settingsRepository.GetAsync(ct);
        var business=await BusinessAsync(sale,ct);
        var document = new ReceiptDocument(sale, settings, media.ResolvePath(business?.Logo??settings.LogoPath), business);
        var pages = document.GenerateImages(new ImageGenerationSettings { RasterDpi = 144 }).ToArray();
        return new ReceiptPreview(document.GeneratePdf(), pages);
    }
}

public sealed class ShelfLabelService : IShelfLabelService
{
    private readonly ISettingsRepository _settings;
    public ShelfLabelService(ISettingsRepository settings) => _settings = settings;

    public async Task<byte[]> RenderPdfAsync(IReadOnlyList<Product> products, CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        return new ShelfLabelDocument(products, settings.StoreName, settings.Language).GeneratePdf();
    }

    public async Task SavePdfAsync(IReadOnlyList<Product> products, string path, CancellationToken ct = default)
    {
        var bytes = await RenderPdfAsync(products, ct);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }
}

public sealed class ReportPdfService : IReportPdfService
{
    private readonly ISettingsRepository _settings;
    public ReportPdfService(ISettingsRepository settings) => _settings = settings;

    public async Task<byte[]> RenderPdfAsync(SalesReport report, CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        return new ReportDocument(report, settings.StoreName, settings.Language).GeneratePdf();
    }

    public async Task SavePdfAsync(SalesReport report, string path, CancellationToken ct = default)
    {
        var bytes = await RenderPdfAsync(report, ct);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }
}
