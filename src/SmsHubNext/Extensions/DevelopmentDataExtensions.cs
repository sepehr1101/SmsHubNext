using SmsHubNext.Deployment;

namespace SmsHubNext.Extensions;

public static class DevelopmentDataExtensions
{
    public static async Task SeedDevelopmentDataAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue("DevelopmentSeed:Enabled", false))
            return;

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        DevelopmentDataSeeder seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
        await seeder.Seed(CancellationToken.None);
    }
}
