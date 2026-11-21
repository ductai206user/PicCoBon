using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace PickleballClub.Logging;

/// <summary>
/// Lightweight AsyncLocal correlation context + log-safety helpers.
/// Enables tracing Booking / PayPal / POS / Check-In flows end-to-end without leaking PII or secrets.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _currentId = new();
    private static readonly Regex ValidHeaderRegex = new(@"^[A-Za-z0-9_-]{8,48}$", RegexOptions.Compiled);

    public const string HeaderName = "X-Correlation-Id";

    public static string? CurrentId => _currentId.Value;

    public static string EnsureActive()
    {
        if (!string.IsNullOrEmpty(_currentId.Value))
        {
            return _currentId.Value;
        }

        string generated = CreateNewId();
        _currentId.Value = generated;
        return generated;
    }

    public static string ResolveSafeId(string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            string trimmed = candidate.Trim();
            if (ValidHeaderRegex.IsMatch(trimmed))
            {
                return trimmed;
            }
        }

        return CreateNewId();
    }

    public static string CreateNewId(string prefix = "spk") =>
        $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 1 + 12)];

    public static IDisposable Push(string correlationId)
    {
        string? previous = _currentId.Value;
        _currentId.Value = correlationId;
        return new PopHandle(previous);
    }

    public static IDisposable BeginWorkerCycle(ILogger logger, string workerName)
    {
        string cycleId = CreateNewId("wrk");
        var scopeHandle = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = cycleId,
            ["Worker"] = workerName
        });
        var asyncHandle = Push(cycleId);
        return new CompositeDisposable(scopeHandle, asyncHandle);
    }

    public static string MaskIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "(empty)";
        }

        string clean = StripControlChars(identifier.Trim());
        int atIdx = clean.IndexOf('@');
        if (atIdx > 0)
        {
            string local = clean[..atIdx];
            string domain = clean[(atIdx + 1)..];
            string visibleLocal = local.Length <= 2 ? local[..1] : local[..2];
            return $"{visibleLocal}***@{domain}";
        }

        if (clean.Length <= 4)
        {
            return "***";
        }

        return $"{clean[..2]}***{clean[^2..]}";
    }

    public static string StripControlChars(string? text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string cleaned = text
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength] + "...";
    }

    private sealed class PopHandle : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public PopHandle(string? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _currentId.Value = _previous;
        }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable? _first;
        private readonly IDisposable _second;

        public CompositeDisposable(IDisposable? first, IDisposable second)
        {
            _first = first;
            _second = second;
        }

        public void Dispose()
        {
            _second.Dispose();
            _first?.Dispose();
        }
    }
}
