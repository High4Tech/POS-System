namespace SupermarketPos.Domain.Pricing;

public static class CartStock
{
    public static bool CanAdd(decimal stock, decimal inCart, decimal requested) =>
        requested > 0m && inCart >= 0m && stock >= inCart && requested <= stock - inCart;
}
