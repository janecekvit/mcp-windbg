using Shared.Configuration;

namespace DumpAnalysisService.Security;

/// <summary>
/// Validates client-supplied symbol configuration before it becomes part of the
/// cdb.exe symbol path argument.
/// </summary>
public static class SymbolInputValidator
{
    public const int MaxLength = 2048;

    public static void Validate(SymbolsConfiguration? symbols)
    {
        if (symbols == null)
            return;

        _ValidateValue(symbols.SymbolCache, "symbolCache", allowSeparator: false);
        _ValidateValue(symbols.SymbolPathExtra, "symbolPathExtra", allowSeparator: true);
        _ValidateValue(symbols.SymbolServers, "symbolServers", allowSeparator: true);
    }

    private static void _ValidateValue(string? value, string name, bool allowSeparator)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (value.Length > MaxLength)
            throw new ArgumentException($"{name} exceeds {MaxLength} characters", name);

        // Quotes and control characters could break out of the symbol path argument.
        if (value.Any(c => c == '"' || char.IsControl(c)))
            throw new ArgumentException($"{name} contains a quote or control character", name);

        // A single directory may not inject further symbol path elements.
        if (!allowSeparator && (value.Contains(';') || value.Contains('*')))
            throw new ArgumentException($"{name} must be a single directory (no ';' or '*')", name);
    }
}
