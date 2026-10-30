using Sharelink.Hotkeys;

namespace Sharelink.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Shift+4", Keys.Control | Keys.Shift | Keys.D4)]
    [InlineData("ctrl + alt + F5", Keys.Control | Keys.Alt | Keys.F5)]
    [InlineData("PrintScreen", Keys.PrintScreen)]
    [InlineData("Shift+A", Keys.Shift | Keys.A)]
    public void TryParse_Valid(string text, Keys expected)
    {
        Assert.True(HotkeyParser.TryParse(text, out Keys keys));
        Assert.Equal(expected, keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+42")]
    public void TryParse_Invalid(string text) => Assert.False(HotkeyParser.TryParse(text, out _));

    [Theory]
    [InlineData("Ctrl+Shift+4")]
    [InlineData("Ctrl+Alt+F5")]
    [InlineData("PrintScreen")]
    public void Format_RoundTrips(string text)
    {
        Assert.True(HotkeyParser.TryParse(text, out Keys keys));
        Assert.Equal(text, HotkeyParser.Format(keys));
    }
}
