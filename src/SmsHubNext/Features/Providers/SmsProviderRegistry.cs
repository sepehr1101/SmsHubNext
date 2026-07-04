using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers;

public sealed class SmsProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ISmsProvider> _providers;

    public SmsProviderRegistry(IEnumerable<ISmsProvider> providers)
    {
        _providers = providers.ToDictionary(
            provider => provider.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ISmsProvider> Providers => _providers.Values.ToArray();

    public Result<ISmsProvider> Resolve(string providerCode)
    {
        if (_providers.TryGetValue(providerCode, out ISmsProvider? provider))
            return Result.Success(provider);

        return Error.Provider(
            "providers.not_registered",
            UserMessages.Providers.NotRegistered(providerCode));
    }
}
