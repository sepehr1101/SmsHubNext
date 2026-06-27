using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Results;

public class ResultTests
{
    [Fact]
    public void Success_is_successful_and_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var error = Error.Validation("code", "message");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Generic_success_exposes_the_value()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        Result<int> result = Error.NotFound("missing", "not found");

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => { _ = result.Value; });
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Provider)]
    [InlineData(ErrorType.Unexpected)]
    public void Error_factories_set_the_matching_type(ErrorType type)
    {
        var error = type switch
        {
            ErrorType.Validation => Error.Validation("c", "m"),
            ErrorType.NotFound => Error.NotFound("c", "m"),
            ErrorType.Conflict => Error.Conflict("c", "m"),
            ErrorType.Unauthorized => Error.Unauthorized("c", "m"),
            ErrorType.Provider => Error.Provider("c", "m"),
            _ => Error.Unexpected("c", "m"),
        };

        Assert.Equal(type, error.Type);
    }
}
