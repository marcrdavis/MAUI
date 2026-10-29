using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

public static class EnvironmentCopyRegistration
{
    // Keep the optional feature's registration in one place for removal without changing existing CRUD callers.
    public static IServiceCollection AddEnvironmentCopy(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EnvironmentCopyOptions>(configuration.GetSection(EnvironmentCopyOptions.SectionName));
        services.AddSingleton<EnvironmentCopyStore>();
        services.AddScoped<EnvironmentCopyCoordinator>();
        services.AddScoped<EnvironmentCopyKeyIdentity>();
        services.AddScoped<IEnvironmentCopyNativeTransfer>(sp => (MauiNativePortabilityService)sp.GetRequiredService<IMauiNativePortabilityService>());
        services.AddHostedService<EnvironmentCopyWorker>(); return services;
    }
}
