using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Business;

// Non-secret, locally stored configuration. FBR tokens and activation certificates use protected storage.
public sealed record BusinessProfile
{
    public string Language { get; init; } = "en";
    public string BusinessType { get; set; } = "other";
    public string MedicineTemplate {get;set;}="none";
    public bool EnableLooseGoods { get; set; }
    public bool EnablePackagedUnits { get; set; }
    public bool EnableDigitalWalletServices { get; set; }
    public bool EnableMobileRecharge { get; set; }
    [JsonIgnore] public bool DeviceTrackingAvailable => BusinessType is "electronics" or "mobile" or "retail" or "other";
    [JsonIgnore] public bool DigitalServicesAvailable => DigitalWalletAvailable || MobileRechargeAvailable;
    [JsonIgnore] public bool IsPharmacy=>string.Equals(BusinessType?.Trim(),"pharmacy",StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool LooseGoodsAvailable=>!IsPharmacy&&EnableLooseGoods;
    [JsonIgnore] public bool PackagingAvailable=>IsPharmacy||EnablePackagedUnits;
    [JsonIgnore] public bool DigitalWalletAvailable=>!IsPharmacy&&EnableDigitalWalletServices;
    [JsonIgnore] public bool MobileRechargeAvailable=>!IsPharmacy&&EnableMobileRecharge;
    public void EnforceBusinessFeatures(){if(IsPharmacy){EnableLooseGoods=EnableDigitalWalletServices=EnableMobileRecharge=false;EnablePackagedUnits=true;}}
    public string InvoiceExportFolder { get; init; } = "";
    public string WhatsAppTemplate { get; init; } = "";
    public void ApplyBusinessDefaults()
    {
        EnableLooseGoods=BusinessType is "grocery" or "supermarket";
        EnablePackagedUnits=BusinessType is "grocery" or "supermarket" or "retail" or "mobile" or "pharmacy";
        EnableDigitalWalletServices=false;EnableMobileRecharge=BusinessType=="mobile";
        MedicineTemplate=IsPharmacy?"pakistan-drap":"none";
    }
    public bool Registered { get; init; }
    public string EntityType { get; init; } = "sole";
    public string StoreName { get; init; } = "";
    public string LegalName { get; init; } = "";
    public string Owner { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Phone2{get;init;}="";
    public IReadOnlyList<ReceiptNote> ReceiptNotes{get;init;}=[];
    public string Email { get; init; } = "";
    public string Address { get; init; } = "";
    public string City { get; init; } = "";
    public string Province { get; init; } = "";
    public string PostalCode { get; init; } = "";
    public string Country { get; init; } = "Pakistan";
    public string? Logo { get; init; }
    public bool SetupLogoChosen { get; init; }
    public string Ntn { get; init; } = "";
    public bool SalesTaxRegistered { get; init; }
    public string Strn { get; init; } = "";
    public string Jurisdiction { get; init; } = "";
    public string Formation { get; init; } = "";
    public string Branch { get; init; } = "";
    public string Terminal { get; init; } = "";
    public string InvoicePrefix { get; init; } = "";
    public string InvoiceTitle { get; init; } = "";
    public string InvoiceLanguage { get; init; } = "en";
    public string Paper { get; init; } = "80";
    public string Printer { get; init; } = "";
    public string BackupFolder { get; init; } = "";
    public bool DailyBackups{get;init;}=true;
    public bool FbrRequired { get; init; }
    public string FbrRoute { get; init; } = "none"; // POS and Digital Invoicing are distinct routes.
    public string FbrProvider { get; init; } = "";
    public string FbrEnvironment { get; init; } = "sandbox";
    public string FbrEndpoint { get; init; } = "";
    public string FbrPosId { get; init; } = "";
    public string FbrSchema { get; init; } = "";
    public string FbrStatus { get; init; } = "not-configured";
    public IReadOnlyList<LocalBusinessDocument> Documents { get; init; } = [];

    // Shared by the wizard and the final persistence boundary. Keys identify the actual input.
    public IReadOnlyDictionary<string, string> FieldErrors(bool required = true)
    {
        var errors = new Dictionary<string, string>();
        if(MedicineTemplate is not ("none" or "pakistan-drap"))errors[nameof(MedicineTemplate)]="Validation_Choice";
        if(BusinessType is not ("other" or "grocery" or "supermarket" or "retail" or "pharmacy" or "electronics" or "mobile"))errors[nameof(BusinessType)]="Validation_Choice";
        void Check(string field, string value, int max, bool mandatory = false)
        {
            if (mandatory && string.IsNullOrWhiteSpace(value)) errors[field] = "Validation_Required";
            else if (value.Length > max || value.Any(char.IsControl)) errors[field] = "Validation_Text";
        }
        Check(nameof(StoreName), StoreName, 150, required);
        Check(nameof(LegalName), LegalName, 200, required && Registered);
        Check(nameof(Address), Address, 300, required && Registered);
        Check(nameof(City), City, 200, required && Registered);
        foreach (var pair in new[] { (nameof(Owner),Owner), (nameof(Province),Province), (nameof(Jurisdiction),Jurisdiction),
            (nameof(Formation),Formation), (nameof(Branch),Branch), (nameof(Terminal),Terminal), (nameof(Printer),Printer),
            (nameof(FbrProvider),FbrProvider), (nameof(FbrPosId),FbrPosId), (nameof(FbrSchema),FbrSchema), (nameof(InvoiceTitle),InvoiceTitle) }) Check(pair.Item1,pair.Item2,200);
        Check(nameof(Phone),Phone,30); Check(nameof(Email),Email,200); Check(nameof(PostalCode),PostalCode,30);
        Check(nameof(Phone2),Phone2,30);
        if(Phone2.Length>0&&(!Regex.IsMatch(Phone2,@"^\+?[0-9 ()-]+$")||Phone2.Count(char.IsDigit) is <7 or >15))errors[nameof(Phone2)]="Validation_Phone";
        if(ReceiptNotes.Count>20||ReceiptNotes.Any(n=>n.Text.Length>300||n.Text.Any(char.IsControl))||ReceiptNotes.Sum(n=>n.Text.Length)>3000)errors[nameof(ReceiptNotes)]="Validation_Text";
        Check(nameof(BackupFolder),BackupFolder,400); Check(nameof(FbrEndpoint),FbrEndpoint,400);
        Check(nameof(InvoiceExportFolder),InvoiceExportFolder,400);
        if(WhatsAppTemplate.Length>2000||WhatsAppTemplate.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t')))errors[nameof(WhatsAppTemplate)]="Validation_Text";
        if (Phone.Length > 0 && (!Regex.IsMatch(Phone,@"^\+?[0-9 ()-]+$") || Phone.Count(char.IsDigit) is < 7 or > 15)) errors[nameof(Phone)]="Validation_Phone";
        if (Email.Length > 0 && (!System.Net.Mail.MailAddress.TryCreate(Email,out var email) || email.Address != Email)) errors[nameof(Email)]="Validation_Email";
        if (PostalCode.Length > 0 && !Regex.IsMatch(PostalCode,"^[0-9]{5}$")) errors[nameof(PostalCode)]="Validation_Postal";
        if (Ntn.Length > 0 && !Regex.IsMatch(Ntn,"^(?:[0-9]{7}(?:-[0-9])?|[0-9]{13})$")) errors[nameof(Ntn)]="Setup_NtnInvalid";
        if (SalesTaxRegistered && required && string.IsNullOrWhiteSpace(Strn)) errors[nameof(Strn)]="Validation_Required";
        else if (Strn.Length > 0 && (!Regex.IsMatch(Strn,"^[0-9]{13}$") || !SalesTaxRegistered || !Registered)) errors[nameof(Strn)]="Setup_StrnInvalid";
        if (SalesTaxRegistered && !Registered) errors[nameof(SalesTaxRegistered)]="Setup_StrnInvalid";
        if (InvoicePrefix.Length > 12 || !Regex.IsMatch(InvoicePrefix,"^[A-Za-z0-9-]*$")) errors[nameof(InvoicePrefix)]="Validation_Prefix";
        if (Language is not ("en" or "ur")) errors[nameof(Language)]="Validation_Choice";
        if (InvoiceLanguage is not ("en" or "ur")) errors[nameof(InvoiceLanguage)]="Validation_Choice";
        if (Paper is not ("58" or "80" or "A4")) errors[nameof(Paper)]="Validation_Choice";
        if (EntityType is not ("sole" or "aop" or "private" or "public" or "other")) errors[nameof(EntityType)]="Validation_Choice";
        if (FbrRoute is not ("none" or "pos" or "digital")) errors[nameof(FbrRoute)]="Validation_Choice";
        if (FbrEnvironment is not ("sandbox" or "production")) errors[nameof(FbrEnvironment)]="Validation_Choice";
        if (FbrStatus is not ("not-configured" or "pending" or "sandbox" or "connection-error" or "attention")) errors[nameof(FbrStatus)]="Setup_ProductionPending";
        if (FbrEndpoint.Length > 0 && (!Uri.TryCreate(FbrEndpoint,UriKind.Absolute,out var endpoint) || endpoint.Scheme!="https" || endpoint.UserInfo.Length>0)) errors[nameof(FbrEndpoint)]="Validation_Endpoint";
        if (required && FbrRequired) errors[nameof(FbrRequired)]="Setup_RequiredIntegration";
        return errors;
    }

    public void Validate(bool completing = true)
    {
        if (FieldErrors(completing).FirstOrDefault() is { Key: not null } error) Fail(error.Value);
        if (Country != "Pakistan") Fail("Setup_Invalid");
        if (Documents.Count > 20 || Documents.Any(d => !Regex.IsMatch(d.Token, "^[a-f0-9]{32}$") || d.Name.Length > 200)) Fail("Setup_Invalid");
    }

    public void ValidateDraft()
    {
        // Drafts may contain unfinished email/NTN input. Completing setup always performs full validation.
        foreach(var property in typeof(BusinessProfile).GetProperties().Where(p=>p.PropertyType==typeof(string)))
        {
            if(property.GetValue(this) is not string value)continue;
            var max=property.Name switch { nameof(StoreName)=>150, nameof(Address)=>300, nameof(Phone)=>30,
                nameof(Ntn)=>15, nameof(Strn)=>13, nameof(InvoicePrefix)=>12, nameof(PostalCode)=>30,
                nameof(BackupFolder) or nameof(FbrEndpoint) or nameof(InvoiceExportFolder)=>400, nameof(Logo)=>500,nameof(WhatsAppTemplate)=>2000, _=>200 };
            if(value.Length>max||value.Any(c=>char.IsControl(c)&&(property.Name!=nameof(WhatsAppTemplate)||c is not ('\r' or '\n' or '\t'))))Fail("Validation_Text");
        }
        var selections=new[]{nameof(Language),nameof(InvoiceLanguage),nameof(Paper),nameof(EntityType),nameof(FbrRoute),nameof(FbrEnvironment),nameof(FbrStatus)};
        if(FieldErrors(false).Any(e=>selections.Contains(e.Key))||Country!="Pakistan"||Documents.Count>20||Documents.Any(d=>!Regex.IsMatch(d.Token,"^[a-f0-9]{32}$")||d.Name.Length>200))Fail("Setup_Invalid");
    }

    private static void Fail(string key) => throw new BusinessValidationException(key);
}
public record LocalBusinessDocument(string Token, string Name);

public sealed class BusinessValidationException(string key) : DomainException(key) { }

public sealed record ReceiptNote
{
    public string Text{get;set;}="";
    public bool Enabled{get;set;}=true;
    public int Order{get;set;}
}
public sealed class BusinessConfiguration
{
    public int Id { get; set; } = 1;
    public int SchemaVersion { get; set; } = 1;
    public string ProfileJson { get; set; } = "{}";
    public int Step { get; set; }
    public bool Completed { get; set; }
    public bool ImportedExisting { get; set; }
    public int Revision { get; set; }
    public string? RecoveryHash { get; set; }
    public string? RecoveryUsername { get; set; }
    public int RecoveryFailures { get; set; }
    public DateTime? RecoveryLockedUntil { get; set; }
}

public sealed class BusinessAudit
{
    public int Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Summary { get; set; } = ""; // field names/identifiers only, never credentials
}

public sealed class ProductTaxRule
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string HsCode { get; set; } = "";
    public string SaleType { get; set; } = "";
    public string Classification { get; set; } = "standard";
    public string Sro { get; set; } = "";
    public string AuthorityReference { get; set; } = "";
    public decimal Rate { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

public sealed class FiscalSubmission
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public string Route { get; set; } = "none";
    public string Environment { get; set; } = "sandbox";
    public string Status { get; set; } = "not-configured";
    public string InvoiceJson { get; set; } = "{}";
    public string? AuthorityInvoiceNumber { get; set; }
    public string? ValidationCodes { get; set; }
}
