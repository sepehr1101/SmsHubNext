namespace SmsHubNext.Shared.Sms;

/// <summary>
/// How an SMS body splits on the wire: the detected <paramref name="Encoding"/>,
/// the encoded length in that encoding (<paramref name="CharacterCount"/> — GSM-7
/// septets with extension chars counted twice, or UTF-16 code units for UCS-2),
/// and the number of billable parts (<paramref name="SegmentCount"/>).
///
/// These three are frozen onto the <c>Message</c> at submission (README §6.3).
/// </summary>
public sealed record SmsSegmentInfo(SmsEncoding Encoding, int CharacterCount, int SegmentCount);
