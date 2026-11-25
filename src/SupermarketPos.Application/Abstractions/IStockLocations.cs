using SupermarketPos.Domain.Entities;
namespace SupermarketPos.Application.Abstractions;
public interface IStockLocations
{
    Task<string?> AllocateSaleAsync(Product product,int? batchId,long baseQuantity,int? locationId,CancellationToken ct);
    Task<string?> PlaceReceivedAsync(int productId,int? batchId,long baseQuantity,int? locationId,CancellationToken ct);
}
