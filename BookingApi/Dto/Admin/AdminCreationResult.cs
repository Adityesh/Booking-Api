namespace BookingApi.Dto.Admin;

public enum AdminCreationResult
{
    Created,
    AdminAlreadyExists,
    UsernameTaken,
    InvalidInput
}