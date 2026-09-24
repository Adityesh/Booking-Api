using BookingApi.Data;

namespace BookingApi.Dto;

public record CreateResourceDto(string Name, ResourceType Type, int Capacity);