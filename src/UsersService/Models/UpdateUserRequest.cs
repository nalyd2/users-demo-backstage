namespace Platform.UsersService.Models;

/// <summary>
/// Request to update an existing user profile. All fields are optional (partial update).
/// </summary>
public sealed record UpdateUserRequest
{
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public string? Department { get; init; }
    public string? JobTitle { get; init; }
    public string[]? Roles { get; init; }
}
