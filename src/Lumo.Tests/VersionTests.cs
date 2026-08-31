using Lumo.Core;
using Xunit;

namespace Lumo.Tests;

// ============================================================================
// v2.4.0-alpha.7 — the display-version parser (Core/AppVersion.cs).
// Every user-visible label (tray tooltip, hotkey tooltip, Settings about) now
// derives from InformationalVersion; these pins keep the extraction honest.
// ============================================================================

public class VersionTests
{
    [Theory]
    [InlineData("Lumo 2.4.0-alpha.7 (ALPHA — unstable build)", "2.4.0-alpha.7")]
    [InlineData("Lumo 2.5 (ALPHA — unstable build)", "2.5")]
    [InlineData("2.4.0", "2.4.0")]
    [InlineData("abc 1.2.3-beta.4 x", "1.2.3-beta.4")]
    [InlineData("Lumo 1.0.0-alpha.1+build.5", "1.0.0-alpha.1+build.5")]
    [InlineData("1.2.3 4.5.6", "1.2.3")]   // first digit-headed token wins
    public void FromInformational_Extracts_First_Digit_Token(string input, string expected)
        => Assert.Equal(expected, AppVersion.FromInformational(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no version here")]
    [InlineData("Lumo")]
    public void FromInformational_Falls_Back_When_No_Version_Token(string? input)
        => Assert.Equal(AppVersion.Fallback, AppVersion.FromInformational(input));

    [Fact]
    public void Fallback_Is_A_Plausible_Version_Label()
        => Assert.Matches(@"^\d+(\.\d+)+$", AppVersion.Fallback);

    [Fact]
    public void Label_Always_Looks_Like_A_Version()
    {
        // net8.0 compiles the sources into the test assembly (no InformationalVersion
        // attribute → Fallback); net8.0-windows references the real Lumo assembly and
        // gets the full "2.4.0-alpha.N" label. Either way the shape must be version-like.
        Assert.Matches(@"^\d+(\.\d+)+(-[\w.]+)?(\+[\w.]+)?$", AppVersion.Label);
    }
}
