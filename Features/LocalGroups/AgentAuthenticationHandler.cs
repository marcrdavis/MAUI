using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MoveItAutomationUI.Features.LocalGroups;

public sealed class AgentAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, LocalGroupsConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string ServerClaim = "MauiLocalGroups.Server";
    public const string CertificateClaim = "MauiLocalGroups.Certificate";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var settings = configuration.Read();
        if (!settings.Enabled.Value || !Request.IsHttps)
            return AuthenticateResult.Fail("User Management agent access is unavailable.");
        // Only the TLS connection's certificate is trusted. Forwarded certificate headers are never read.
        var certificate = await Context.Connection.GetClientCertificateAsync(Context.RequestAborted);
        if (certificate is null) return AuthenticateResult.Fail("A registered client certificate is required.");
        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var matches = settings.Agents.Where(a => a.CertificateSha256.Equals(fingerprint, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || !ValidCertificate(certificate, DateTimeOffset.UtcNow))
            return AuthenticateResult.Fail("The client certificate is not authorized.");
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, matches[0].ServerKey), new Claim(ServerClaim, matches[0].ServerKey),
            new Claim(CertificateClaim, fingerprint)
        ], Scheme.Name)), Scheme.Name));
    }

    internal static bool ValidCertificate(X509Certificate2 certificate, DateTimeOffset now)
    {
        if (now.UtcDateTime < certificate.NotBefore.ToUniversalTime() || now.UtcDateTime > certificate.NotAfter.ToUniversalTime()) return false;
        var eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().ToArray();
        if (!eku.SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>()).Any(oid => oid.Value == "1.3.6.1.5.5.7.3.2")) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        return chain.Build(certificate);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    { Response.StatusCode = (int)HttpStatusCode.Unauthorized; return Task.CompletedTask; }
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    { Response.StatusCode = (int)HttpStatusCode.Forbidden; return Task.CompletedTask; }
}
