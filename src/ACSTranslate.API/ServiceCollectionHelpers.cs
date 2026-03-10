using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ACSTranslate;

public static class ServiceCollectionHelpers
{
    public static IServiceCollection BindConfiguration<T>(this IServiceCollection services, string sectionName) where T : class
        => services.AddTransient<T>((context)
            => context.GetRequiredService<IConfiguration>().GetSection(sectionName).Get<T>()
               ?? throw new InvalidOperationException($"Configuration section '{sectionName}' is missing or invalid for type {typeof(T).Name}."));
                
    public static IServiceCollection BindConfiguration<T>(this IServiceCollection services) where T : class
        => services.AddTransient<T>((context)
            => context.GetRequiredService<IConfiguration>().Get<T>()
               ?? throw new InvalidOperationException($"Root configuration is missing or invalid for type {typeof(T).Name}."));
}
