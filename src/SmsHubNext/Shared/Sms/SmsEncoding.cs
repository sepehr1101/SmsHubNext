namespace SmsHubNext.Shared.Sms;

/// <summary>Wire encoding used to carry an SMS body.</summary>
public enum SmsEncoding
{
    /// <summary>GSM 03.38 7-bit default alphabet (Latin); 160/153 chars per part.</summary>
    Gsm7,

    /// <summary>UCS-2 (UTF-16) for non-GSM text such as Persian; 70/67 chars per part.</summary>
    Ucs2,
}
