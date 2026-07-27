using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Platform.UsersService.Controllers;

namespace Platform.UsersService.Tests.Controllers;

public sealed class HealthControllerTests
{
    [Fact]
    public async Task Liveness_ShouldReturn200()
    {
        using var host = await CreateTestHostAsync();

        var response = await host.GetTestClient().GetAsync("/api/health/live");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_ShouldReturn200()
    {
        using var host = await CreateTestHostAsync();

        var response = await host.GetTestClient().GetAsync("/api/health/ready");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    private static async Task<IHost> CreateTestHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        var app = builder.Build();
        app.UseRouting();
        app.MapHealthEndpoints();
        await app.StartAsync();
        return app;
    }
}
