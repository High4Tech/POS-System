using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;

namespace SupermarketPos.Application.Services;

public sealed record HistoricalSaleItem(string Barcode, string? Name, decimal Quantity, decimal UnitPrice,
    decimal VatPercent, decimal? UnitCost = null, decimal? ChargedGross = null);

public sealed record HistoricalSale(string Reference, DateTime SoldAt, string PaymentMethod, decimal? CashTendered,
    SaleCustomer? Customer, IReadOnlyList<HistoricalSaleItem> Items);

public sealed record SaleImportResult(int Count, DateTime FirstDate, DateTime LastDate);

public sealed class BulkRecordService(IProductRepository products, ICategoryRepository categories,
    IPurchaseRepository purchases, ISaleRepository sales, IReceiptNumberGenerator receiptNumbers,
    IBarcodeService barcodes, IUnitOfWork uow)
{
    public const string ProductTemplate = "Barcode,Name,Category,SellingPricePKR,VatPercent,Stock,Unit,UnitCostPKR,Supplier,PhotoPath";
    public const string SaleTemplate = "ReceiptReference,SoldAt,PaymentMethod,Barcode,ProductName,Qty,SellingPricePKR,VatPercent,UnitCostPKR,ChargedLineTotalPKR,CashTenderedPKR,CustomerName,CustomerPhone,CustomerEmail,CustomerAddress";

    public Task<int> ImportProductsAsync(TextReader reader, CancellationToken ct = default) =>
        ImportProductsAsync(reader, null, ct);

    public Task<int> ImportProductsAsync(TextReader reader, Func<string, string>? savePhoto, CancellationToken ct = default)
    {
        var rows = ReadCsv(reader, ProductTemplate, optionalTrailing: 1);
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var catalog = await categories.GetAllAsync(token);
            var suppliers = await purchases.GetSuppliersAsync(token);
            var catalogProducts = (await products.GetAllAsync(token)).ToDictionary(p => p.Barcode, p => p.Id, StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                await AtRowAsync(row.Number, async () =>
                {
                    var barcode = ValidateBarcode(row.Fields[0]);
                    if (!used.Add(barcode)) throw new DomainException($"Barcode '{barcode}' appears more than once in this file.");
                    var photo = row.Fields[9];
                    if (catalogProducts.TryGetValue(barcode, out var existingId))
                    {
                        if (string.IsNullOrWhiteSpace(photo) || savePhoto is null)
                            throw new DomainException($"Barcode '{barcode}' already exists. Enter PhotoPath to update its photo.");
                        var existing = await products.GetByIdAsync(existingId, token)
                            ?? throw new DomainException($"Product '{barcode}' no longer exists.");
                        existing.SetImagePath(savePhoto(photo));
                        return;
                    }
                    var name = Required(row.Fields[1], 200, "product name");
                    var categoryName = Required(row.Fields[2], 100, "category");
                    var found = catalog.Where(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                    if (found.Length != 1) throw new DomainException($"Select one existing category for '{categoryName}'.");
                    var unit = row.Fields[6].ToLowerInvariant() switch
                    {
                        "piece" or "pc" => Unit.Piece,
                        "kg" or "kilogram" => Unit.Kilogram,
                        "l" or "litre" or "liter" => Unit.Litre,
                        _ => throw new DomainException("Unit must be piece or kg.")
                    };
                    var stock = Number(row.Fields[5], "Stock");
                    if (stock < 0m || stock != Math.Round(stock, unit == Unit.Piece ? 0 : 3))
                        throw new DomainException("Enter valid stock for the selected unit.");
                    var product = new Product(barcode, name, Amount(row.Fields[3], "Selling price"),
                        Vat(Number(row.Fields[4], "VAT percent")), unit, stock, found[0].Id);
                    product.SetPurchaseCost(OptionalAmount(row.Fields[7], "Unit cost"));
                    if (!string.IsNullOrWhiteSpace(row.Fields[8]))
                    {
                        var supplier = suppliers.FirstOrDefault(s => s.Name.Equals(row.Fields[8], StringComparison.OrdinalIgnoreCase))
                            ?? throw new DomainException($"Supplier '{row.Fields[8]}' was not found.");
                        product.SetSupplier(supplier.Id);
                    }
                    if (!string.IsNullOrWhiteSpace(photo))
                    {
                        if (savePhoto is null) throw new DomainException("Photo import is unavailable.");
                        product.SetImagePath(savePhoto(photo));
                    }
                    await products.AddAsync(product, token);
                    if (stock > 0m)
                    {
                        await uow.SaveChangesAsync(token);
                        uow.RecordMovement(new StockMovement(product.Id, product.Name, stock,
                            StockMovementKind.OpeningStock, DateTime.Now));
                    }
                });
            }
            await uow.SaveChangesAsync(token);
            return rows.Count;
        }, ct);
    }

    public Task<SaleImportResult> ImportSalesAsync(TextReader reader, User operatorUser, CancellationToken ct = default)
    {
        var rows = ReadCsv(reader, SaleTemplate);
        var groups = new Dictionary<string, (HistoricalSale Header, List<HistoricalSaleItem> Items)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            try
            {
                var f = row.Fields;
                var reference = Required(f[0], 80, "receipt reference");
                var header = new HistoricalSale(reference, ReadDate(f[1]), f[2].ToUpperInvariant(),
                    OptionalAmount(f[10], "Cash tendered"), SaleCustomer.Create(f[11], f[12], f[13], f[14]), []);
                if (!groups.TryGetValue(reference, out var group))
                    groups.Add(reference, group = (header, []));
                if (group.Header.SoldAt != header.SoldAt || group.Header.PaymentMethod != header.PaymentMethod ||
                    group.Header.CashTendered != header.CashTendered || group.Header.Customer != header.Customer)
                    throw new DomainException($"Use the same date, payment and customer data on every line of receipt '{reference}'.");
                group.Items.Add(new HistoricalSaleItem(f[3], f[4], Number(f[5], "Qty"),
                    Amount(f[6], "Selling price"), Number(f[7], "VAT percent"),
                    OptionalAmount(f[8], "Unit cost"), OptionalAmount(f[9], "Charged line total")));
            }
            catch (DomainException error) { throw new DomainException($"CSV row {row.Number}. {error.Message}"); }
        }
        return RecordSalesAsync(groups.Values.Select(g => g.Header with { Items = g.Items }).ToArray(), operatorUser, ct);
    }

    public Task<SaleImportResult> RecordSalesAsync(IReadOnlyList<HistoricalSale> records, User operatorUser,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (records.Count == 0) throw new DomainException("Enter at least one sale.");
            var catalog = (await products.GetAllAsync(token)).ToDictionary(p => p.Barcode, StringComparer.Ordinal);
            foreach (var entry in records)
            {
                var reference = Required(entry.Reference, 80, "receipt reference");
                if (await sales.HasSourceReceiptAsync(reference, token))
                    throw new DomainException($"Receipt '{reference}' is already in sales history.");
                try
                {
                    var lines = entry.Items.Select(item => BuildLine(item, catalog)).ToArray();
                    if (lines.Length == 0) throw new DomainException("Enter at least one sold item.");
                    var vat = PricingService.ExtractGroups(lines.Select(l => (l.VatRate, l.LineGross)));
                    var gross = lines.Sum(l => l.LineGross);
                    var tendered = entry.CashTendered is { } amount ? ValidateAmount(amount, "Cash tendered") : (decimal?)null;
                    var payment = Payment.Record(entry.PaymentMethod.Trim().ToUpperInvariant(), gross, tendered);
                    var receipt = await receiptNumbers.NextAsync(entry.SoldAt, token);
                    var sale = new Sale(receipt, operatorUser.Id, operatorUser.DisplayName, lines, gross,
                        vat.Sum(v => v.Net), vat.Sum(v => v.Vat), payment, entry.SoldAt, entry.Customer, reference);
                    await sales.AddAsync(sale, token);
                    await uow.SaveChangesAsync(token);
                }
                catch (DomainException error) { throw new DomainException($"Receipt '{reference}'. {error.Message}"); }
                catch (OverflowException) { throw new DomainException($"Receipt '{reference}' contains an amount that is too large."); }
            }
            return new SaleImportResult(records.Count, records.Min(r => r.SoldAt.Date), records.Max(r => r.SoldAt.Date));
        }, ct);

    public Task<bool> SaveCustomerAsync(int saleId, SaleCustomer? customer, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var sale = await sales.GetByIdAsync(saleId, token) ?? throw new DomainException("Sale no longer exists.");
            sale.SetCustomer(customer);
            await uow.SaveChangesAsync(token);
            return true;
        }, ct);

    private SaleLine BuildLine(HistoricalSaleItem item, IReadOnlyDictionary<string, Product> catalog)
    {
        var barcode = string.IsNullOrWhiteSpace(item.Barcode) ? "" : ValidateBarcode(item.Barcode);
        catalog.TryGetValue(barcode, out var product);
        var name = Required(string.IsNullOrWhiteSpace(item.Name) ? product?.Name : item.Name, 200, "product name");
        if (item.Quantity <= 0m || item.Quantity != Math.Round(item.Quantity, 3))
            throw new DomainException("Qty must be positive with up to three decimals.");
        var unitPrice = ValidateAmount(item.UnitPrice, "Selling price");
        decimal? cost = item.UnitCost is { } value ? ValidateAmount(value, "Unit cost") : null;
        var original = Money.Round2(item.Quantity * unitPrice);
        var gross = item.ChargedGross is { } charged ? ValidateAmount(charged, "Charged line total") : original;
        return new SaleLine(product?.Id ?? 0, barcode, name, unitPrice, Vat(item.VatPercent), item.Quantity,
            gross < original ? (original - gross).ToString("0.00", CultureInfo.InvariantCulture) + " PKR" : null, gross, cost);
    }

    private string ValidateBarcode(string input) =>
        Required(barcodes.Normalize(input), 64, "barcode");

    private static string Required(string? input, int limit, string label)
    {
        var value = (input ?? "").Trim();
        if (value.Length == 0 || value.Length > limit || value.Any(char.IsControl))
            throw new DomainException($"Enter a valid {label} of up to {limit} characters.");
        return value;
    }

    private static decimal Vat(decimal percent) => percent is >= 0m and <= 100m
        ? percent / 100m : throw new DomainException("VAT percent must be between 0 and 100.");

    public static decimal Number(string? input, string label) =>
        decimal.TryParse(input?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var result) ? result
            : throw new DomainException($"{label} must be a number. Use a period for decimals.");

    private static decimal Amount(string input, string label) => ValidateAmount(Number(input, label), label);
    private static decimal? OptionalAmount(string input, string label) =>
        string.IsNullOrWhiteSpace(input) ? null : Amount(input, label);

    private static decimal ValidateAmount(decimal value, string label) => value >= 0m && value == Money.Round2(value)
        ? value : throw new DomainException($"{label} must be nonnegative with up to two decimals.");

    private static DateTime ReadDate(string input) =>
        DateTime.TryParseExact(input, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date
            : throw new DomainException("SoldAt must use yyyy-MM-dd or yyyy-MM-dd HH:mm.");

    private static async Task AtRowAsync(long row, Func<Task> action)
    {
        try { await action(); }
        catch (DomainException error) { throw new DomainException($"CSV row {row}. {error.Message}"); }
        catch (OverflowException) { throw new DomainException($"CSV row {row} contains an amount that is too large."); }
    }

    public sealed record CsvRow(long Number, string[] Fields);

    public static IReadOnlyList<CsvRow> ReadCsv(TextReader reader, string template, int optionalTrailing = 0)
    {
        using var csv = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        csv.SetDelimiters(",");
        var expected = template.Split(',');
        var rows = new List<CsvRow>();
        try
        {
            var header = csv.ReadFields();
            var minimumColumns = expected.Length - optionalTrailing;
            if (header is null || header.Length < minimumColumns || header.Length > expected.Length ||
                !header.SequenceEqual(expected.Take(header.Length), StringComparer.OrdinalIgnoreCase))
                throw new DomainException("Use the downloaded CSV template with its original column order.");
            while (!csv.EndOfData)
            {
                var row = csv.LineNumber;
                var values = csv.ReadFields();
                if (values is null || values.All(string.IsNullOrWhiteSpace)) continue;
                if (values.Length < minimumColumns || values.Length > header.Length)
                    throw new DomainException($"CSV row {row} has the wrong number of columns.");
                if (values.Length < expected.Length)
                    values = values.Concat(Enumerable.Repeat("", expected.Length - values.Length)).ToArray();
                rows.Add(new CsvRow(row, values));
            }
        }
        catch (MalformedLineException error) { throw new DomainException($"CSV row {error.LineNumber} has invalid quoting."); }
        if (rows.Count == 0) throw new DomainException("The CSV has no records to import.");
        return rows;
    }
}
