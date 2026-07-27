using Platform.UsersService.Models;

namespace Platform.UsersService.Services;

/// <summary>
/// Application service for user profile CRUD operations.
/// </summary>
public interface IUserService
{
    Task<UserResult<PaginatedList<UserDto>>> GetUsersAsync(
        string? search, string? department, string? role,
        int pageSize, string? continuationToken, Guid tenantId,
        CancellationToken ct = default);

    Task<UserResult<UserDto>> GetUserByIdAsync(Guid userId, Guid tenantId, string? requestorRole, Guid requestorId, CancellationToken ct = default);

    Task<UserResult<UserDto>> CreateUserAsync(CreateUserRequest request, Guid tenantId, CancellationToken ct = default);

    Task<UserResult<UserDto>> UpdateUserAsync(Guid userId, UpdateUserRequest request, Guid tenantId, string? requestorRole, Guid requestorId, CancellationToken ct = default);

    Task<UserResult> DeleteUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
}

public sealed record UserResult<T>(T? Value, bool IsSuccess, string? ErrorMessage, int StatusCode)
{
    public static UserResult<T> Success(T value) => new(value, true, null, 200);
    public static UserResult<T> Failure(string error, int statusCode) => new(default, false, error, statusCode);
}

public sealed record UserResult(bool IsSuccess, string? ErrorMessage, int StatusCode)
{
    public static UserResult Success() => new(true, null, 200);
    public static UserResult Failure(string error, int statusCode) => new(false, error, statusCode);
}

public sealed record PaginatedList<T>(IReadOnlyList<T> Items, int PageSize, string? ContinuationToken, bool HasMore, int TotalCount);
