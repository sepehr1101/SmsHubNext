namespace SmsHubNext.Shared.Enums;

/// <summary>
/// Level in the self-referencing geographic hierarchy (README §4.7).
/// Persisted as <c>TINYINT</c> — values are stable and must not be renumbered.
/// </summary>
public enum GeoSectionType : byte
{
    Province = 1,
    City = 2,
    Zone = 3,
}
