using Microsoft.EntityFrameworkCore;

namespace ACSTranslate;

public static class DbContextHelpers
{
    public static async Task EnsureDbCreatedAsync<T>(this IHost app) where T : DbContext
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<T>().Database.EnsureCreatedAsync();
    }
}