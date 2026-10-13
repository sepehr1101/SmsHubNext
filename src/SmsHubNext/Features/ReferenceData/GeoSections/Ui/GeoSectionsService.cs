using SmsHubNext.Shared.Ui;
using GeoSectionItem = SmsHubNext.Features.ReferenceData.GeoSections.GeoSection;
using CreateGeoSectionRequest = SmsHubNext.Features.ReferenceData.GeoSections.CreateGeoSectionRequest;
using CreateGeoSectionResponse = SmsHubNext.Features.ReferenceData.GeoSections.CreateGeoSectionResponse;
using UpdateGeoSectionRequest = SmsHubNext.Features.ReferenceData.GeoSections.UpdateGeoSectionRequest;

namespace SmsHubNext.Features.ReferenceData.GeoSections.Ui;

public sealed class GeoSectionsService(
    UiOperations operations,
    ListGeoSectionsHandler listGeoSectionsHandler,
    CreateGeoSectionHandler createGeoSectionHandler,
    UpdateGeoSectionHandler updateGeoSectionHandler,
    DeleteGeoSectionHandler deleteGeoSectionHandler)
{
    public Task<UiResult<IReadOnlyList<GeoSectionItem>>> List(CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listGeoSectionsHandler.Handle(cancellationToken), token);

    public Task<UiResult<CreateGeoSectionResponse>> Create(CreateGeoSectionRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => createGeoSectionHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<string?>> Update(int id, UpdateGeoSectionRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => updateGeoSectionHandler.Handle(id, request, cancellationToken), token);

    public Task<UiResult<string?>> Delete(int id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => deleteGeoSectionHandler.Handle(id, identity.ApiKeyId, cancellationToken), token);

}
