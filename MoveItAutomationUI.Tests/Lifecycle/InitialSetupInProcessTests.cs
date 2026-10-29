// Project: MAUI - MOVEit Automation UI
// RC2 first-run Setup without IIS recycle: activation state, runtime DB baseline, and access gate.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Lifecycle;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class InitialSetupInProcessTests
{
    [Fact]
    public async Task SetupMiddleware_ActivatesInPlace_AndPreservesNestedIisApplicationPath()
    {
        var configuration = CreateUnconfigured();
        var state = MauiSetupState.Capture(configuration);
        Assert.False(state.IsConfiguredAtStartup);
        var calls = 0;
        var middleware = new InitialSetupAccessMiddleware(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        var before = NewContext("/Company/Automation", "/Account/Login", "GET");
        await middleware.InvokeAsync(before, state);
        Assert.Equal(StatusCodes.Status302Found, before.Response.StatusCode);
        Assert.Equal("/Company/Automation/Setup", before.Response.Headers["Location"].ToString());
        Assert.Equal(0, calls);

        // Models the final publication step AFTER durable configuration write/reload/DB inspection.
        state.MarkConfigured();
        var setup = NewContext("/Company/Automation", "/Setup", "GET");
        await middleware.InvokeAsync(setup, state);
        Assert.Equal(StatusCodes.Status302Found, setup.Response.StatusCode);
        Assert.Equal("/Company/Automation/Account/Login", setup.Response.Headers["Location"].ToString());
        Assert.Equal(0, calls);

        var post = NewContext("/Company/Automation", "/Setup/Complete", "POST");
        await middleware.InvokeAsync(post, state);
        Assert.Equal(StatusCodes.Status404NotFound, post.Response.StatusCode);
        Assert.Equal(0, calls);

        var after = NewContext("/Company/Automation", "/Account/Login", "GET");
        await middleware.InvokeAsync(after, state);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void EffectiveRuntimeConfiguration_CanActivateAfterSetup_ButStagedSettingsCannotChangeIt()
    {
        var configuration = CreateUnconfigured();
        var effective = ConfigurationStartupState.Capture(configuration);
        var originalProvider = effective.DatabaseProvider;

        configuration["MetadataDatabase:Provider:Value"] = "SqlServer";
        configuration["MetadataDatabase:ConnectionString:Value"] = "Server=sql.example.invalid;Database=MAUI;Integrated Security=True;";
        configuration["MoveIt:AllowInvalidServerCertificates:Value"] = "true";
        Assert.Equal(originalProvider, effective.DatabaseProvider);
        Assert.False(effective.AllowInvalidServerCertificates);

        effective.ActivateAfterInitialSetup(configuration);
        Assert.Equal("SqlServer", effective.DatabaseProvider);
        Assert.Contains("sql.example.invalid", effective.DatabaseConnectionString);
        Assert.True(effective.AllowInvalidServerCertificates);

        // This is a staged Settings change. It must NOT change the active DB or TLS policy.
        configuration["MetadataDatabase:Provider:Value"] = "SQLite";
        configuration["MetadataDatabase:ConnectionString:Value"] = "Data Source=Data/maui.db";
        configuration["MoveIt:AllowInvalidServerCertificates:Value"] = "false";
        Assert.Equal("SqlServer", effective.DatabaseProvider);
        Assert.True(effective.AllowInvalidServerCertificates);
    }

    [Fact]
    public void SetupState_RecognizesAlreadyConfiguredInstallations()
    {
        var configuration = CreateUnconfigured();
        configuration["MoveIt:Servers:0:Key"] = "prod";
        configuration["MoveIt:Servers:0:WebAdminUrl"] = "https://prod.example.invalid";
        Assert.True(MauiSetupState.Capture(configuration).IsConfiguredAtStartup);
    }

    private static IConfigurationRoot CreateUnconfigured() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MetadataDatabase:Provider:Value"] = "SQLite",
            ["MetadataDatabase:ConnectionString:Value"] = "Data Source=Data/maui.db",
            ["MoveIt:AllowInvalidServerCertificates:Value"] = "false",
        }).Build();

    private static DefaultHttpContext NewContext(string pathBase, string path, string method)
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = pathBase;
        context.Request.Path = path;
        context.Request.Method = method;
        return context;
    }
}
