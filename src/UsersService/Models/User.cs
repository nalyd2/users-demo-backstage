namespace Platform.UsersService.Models;

/// <summary>
/// Represents a user profile in the platform.
/// </summary>
public sealed record UserDto
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public string? Department { get; init; }
    public string? JobTitle { get; init; }
    public string[] Roles { get; init; } = Array.Empty<string>();
    public DateTimeOffset? LastLoginAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Internal entity used for persistence.
/// </summary>
public sealed record UserEntity
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Department { get; init; }
    public string? JobTitle { get; init; }
    public string[] Roles { get; init; } = Array.Empty<string>();
    public DateTimeOffset? LastLoginAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public UserDto ToDto() => new()
    {
        Id = Id,
        Username = Username,
        Email = Email,
        DisplayName = DisplayName,
        Department = Department,
        JobTitle = JobTitle,
        Roles = Roles,
        LastLoginAt = LastLoginAt,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}
