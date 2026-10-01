using Inkspire.Panel.Setup;
using Xunit;

namespace InkspirePanel.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Str0ng!Passw0rd")]
    [InlineData("correct Horse 9 battery")]
    [InlineData("Aa1!Aa1!Aa1!")]
    public void Accepts_what_the_app_accepts(string password) => Assert.Null(PasswordPolicy.Validate(password));

    [Theory]
    [InlineData("Short1!a", "at least 12")]
    [InlineData("alllowercase1!x", "upper-case and lower-case")]
    [InlineData("ALLUPPERCASE1!X", "upper-case and lower-case")]
    [InlineData("NoNumbersHere!!", "number")]
    [InlineData("NoSymbolsHere123", "symbol")]
    public void Explains_what_is_missing(string password, string fragment)
    {
        var message = PasswordPolicy.Validate(password);
        Assert.NotNull(message);
        Assert.Contains(fragment, message, StringComparison.OrdinalIgnoreCase);
    }
}
