namespace SmsHubNext.Shared.Sms;

/// <summary>
/// Pure segment/encoding calculation for an SMS body. Given the text it detects
/// the encoding (GSM 03.38 7-bit vs UCS-2), measures the encoded length and
/// counts the parts. No tariffs, no I/O.
/// </summary>
/// <remarks>
/// Simplification: the rare rule that a GSM-7 escape pair (e.g. <c>€</c>, <c>[</c>)
/// or a UCS-2 surrogate pair must not straddle a segment boundary is not modelled —
/// lengths are counted as plain encoding units. Revisit only if exact per-carrier
/// parity proves necessary.
/// </remarks>
public static class SmsPartCalculator
{
    private const int Gsm7SingleSegment = 160;
    private const int Gsm7MultiSegment = 153;
    private const int Ucs2SingleSegment = 70;
    private const int Ucs2MultiSegment = 67;

    // GSM 03.38 default alphabet (1 septet each). Control codes LF/CR included; ESC excluded.
    private static readonly HashSet<char> Gsm7Basic = new(
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?" +
        "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà");

    // GSM 03.38 extension table (escape + char ⇒ 2 septets each).
    private static readonly HashSet<char> Gsm7Extension = new("\f^{}\\[~]|€");

    public static SmsSegmentInfo Calculate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (TryGetGsm7Length(text, out int gsm7Length))
        {
            return new SmsSegmentInfo(
                SmsEncoding.Gsm7,
                gsm7Length,
                SegmentCount(gsm7Length, Gsm7SingleSegment, Gsm7MultiSegment));
        }

        int ucs2Length = text.Length; // UTF-16 code units (surrogate pairs count as 2)
        return new SmsSegmentInfo(
            SmsEncoding.Ucs2,
            ucs2Length,
            SegmentCount(ucs2Length, Ucs2SingleSegment, Ucs2MultiSegment));
    }

    private static bool TryGetGsm7Length(string text, out int length)
    {
        length = 0;
        foreach (char c in text)
        {
            if (Gsm7Basic.Contains(c))
            {
                length++;
            }
            else if (Gsm7Extension.Contains(c))
            {
                length += 2;
            }
            else
            {
                length = 0;
                return false;
            }
        }

        return true;
    }

    private static int SegmentCount(int encodedLength, int singleSegment, int multiSegment)
        => encodedLength <= singleSegment ? 1 : (encodedLength + multiSegment - 1) / multiSegment;
}
