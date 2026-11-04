using SmsHubNext.Features.DispatchOperations;
using SmsHubNext.Shared.Database;
using Xunit;

namespace SmsHubNext.UnitTests.Features.DispatchOperations;

public sealed class LiveMessageSearchTests
{
    [Theory]
    [InlineData("09131234567")]
    [InlineData("989131234567")]
    [InlineData("+989131234567")]
    [InlineData("00989131234567")]
    [InlineData("۰۹۱۳۱۲۳۴۵۶۷")]
    [InlineData("٠٩١٣١٢٣٤٥٦٧")]
    [InlineData(" ۰۹۱۳ ۱۲۳ ۴۵۶۷ ")]
    [InlineData("+98 (913) 123-4567")]
    public void National_and_international_phone_formats_find_the_same_stored_number(string input)
    {
        LiveMessageSearch search = LiveMessageSearch.Parse(input);
        Assert.True(search.HasSearch);
        Assert.True(search.IsValid);
        Assert.Equal("989131234567", search.MobileNumber);
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData(" ۴۲ ", 42L)]
    [InlineData("٤٢", 42L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void Message_ids_remain_typed_and_are_not_rewritten_as_phone_numbers(string input, long expected)
    {
        LiveMessageSearch search = LiveMessageSearch.Parse(input);
        Assert.True(search.IsValid);
        Assert.Equal(expected, search.MessageId);
        Assert.Null(search.MobileNumber);
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("%913%")]
    [InlineData("1 OR 1=1")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("9223372036854775808")]
    public void Invalid_search_never_turns_into_an_unfiltered_query(string input)
    {
        LiveMessageSearch search = LiveMessageSearch.Parse(input);
        Assert.True(search.HasSearch);
        Assert.False(search.IsValid);
    }

    [Fact]
    public void Empty_search_is_the_only_unfiltered_search()
    {
        LiveMessageSearch search = LiveMessageSearch.Parse("  ");
        Assert.False(search.HasSearch);
        Assert.True(search.IsValid);
    }

    [Fact]
    public async Task Invalid_search_and_invalid_paging_are_rejected_before_opening_a_database_connection()
    {
        LiveDispatchHandler handler = new(new Db("invalid connection string"), TimeProvider.System, new LiveDispatchCache());
        LiveMessageRequest[] requests =
        [
            new() { Search = "not a number" }, new() { PageSize = 1000 },
            new() { Before = new("1405/13/01", 1) }, new() { Before = new("1405/07/01", 0) },
            new() { Hours = 25 }, new() { Status = 255 }, new() { Provider = 0 }, new() { Batch = -1 },
        ];
        foreach (LiveMessageRequest request in requests)
            Assert.True((await handler.Messages(request, CancellationToken.None)).IsFailure);
    }
}
