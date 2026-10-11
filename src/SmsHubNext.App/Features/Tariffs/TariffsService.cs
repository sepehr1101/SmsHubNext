using SmsHubNext.App.Shared.Api;
namespace SmsHubNext.App.Features.Tariffs;
public sealed class TariffsService
{
    private readonly ManagementApiClient _api;
    public TariffsService(ManagementApiClient api) => _api = api;
    public Task<ApiClientResult<IReadOnlyList<TariffItem>>> List(CancellationToken token) => _api.Get<IReadOnlyList<TariffItem>>("tariffs", token);
    public Task<ApiClientResult<CreateTariffResponse>> Create(CreateTariffRequest request, CancellationToken token) => _api.Post<CreateTariffRequest, CreateTariffResponse>("tariffs", request, token);
    public Task<ApiClientResult<string?>> Update(int id, UpdateTariffRequest request, CancellationToken token) => _api.Put($"tariffs/{id}", request, token);
    public Task<ApiClientResult<string?>> Delete(int id, CancellationToken token) => _api.Delete($"tariffs/{id}", token);
}
