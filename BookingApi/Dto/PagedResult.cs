namespace BookingApi.Dto;

public record PagedResult<TData>(int Page, int PageSize, int Count, IList<TData> Data);