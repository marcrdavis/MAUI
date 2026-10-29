// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationConfigurationService
{
    // Purpose: Exports the complete MOVEit Automation configuration using the selected protection mode.
    Task<string> ExportAsync(string mode, string passphrase, CancellationToken cancellationToken = default);
    // Purpose: Exports configuration using an explicitly authenticated state for unattended/background backup.
    Task<string> ExportAsync(MoveItSessionState authenticatedState, string mode, string passphrase, CancellationToken cancellationToken = default);
    // Purpose: Imports a complete MOVEit Automation configuration using the selected protection mode.
    Task ImportAsync(string mode, string configXml, string passphrase, CancellationToken cancellationToken = default);
    // Purpose: Imports configuration using an explicitly authenticated state for backup restore/background workflows.
    Task ImportAsync(MoveItSessionState authenticatedState, string mode, string configXml, string passphrase, CancellationToken cancellationToken = default);
}

// Owns whole-configuration backup/restore operations. MOVEit remains authoritative; MAUI only brokers the
// documented REST API calls and clears its transient resource caches after a successful restore.
public sealed class AutomationConfigurationService : IAutomationConfigurationService
{
    private readonly IMoveItRestClient _restClient;
    private readonly IMemoryCache _memoryCache;

    // Purpose: Initializes automation configuration service with its required dependencies.
    public AutomationConfigurationService(IMoveItRestClient restClient, IMemoryCache memoryCache)
    {
        _restClient = restClient;
        _memoryCache = memoryCache;
    }

    // Purpose: Exports the complete MOVEit Automation configuration using the selected protection mode.
    public Task<string> ExportAsync(string mode, string passphrase, CancellationToken cancellationToken = default)
    {
        var normalizedMode = NormalizeExportMode(mode);
        if (normalizedMode == MoveItConfigurationExportModes.Passphrase)
        {
            var error = PortabilitySupport.PassphrasePolicyError(passphrase, "Portable configuration export passphrase");
            if (error.Length > 0) throw new ArgumentException(error);
        }

        return normalizedMode switch
        {
            MoveItConfigurationExportModes.Passphrase => _restClient.ExportConfigurationAsync(passphrase, sanitize: false, cancellationToken),
            MoveItConfigurationExportModes.Sanitized => _restClient.ExportConfigurationAsync(null, sanitize: true, cancellationToken),
            _ => _restClient.ExportConfigurationAsync(null, sanitize: false, cancellationToken)
        };
    }

    // Purpose: Exports configuration using an explicitly authenticated state for unattended/background backup.
    public Task<string> ExportAsync(MoveItSessionState authenticatedState, string mode, string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticatedState);
        var normalizedMode = NormalizeExportMode(mode);
        if (normalizedMode == MoveItConfigurationExportModes.Passphrase)
        {
            var error = PortabilitySupport.PassphrasePolicyError(passphrase, "Portable configuration export passphrase");
            if (error.Length > 0) throw new ArgumentException(error);
        }

        return normalizedMode switch
        {
            MoveItConfigurationExportModes.Passphrase => _restClient.ExportConfigurationAsync(authenticatedState, passphrase, sanitize: false, cancellationToken),
            MoveItConfigurationExportModes.Sanitized => _restClient.ExportConfigurationAsync(authenticatedState, null, sanitize: true, cancellationToken),
            _ => _restClient.ExportConfigurationAsync(authenticatedState, null, sanitize: false, cancellationToken)
        };
    }

    // Purpose: Imports a complete MOVEit Automation configuration using the selected protection mode.
    public async Task ImportAsync(string mode, string configXml, string passphrase, CancellationToken cancellationToken = default)
    {
        var normalizedMode = NormalizeImportMode(mode);
        if (string.IsNullOrWhiteSpace(configXml)) throw new ArgumentException("The MOVEit configuration XML is empty.");
        if (normalizedMode == MoveItConfigurationImportModes.Passphrase && string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("The passphrase used to export this configuration is required.");

        if (normalizedMode == MoveItConfigurationImportModes.ServerKey)
            await _restClient.ImportConfigurationWithServerKeyAsync(configXml, cancellationToken);
        else
            await _restClient.ImportConfigurationWithPassphraseAsync(configXml, passphrase, cancellationToken);

        // All MOVEit inventory caches are transient IMemoryCache entries. A whole-config restore can change any
        // resource type, so invalidate them together rather than trying to infer which resources changed.
        if (_memoryCache is MemoryCache concreteCache)
            concreteCache.Compact(1.0);
    }

    // Purpose: Imports configuration using an explicitly authenticated state and clears transient resource caches.
    public async Task ImportAsync(MoveItSessionState authenticatedState, string mode, string configXml, string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticatedState);
        var normalizedMode = NormalizeImportMode(mode);
        if (string.IsNullOrWhiteSpace(configXml)) throw new ArgumentException("The MOVEit configuration XML is empty.");
        if (normalizedMode == MoveItConfigurationImportModes.Passphrase && string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("The passphrase used to export this configuration is required.");

        if (normalizedMode == MoveItConfigurationImportModes.ServerKey)
            await _restClient.ImportConfigurationWithServerKeyAsync(authenticatedState, configXml, cancellationToken);
        else
            await _restClient.ImportConfigurationWithPassphraseAsync(authenticatedState, configXml, passphrase, cancellationToken);

        if (_memoryCache is MemoryCache concreteCache)
            concreteCache.Compact(1.0);
    }

    // Purpose: Normalizes export mode.
    private static string NormalizeExportMode(string? mode)
    {
        if (string.Equals(mode, MoveItConfigurationExportModes.ServerKey, StringComparison.OrdinalIgnoreCase)) return MoveItConfigurationExportModes.ServerKey;
        if (string.Equals(mode, MoveItConfigurationExportModes.Sanitized, StringComparison.OrdinalIgnoreCase)) return MoveItConfigurationExportModes.Sanitized;
        if (string.Equals(mode, MoveItConfigurationExportModes.Passphrase, StringComparison.OrdinalIgnoreCase)) return MoveItConfigurationExportModes.Passphrase;
        throw new ArgumentException("The requested MOVEit configuration export type is not supported.");
    }

    // Purpose: Normalizes import mode.
    private static string NormalizeImportMode(string? mode)
    {
        if (string.Equals(mode, MoveItConfigurationImportModes.ServerKey, StringComparison.OrdinalIgnoreCase)) return MoveItConfigurationImportModes.ServerKey;
        if (string.Equals(mode, MoveItConfigurationImportModes.Passphrase, StringComparison.OrdinalIgnoreCase)) return MoveItConfigurationImportModes.Passphrase;
        throw new ArgumentException("The requested MOVEit configuration import type is not supported.");
    }
}
