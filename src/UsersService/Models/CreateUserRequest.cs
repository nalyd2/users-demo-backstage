namespace Platform.UsersService.Models;

/// <summary>
/// Request to create a new user profile.
/// </summary>
public sealed record CreateUserRequest
{
    public required string Username { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public string? Department { get; init; }
    public string? JobTitle { get; init; }
    public string[] Roles { get; init; } = Array.Empty<string>();
}
