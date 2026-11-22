using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace PickleballClub.Logging;

public sealed class CleanSpikeConsoleFormatterOptions : ConsoleFormatterOptions
{
    public LoggerColorBehavior ColorBehavior { get; set; } = LoggerColorBehavior.Default;
    public bool IncludeCorrelationId { get; set; } = true;
}

/// <summary>
/// Compact single-line developer console formatter for Spike OS.
/// Outputs: HH:mm:ss  LEVEL  Category       Message [cid:...]
/// Uses zero external dependencies and honors NO_COLOR / terminal redirection.
/// </summary>
public sealed class CleanSpikeConsoleFormatter : ConsoleFormatter, IDisposable
{
    public const string FormatterName = "spike-clean";

    private readonly IDisposable? _optionsReloadToken;
    private CleanSpikeConsoleFormatterOptions _options;

    private const string AnsiReset = "\x1b[0m";
    private const string AnsiMuted = "\x1b[90m";
    private const string AnsiInfo = "\x1b[32m";
    private const string AnsiDebug = "\x1b[36m";
    private const string AnsiWarn = "\x1b[33m";
    private const string AnsiError = "\x1b[31;1m";
    private const string AnsiCategory = "\x1b[96m";
    private const string AnsiCid = "\x1b[35m";

    public CleanSpikeConsoleFormatter(IOptionsMonitor<CleanSpikeConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options.CurrentValue;
        _optionsReloadToken = options.OnChange(updated => _options = updated);
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        string? message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception == null)
        {
            return;
        }

        bool useColors = ShouldUseColors();
        DateTime localTime = _options.UseUtcTimestamp ? DateTime.UtcNow : DateTime.Now;
        string timeText = localTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        (string levelLabel, string levelColor) = logEntry.LogLevel switch
        {
            LogLevel.Trace => ("TRACE", AnsiMuted),
            LogLevel.Debug => ("DEBUG", AnsiDebug),
            LogLevel.Information => ("INFO ", AnsiInfo),
            LogLevel.Warning => ("WARN ", AnsiWarn),
            LogLevel.Error => ("ERROR", AnsiError),
            LogLevel.Critical => ("CRIT ", AnsiError),
            _ => ("INFO ", AnsiInfo)
        };

        string categoryLabel = FormatCategoryName(logEntry.Category).PadRight(14);
        string? correlationId = _options.IncludeCorrelationId ? ExtractCorrelationId(scopeProvider) : null;

        if (useColors)
        {
            textWriter.Write(AnsiMuted);
            textWriter.Write(timeText);
            textWriter.Write(AnsiReset);
            textWriter.Write("  ");

            textWriter.Write(levelColor);
            textWriter.Write(levelLabel);
            textWriter.Write(AnsiReset);
            textWriter.Write("  ");

            textWriter.Write(AnsiCategory);
            textWriter.Write(categoryLabel);
            textWriter.Write(AnsiReset);
            textWriter.Write(' ');
        }
        else
        {
            textWriter.Write(timeText);
            textWriter.Write("  ");
            textWriter.Write(levelLabel);
            textWriter.Write("  ");
            textWriter.Write(categoryLabel);
            textWriter.Write(' ');
        }

        if (!string.IsNullOrEmpty(message))
        {
            textWriter.Write(message);
        }

        if (!string.IsNullOrEmpty(correlationId))
        {
            textWriter.Write(' ');
            if (useColors)
            {
                textWriter.Write(AnsiMuted);
                textWriter.Write("[cid:");
                textWriter.Write(AnsiCid);
                textWriter.Write(correlationId);
                textWriter.Write(AnsiMuted);
                textWriter.Write(']');
                textWriter.Write(AnsiReset);
            }
            else
            {
                textWriter.Write("[cid:");
                textWriter.Write(correlationId);
                textWriter.Write(']');
            }
        }

        textWriter.WriteLine();

        if (logEntry.Exception != null)
        {
            if (useColors) textWriter.Write(AnsiError);
            textWriter.Write("           -> ");
            textWriter.WriteLine(logEntry.Exception.ToString());
            if (useColors) textWriter.Write(AnsiReset);
        }
    }

    private bool ShouldUseColors()
    {
        return _options.ColorBehavior switch
        {
            LoggerColorBehavior.Enabled => true,
            LoggerColorBehavior.Disabled => false,
            _ => !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
        };
    }

    private static string? ExtractCorrelationId(IExternalScopeProvider? scopeProvider)
    {
        string? found = null;
        scopeProvider?.ForEachScope((scope, _) =>
        {
            if (found != null) return;
            if (scope is IEnumerable<KeyValuePair<string, object?>> kvPairs)
            {
                foreach (var kv in kvPairs)
                {
                    if (string.Equals(kv.Key, "CorrelationId", StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                    {
                        found = kv.Value.ToString();
                        return;
                    }
                }
            }
        }, (object?)null);

        return !string.IsNullOrWhiteSpace(found) ? found : CorrelationContext.CurrentId;
    }

    public static string FormatCategoryName(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "SpikeOS";
        }

        if (category.StartsWith("PickleballClub.Program", StringComparison.Ordinal) ||
            category.StartsWith("Microsoft.Hosting.Lifetime", StringComparison.Ordinal) ||
            category.StartsWith("SpikeOS.", StringComparison.Ordinal))
        {
            return "SpikeOS";
        }

        if (category.Contains("BookingLifecycle", StringComparison.Ordinal) ||
            category.Contains("BookingService", StringComparison.Ordinal) ||
            category.EndsWith(".BookingsController", StringComparison.Ordinal) ||
            category.EndsWith(".BookingController", StringComparison.Ordinal))
        {
            return "Booking";
        }

        if (category.Contains("PayPal", StringComparison.Ordinal))
        {
            return "PayPal";
        }

        if (category.EndsWith(".PaymentController", StringComparison.Ordinal))
        {
            return "Payments";
        }

        if (category.Contains("OrderService", StringComparison.Ordinal) ||
            category.EndsWith(".ProductsController", StringComparison.Ordinal) ||
            category.EndsWith(".ProductController", StringComparison.Ordinal))
        {
            return "ProShop";
        }

        if (category.Contains("QrToken", StringComparison.Ordinal))
        {
            return "QrCheckIn";
        }

        if (category.EndsWith(".AccountController", StringComparison.Ordinal) ||
            category.EndsWith(".LoginController", StringComparison.Ordinal) ||
            category.EndsWith(".RegisterController", StringComparison.Ordinal))
        {
            return "Auth";
        }

        if (category.Contains(".Areas.Admin.", StringComparison.Ordinal))
        {
            return "Admin";
        }

        if (category.EndsWith(".ApiDbController", StringComparison.Ordinal))
        {
            return "Api";
        }

        if (category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            return "Database";
        }

        if (category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
        {
            return "AspNetCore";
        }

        int lastDot = category.LastIndexOf('.');
        string shortName = lastDot >= 0 && lastDot < category.Length - 1
            ? category[(lastDot + 1)..]
            : category;

        return shortName.Length > 14 ? shortName[..14] : shortName;
    }

    public void Dispose() => _optionsReloadToken?.Dispose();
}
