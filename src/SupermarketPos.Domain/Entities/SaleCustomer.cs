using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Entities;

public sealed record SaleCustomer(string? Name, string? Phone, string? Email, string? Address)
{
    public static SaleCustomer? Create(string? name, string? phone, string? email, string? address)
    {
        var customer = new SaleCustomer(Clean(name, 150), Clean(phone, 60), Clean(email, 150), Clean(address, 300));
        return customer is { Name: null, Phone: null, Email: null, Address: null } ? null : customer;
    }

    private static string? Clean(string? input, int max)
    {
        var text = input?.Trim();
        if (text?.Length > max) throw new DomainException($"Customer details must be at most {max} characters per field.");
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
