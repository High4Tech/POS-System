using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Wpf.Common;

public sealed partial class StoreProfile : ObservableObject
{
    [ObservableProperty] private string _name = "Tech Store";
    [ObservableProperty] private string? _logoPath;

    public decimal LowStockThreshold { get; private set; }

    public void Apply(AppSettings settings)
    {
        Name = settings.StoreName;
        LogoPath = settings.LogoPath;
        LowStockThreshold = settings.LowStockThreshold;
    }
}
