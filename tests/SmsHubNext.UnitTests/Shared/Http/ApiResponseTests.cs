using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Http;

public sealed class ApiResponseTests
{
    [Fact]
    public void Success_wraps_data_with_metadata()
    {
        DefaultHttpContext context = Context();

        ApiResponse<int> response = ApiResponse.Success(42, context, StatusCodes.Status202Accepted);

        Assert.True(response.Success);
        Assert.Equal("accepted", response.Code);
        Assert.Equal(42, response.Data);
        Assert.False(string.IsNullOrWhiteSpace(response.Meta.TraceId));
    }

    [Fact]
    public void Failure_preserves_error_code_and_message()
    {
        DefaultHttpContext context = Context();
        Error error = Error.Validation("sample.invalid", "Invalid sample.");

        ApiResponse<object> response = ApiResponse.Failure(error, context);

        Assert.False(response.Success);
        Assert.Equal("sample.invalid", response.Code);
        Assert.Equal("Invalid sample.", response.Message);
        Assert.Null(response.Data);
    }

    private static DefaultHttpContext Context()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
    }
}
