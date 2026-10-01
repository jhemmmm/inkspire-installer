using Inkspire.Panel.Runtime;
using Xunit;

namespace InkspirePanel.Tests;

public class PhpIniPatcherTests
{
    [Fact]
    public void Set_replaces_a_commented_out_directive_in_place()
    {
        var ini = new PhpIniPatcher("[PHP]\n;date.timezone =\nfoo=1\n").Set("date.timezone", "UTC").Build();
        Assert.Equal("[PHP]\ndate.timezone = UTC\nfoo=1\n", ini);
    }

    [Fact]
    public void Set_appends_when_the_directive_is_absent()
    {
        var ini = new PhpIniPatcher("[PHP]\n").Set("opcache.jit", "off").Build();
        Assert.Contains("; ---- Added by Inkspire Control Panel ----", ini);
        Assert.EndsWith("opcache.jit = off\n", ini);
    }

    [Fact]
    public void Set_never_mistakes_an_extension_line_for_a_directive()
    {
        var ini = new PhpIniPatcher("extension=curl\n").Set("extension_dir", "\"ext\"").Build();
        Assert.Contains("extension=curl", ini);
        Assert.Contains("extension_dir = \"ext\"", ini);
    }

    [Theory]
    [InlineData(";extension=curl")]
    [InlineData("; extension=curl")]
    [InlineData(";extension=php_curl.dll")]
    [InlineData("extension=curl")]
    [InlineData(";extension=\"curl\"")]
    public void EnableExtension_normalises_every_spelling(string line)
    {
        var ini = new PhpIniPatcher(line + "\n").EnableExtension("curl").Build();
        Assert.Equal("extension=curl\n", ini);
    }

    [Fact]
    public void EnableExtension_adds_a_line_when_the_template_lacks_it()
    {
        var ini = new PhpIniPatcher("[PHP]\n").EnableExtension("intl").EnableExtension("intl").Build();
        Assert.Equal(1, ini.Split("extension=intl").Length - 1);
    }

    [Fact]
    public void EnableZendExtension_handles_opcache()
    {
        var ini = new PhpIniPatcher(";zend_extension=opcache\n").EnableZendExtension("opcache").Build();
        Assert.Equal("zend_extension=opcache\n", ini);
    }
}
