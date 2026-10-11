namespace SmsHubNext.App.Features.GeoSections;

public enum GeoSectionType : byte { Province = 1, City = 2, Zone = 3 }
public sealed record GeoSectionItem(int Id, int? ParentGeoSectionId, GeoSectionType SectionType, string Name, string Code, string Path, bool IsActive);
public sealed record CreateGeoSectionRequest(int? ParentGeoSectionId, GeoSectionType SectionType, string Name, string Code);
public sealed record CreateGeoSectionResponse(int Id, string Path);
public sealed record UpdateGeoSectionRequest(string Name, string Code, bool IsActive);
