using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ACSTranslate;

public static class ServiceCollectionHelpers
{
    public static IServiceCollection BindConfiguration<T>(this IServiceCollection services, string sectionName) where T : class
        => services.AddTransient((context)
            => context.GetRequiredService<IConfiguration>().GetSection(sectionName).Get<T>());
                
    public static IServiceCollection BindConfiguration<T>(this IServiceCollection services) where T : class
        => services.AddTransient((context)
            => context.GetRequiredService<IConfiguration>().Get<T>());
}
