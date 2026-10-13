using SmsHubNext.Shared.Ui;
using ApiKeyItem = SmsHubNext.Features.ApiKeys.ApiKey;
using IssueApiKeyRequest = SmsHubNext.Features.ApiKeys.IssueApiKeyRequest;
using IssueApiKeyResponse = SmsHubNext.Features.ApiKeys.IssueApiKeyResponse;
using UpdateApiKeyRequest = SmsHubNext.Features.ApiKeys.UpdateApiKeyRequest;
using ApiKeyIpRestrictionItem = SmsHubNext.Features.ApiKeys.ApiKeyIpRestriction;
using AddIpRestrictionRequest = SmsHubNext.Features.ApiKeys.AddIpRestrictionRequest;
using UpdateIpRestrictionRequest = SmsHubNext.Features.ApiKeys.UpdateIpRestrictionRequest;

namespace SmsHubNext.Features.ApiKeys.Ui;

public sealed class ApiKeysService(
    UiOperations operations,
    ListApiKeysHandler listApiKeysHandler,
    IssueApiKeyHandler issueApiKeyHandler,
    UpdateApiKeyHandler updateApiKeyHandler,
    RevokeApiKeyHandler revokeApiKeyHandler,
    ListIpRestrictionsHandler listIpRestrictionsHandler,
    AddIpRestrictionHandler addIpRestrictionHandler,
    UpdateIpRestrictionHandler updateIpRestrictionHandler,
    DeleteIpRestrictionHandler deleteIpRestrictionHandler)
{
    public Task<UiResult<IReadOnlyList<ApiKeyItem>>> List(short customerId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listApiKeysHandler.Handle(customerId, cancellationToken), token);

    public Task<UiResult<IssueApiKeyResponse>> Issue(IssueApiKeyRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => issueApiKeyHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<string?>> Update(int id, UpdateApiKeyRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => updateApiKeyHandler.Handle(id, request, cancellationToken), token);

    public Task<UiResult<string?>> Revoke(int id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => revokeApiKeyHandler.Handle(id, identity.ApiKeyId, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<ApiKeyIpRestrictionItem>>> ListIpRestrictions(int apiKeyId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listIpRestrictionsHandler.Handle(apiKeyId, cancellationToken), token);

    public Task<UiResult<ApiKeyIpRestrictionItem>> AddIpRestriction(int apiKeyId, AddIpRestrictionRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => addIpRestrictionHandler.Handle(apiKeyId, request, cancellationToken), token);

    public Task<UiResult<string?>> UpdateIpRestriction(int apiKeyId, int id, UpdateIpRestrictionRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => updateIpRestrictionHandler.Handle(apiKeyId, id, request, cancellationToken), token);

    public Task<UiResult<string?>> DeleteIpRestriction(int apiKeyId, int id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => deleteIpRestrictionHandler.Handle(apiKeyId, id, identity.ApiKeyId, cancellationToken), token);

}
