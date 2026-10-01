using System.Globalization;

namespace Inkspire.Panel.Setup;

/// <summary>Mirrors Inkspire's <c>Password::defaults()</c> (AppServiceProvider): at least 12 characters, upper and lower
/// case letters, a number and a symbol. The "not in a known breach" check needs the internet and is left to the app.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;

    /// <summary>Null when acceptable, otherwise a message telling the user what is missing.</summary>
    public static string? Validate(string password)
    {
        if (password.Length < MinLength) return $"Use at least {MinLength} characters.";
        if (!password.Any(char.IsLower) || !password.Any(char.IsUpper)) return "Use both upper-case and lower-case letters.";
        if (!password.Any(char.IsDigit)) return "Include at least one number.";
        if (!password.Any(IsSymbol)) return "Include at least one symbol, for example ! ? # % or a space.";
        return null;
    }

    // Laravel's symbols() rule is \p{Z}|\p{S}|\p{P}: separators (space), symbols and punctuation.
    private static bool IsSymbol(char c) => CharUnicodeInfo.GetUnicodeCategory(c) is
        UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
        or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol
        or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
        or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
        or UnicodeCategory.OtherPunctuation;
}
