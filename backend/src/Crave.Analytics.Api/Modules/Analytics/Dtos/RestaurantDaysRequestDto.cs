namespace Crave.Analytics.Api.Modules.Analytics.Dtos;

/// <summary>
/// Query for GET …/days — bound from route + query params (api-contracts.md §1.1).
/// </summary>
public sealed record RestaurantDaysRequestDto(int RestaurantId, string? From, string? To);
