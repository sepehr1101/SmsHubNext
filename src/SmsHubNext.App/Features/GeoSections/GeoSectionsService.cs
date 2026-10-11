using SmsHubNext.App.Shared.Api;
namespace SmsHubNext.App.Features.GeoSections;
public sealed class GeoSectionsService
{
    private readonly ManagementApiClient _api;
    public GeoSectionsService(ManagementApiClient api) => _api = api;
    public Task<ApiClientResult<IReadOnlyList<GeoSectionItem>>> List(CancellationToken token) => _api.Get<IReadOnlyList<GeoSectionItem>>("reference-data/geo-sections", token);
    public Task<ApiClientResult<CreateGeoSectionResponse>> Create(CreateGeoSectionRequest request, CancellationToken token) => _api.Post<CreateGeoSectionRequest, CreateGeoSectionResponse>("reference-data/geo-sections", request, token);
    public Task<ApiClientResult<string?>> Update(int id, UpdateGeoSectionRequest request, CancellationToken token) => _api.Put($"reference-data/geo-sections/{id}", request, token);
    public Task<ApiClientResult<string?>> Delete(int id, CancellationToken token) => _api.Delete($"reference-data/geo-sections/{id}", token);
}
