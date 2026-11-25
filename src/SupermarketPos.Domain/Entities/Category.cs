using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Entities;

public sealed class Category
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? ColorHex { get; private set; }
    public string? ImageFileName { get; private set; }

    private Category() { } // EF

    public Category(string name)
    {
        Rename(name);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name cannot be empty.");
        Name = name.Trim();
    }

    public void SetColor(string colorHex)
    {
        if (colorHex.Length != 7 || colorHex[0] != '#' || !colorHex.Skip(1).All(char.IsAsciiHexDigit))
            throw new DomainException("Use an RGB color such as #2563EB.");
        ColorHex = colorHex.ToUpperInvariant();
    }

    public void SetImage(string? fileName) => ImageFileName = fileName;
}
