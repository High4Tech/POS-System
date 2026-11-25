using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Entities;

public sealed class Supplier
{
    public int Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? Kind { get; private set; }
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }

    private Supplier() { }

    public Supplier(string name) => Update(new SupplierDetails(name));

    public void Update(SupplierDetails details)
    {
        var name = (details.Name ?? "").Trim();
        if (name.Length is 0 or > 150)
            throw new DomainException("Enter a supplier name of up to 150 characters.");
        var kind = details.Kind?.Trim();
        if (!string.IsNullOrEmpty(kind) && kind is not ("COMPANY" or "INDIVIDUAL"))
            throw new DomainException("Select Company or Individual.");
        var contact = Clean(details.ContactName, 150);
        var phone = Clean(details.Phone, 60);
        var email = Clean(details.Email, 150);
        var address = Clean(details.Address, 300);
        Name = name;
        Kind = string.IsNullOrEmpty(kind) ? null : kind;
        ContactName = contact;
        Phone = phone;
        Email = email;
        Address = address;
    }

    private static string? Clean(string? input, int max)
    {
        var text = input?.Trim();
        if (text?.Length > max) throw new DomainException($"Supplier details must be at most {max} characters per field.");
        return string.IsNullOrEmpty(text) ? null : text;
    }
}

public sealed record SupplierDetails(string Name, string? Kind = null, string? ContactName = null,
    string? Phone = null, string? Email = null, string? Address = null);
