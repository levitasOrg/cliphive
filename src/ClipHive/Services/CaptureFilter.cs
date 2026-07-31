using System.Text.RegularExpressions;

namespace ClipHive;

/// <summary>
/// User-configurable capture exclusions (settings.json: IgnoredApps / IgnorePatterns).
/// Pure logic, no Win32 — the clipboard monitor consults it before recording.
/// Invalid regex patterns are skipped rather than breaking capture.
/// </summary>
public sealed class CaptureFilter
{
    private IReadOnlySet<string> _ignoredApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<Regex> _patterns = Array.Empty<Regex>();

    /// <summary>
    /// Replaces the active exclusion lists. Called at startup and whenever settings
    /// are saved. <paramref name="ignoredApps"/> holds process names without path or
    /// .exe suffix (e.g. "KeePass"); <paramref name="ignorePatterns"/> holds regexes
    /// matched against captured text.
    /// </summary>
    public void Update(IEnumerable<string>? ignoredApps, IEnumerable<string>? ignorePatterns)
    {
        _ignoredApps = new HashSet<string>(
            (ignoredApps ?? Array.Empty<string>())
                .Select(NormalizeApp)
                .Where(a => a.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var compiled = new List<Regex>();
        foreach (var pattern in ignorePatterns ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            try
            {
                compiled.Add(new Regex(pattern,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(250)));
            }
            catch (ArgumentException)
            {
                // Invalid user regex — skip it, never break capture.
            }
        }
        _patterns = compiled;
    }

    /// <summary>True when content copied from <paramref name="sourceApp"/> must not be recorded.</summary>
    public bool ShouldIgnoreApp(string? sourceApp) =>
        sourceApp is not null && _ignoredApps.Contains(NormalizeApp(sourceApp));

    /// <summary>True when the captured text matches any user ignore pattern.</summary>
    public bool ShouldIgnoreText(string text)
    {
        foreach (var regex in _patterns)
        {
            try
            {
                if (regex.IsMatch(text)) return true;
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological pattern on huge input — treat as no match.
            }
        }
        return false;
    }

    private static string NormalizeApp(string app)
    {
        var name = app.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }
}
