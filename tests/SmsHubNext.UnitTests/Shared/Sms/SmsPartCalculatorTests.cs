using SmsHubNext.Shared.Sms;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Sms;

public class SmsPartCalculatorTests
{
    private const char Gsm = 'a';        // 1 GSM-7 septet
    private const char Persian = 'ا'; // 'ا' — forces UCS-2, 1 UTF-16 unit

    [Fact]
    public void Short_latin_text_is_a_single_gsm7_segment()
    {
        var info = SmsPartCalculator.Calculate("Hello");

        Assert.Equal(SmsEncoding.Gsm7, info.Encoding);
        Assert.Equal(5, info.CharacterCount);
        Assert.Equal(1, info.SegmentCount);
    }

    [Theory]
    [InlineData(160, 1)]   // exactly one segment
    [InlineData(161, 2)]   // overflow ⇒ 153-char parts
    [InlineData(306, 2)]   // 2 × 153
    [InlineData(307, 3)]
    public void Gsm7_segments_split_at_160_then_153(int length, int expectedSegments)
    {
        var info = SmsPartCalculator.Calculate(new string(Gsm, length));

        Assert.Equal(SmsEncoding.Gsm7, info.Encoding);
        Assert.Equal(length, info.CharacterCount);
        Assert.Equal(expectedSegments, info.SegmentCount);
    }

    [Theory]
    [InlineData("[")]
    [InlineData("€")]
    [InlineData("}")]
    public void Gsm7_extension_chars_count_as_two_septets(string text)
    {
        var info = SmsPartCalculator.Calculate(text);

        Assert.Equal(SmsEncoding.Gsm7, info.Encoding);
        Assert.Equal(2, info.CharacterCount);
        Assert.Equal(1, info.SegmentCount);
    }

    [Fact]
    public void Persian_text_is_ucs2()
    {
        var info = SmsPartCalculator.Calculate("سلام");

        Assert.Equal(SmsEncoding.Ucs2, info.Encoding);
        Assert.Equal(4, info.CharacterCount);
        Assert.Equal(1, info.SegmentCount);
    }

    [Theory]
    [InlineData(70, 1)]    // exactly one UCS-2 segment
    [InlineData(71, 2)]    // overflow ⇒ 67-char parts
    [InlineData(134, 2)]   // 2 × 67
    [InlineData(135, 3)]
    public void Ucs2_segments_split_at_70_then_67(int length, int expectedSegments)
    {
        var info = SmsPartCalculator.Calculate(new string(Persian, length));

        Assert.Equal(SmsEncoding.Ucs2, info.Encoding);
        Assert.Equal(length, info.CharacterCount);
        Assert.Equal(expectedSegments, info.SegmentCount);
    }

    [Fact]
    public void Emoji_forces_ucs2_and_counts_surrogate_code_units()
    {
        var info = SmsPartCalculator.Calculate("😀"); // U+1F600 — a surrogate pair

        Assert.Equal(SmsEncoding.Ucs2, info.Encoding);
        Assert.Equal(2, info.CharacterCount);
        Assert.Equal(1, info.SegmentCount);
    }

    [Fact]
    public void Empty_text_is_a_single_empty_gsm7_segment()
    {
        var info = SmsPartCalculator.Calculate(string.Empty);

        Assert.Equal(SmsEncoding.Gsm7, info.Encoding);
        Assert.Equal(0, info.CharacterCount);
        Assert.Equal(1, info.SegmentCount);
    }

    [Fact]
    public void Null_text_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => SmsPartCalculator.Calculate(null!));
    }
}
