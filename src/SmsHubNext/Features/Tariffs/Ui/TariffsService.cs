using SmsHubNext.Shared.Ui;
using TariffItem = SmsHubNext.Features.Tariffs.TariffResponse;
using CreateTariffRequest = SmsHubNext.Features.Tariffs.CreateTariffRequest;
using CreateTariffResponse = SmsHubNext.Features.Tariffs.CreateTariffResponse;
using UpdateTariffRequest = SmsHubNext.Features.Tariffs.UpdateTariffRequest;

namespace SmsHubNext.Features.Tariffs.Ui;

public sealed class TariffsService(
    UiOperations operations,
    ListTariffsHandler listTariffsHandler,
    CreateTariffHandler createTariffHandler,
    UpdateTariffHandler updateTariffHandler,
    DeleteTariffHandler deleteTariffHandler)
{
    public Task<UiResult<IReadOnlyList<TariffItem>>> List(CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listTariffsHandler.Handle(cancellationToken), token);

    public Task<UiResult<CreateTariffResponse>> Create(CreateTariffRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => createTariffHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<string?>> Update(int id, UpdateTariffRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => updateTariffHandler.Handle(id, request, cancellationToken), token);

    public Task<UiResult<string?>> Delete(int id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => deleteTariffHandler.Handle(id, identity.ApiKeyId, cancellationToken), token);

}
