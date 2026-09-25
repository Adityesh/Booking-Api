using BookingApi.Data;

namespace BookingApi.Dto;

public record ResourceResponseDto(int Id, string Name, ResourceType Type, int Capacity, bool IsActive);