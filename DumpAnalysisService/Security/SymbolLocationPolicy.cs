using Shared.Configuration;

namespace DumpAnalysisService.Security;

/// <summary>
/// Symbol locations from tool arguments are limited to configured ones. A tool argument may name a
/// local absolute directory or an https symbol server; a UNC share, a device path or a plain http
/// server only under a prefix listed in Debugger:AllowedSymbolLocations. Headers, the REST body and
/// the server's own settings are not checked here (they come from the client's configuration or the
/// operator); <see cref="SymbolInputValidator"/> applies to every client value.
/// </summary>
public static class SymbolLocationPolicy
{
    // symsrv keywords that may appear between '*' in a symbol path element
    private static readonly HashSet<string> _keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "srv", "symsrv", "symsrv.dll", "cache",
    };

    /// <exception cref="ArgumentException">A location is neither allowed by default nor under a configured prefix.</exception>
    public static void EnsureAllowed(SymbolsConfiguration? fromToolArguments, IReadOnlyCollection<string> allowedPrefixes)
    {
        if (fromToolArguments is null)
            return;

        _Check(fromToolArguments.SymbolCache, "symbol_cache", allowedPrefixes);
        _Check(fromToolArguments.SymbolPathExtra, "symbol_path_extra", allowedPrefixes);
        _Check(fromToolArguments.SymbolServers, "symbol_servers", allowedPrefixes);
    }

    /// <summary>True when one location (no ';' or '*') may come from a tool argument.</summary>
    public static bool IsAllowed(string location, IReadOnlyCollection<string> allowedPrefixes)
    {
        location = location.Trim();
        if (location.Length == 0)
            return true;

        if (_HasParentSegment(location))
            return false;

        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !location.StartsWith(@"\\", StringComparison.Ordinal))
            return uri.Scheme == "https" || _UnderPrefix(location, allowedPrefixes);

        // UNC shares and device paths (\\server\share, //server/share, \\?\..., \\.\...)
        if (location.StartsWith(@"\\", StringComparison.Ordinal) || location.StartsWith("//", StringComparison.Ordinal))
            return _UnderPrefix(location, allowedPrefixes);

        // Local absolute directory: drive letter, colon, separator
        if (location.Length >= 3 && char.IsAsciiLetter(location[0]) && location[1] == ':' && location[2] is '\\' or '/')
            return true;

        // Relative paths, other schemes, anything else: only if configured
        return _UnderPrefix(location, allowedPrefixes);
    }

    private static void _Check(string? value, string name, IReadOnlyCollection<string> allowedPrefixes)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var element in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var part in element.Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (_keywords.Contains(part))
                    continue;

                if (!IsAllowed(part, allowedPrefixes))
                    throw new ArgumentException(
                        $"{name}: symbol location '{part}' is not allowed from tool arguments; local directories and https servers are, " +
                        "other locations only under Debugger:AllowedSymbolLocations", name);
            }
        }
    }

    private static bool _UnderPrefix(string location, IReadOnlyCollection<string> allowedPrefixes)
    {
        var normalized = _Normalize(location);
        foreach (var prefix in allowedPrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            var p = _Normalize(prefix.Trim()).TrimEnd('/');
            if (normalized.Equals(p, StringComparison.OrdinalIgnoreCase))
                return true;
            if (normalized.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // Compare with one separator style; the scheme and host are case-insensitive anyway.
    private static string _Normalize(string value) => value.Replace('\\', '/');

    private static bool _HasParentSegment(string location) =>
        _Normalize(location).Split('/').Any(segment => segment == "..");
}
