namespace Crave.Analytics.Api.Modules.Analytics.Dtos;

public class RestaurantDayResponseDto
{
    public string Date { get; set; } = "";
    public long OrdersCount { get; set; }
    public long RevenueMinor { get; set; }
    public string Currency { get; set; } = "";
    public long AvgOrderMinor { get; set; }
}
