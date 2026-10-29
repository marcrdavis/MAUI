using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

public sealed record HostSshAlgorithmCatalog(
    IReadOnlyList<HostProfileChoice> Encryption,
    IReadOnlyList<HostProfileChoice> KeyExchange);

// Native discovery is scoped to the authenticated MOVEit connection. Do not cache a
// static supported catalogue or substitute available choices after discovery fails.
public static class HostSshAlgorithms
{
    public static async Task<HostSshAlgorithmCatalog> LoadAsync(IMoveItRestClient rest, CancellationToken cancellationToken = default, SshAlgorithmOptions? defaults = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var encryption = rest.GetSshEncryptionListV0Async(cancellationToken);
        var keyExchange = rest.GetSshDefaultKexAlgorithmsV0Async(cancellationToken);
        await Task.WhenAll(encryption, keyExchange);
        defaults ??= new SshAlgorithmOptions();
        return new(Parse(await encryption, defaults.Encryption.Value), Parse(await keyExchange, defaults.KeyExchange.Value));
    }

    public static IReadOnlyList<HostProfileChoice> Parse(JsonObject response, string defaultSelection)
    {
        var supported = Names(response, "IPWorks");
        if (supported.Count == 0)
            throw new InvalidOperationException("MOVEit did not return a supported SSH algorithm list.");
        var supportedSet = supported.ToHashSet(StringComparer.Ordinal);
        var selected = SelectionNames(defaultSelection).Where(supportedSet.Contains).ToArray();
        var selectedSet = selected.ToHashSet(StringComparer.Ordinal);
        // Preserve administrator-selected preference order. WeOnlyDo is a provider
        // list, not the native editor's default-selection contract, and is ignored.
        return selected.Select(value => new HostProfileChoice(value, value, true))
            .Concat(supported.Where(value => !selectedSet.Contains(value)).Select(value => new HostProfileChoice(value, value))).ToArray();
    }

    public static string NormalizeSelection(string value)
    {
        if (value.Length > 16384) throw new ArgumentException("An SSH algorithm selection is too long.");
        var names = SelectionNames(value);
        if (names.Count == 0 || names.Any(name => name.Any(char.IsWhiteSpace)))
            throw new ArgumentException("Select at least one valid algorithm in each SSH/KEX list.");
        return string.Join(",", names);
    }

    private static IReadOnlyList<string> SelectionNames(string value)
        => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();

    public static string DefaultSelection(IReadOnlyList<HostProfileChoice> choices)
    {
        var selected = string.Join(",", choices.Where(choice => choice.DefaultEnabled).Select(choice => choice.Value));
        if (selected.Length == 0)
            throw new InvalidOperationException("The configured SSH/KEX defaults do not match the algorithms supported by this MOVEit server. Update SSH/KEX Algorithms in Settings before creating an SFTP Host.");
        return selected;
    }

    private static IReadOnlyList<string> Names(JsonObject response, string property)
    {
        if (response[property] is not JsonValue value || !value.TryGetValue<string>(out var csv) || csv is null)
            throw new InvalidOperationException("MOVEit returned an incomplete SSH algorithm discovery response.");
        var names = csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (names.Any(name => name.Any(char.IsWhiteSpace)))
            throw new InvalidOperationException("MOVEit returned an invalid SSH algorithm name.");
        return names;
    }
}
