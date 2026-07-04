using SmsHubNext.Features.DispatchOperations;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.UnitTests.Features.DispatchOperations;

public sealed class DispatchOperationsRequestTests
{
    [Fact]
    public void Validates_optional_jalali_range_order()
    {
        DispatchOperationsRequest request = new DispatchOperationsRequest
        {
            FromJalali = "1405/02/01",
            ToJalali = "1405/01/01",
        };

        Result result = request.Validate(includePaging: false);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Validates_page_size_for_batch_list(int take)
    {
        DispatchOperationsRequest request = new DispatchOperationsRequest { Take = take };

        Result result = request.Validate(includePaging: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }
}
