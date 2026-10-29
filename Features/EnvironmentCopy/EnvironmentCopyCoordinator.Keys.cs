namespace MoveItAutomationUI.Features.EnvironmentCopy;

public sealed partial class EnvironmentCopyCoordinator
{
    private static async Task VerifyPgpIdentitiesAsync(CopyPlan plan, CopyEnvironmentConnection source, CopyEnvironmentConnection destination, bool initialize, CancellationToken ct)
    {
        var verified = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items.Where(x => x.Source.Type == "KeyCertificate" && x.Source.Subtype == "Pgp" && x.Revision.Length > 0))
        {
            var fingerprint = await source.UseAsync(sp => sp.GetRequiredService<EnvironmentCopyKeyIdentity>().FingerprintAsync(item.Source.Id, ct));
            if (!initialize && EnvironmentCopyObjects.NormalizeFingerprint(item.Source.Fingerprint) != EnvironmentCopyObjects.NormalizeFingerprint(fingerprint))
                throw new InvalidOperationException("A source PGP key changed after analysis. Analyze and review again.");
            item.Source = item.Source with { Fingerprint = fingerprint }; item.Definition["Fingerprint"] = fingerprint;
            foreach (var candidate in plan.DestinationObjects.Where(x => x.Type == "KeyCertificate" && x.Subtype == "Pgp"
                && (x.Id == item.Source.Id || x.Name.Equals(item.Source.Name, StringComparison.OrdinalIgnoreCase) || x.Id == item.Choice.TargetId)).ToArray())
            {
                if (!verified.TryGetValue(candidate.Id, out var targetFingerprint))
                {
                    targetFingerprint = await destination.UseAsync(sp => sp.GetRequiredService<EnvironmentCopyKeyIdentity>().FingerprintAsync(candidate.Id, ct)); verified[candidate.Id] = targetFingerprint;
                }
                plan.DestinationObjects[plan.DestinationObjects.IndexOf(candidate)] = candidate with { Fingerprint = targetFingerprint };
            }
        }
    }
}
