namespace SupermarketPos.Domain.Common;

public static class SoftwareBrand
{
    public static string Name=>AppContext.GetData("SupermarketPos.SoftwareName") as string??"ORMA";
}
