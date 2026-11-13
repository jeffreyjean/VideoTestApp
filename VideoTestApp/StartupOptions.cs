using System.Globalization;

namespace VideoTestApp;

public sealed record StartupOptions(bool Stress, bool Loop, string? FilePath, IReadOnlyList<string> Warnings,
    TimeSpan? LoopDuration = null)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var stress = false;
        var loop = false;
        TimeSpan? loopDuration = null;
        string? filePath = null;
        var warnings = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg.Equals("-Stress", StringComparison.OrdinalIgnoreCase))
            {
                stress = true;
            }
            else if (arg.Equals("-Loop", StringComparison.OrdinalIgnoreCase))
            {
                stress = true;
                loop = true;
                loopDuration = null;
                if (index + 1 < args.Count &&
                    (!args[index + 1].StartsWith('-') ||
                     double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                {
                    var value = args[++index];
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) &&
                        double.IsFinite(minutes) && minutes > 0 && minutes < TimeSpan.MaxValue.TotalMinutes &&
                        TimeSpan.FromMinutes(minutes) is var duration && duration > TimeSpan.Zero)
                    {
                        loopDuration = duration;
                    }
                    else
                    {
                        warnings.Add($"Invalid -Loop duration '{value}': use a positive number of minutes. " +
                                     "Looping was disabled; -Stress will play the Media folder once.");
                        loop = false;
                    }
                }
            }
            else if (arg.StartsWith('-'))
            {
                warnings.Add($"Unknown argument '{arg}' was ignored.");
            }
            else if (filePath is null)
            {
                filePath = arg;
            }
            else
            {
                warnings.Add($"Extra argument '{arg}' was ignored.");
            }
        }

        if (stress && filePath is not null)
        {
            warnings.Add($"'{filePath}' was ignored because -Stress plays the Media folder.");
            filePath = null;
        }

        return new StartupOptions(stress, loop, filePath, warnings, loopDuration);
    }
}
