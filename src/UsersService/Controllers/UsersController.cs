using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Platform.UsersService.Models;
using Platform.UsersService.Services;

namespace Platform.UsersService.Controllers;

/// <summary>
/// Handles user profile CRUD endpoints with JWT-based RBAC.
/// </summary>
public static class UsersEndpoints
{
    public static RouteGroupBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .WithOpenApi()
            .RequireAuthorization();

        group.MapGet("/", GetUsersAsync)
            .WithName("GetUsers")
            .WithDescription("Lists users with pagination, search, and filtering. Requires admin or operator role.")
            .Produces<PaginatedList<UserDto>>(200)
            .Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403);

        group.MapGet("/{userId:guid}", GetUserByIdAsync)
            .WithName("GetUserById")
            .WithDescription("Gets a user by ID. Users can only access their own profile.")
            .Produces<UserDto>(200)
            .Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403)
            .Produces<ProblemDetails>(404);

        group.MapPost("/", CreateUserAsync)
            .WithName("CreateUser")
            .WithDescription("Creates a new user profile. Requires admin role.")
            .Produces<UserDto>(201)
            .Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403)
            .Produces<ProblemDetails>(409);

        group.MapPut("/{userId:guid}", UpdateUserAsync)
            .WithName("UpdateUser")
            .WithDescription("Updates a user profile. Field-level RBAC applies.")
            .Produces<UserDto>(200)
            .Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403)
            .Produces<ProblemDetails>(404);

        group.MapDelete("/{userId:guid}", DeleteUserAsync)
            .WithName("DeleteUser")
            .WithDescription("Soft-deletes a user. Requires admin role.")
            .Produces(200)
            .Produces<ProblemDetails>(401)
            .Produces<ProblemDetails>(403)
            .Produces<ProblemDetails>(404);

        return group;
    }

    private static async Task<IResult> GetUsersAsync(
        HttpContext httpContext,
        IUserService userService,
        [FromQuery] string? search,
        [FromQuery] string? department,
        [FromQuery] string? role,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? continuationToken = null,
        CancellationToken ct = default)
    {
        var (tenantId, roles, userId) = GetUserContext(httpContext.User);
        if (!roles.Contains("admin") && !roles.Contains("operator"))
            return Results.Forbid();

        var result = await userService.GetUsersAsync(search, department, role, pageSize, continuationToken, tenantId, ct);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetUserByIdAsync(
        Guid userId,
        HttpContext httpContext,
        IUserService userService,
        CancellationToken ct)
    {
        var (tenantId, roles, requestorId) = GetUserContext(httpContext.User);
        var requestorRole = roles.FirstOrDefault();

        var result = await userService.GetUserByIdAsync(userId, tenantId, requestorRole, requestorId, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : MapError(result);
    }

    private static async Task<IResult> CreateUserAsync(
        [FromBody] CreateUserRequest request,
        HttpContext httpContext,
        IUserService userService,
        CancellationToken ct)
    {
        var (tenantId, roles, _) = GetUserContext(httpContext.User);
        if (!roles.Contains("admin"))
            return Results.Forbid();

        var result = await userService.CreateUserAsync(request, tenantId, ct);
        return result.IsSuccess
            ? Results.Created($"/api/users/{result.Value!.Id}", result.Value)
            : MapError(result);
    }

    private static async Task<IResult> UpdateUserAsync(
        Guid userId,
        [FromBody] UpdateUserRequest request,
        HttpContext httpContext,
        IUserService userService,
        CancellationToken ct)
    {
        var (tenantId, roles, requestorId) = GetUserContext(httpContext.User);
        var requestorRole = roles.FirstOrDefault();

        var result = await userService.UpdateUserAsync(userId, request, tenantId, requestorRole, requestorId, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : MapError(result);
    }

    private static async Task<IResult> DeleteUserAsync(
        Guid userId,
        HttpContext httpContext,
        IUserService userService,
        CancellationToken ct)
    {
        var (tenantId, roles, _) = GetUserContext(httpContext.User);
        if (!roles.Contains("admin"))
            return Results.Forbid();

        var result = await userService.DeleteUserAsync(userId, tenantId, ct);
        return result.IsSuccess ? Results.Ok(new { message = $"User {userId} has been deleted." }) : MapError(result);
    }

    private static (Guid TenantId, string[] Roles, Guid UserId) GetUserContext(ClaimsPrincipal user)
    {
        var tenantId = Guid.TryParse(user.FindFirstValue("tid"), out var tid) ? tid : Guid.Empty;
        var userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : Guid.Empty;
        var roles = user.FindAll("roles").Select(c => c.Value).ToArray();
        return (tenantId, roles, userId);
    }

    private static IResult MapError(UserResult result) => result.StatusCode switch
    {
        404 => Results.Problem(type: "https://errors.internal.platform/not-found", title: "Not Found", statusCode: 404, detail: result.ErrorMessage),
        403 => Results.Problem(type: "https://errors.internal.platform/forbidden", title: "Forbidden", statusCode: 403, detail: result.ErrorMessage),
        409 => Results.Problem(type: "https://errors.internal.platform/conflict", title: "Conflict", statusCode: 409, detail: result.ErrorMessage),
        _ => Results.Problem(type: "https://errors.internal.platform/error", title: "Error", statusCode: 500, detail: result.ErrorMessage)
    };

    private static IResult MapError<T>(UserResult<T> result) => result.StatusCode switch
    {
        404 => Results.Problem(type: "https://errors.internal.platform/not-found", title: "Not Found", statusCode: 404, detail: result.ErrorMessage),
        403 => Results.Problem(type: "https://errors.internal.platform/forbidden", title: "Forbidden", statusCode: 403, detail: result.ErrorMessage),
        409 => Results.Problem(type: "https://errors.internal.platform/conflict", title: "Conflict", statusCode: 409, detail: result.ErrorMessage),
        _ => Results.Problem(type: "https://errors.internal.platform/error", title: "Error", statusCode: 500, detail: result.ErrorMessage)
    };
}
