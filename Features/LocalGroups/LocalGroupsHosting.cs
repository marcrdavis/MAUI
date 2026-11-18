using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace MoveItAutomationUI.Features.LocalGroups;

internal static class LocalGroupsHosting
{
    internal static void ConfigureHttps(KestrelServerOptions options)
        => options.ConfigureHttpsDefaults(https => https.ClientCertificateMode = ClientCertificateMode.AllowCertificate);
}
