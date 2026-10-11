using SmsHubNext.App.Shared.Api;
namespace SmsHubNext.App.Features.ApiKeys;
public sealed class ApiKeysService
{
    private readonly ManagementApiClient _api;
    public ApiKeysService(ManagementApiClient api) => _api = api;
    public Task<ApiClientResult<IReadOnlyList<ApiKeyItem>>> List(short customerId, CancellationToken token) => _api.Get<IReadOnlyList<ApiKeyItem>>($"api-keys?customerId={customerId}", token);
    public Task<ApiClientResult<IssueApiKeyResponse>> Issue(IssueApiKeyRequest request, CancellationToken token) => _api.Post<IssueApiKeyRequest, IssueApiKeyResponse>("api-keys", request, token);
    public Task<ApiClientResult<string?>> Update(int id, UpdateApiKeyRequest request, CancellationToken token) => _api.Put($"api-keys/{id}", request, token);
    public Task<ApiClientResult<string?>> Revoke(int id, CancellationToken token) => _api.Delete($"api-keys/{id}", token);
    public Task<ApiClientResult<IReadOnlyList<ApiKeyIpRestrictionItem>>> ListIpRestrictions(int apiKeyId, CancellationToken token) => _api.Get<IReadOnlyList<ApiKeyIpRestrictionItem>>($"api-keys/{apiKeyId}/ip-restrictions", token);
    public Task<ApiClientResult<ApiKeyIpRestrictionItem>> AddIpRestriction(int apiKeyId, AddIpRestrictionRequest request, CancellationToken token) => _api.Post<AddIpRestrictionRequest, ApiKeyIpRestrictionItem>($"api-keys/{apiKeyId}/ip-restrictions", request, token);
    public Task<ApiClientResult<string?>> UpdateIpRestriction(int apiKeyId, int id, UpdateIpRestrictionRequest request, CancellationToken token) => _api.Put($"api-keys/{apiKeyId}/ip-restrictions/{id}", request, token);
    public Task<ApiClientResult<string?>> DeleteIpRestriction(int apiKeyId, int id, CancellationToken token) => _api.Delete($"api-keys/{apiKeyId}/ip-restrictions/{id}", token);
}
