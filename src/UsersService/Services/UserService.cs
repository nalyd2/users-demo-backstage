using Platform.UsersService.Models;

namespace Platform.UsersService.Services;

/// <summary>
/// Implements user profile CRUD operations with RBAC enforcement.
///
/// For this reference implementation, an in-memory store is used.
/// In production, this would be backed by PostgreSQL via Dapper.
/// </summary>
internal sealed class UserService : IUserService
{
    private static readonly List<UserEntity> _users = new();
    private static int _continuationCounter;

    private readonly ILogger<UserService> _logger;

    public UserService(ILogger<UserService> logger)
    {
        _logger = logger;

        // Seed demo data on first instantiation
        if (_users.Count == 0)
        {
            _users.Add(new UserEntity
            {
                Id = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
                TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Username = "admin",
                Email = "admin@platform.internal",
                DisplayName = "Platform Admin",
                Department = "Platform Engineering",
                JobTitle = "Platform Architect",
                Roles = new[] { "admin", "developer" },
                CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6),
                UpdatedAt = DateTimeOffset.UtcNow
            });
            _users.Add(new UserEntity
            {
                Id = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"),
                TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Username = "jane.dev",
                Email = "jane.dev@contoso.com",
                DisplayName = "Jane Developer",
                Department = "Engineering",
                JobTitle = "Senior Developer",
                Roles = new[] { "developer" },
                CreatedAt = DateTimeOffset.UtcNow.AddMonths(-3),
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    public Task<UserResult<PaginatedList<UserDto>>> GetUsersAsync(
        string? search, string? department, string? role,
        int pageSize, string? continuationToken, Guid tenantId,
        CancellationToken ct = default)
    {
        var query = _users
            .Where(u => u.TenantId == tenantId && u.DeletedAt == null)
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                u.Username.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (u.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(department))
            query = query.Where(u => u.Department == department);

        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Roles.Contains(role));

        var all = query.ToList();
        var page = all.Take(pageSize).ToList();
        var hasMore = all.Count > pageSize;
        var nextToken = hasMore ? $"page_{++_continuationCounter}" : null;

        return Task.FromResult(UserResult<PaginatedList<UserDto>>.Success(
            new PaginatedList<UserDto>(
                page.Select(u => u.ToDto()).ToList(),
                pageSize,
                nextToken,
                hasMore,
                all.Count)));
    }

    public Task<UserResult<UserDto>> GetUserByIdAsync(
        Guid userId, Guid tenantId, string? requestorRole, Guid requestorId,
        CancellationToken ct = default)
    {
        var user = _users.FirstOrDefault(u =>
            u.Id == userId && u.TenantId == tenantId && u.DeletedAt == null);

        if (user is null)
            return Task.FromResult(UserResult<UserDto>.Failure("User not found.", 404));

        // RBAC: 'user' role can only see self
        if (requestorRole == "user" && userId != requestorId)
            return Task.FromResult(UserResult<UserDto>.Failure("Forbidden.", 403));

        return Task.FromResult(UserResult<UserDto>.Success(user.ToDto()));
    }

    public Task<UserResult<UserDto>> CreateUserAsync(
        CreateUserRequest request, Guid tenantId, CancellationToken ct = default)
    {
        // Check uniqueness
        if (_users.Any(u => u.TenantId == tenantId && u.Username == request.Username && u.DeletedAt == null))
            return Task.FromResult(UserResult<UserDto>.Failure("Username already taken.", 409));

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = request.Username,
            Email = request.Email,
            DisplayName = request.DisplayName,
            Department = request.Department,
            JobTitle = request.JobTitle,
            Roles = request.Roles,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _users.Add(user);
        _logger.LogInformation("User created: {UserId} ({Username})", user.Id, user.Username);

        return Task.FromResult(UserResult<UserDto>.Success(user.ToDto()));
    }

    public Task<UserResult<UserDto>> UpdateUserAsync(
        Guid userId, UpdateUserRequest request, Guid tenantId,
        string? requestorRole, Guid requestorId, CancellationToken ct = default)
    {
        var user = _users.FirstOrDefault(u =>
            u.Id == userId && u.TenantId == tenantId && u.DeletedAt == null);

        if (user is null)
            return Task.FromResult(UserResult<UserDto>.Failure("User not found.", 404));

        // RBAC enforcement
        if (requestorRole == "user" && userId != requestorId)
            return Task.FromResult(UserResult<UserDto>.Failure("Forbidden.", 403));

        // Only admins can change roles
        if (request.Roles is not null && requestorRole != "admin")
            return Task.FromResult(UserResult<UserDto>.Failure("Only admins can modify roles.", 403));

        // Apply changes (partial update — only update provided fields)
        var updated = user with
        {
            Email = request.Email ?? user.Email,
            DisplayName = request.DisplayName ?? user.DisplayName,
            Department = request.Department ?? user.Department,
            JobTitle = request.JobTitle ?? user.JobTitle,
            Roles = request.Roles ?? user.Roles,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _users.Remove(user);
        _users.Add(updated);
        _logger.LogInformation("User updated: {UserId}", userId);

        return Task.FromResult(UserResult<UserDto>.Success(updated.ToDto()));
    }

    public Task<UserResult> DeleteUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var user = _users.FirstOrDefault(u =>
            u.Id == userId && u.TenantId == tenantId && u.DeletedAt == null);

        if (user is null)
            return Task.FromResult(UserResult.Failure("User not found.", 404));

        // Soft-delete
        var deleted = user with { DeletedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _users.Remove(user);
        _users.Add(deleted);

        _logger.LogInformation("User soft-deleted: {UserId}", userId);
        return Task.FromResult(UserResult.Success());
    }
}
