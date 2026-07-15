using SmsHubNext.Features.Landing;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Landing;

public sealed class ServiceLandingPageTests
{
    [Fact]
    public void Landing_page_shows_api_documentation_when_enabled()
    {
        string html = ServiceLandingPage.GetHtml(openApiEnabled: true);

        Assert.Contains("<html lang=\"fa\" dir=\"rtl\">", html);
        Assert.Contains("name=\"viewport\"", html);
        Assert.Contains("href=\"/health\"", html);
        Assert.Contains("href=\"/service-info\"", html);
        Assert.Contains("href=\"/scalar/v1\"", html);
        Assert.Contains("fetch('/health'", html);
        Assert.DoesNotContain("localhost:8080", html);
    }

    [Fact]
    public void Landing_page_hides_api_documentation_when_disabled()
    {
        string html = ServiceLandingPage.GetHtml(openApiEnabled: false);

        Assert.DoesNotContain("/scalar/v1", html);
        Assert.DoesNotContain("OPENAPI_DOCUMENTATION_BUTTON", html);
    }
}
