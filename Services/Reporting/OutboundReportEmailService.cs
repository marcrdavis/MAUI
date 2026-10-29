// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Renders and emails outbound reports as an HTML table plus a matching CSV attachment.

using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Reporting;

public interface IOutboundReportEmailService
{
    OutboundEmailConfigurationStatus GetConfigurationStatus();
    Task SendAsync(string title, string description, IReadOnlyList<string> recipients, OutboundReportData data, CancellationToken cancellationToken = default);
    Task SendTestAsync(string recipient, CancellationToken cancellationToken = default);
    Task SendHtmlAsync(string subject, string htmlBody, IReadOnlyList<string> recipients, CancellationToken cancellationToken = default);
}

public sealed class OutboundReportEmailService : IOutboundReportEmailService
{
    private const int HtmlRowLimit = 500;
    // System.Net.Mail.SmtpClient uses ServicePointManager for server-certificate validation.
    // Serialize all SMTP sends so the temporary certificate override cannot bleed into another SMTP send.
    private static readonly SemaphoreSlim SmtpSendGate = new(1, 1);
    private readonly IConfiguration _configuration;
    private readonly ISmtpSecretProtector _protector;

    public OutboundReportEmailService(IConfiguration configuration, ISmtpSecretProtector protector)
    {
        _configuration = configuration;
        _protector = protector;
    }

    public OutboundEmailConfigurationStatus GetConfigurationStatus()
    {
        SmtpOptions settings;
        try
        {
            settings = _configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            return NotConfigured($"SMTP configuration could not be read: {ex.GetBaseException().Message}");
        }

        if (string.IsNullOrWhiteSpace(settings.Host))
            return NotConfigured("SMTP Server is not configured.");
        if (settings.Port is < 1 or > 65535)
            return NotConfigured("SMTP Port is invalid.");
        if (string.IsNullOrWhiteSpace(settings.FromAddress))
            return NotConfigured("SMTP From Address is not configured.");
        try { _ = new MailAddress(settings.FromAddress.Trim()); }
        catch (FormatException) { return NotConfigured("SMTP From Address is invalid."); }

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            if (string.IsNullOrWhiteSpace(settings.ProtectedPassword))
                return NotConfigured("SMTP credentials are incomplete.");
            try { _ = _protector.Unprotect(settings.ProtectedPassword); }
            catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
            {
                return NotConfigured("The saved SMTP password cannot be decrypted. Re-enter the password in SMTP Configuration.");
            }
        }

        return new OutboundEmailConfigurationStatus(true, "Outbound email is configured.");
    }

    public async Task SendAsync(string title, string description, IReadOnlyList<string> recipients, OutboundReportData data, CancellationToken cancellationToken = default)
    {
        if (recipients is null || recipients.Count == 0) throw new ArgumentException("At least one report recipient is required.");
        var settings = GetSettings();
        using var message = BuildMessage(settings, title, description, recipients, data);
        using var client = BuildClient(settings);
        await SendMailAsync(client, message, settings, cancellationToken);
    }

    public async Task SendHtmlAsync(string subject, string htmlBody, IReadOnlyList<string> recipients, CancellationToken cancellationToken = default)
    {
        if (recipients is null || recipients.Count == 0) throw new ArgumentException("At least one email recipient is required.", nameof(recipients));
        var settings = GetSettings();
        using var message = new MailMessage { From = BuildFrom(settings), Subject = subject, Body = htmlBody, IsBodyHtml = true };
        foreach (var recipient in recipients) message.To.Add(new MailAddress(recipient));
        using var client = BuildClient(settings);
        await SendMailAsync(client, message, settings, cancellationToken);
    }

    public async Task SendTestAsync(string recipient, CancellationToken cancellationToken = default)
    {
        var address = OutboundReportRepository.NormalizeRecipients(recipient).Single();
        var settings = GetSettings();
        using var message = new MailMessage
        {
            From = BuildFrom(settings),
            Subject = "MAUI SMTP Test",
            Body = "<html><body><h2>MAUI SMTP Test</h2><p>This message confirms that MAUI can send outbound report email using the configured SMTP settings.</p></body></html>",
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(address));
        using var client = BuildClient(settings);
        await SendMailAsync(client, message, settings, cancellationToken);
    }

    private SmtpOptions GetSettings()
    {
        var status = GetConfigurationStatus();
        if (!status.IsConfigured) throw new InvalidOperationException(status.Message);
        return _configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
    }

    private static OutboundEmailConfigurationStatus NotConfigured(string reason)
    {
        var smtpServerMissing = reason.Equals("SMTP Server is not configured.", StringComparison.Ordinal);
        var prefix = smtpServerMissing
            ? "Unable to run report because outbound email has not been configured."
            : "Unable to run report because outbound email configuration is incomplete.";
        var detail = smtpServerMissing ? string.Empty : $"{reason} ";
        return new OutboundEmailConfigurationStatus(
            false,
            $"{prefix} {detail}Configure SMTP under Configuration > Settings > SMTP Configuration. Scheduled reports can still be saved and will remain pending until outbound email is configured.");
    }

    private SmtpClient BuildClient(SmtpOptions settings)
    {
#pragma warning disable SYSLIB0014 // SmtpClient remains sufficient for MAUI's configured SMTP relay workflow.
        var client = new SmtpClient(settings.Host.Trim(), settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = string.IsNullOrWhiteSpace(settings.Username)
        };
#pragma warning restore SYSLIB0014
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            string password;
            try
            {
                password = string.IsNullOrWhiteSpace(settings.ProtectedPassword)
                    ? string.Empty
                    : _protector.Unprotect(settings.ProtectedPassword);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
            {
                client.Dispose();
                throw new InvalidOperationException("The saved SMTP password cannot be decrypted on this server. Re-enter and save the SMTP password in Configuration > Settings > SMTP Configuration.", ex);
            }
            client.Credentials = new NetworkCredential(settings.Username.Trim(), password);
        }
        return client;
    }

    private static async Task SendMailAsync(SmtpClient client, MailMessage message, SmtpOptions settings, CancellationToken cancellationToken)
    {
        await SmtpSendGate.WaitAsync(cancellationToken);
        RemoteCertificateValidationCallback? previousCallback = null;
        var overrideCertificateValidation = settings.EnableSsl && settings.IgnoreCertificateErrors;
        try
        {
            if (overrideCertificateValidation)
            {
                previousCallback = ServicePointManager.ServerCertificateValidationCallback;
                ServicePointManager.ServerCertificateValidationCallback = static (_, _, _, _) => true;
            }

            await client.SendMailAsync(message, cancellationToken);
        }
        finally
        {
            if (overrideCertificateValidation)
                ServicePointManager.ServerCertificateValidationCallback = previousCallback;
            SmtpSendGate.Release();
        }
    }

    private MailMessage BuildMessage(SmtpOptions settings, string title, string description, IReadOnlyList<string> recipients, OutboundReportData data)
    {
        var message = new MailMessage
        {
            From = BuildFrom(settings),
            Subject = title,
            IsBodyHtml = true,
            Body = BuildHtml(title, description, data)
        };
        foreach (var recipient in recipients) message.To.Add(new MailAddress(recipient));

        var csv = BuildCsv(title, description, data);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv);
        var stream = new MemoryStream(bytes, writable: false);
        message.Attachments.Add(new Attachment(stream, BuildAttachmentName(title), "text/csv"));
        return message;
    }

    private static MailAddress BuildFrom(SmtpOptions settings)
        => string.IsNullOrWhiteSpace(settings.FromName)
            ? new MailAddress(settings.FromAddress.Trim())
            : new MailAddress(settings.FromAddress.Trim(), settings.FromName.Trim());

    private static string BuildHtml(string title, string description, OutboundReportData data)
    {
        var builder = new StringBuilder();
        builder.Append("<!doctype html><html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#172033\">");
        builder.Append("<h2 style=\"margin:0 0 8px\">").Append(WebUtility.HtmlEncode(title)).Append("</h2>");
        if (!string.IsNullOrWhiteSpace(description))
            builder.Append("<p style=\"margin:0 0 14px;white-space:pre-wrap\">").Append(WebUtility.HtmlEncode(description)).Append("</p>");
        builder.Append("<p style=\"font-size:12px;color:#596579\">Generated ")
            .Append(WebUtility.HtmlEncode(DateTimeOffset.Now.ToString("MMM d, yyyy h:mm:ss tt zzz", CultureInfo.CurrentCulture)))
            .Append(" · ").Append(data.Rows.Count.ToString("N0", CultureInfo.CurrentCulture)).Append(" row(s)</p>");

        if (data.Rows.Count > HtmlRowLimit)
            builder.Append("<p style=\"font-size:12px;color:#596579\"><strong>Preview:</strong> The first ")
                .Append(HtmlRowLimit.ToString(CultureInfo.InvariantCulture))
                .Append(" rows are shown below. The CSV attachment contains the complete report.</p>");

        builder.Append("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:12px;border:1px solid #cfd6df\"><thead><tr>");
        foreach (var column in data.Columns)
            builder.Append("<th style=\"text-align:left;background:#eef2f7;border:1px solid #cfd6df;white-space:nowrap\">")
                .Append(WebUtility.HtmlEncode(column.Label)).Append("</th>");
        builder.Append("</tr></thead><tbody>");

        foreach (var row in data.Rows.Take(HtmlRowLimit))
        {
            builder.Append("<tr>");
            foreach (var column in data.Columns)
            {
                var value = row.Values.TryGetValue(column.Key, out var text) ? text ?? string.Empty : string.Empty;
                builder.Append("<td style=\"vertical-align:top;border:1px solid #d8dee7;white-space:pre-wrap\">")
                    .Append(WebUtility.HtmlEncode(value)).Append("</td>");
            }
            builder.Append("</tr>");
        }
        builder.Append("</tbody></table><p style=\"font-size:11px;color:#7a8494;margin-top:14px\">Generated automatically by MAUI.</p></body></html>");
        return builder.ToString();
    }

    private static string BuildCsv(string title, string description, OutboundReportData data)
    {
        var lines = new List<string>
        {
            EscapeCsv(ProtectCsvCell(title, "text")),
            EscapeCsv(ProtectCsvCell(description, "text")),
            EscapeCsv($"Generated: {DateTimeOffset.Now:O}"),
            string.Empty,
            string.Join(',', data.Columns.Select(column => EscapeCsv(ProtectCsvCell(column.Label, "text"))))
        };
        foreach (var row in data.Rows)
        {
            lines.Add(string.Join(',', data.Columns.Select(column =>
            {
                var value = row.Values.TryGetValue(column.Key, out var text) ? text ?? string.Empty : string.Empty;
                return EscapeCsv(ProtectCsvCell(value, column.Type));
            })));
        }
        return string.Join("\r\n", lines);
    }

    private static string ProtectCsvCell(string value, string type)
        => !type.Equals("number", StringComparison.OrdinalIgnoreCase) && value.Length > 0 && "=+-@".Contains(value[0]) ? $"'{value}" : value;

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static string BuildAttachmentName(string title)
    {
        var safe = new string((title ?? "Report").Select(character => Path.GetInvalidFileNameChars().Contains(character) || char.IsControl(character) ? '_' : character).ToArray());
        safe = string.Join('_', safe.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (safe.Length > 80) safe = safe[..80];
        if (safe.Length == 0) safe = "Report";
        return $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
    }
}
