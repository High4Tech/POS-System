using SupermarketPos.Domain.Business;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Tests;

public sealed class OnboardingValidationTests
{
    [Fact]
    public void RequiredErrorsIdentifyEveryMissingFieldBeforeCompletion()
    {
        var errors=new BusinessProfile{Registered=true}.FieldErrors();
        foreach(var field in new[]{"StoreName","LegalName","Address","City"})Assert.Equal("Validation_Required",errors[field]);
        Assert.Empty(new BusinessProfile{StoreName="Small shop"}.FieldErrors());
        Assert.Empty(new BusinessProfile{Registered=true,SalesTaxRegistered=true}.FieldErrors(false));
        Assert.Equal("Validation_Required",new BusinessProfile{Registered=true,SalesTaxRegistered=true}.FieldErrors()["Strn"]);
    }
    [Theory]
    [InlineData("Phone","123")]
    [InlineData("Phone","0300-ABC-1234")]
    [InlineData("Email","not-an-email")]
    [InlineData("Email","Owner <owner@example.com>")]
    [InlineData("Ntn","123")]
    [InlineData("Strn","123")]
    [InlineData("InvoicePrefix","LONG PREFIX WITH SPACES")]
    [InlineData("PostalCode","AB123")]
    [InlineData("FbrEndpoint","http://example.com")]
    [InlineData("StoreName"," ")]
    public void InvalidInputIsAssignedToItsOwnField(string field,string value)
    {
        var p=new BusinessProfile{StoreName="Test",Registered=true,LegalName="Test legal",Address="Test road",City="Lahore",SalesTaxRegistered=true,Strn="1234567890123"};
        typeof(BusinessProfile).GetProperty(field)!.SetValue(p,value);
        Assert.Contains(field,p.FieldErrors().Keys);
        Assert.Throws<BusinessValidationException>(()=>p.Validate());
    }
    [Fact]
    public void CorrectlyShapedDummyDetailsPassWithoutClaimingTaxVerification()
    {
        var p=new BusinessProfile{StoreName="Test",Registered=true,LegalName="Test legal",Address="Test road",City="Lahore",Phone="+92 300 1234567",Email="owner@example.com",Ntn="1234567-8",SalesTaxRegistered=true,Strn="1234567890123",PostalCode="54000",InvoicePrefix="TEST-1"};
        Assert.Empty(p.FieldErrors());p.Validate();
        Assert.Equal("not-configured",p.FbrStatus);
        Assert.Equal("Setup_RequiredIntegration",(p with{FbrRequired=true}).FieldErrors()["FbrRequired"]);
    }
    [Theory]
    [InlineData("")]
    [InlineData("dummy-license")]
    [InlineData("POS-1234")]
    public void MalformedActivationNeverReachesNetworkOrExportsRequest(string key)
    {
        Assert.Throws<CommercialException>(()=>LicenseClient.ValidateActivationInput(key,"Counter 1"));
    }
    [Fact]
    public void LicenseFormatAndDeviceLabelAreValidatedSeparately()
    {
        var key="POS-"+new string('a',48);
        LicenseClient.ValidateActivationInput(key,"Counter 1");
        Assert.Throws<CommercialException>(()=>LicenseClient.ValidateActivationInput(key,""));
        Assert.Throws<CommercialException>(()=>LicenseClient.ValidateActivationInput(key,new string('x',101)));
        Assert.Throws<CommercialException>(()=>LicenseClient.ValidateActivationInput(key,"Name\nInjected"));
    }
}
