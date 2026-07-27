using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Platform.UsersService.Models;
using Platform.UsersService.Services;

namespace Platform.UsersService.Tests.Services;

public sealed class UserServiceTests
{
    private readonly IUserService _userService;
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public UserServiceTests()
    {
        var logger = Substitute.For<ILogger<UserService>>();
        _userService = new UserService(logger);
    }

    [Fact]
    public async Task GetUsersAsync_ShouldReturnUsersForTenant()
    {
        // Act
        var result = await _userService.GetUsersAsync(null, null, null, 20, null, TenantId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().NotBeEmpty();
        result.Value.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnUser_WhenUserExists()
    {
        // Arrange
        var userId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        // Act
        var result = await _userService.GetUserByIdAsync(userId, TenantId, "admin", Guid.Empty);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Username.Should().Be("admin");
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturn404_WhenUserDoesNotExist()
    {
        // Act
        var result = await _userService.GetUserByIdAsync(Guid.NewGuid(), TenantId, "admin", Guid.Empty);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturn403_WhenUserAccessesOtherProfile()
    {
        // Arrange
        var userId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var otherUserId = Guid.NewGuid();

        // Act
        var result = await _userService.GetUserByIdAsync(userId, TenantId, "user", otherUserId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldCreateAndReturnUser()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Username = "new.user",
            Email = "new.user@contoso.com",
            DisplayName = "New User",
            Roles = new[] { "developer" }
        };

        // Act
        var result = await _userService.CreateUserAsync(request, TenantId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Username.Should().Be("new.user");
        result.Value.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateUserAsync_ShouldReturn409_WhenUsernameTaken()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Username = "admin", // Already exists from seed data
            Email = "another@contoso.com"
        };

        // Act
        var result = await _userService.CreateUserAsync(request, TenantId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldUpdateAndReturnUser()
    {
        // Arrange
        var userId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var request = new UpdateUserRequest { DisplayName = "Updated Admin" };

        // Act
        var result = await _userService.UpdateUserAsync(userId, request, TenantId, "admin", Guid.Empty);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DisplayName.Should().Be("Updated Admin");
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturn403_WhenNonAdminChangesRoles()
    {
        // Arrange
        var userId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var request = new UpdateUserRequest { Roles = new[] { "developer" } };

        // Act
        var result = await _userService.UpdateUserAsync(userId, request, TenantId, "operator", Guid.Empty);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task DeleteUserAsync_ShouldSoftDeleteUser()
    {
        // Arrange
        var createResult = await _userService.CreateUserAsync(
            new CreateUserRequest { Username = "to.delete", Email = "delete@contoso.com" }, TenantId);
        var userId = createResult.Value!.Id;

        // Act
        var deleteResult = await _userService.DeleteUserAsync(userId, TenantId);

        // Assert
        deleteResult.IsSuccess.Should().BeTrue();

        // Verify user is no longer accessible
        var getResult = await _userService.GetUserByIdAsync(userId, TenantId, "admin", Guid.Empty);
        getResult.IsSuccess.Should().BeFalse();
        getResult.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetUsersAsync_ShouldFilterBySearch()
    {
        // Act
        var result = await _userService.GetUsersAsync("jane", null, null, 20, null, TenantId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().Contain(u => u.Username == "jane.dev");
    }
}
