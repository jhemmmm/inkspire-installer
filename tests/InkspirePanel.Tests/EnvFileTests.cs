using Inkspire.Panel.Runtime;
using Xunit;

namespace InkspirePanel.Tests;

public class EnvFileTests
{
    private const string Sample = "# Inkspire\nAPP_NAME=Laravel\nAPP_KEY=\n\nAPP_DEBUG=true\nMAIL_FROM_NAME=\"${APP_NAME}\"\n# DB_HOST=127.0.0.1\nDB_CONNECTION=sqlite\n";

    [Fact]
    public void Untouched_file_round_trips_byte_for_byte()
    {
        Assert.Equal(Sample, EnvFile.Parse(Sample).Build());
    }

    [Fact]
    public void Set_changes_only_the_line_it_names_and_keeps_position()
    {
        var env = EnvFile.Parse(Sample).Set("APP_DEBUG", "false");
        Assert.Equal(Sample.Replace("APP_DEBUG=true", "APP_DEBUG=false"), env.Build());
    }

    [Fact]
    public void Set_to_the_same_value_keeps_the_users_own_quoting()
    {
        var env = EnvFile.Parse("APP_NAME=\"Shop One\"\n").Set("APP_NAME", "Shop One");
        Assert.Equal("APP_NAME=\"Shop One\"\n", env.Build());
    }

    [Fact]
    public void Set_appends_missing_keys_and_null_removes()
    {
        var env = EnvFile.Parse("A=1\nB=2\n").Set("C", "3").Set("A", null);
        Assert.Equal("B=2\nC=3\n", env.Build());
        Assert.False(env.Has("A"));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("C:/Inkspire/data/inkspire.sqlite", "C:/Inkspire/data/inkspire.sqlite")]
    [InlineData("Shop One", "'Shop One'")]
    [InlineData("pa$$ word#1", "'pa$$ word#1'")]
    [InlineData("it's", "\"it's\"")]
    [InlineData("back\\slash", "\"back\\\\slash\"")]
    [InlineData("", "")]
    public void Format_protects_values_phpdotenv_would_misread(string value, string expected)
    {
        Assert.Equal(expected, EnvFile.Format(value));
    }

    [Theory]
    [InlineData("pa$$ word#1")]
    [InlineData("it's \"quoted\" \\ and $VAR")]
    [InlineData("two\nlines")]
    [InlineData("   leading and trailing   ")]
    public void Values_survive_a_set_and_reload(string value)
    {
        var text = EnvFile.Empty().Set("SECRET", value).Build();
        Assert.Equal(value, EnvFile.Parse(text).Get("SECRET"));
    }

    [Fact]
    public void Quoted_values_and_inline_comments_are_decoded()
    {
        var env = EnvFile.Parse("A=\"x y\"\nB='lit $eral'\nC=plain # trailing comment\nD=\"has \\\"quote\\\"\"\nexport E=5\n");
        Assert.Equal("x y", env.Get("A"));
        Assert.Equal("lit $eral", env.Get("B"));
        Assert.Equal("plain", env.Get("C"));
        Assert.Equal("has \"quote\"", env.Get("D"));
        Assert.Equal("5", env.Get("E"));
    }

    [Fact]
    public void First_definition_wins_like_phpdotenv()
    {
        Assert.Equal("one", EnvFile.Parse("K=one\nK=two\n").Get("K"));
    }

    [Fact]
    public void Crlf_input_is_normalised()
    {
        Assert.Equal("A=1\nB=2\n", EnvFile.Parse("A=1\r\nB=2\r\n").Build());
    }

    [Fact]
    public void Comments_are_not_entries()
    {
        var env = EnvFile.Parse(Sample);
        Assert.False(env.Has("DB_HOST"));
        Assert.Equal(new[] { "APP_NAME", "APP_KEY", "APP_DEBUG", "MAIL_FROM_NAME", "DB_CONNECTION" }, env.Keys);
    }
}
