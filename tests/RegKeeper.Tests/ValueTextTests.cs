using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class ValueTextTests
{
    [Theory]
    [InlineData(RawValue.Sz, @"C:\x y")]
    [InlineData(RawValue.ExpandSz, @"%TEMP%\a")]
    [InlineData(RawValue.MultiSz, "a\r\nb c")]
    [InlineData(RawValue.Dword, "4294967295")]
    [InlineData(RawValue.Qword, "18446744073709551615")]
    [InlineData(RawValue.Binary, "00 FF 1A")]
    [InlineData(8u, "")]
    public void TextRoundTrips(uint type, string text) =>
        Assert.Equal(text, ValueText.ToText(ValueText.Parse("n", type, text)));

    [Fact]
    public void ParsesHexAndRejectsOverflow()
    {
        Assert.Equal(255u, BitConverter.ToUInt32(ValueText.Parse("n", RawValue.Dword, " 0xff ").Data));
        Assert.Equal(new byte[] { 0xAB, 0xCD }, ValueText.Parse("n", RawValue.Binary, "ab,cd").Data);
        Assert.Throws<OverflowException>(() => ValueText.Parse("n", RawValue.Dword, "4294967296"));
        Assert.Throws<FormatException>(() => ValueText.Parse("n", RawValue.Dword, "abc"));
    }
}
