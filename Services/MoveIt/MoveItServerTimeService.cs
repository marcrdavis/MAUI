// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IMoveItServerTimeService
{
    // Adds an explicit UTC offset only to a timestamp that is known to be an unzoned MOVEit server-local value.
    // Zoned timestamps are returned unchanged. If the current MOVEit server offset cannot be established without
    // ambiguity, the original value is returned rather than guessing.
    string NormalizeKnownServerLocalTimestamp(string? value);

    // Converts an absolute UTC timestamp to the MOVEit server's local clock.
    DateTimeOffset ConvertUtcToServerTime(DateTimeOffset utc);

    // Resolves one unzoned MOVEit server-local time to its absolute timestamp(s). Ambiguous DST times may return two values.
    IReadOnlyList<DateTimeOffset> ResolveServerLocalTime(DateTime local);
}

public sealed class MoveItServerTimeService : IMoveItServerTimeService
{
    private readonly IMoveItSessionService _sessionService;
    private ServerClock? _serverClock;

    // Purpose: Initializes MOVEit server time service with its required dependencies.
    public MoveItServerTimeService(IMoveItSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    // Purpose: Normalizes a MOVEit server-local timestamp for consistent MAUI display.
    public string NormalizeKnownServerLocalTimestamp(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || HasExplicitZone(text)) return text;

        var timezone = _sessionService.Get()?.Info?.ServerTimezoneInfo;
        if (timezone is null) return text;

        if (!int.TryParse(timezone.TimeZoneBias, NumberStyles.Integer, CultureInfo.InvariantCulture, out var standardBiasMinutes))
            return text;

        var effectiveBiasMinutes = standardBiasMinutes;
        var name = timezone.TimeZoneName?.Trim() ?? string.Empty;

        // MOVEit supplies both the standard bias and DaylightBias together with a current time-zone name.
        // Apply DaylightBias only when MOVEit explicitly identifies the server as currently using daylight time.
        // If the name does not state Standard or Daylight, leave the timestamp untouched rather than inferring state.
        if (name.Contains("Daylight", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(timezone.DaylightBias, NumberStyles.Integer, CultureInfo.InvariantCulture, out var daylightBiasMinutes))
                return text;
            effectiveBiasMinutes += daylightBiasMinutes;
        }
        else if (!name.Contains("Standard", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return text;

        parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);

        // MOVEit/Windows bias semantics are UTC = local + bias, so the DateTimeOffset UTC offset is the negative bias.
        var offset = TimeSpan.FromMinutes(-effectiveBiasMinutes);
        if (offset < TimeSpan.FromHours(-14) || offset > TimeSpan.FromHours(14)) return text;

        return new DateTimeOffset(parsed, offset).ToString("o", CultureInfo.InvariantCulture);
    }

    // Converts UTC to the MOVEit server's local time using the same session-level timezone information everywhere MAUI needs it.
    public DateTimeOffset ConvertUtcToServerTime(DateTimeOffset utc)
        => GetServerClock().ToLocal(utc);

    // Resolves a MOVEit server-local wall-clock value. Invalid DST gap times are omitted; ambiguous fall-back times return both offsets.
    public IReadOnlyList<DateTimeOffset> ResolveServerLocalTime(DateTime local)
        => GetServerClock().Resolve(local);

    private ServerClock GetServerClock()
    {
        if (_serverClock is not null) return _serverClock;

        var timezone = _sessionService.Get()?.Info?.ServerTimezoneInfo;
        if (timezone is not null)
        {
            var name = timezone.TimeZoneName?.Trim() ?? string.Empty;
            foreach (var candidate in GetTimeZoneCandidates(name))
            {
                try
                {
                    _serverClock = new ServerClock(TimeZoneInfo.FindSystemTimeZoneById(candidate));
                    return _serverClock;
                }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }

            if (int.TryParse(timezone.TimeZoneBias, NumberStyles.Integer, CultureInfo.InvariantCulture, out var standardBiasMinutes))
            {
                var effectiveBiasMinutes = standardBiasMinutes;
                if (name.Contains("Daylight", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(timezone.DaylightBias, NumberStyles.Integer, CultureInfo.InvariantCulture, out var daylightBiasMinutes))
                    effectiveBiasMinutes += daylightBiasMinutes;

                var offset = TimeSpan.FromMinutes(-effectiveBiasMinutes);
                if (offset >= TimeSpan.FromHours(-14) && offset <= TimeSpan.FromHours(14))
                {
                    _serverClock = new ServerClock(offset);
                    return _serverClock;
                }
            }
        }

        throw new InvalidOperationException("MOVEit server time-zone information is unavailable or invalid.");
    }

    private static IEnumerable<string> GetTimeZoneCandidates(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;

        yield return name;

        const string daylightSuffix = " Daylight Time";
        if (name.EndsWith(daylightSuffix, StringComparison.OrdinalIgnoreCase))
            yield return name[..^daylightSuffix.Length] + " Standard Time";
    }

    private sealed class ServerClock
    {
        private readonly TimeZoneInfo? _timeZone;
        private readonly TimeSpan? _fixedOffset;

        public ServerClock(TimeZoneInfo timeZone)
        {
            _timeZone = timeZone;
        }

        public ServerClock(TimeSpan fixedOffset)
        {
            _fixedOffset = fixedOffset;
        }

        public DateTimeOffset ToLocal(DateTimeOffset utc)
            => _timeZone is not null
                ? TimeZoneInfo.ConvertTime(utc, _timeZone)
                : utc.ToOffset(_fixedOffset ?? TimeSpan.Zero);

        public IReadOnlyList<DateTimeOffset> Resolve(DateTime local)
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (_timeZone is null)
                return new[] { new DateTimeOffset(local, _fixedOffset ?? TimeSpan.Zero) };

            if (_timeZone.IsInvalidTime(local)) return Array.Empty<DateTimeOffset>();
            if (_timeZone.IsAmbiguousTime(local))
                return _timeZone.GetAmbiguousTimeOffsets(local).Select(offset => new DateTimeOffset(local, offset)).ToList();

            return new[] { new DateTimeOffset(local, _timeZone.GetUtcOffset(local)) };
        }
    }

    // Purpose: Returns whether explicit zone.
    private static bool HasExplicitZone(string value)
    {
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;
        var separator = value.IndexOf('T');
        if (separator < 0) separator = value.IndexOf(' ');
        if (separator < 0) return false;
        return value.IndexOf('+', separator + 1) >= 0 || value.IndexOf('-', separator + 1) >= 0;
    }
}
