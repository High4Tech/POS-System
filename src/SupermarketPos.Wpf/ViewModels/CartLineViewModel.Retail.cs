using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class CartLineViewModel
{
    private IReadOnlyList<ServiceProviderAccount> _providers=[];
    private IReadOnlyList<MobileServicePackage> _packages=[];
    private bool _loadingServiceOptions;
    public record RetailChoice(string Code,string Name);
    public RetailChoice[] ProductKinds=>new[]{"labeled","loose"}.Select(c=>new RetailChoice(c,LocalizationManager.Instance["Retail_"+c])).ToArray();
    public RetailChoice[] ServiceTypes=>new[]{"SERVICE","LOAD","PACKAGE","CASHIN","CASHOUT","TRANSFER"}.Select(c=>new RetailChoice(c,LocalizationManager.Instance["Service_"+c])).ToArray();
    public RetailChoice[] ServiceStates=>new[]{"Confirmed","Pending","Failed"}.Select(c=>new RetailChoice(c,LocalizationManager.Instance["Service_"+c])).ToArray();
    public bool CanChooseKind=>_retail&&Line.Product.QuickEntry;
    public bool IsDigital=>_retail&&Line.Product.Kind=="digital";
    public bool IsFinancialService=>_retail&&Line.Product.IsFinancialService;
    public bool IsPackageService=>IsFinancialService&&SelectedServiceType=="PACKAGE";
    public string ProductKindName=>LocalizationManager.Instance["Retail_"+ProductKind];
    public string ProductKind
    {
        get=>Line.Product.Kind;
        set
        {
            if(!CanChooseKind||value==ProductKind)return;
            if(value=="digital"){EditError=LocalizationManager.Instance["Digital_Separate"];OnPropertyChanged();return;}
            try{if(value!="loose")Line.SetQuickUnit(Unit.Piece);Line.Product.SetRetailKind(value);NotifyRetail();EditError=null;}
            catch(DomainException e){EditError=LocalizationManager.Instance[e.Message];OnPropertyChanged();}
        }
    }
    public string SelectedServiceType
    {
        get=>Line.Product.ServiceType;
        set
        {
            if(!IsDigital||value==SelectedServiceType)return;
            try{Line.Product.ConfigureService(value);if(value!="SERVICE")Line.SetQuantity(1);SelectedServiceProviderId=null;SelectedServicePackageId=null;ServiceExternallyConfirmed=false;NotifyRetail();}
            catch(DomainException e){EditError=LocalizationManager.Instance[e.Message];OnPropertyChanged();}
        }
    }
    public IEnumerable<ServiceProviderAccount> ServiceProviders=>_providers.Where(p=>p.IsActive&&p.AllowedTypes.Split(',').Contains(SelectedServiceType));
    public IEnumerable<MobileServicePackage> ServicePackages=>_packages.Where(p=>p.IsActive&&p.ProviderId==SelectedServiceProviderId);
    [ObservableProperty]private int? _selectedServiceProviderId;
    [ObservableProperty]private int? _selectedServicePackageId;
    [ObservableProperty]private string _serviceRecipient="";
    [ObservableProperty]private string _serviceReference="";
    [ObservableProperty]private bool _serviceExternallyConfirmed;
    [ObservableProperty]private string _serviceStatus="Confirmed";
    [ObservableProperty]private string _serviceFeeText="";
    public Guid ServiceConfirmationId{get;set;}=Guid.NewGuid();
    partial void OnSelectedServiceProviderIdChanged(int? value)
    {
        if(!IsDigital||_loadingServiceOptions)return;
        Line.Product.ConfigureService(SelectedServiceType,value);
        if(_packages.FirstOrDefault(p=>p.Id==SelectedServicePackageId)?.ProviderId!=value)SelectedServicePackageId=null;
        OnPropertyChanged(nameof(ServicePackages));ServiceExternallyConfirmed=false;NotifyRetail();
    }
    partial void OnSelectedServicePackageIdChanged(int? value)
    {
        if(!IsPackageService||_loadingServiceOptions)return;Line.Product.ConfigureService("PACKAGE",SelectedServiceProviderId,value);
        if(_packages.FirstOrDefault(p=>p.Id==value) is{} package){Line.SetUnitPrice(package.Price);RefreshPrice();}
        ServiceExternallyConfirmed=false;NotifyRetail();
    }
    partial void OnServiceStatusChanged(string value){ServiceExternallyConfirmed=false;NotifyRetail();}
    partial void OnServiceRecipientChanged(string value){ServiceExternallyConfirmed=false;}
    partial void OnServiceReferenceChanged(string value){ServiceExternallyConfirmed=false;}
    partial void OnServiceFeeTextChanged(string value){ServiceExternallyConfirmed=false;NotifyRetail();}
    partial void OnServiceExternallyConfirmedChanged(bool value)=>_onChanged();
    public void SetServiceOptions(IReadOnlyList<ServiceProviderAccount> providers,IReadOnlyList<MobileServicePackage> packages)
    {
        if(_providers.FirstOrDefault(p=>p.Id==SelectedServiceProviderId)?.Revision!=providers.FirstOrDefault(p=>p.Id==SelectedServiceProviderId)?.Revision)ServiceExternallyConfirmed=false;
        _providers=providers;_packages=packages;
        _loadingServiceOptions=true;
        try{SelectedServiceProviderId=Line.Product.ServiceProviderId;SelectedServicePackageId=Line.Product.ServicePackageId;}
        finally{_loadingServiceOptions=false;}
        if(IsPackageService&&_packages.FirstOrDefault(p=>p.Id==SelectedServicePackageId) is{} package&&Line.GrossUnitPrice!=package.Price){Line.SetUnitPrice(package.Price);RefreshPrice();ServiceExternallyConfirmed=false;}
        OnPropertyChanged(nameof(ServiceProviders));OnPropertyChanged(nameof(ServicePackages));OnPropertyChanged(nameof(SelectedServiceProviderId));OnPropertyChanged(nameof(SelectedServicePackageId));OnPropertyChanged(nameof(CanEditPrice));
    }
    private void NotifyRetail()
    {
        foreach(var name in new[]{nameof(ProductKind),nameof(ProductKindName),nameof(IsDigital),nameof(IsFinancialService),nameof(IsPackageService),nameof(SelectedServiceType),nameof(ServiceProviders),nameof(ServicePackages),nameof(HasUnitChoices),nameof(SellingUnits),nameof(UnitText),nameof(SelectedSellingUnit),nameof(LineTotalText),nameof(CanEditPrice)})OnPropertyChanged(name);
        _onChanged();
    }
    public decimal ServiceCash
    {
        get
        {
            if(!IsFinancialService||ServiceStatus!="Confirmed")return 0;
            try{return Quote(false).CashChange;}catch(DomainException){return SelectedServiceType=="CASHOUT"?-Line.GrossUnitPrice:Line.GrossUnitPrice;}
        }
    }
    private ServiceTransaction Quote(bool requireProvider)
    {
        if(Line.Quantity!=1)throw new DomainException("Retail_OneService");
        var provider=_providers.FirstOrDefault(p=>p.Id==SelectedServiceProviderId&&p.IsActive&&p.AllowedTypes.Split(',').Contains(SelectedServiceType));
        if(requireProvider&&provider is null)throw new DomainException("Service_SelectProvider");
        if(SelectedServiceType=="PACKAGE")return DigitalServiceLedger.PackageQuote(_packages.SingleOrDefault(p=>p.Id==SelectedServicePackageId&&p.ProviderId==provider?.Id&&p.IsActive)??throw new DomainException("Service_NoPackages"));
        var quote=DigitalServiceLedger.ProviderQuote(provider??new ServiceProviderAccount(),SelectedServiceType,Line.GrossUnitPrice);
        if(ServiceFeeText.Length>0)
        {
            if(!decimal.TryParse(ServiceFeeText,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var fee)||fee<0||fee!=Money.Round2(fee))throw new DomainException("Service_InvalidAmount");
            quote.CustomerFee=fee;quote.Collected=quote.Principal+fee;
        }
        return quote;
    }
    public RetailServicePayment BuildServicePayment()
    {
        var quote=Quote(true);quote.ConfirmationId=ServiceConfirmationId;quote.Status=ServiceStatus;quote.CustomerPhone=ServiceRecipient.Trim();quote.Reference=ServiceReference.Trim();quote.OccurredAt=DateTime.Now;
        if(quote.Type is "LOAD" or "PACKAGE")quote.CustomerPhone=InvoicePhone.Normalize(quote.CustomerPhone);
        if(quote.Type=="TRANSFER"&&quote.CustomerPhone.Length==0)throw new DomainException("Retail_RecipientRequired");
        quote.Validate();if(quote.Status=="Confirmed"&&!ServiceExternallyConfirmed)throw new DomainException("Service_ExternalRequired");
        Line.Product.ConfigureService(quote.Type,quote.ProviderId,quote.PackageId);
        return new(quote,ServiceExternallyConfirmed);
    }
}
