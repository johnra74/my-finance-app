using Microsoft.Extensions.DependencyInjection;

namespace MyFinance.App.Services;

/// <summary>
/// Resolves a service on demand.
/// </summary>
/// <remarks>
/// A narrow seam over the container, used where a view model has to construct another page
/// at the moment the user asks for it — the register, which needs an account id it cannot
/// know at composition time. Narrower than injecting <see cref="IServiceProvider"/>
/// wholesale, and trivial to substitute in a test.
/// </remarks>
public interface IServiceProviderAccessor
{
    T GetRequired<T>()
        where T : notnull;
}

/// <inheritdoc cref="IServiceProviderAccessor" />
public sealed class ServiceProviderAccessor : IServiceProviderAccessor
{
    private readonly IServiceProvider _services;

    public ServiceProviderAccessor(IServiceProvider services)
    {
        _services = services;
    }

    public T GetRequired<T>()
        where T : notnull =>
        _services.GetRequiredService<T>();
}
