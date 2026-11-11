namespace VideoTestApp;

public sealed record StartupOptions(bool Stress, bool Loop, string? FilePath, IReadOnlyList<string> Warnings)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var stress = false;
        var loop = false;
        string? filePath = null;
        var warnings = new List<string>();

        foreach (var arg in args)
        {
            if (arg.Equals("-Stress", StringComparison.OrdinalIgnoreCase))
            {
                stress = true;
            }
            else if (arg.Equals("-Loop", StringComparison.OrdinalIgnoreCase))
            {
                loop = true;
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

        if (loop && !stress)
        {
            warnings.Add("-Loop only applies together with -Stress and was ignored.");
            loop = false;
        }

        if (stress && filePath is not null)
        {
            warnings.Add($"'{filePath}' was ignored because -Stress plays the Media folder.");
            filePath = null;
        }

        return new StartupOptions(stress, loop, filePath, warnings);
    }
}
