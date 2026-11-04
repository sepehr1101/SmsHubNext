using System.Collections;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SmsHubNext.Features.Administration.Ui.Components;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class PersianTableLocalizationTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    [InlineData("")]
    public void All_table_resources_are_translated_through_the_registered_interceptor(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMudServices();
            services.AddPersianMudLocalization();
            using ServiceProvider provider = services.BuildServiceProvider();
            ILocalizationInterceptor interceptor = provider.GetRequiredService<ILocalizationInterceptor>();
            ResourceManager manager = new("MudBlazor.Resources.LanguageResource", typeof(MudLocalizer).Assembly);
            ResourceSet resources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
            List<string> missing = [];
            foreach (DictionaryEntry resource in resources)
            {
                string key = (string)resource.Key;
                if (!key.StartsWith("MudDataGrid", StringComparison.Ordinal) && !key.StartsWith("MudTable", StringComparison.Ordinal)) continue;
                string translated = interceptor.Handle(key, 1, 2, 3).Value;
                if (!translated.Any(character => character is >= '\u0600' and <= '\u06ff')) missing.Add(key);
            }
            Assert.True(missing.Count == 0, string.Join(", ", missing));
            Assert.Equal("۱ تا ۱۰ از ۲۵", interceptor.Handle("MudDataGridPager_InfoFormat", 1, 10, 25).Value);
            Assert.Equal("ستون", interceptor.Handle("MudDataGrid_Column").Value);
            Assert.Equal("افزودن فیلتر", interceptor.Handle("MudDataGrid_AddFilter").Value);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}


