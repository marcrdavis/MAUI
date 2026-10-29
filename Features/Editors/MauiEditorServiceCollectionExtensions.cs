// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Single DI integration point for optional MAUI-owned editors.

using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Features.Editors.Hosts;

namespace MoveItAutomationUI.Features.Editors;

public static class MauiEditorServiceCollectionExtensions
{
    public static IServiceCollection AddMauiEditorFeatures(this IServiceCollection services)
    {
        TraditionalTaskEditorHostCapabilities.ValidateCatalog();

        services.AddScoped<IHostEditorService, HostEditorService>();
        services.AddScoped<IHostEditorSaveService, HostEditorSaveService>();
        services.AddScoped<ITraditionalTaskEditorService, TraditionalTaskEditorService>();
        services.AddScoped<ITraditionalTaskEditorSaveService, TraditionalTaskEditorSaveService>();
        services.AddScoped<ITraditionalTaskEditorFolderBrowseService, TraditionalTaskEditorFolderBrowseService>();
        services.AddScoped<ITraditionalTaskEditorTransferExceptionService, TraditionalTaskEditorTransferExceptionService>();
        services.AddScoped<ITraditionalTaskEditorSourceTimestampService, TraditionalTaskEditorSourceTimestampService>();
        services.AddScoped<Services.MoveIt.Resources.Scripts.ScriptCrudCoordinator>();
        services.AddScoped<Services.MoveIt.Resources.Scripts.IScriptCrudCoordinator>(provider => provider.GetRequiredService<Services.MoveIt.Resources.Scripts.ScriptCrudCoordinator>());
        services.AddScoped<Services.Metadata.ScriptTemplateService>();
        services.AddScoped<Services.MoveIt.Resources.Scripts.ScriptMetadataSaveService>();
        services.AddScoped<Services.MoveIt.Resources.DateLists.DateListCrudCoordinator>();
        services.AddScoped<Services.MoveIt.Resources.DateLists.IDateListCrudCoordinator>(provider => provider.GetRequiredService<Services.MoveIt.Resources.DateLists.DateListCrudCoordinator>());
        services.AddScoped<Services.MoveIt.SystemSettings.ISystemSettingsService, Services.MoveIt.SystemSettings.SystemSettingsService>();
        return services;
    }
}
