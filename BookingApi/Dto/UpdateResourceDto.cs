using BookingApi.Data;

namespace BookingApi.Dto;

public record UpdateResourceDto(string Name, int Capacity, ResourceType Type);