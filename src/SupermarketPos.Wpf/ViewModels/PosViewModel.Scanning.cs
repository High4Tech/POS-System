using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Scanning;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PosViewModel
{
    private bool _registerActive;
    private bool _readingBarcode;
    private bool _checkoutBusy;
    private bool _pendingPackage;
    private readonly UnknownBarcodeNotices _notices = new();

    [ObservableProperty] private bool _isScanActive;
    [ObservableProperty] private bool _isAddingScannedProduct;
    [ObservableProperty] private string? _unknownBarcode;
    [ObservableProperty] private string? _moreBarcodeNotices;
    [ObservableProperty] private string? _stockWarning;
    [ObservableProperty] private BatchScanPrompt? _batchPrompt;
    [ObservableProperty] private InventoryBatch? _selectedBatch;
    public Func<int, Task>? ReviewInventory { get; set; }
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    partial void OnBatchPromptChanged(BatchScanPrompt? value) => UpdateScanWorkflow();

    public string ScanButtonText => Text[IsScanActive ? "Scanner_Stop" : "Scanner_Start"];
    private static LocalizationManager Text => LocalizationManager.Instance;

    public void SetRegisterActive(bool active)
    {
        _registerActive = active;
        if(active)IsScanActive=true;
        OnPropertyChanged(nameof(ScanButtonText));
        UpdateScanWorkflow();
    }

    partial void OnIsPaymentOpenChanged(bool value) => UpdateScanWorkflow();
    private void UpdateScanWorkflow()
    {
        var mode = !_registerActive || !_auth.IsLoggedIn ? ScanMode.Away
            : IsAddingScannedProduct || BatchPrompt is not null || PharmacyWorkspace?.HasPendingEntry==true ? ScanMode.ProductEntry
            : IsPaymentOpen || _checkoutBusy || _clearConfirmation ? ScanMode.Checkout
            : IsScanActive ? ScanMode.Scanning : ScanMode.Idle;
        _scanner.SetWorkflow(this, mode, DispatchBarcodeAsync);
        NotifyPaymentAvailability();
    }

    private void NotifyPaymentAvailability()
    {
        OpenCashCommand.NotifyCanExecuteChanged();
        OpenCardCommand.NotifyCanExecuteChanged();
        OpenCreditCommand.NotifyCanExecuteChanged();
        ClearCartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleScan()
    {
        IsScanActive = !IsScanActive;
        OnPropertyChanged(nameof(ScanButtonText));
        UpdateScanWorkflow();
        if (IsScanActive && !_scanner.IsPaired) _scanner.OpenPanelCommand.Execute(null);
    }

    private Task<ScanOutcome> DispatchBarcodeAsync(ScanRequest request) =>
        CanReadBarcode() ? ReadExactAsync(request)
            : Task.FromResult(new ScanOutcome("paused"));

    private async Task<ScanOutcome> ReadExactAsync(ScanRequest request)
    {
        _readingBarcode = true;
        NotifyPaymentAvailability();
        try
        {
            var gs1 = request.Gs1;
            var match = gs1 is null ? await _lookup.FindAsync(request.Barcode!) : await _lookup.FindGs1Async(gs1);
            if (!CanKeepScan() || !_scanner.CanApply(this, request.Epoch))
                return new ScanOutcome("paused");
            return await ApplyBarcodeAsync(match, gs1?.Gtin ?? request.Barcode!, gs1, scanned: true,epoch:request.Epoch);
        }
        catch (Exception)
        {
            Status = Text["Scanner_LookupError"];
            return new ScanOutcome("error");
        }
        finally
        {
            _readingBarcode = false;
            NotifyPaymentAvailability();
        }
    }

    private bool CanReadBarcode() => CanKeepScan() && !_readingBarcode&&PharmacyWorkspace?.HasPendingEntry!=true;
    private bool CanKeepScan() => !_clearConfirmation&&_registerActive && _auth.IsLoggedIn && !IsAddingScannedProduct &&
        !IsPaymentOpen && !_checkoutBusy && BatchPrompt is null;

    private async Task<ScanOutcome> ApplyBarcodeAsync(BarcodeMatch? match, string code, Gs1Barcode? gs1 = null,
        bool scanned = false, InventoryBatch? confirmed = null,Guid? epoch=null)
    {
        SearchText = string.Empty;
        if(code.StartsWith("POSLOC:",StringComparison.OrdinalIgnoreCase)){Status=Text["Pharmacy_LocationNotProduct"];return new("invalid");}
        string? deviceSerial = null;
        if (match is null && await _ledger.FindUnitAsync(code) is { } deviceMatch)
        {
            var deviceProduct = await _products.GetCurrentForRegisterAsync(deviceMatch.Product.Id);
            if (deviceProduct is not null) { match = new BarcodeMatch(deviceProduct, 1m); deviceSerial = deviceMatch.Serial; }
        }
        if (match is null)
        {
            if(IsPharmacy&&gs1 is null)
            {AddProvisionalMedicine(null,"",code);return new ScanOutcome("pending");}
            if (code.Length > 64)
            {
                Status = Text["Batch_InvalidBarcode"];
                return new ScanOutcome("invalid");
            }
            if (_notices.Add(code, gs1?.HasTrackingData() == true))
                PublishBarcodeNotices();
            return new ScanOutcome("unknown");
        }
        var product = match.Product;
        if (IsPharmacy && !product.SellingUnitConfigured) { Status=Text["Pharmacy_MissingUnit"]; return new("invalid"); }
        if(IsPharmacy&&product.Unit!=Unit.Piece){Status=Text["Pharmacy_PackedOnly"];return new("invalid",product.Name);}
        var needsLocation=false;
        if(IsPharmacy&&product.StockTracked){var places=await _stockLocations.StockAsync(product.Id);needsLocation=places.Select(l=>l.Id).Distinct().Count()>1||places.Length>0&&places.Sum(l=>l.BaseQuantity)<product.BaseStockQuantity;if(!CanKeepScan()||epoch is{} active&&!_scanner.CanApply(this,active))return new("paused");}
        if(IsPharmacy&&PharmacyWorkspace is{} workspace&&(needsLocation||product.TrackExpiry||gs1?.HasTrackingData()==true||product.RequiresPrescription||product.RequiresDispensingReference))
        {
            if(workspace.HasPendingEntry)return new ScanOutcome("busy");
            _pharmacyScanned=scanned;
            await workspace.SelectAsync(new(product.MedicineReferenceId,product.Id,product.Name,"","","","","",product.Barcode,product.UnitPrice,product.StockTracked?product.StockQuantity:null,product.StockTracked?product.StockQuantity>0?"InStock":"OutOfStock":"StockUntracked"),product.Name,code,gs1,match.Packaged);
            return new ScanOutcome("pending",product.Name);
        }
        _pendingPackage=match.Packaged;
        if(match.Packaged&&!EnablePackagedUnits){Status=Text["Measure_FeatureDisabled"];return new ScanOutcome("paused");}
        InventoryBatch? batch = null;
        var qty = match.Quantity ?? (IsPharmacy?product.SellingUnitSize(product.DefaultSellingUnit):1m);
        if (product.IsActive && (product.TrackExpiry || !product.TrackSerial && gs1?.HasTrackingData() == true))
        {
            if (!product.TrackExpiry)
                return OpenBatchPrompt(product, qty, gs1, "batch_unknown", Text["Batch_EnableFirst"]);
            var lot = confirmed?.Lot ?? gs1?.Batch;
            var expiry = confirmed?.ExpiryDate ?? gs1?.ExpiryDate;
            if (lot is not null)
            {
                batch = product.Batches.SingleOrDefault(item => item.Lot == lot);
                if (batch is null)
                    return OpenBatchPrompt(product, qty, gs1, "batch_unknown", Text.Format("Batch_NotFound", product.Name, lot, expiry?.ToString("yyyy-MM-dd") ?? "—"));
                if (expiry.HasValue && expiry != batch.ExpiryDate)
                    return OpenBatchPrompt(product, qty, gs1, "batch_discrepancy", Text.Format("Batch_Mismatch", lot, expiry, batch.ExpiryDate));
                if (batch.ExpiryDate < Today)
                    return OpenBatchPrompt(product, qty, gs1, "expired", Text.Format("Batch_Expired", product.Name, batch.ExpiryDate));
            }
            else
            {
                var eligible = product.Batches.Where(item => item.HoldReason.Length==0 && item.ExpiryDate >= Today && (expiry is null || item.ExpiryDate == expiry)
                        && CartStock.CanAdd(item.Quantity, QuantityInCart(product.Id, item.Id), qty))
                    .OrderBy(item => item.ExpiryDate).ThenBy(item => item.Lot).ToArray();
                var expired = product.Batches.Where(item => item.Quantity > 0m && item.ExpiryDate < Today &&
                    (expiry is null || item.ExpiryDate == expiry)).MaxBy(item => item.ExpiryDate);
                if (eligible.Length == 0 && expired is not null &&
                    !product.Batches.Any(item => item.Quantity > 0m && item.ExpiryDate >= Today && (expiry is null || item.ExpiryDate == expiry)))
                    return OpenBatchPrompt(product, qty, gs1, "expired", Text.Format("Batch_Expired", product.Name, expired.ExpiryDate));
                return eligible.Length > 0
                    ? OpenBatchPrompt(product, qty, gs1, "batch_required", Text.Format("Batch_ConfirmPhysical", product.Name), eligible)
                    : OpenBatchPrompt(product, qty, gs1, "batch_unknown", Text.Format("Batch_NoUsable", product.Name));
            }
        }
        if (gs1?.SerialNumber is { } serial && (Lines.Any(line => line.Line.Product.Id == product.Id && line.Line.SerialNumber == serial) ||
            await _saleHistory.HasSoldSerialAsync(product.Id, serial)))
            return OpenBatchPrompt(product, qty, gs1, "serial_duplicate", Text.Format("Batch_SerialDuplicate", serial));
        if (!CanKeepScan()||epoch is{} current&&!_scanner.CanApply(this,current)) return new ScanOutcome("paused");
        if (!ValidateCartAddition(product, qty, batch, out var rejection))
        {
            if (!scanned) _sound.PlayWarning();
            return rejection!;
        }
        if (product.TrackSerial)
        {
            if (qty != 1m) { Status = "Add tracked devices one at a time."; return new ScanOutcome("invalid"); }
            deviceSerial ??= gs1?.SerialNumber;
            if (deviceSerial is null)
            {
                IsAddingScannedProduct = true;
                UpdateScanWorkflow();
                try
                {
                    var picker = new SerialRecordsWindow(_ledger, product, selecting: true,
                        reserved: Lines.Select(line => line.Line.SerialNumber).OfType<string>()) { Owner = System.Windows.Application.Current.MainWindow };
                    if (picker.ShowDialog() != true) return new ScanOutcome("paused");
                    deviceSerial = picker.SelectedSerial;
                }
                finally { IsAddingScannedProduct = false; UpdateScanWorkflow(); }
            }
            if (deviceSerial is null) return new ScanOutcome("paused");
            SerializedUnit unit;
            try { unit = await _ledger.AvailableUnitAsync(product.Id, deviceSerial, CancellationToken.None); }
            catch (DomainException error)
            {
                Status = StockWarning = error.Message;
                if (!scanned) _sound.PlayWarning();
                return new ScanOutcome("serial_duplicate");
            }
            if (Lines.Any(line => line.Line.SerialNumber == unit.Serial))
            {
                Status = Text.Format("Batch_SerialDuplicate", unit.Serial);
                return new ScanOutcome("serial_duplicate");
            }
            deviceSerial = unit.Serial;
        }
        if (!CanKeepScan()||epoch is{} final&&!_scanner.CanApply(this,final)) return new ScanOutcome("paused");
        AddProduct(product, match.Quantity, batch, deviceSerial ?? gs1?.SerialNumber,match.Packaged);
        if(IsPharmacy)await RefreshLocationLimitsAsync(product.Id);
        Status = Text.Format("Scanner_Added", match.Product.Name);
        decimal? remaining = product.StockTracked?(batch?.Quantity ?? product.StockQuantity) - QuantityInCart(product.Id, batch?.Id):null;
        StockWarning = remaining == 0m ? Text.Format("Scanner_LastStock", match.Product.Name)
            : scanned && remaining <= _profile.LowStockThreshold
                ? Text.Format("Scanner_LowStock", match.Product.Name, remaining) : null;
        return new ScanOutcome("added", match.Product.Name, remaining);
    }

    private CartLineViewModel? FindCartLine(int productId) =>
        Lines.LastOrDefault(line => line.Line.Product.Id == productId);

    private decimal QuantityInCart(int productId) => Lines.Where(line => line.Line.Product.Id == productId).Sum(line => line.Line.Quantity);
    private decimal QuantityInCart(int productId, int? batchId) =>
        Lines.Where(line => line.Line.Product.Id == productId && line.Line.BatchId == batchId).Sum(line => line.Line.Quantity);

    private bool ValidateCartAddition(Product product, decimal qty, InventoryBatch? batch, out ScanOutcome? rejection)
    {
        if(batch?.HoldReason.Length>0){Status=StockWarning=Text["Pharmacy_BatchHeld"]+": "+batch.HoldReason;rejection=new("held",product.Name);return false;}
        foreach (var line in Lines.Where(item => item.Line.Product.Id == product.Id))
            line.UpdateAvailability(product);
        if (!product.IsActive)
        {
            Status = Text.Format("Scanner_Inactive", product.Name);
            StockWarning = Status;
            rejection = new ScanOutcome("inactive", product.Name);
            return false;
        }
        var existing = QuantityInCart(product.Id, batch?.Id);
        if(!product.StockTracked){rejection=null;return true;}
        var stock = batch?.Quantity ?? product.StockQuantity;
        if (!CartStock.CanAdd(stock, existing, qty))
        {
            var remaining = Math.Max(0m, stock - existing);
            StockWarning = remaining == 0m ? Text.Format("Scanner_OutOfStock", product.Name)
                : Text.Format("Scanner_StockRemaining", product.Name, remaining);
            Status = StockWarning;
            rejection = new ScanOutcome("stock", product.Name);
            return false;
        }
        rejection = null;
        return true;
    }

    private ScanOutcome OpenBatchPrompt(Product product, decimal quantity, Gs1Barcode? gs1, string outcome,
        string message, IReadOnlyList<InventoryBatch>? choices = null)
    {
        BatchPrompt = new BatchScanPrompt(product.Id, quantity, gs1, message, choices ?? [],_pendingPackage);
        SelectedBatch = choices?.FirstOrDefault();
        return new ScanOutcome(outcome, product.Name);
    }

    [RelayCommand]
    private void CancelBatch()
    {
        BatchPrompt = null;
        SelectedBatch = null;
    }

    [RelayCommand]
    private async Task ConfirmBatchAsync()
    {
        if (BatchPrompt is not { } prompt || SelectedBatch is not { } selected || _readingBarcode) return;
        _readingBarcode = true;
        try
        {
            var product = await _products.GetCurrentForRegisterAsync(prompt.ProductId);
            if (!_registerActive || !ReferenceEquals(BatchPrompt, prompt)) return;
            CancelBatch();
            if (product is null) { Status = Text["Batch_NoProduct"]; return; }
            await ApplyBarcodeAsync(new BarcodeMatch(product, prompt.Quantity,prompt.Packaged), product.Barcode, prompt.Gs1, confirmed: selected);
        }
        finally
        {
            _readingBarcode = false;
            NotifyPaymentAvailability();
        }
    }

    [RelayCommand]
    private async Task ReviewBatchInventoryAsync()
    {
        if (BatchPrompt is not { } prompt || ReviewInventory is null) return;
        CancelBatch();
        await ReviewInventory(prompt.ProductId);
    }

    [RelayCommand]
    private void DismissUnknown()
    {
        _notices.Dismiss();
        PublishBarcodeNotices();
    }

    private void PublishBarcodeNotices()
    {
        UnknownBarcode = _notices.Current;
        MoreBarcodeNotices = _notices.Remaining > 0
            ? Text.Format("Scanner_AdditionalNotices", _notices.Remaining) : null;
    }

    [RelayCommand]
    private async Task AddScannedProductAsync()
    {
        if (UnknownBarcode is not { } barcode || IsAddingScannedProduct) return;
        IsAddingScannedProduct = true;
        UpdateScanWorkflow();
        try
        {
            await _productEditor.BeginScannerEntryAsync(barcode, trackExpiry: _notices.CurrentIsGs1);
            var window = new ScannedProductWindow(_productEditor)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            if (window.ShowDialog() == true)
            {
                Status = Text["Scanner_Rescan"];
                DismissUnknown();
            }
        }
        catch (Exception)
        {
            Status = Text["Scanner_ProductSaveError"];
            _sound.PlayWarning();
        }
        finally
        {
            IsAddingScannedProduct = false;
            UpdateScanWorkflow();
        }
    }
}

public sealed record BatchScanPrompt(int ProductId, decimal Quantity, Gs1Barcode? Gs1, string Message,
    IReadOnlyList<InventoryBatch> Choices,bool Packaged=false)
{
    public bool HasChoices => Choices.Count > 0;
}
