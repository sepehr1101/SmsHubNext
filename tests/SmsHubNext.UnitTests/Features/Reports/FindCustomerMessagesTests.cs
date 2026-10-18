using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Reports;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Reports;

public sealed class FindCustomerMessagesTests
{
    [Fact]
    public void Bill_lookup_preserves_leading_zeroes_and_phone_lookup_normalizes_local_numbers()
    {
        Result<SearchMessagesRequest> bill = Request("۰۰۱۲۳", null).ToSearch();
        Assert.True(bill.IsSuccess);
        Assert.Equal(MessageSearchField.BillId, bill.Value.Field);
        Assert.Equal("00123", bill.Value.Value);
        Result<SearchMessagesRequest> phone = Request(null, "۰۹۱۲" + new string('۰', 6) + "۱").ToSearch();
        Assert.True(phone.IsSuccess);
        Assert.Equal(MessageSearchField.MobileNumber, phone.Value.Field);
        Assert.Equal("989120000001", phone.Value.Value);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", " ")]
    [InlineData("123", "09120000001")]
    public void Requires_exactly_one_identifier(string? bill, string? phone) =>
        Assert.Equal("reports.search_identifier_required", Request(bill, phone).ToSearch().Error!.Code);

    [Fact]
    public void Requires_complete_cursor_and_valid_dates()
    {
        Assert.True(new FindCustomerMessagesRequest { BillId = "123", FromJalali = "1405/07/01", ToJalali = "1405/07/30", BeforeDate = "1405/07/20" }.ToSearch().IsFailure);
        Assert.True(new FindCustomerMessagesRequest { BillId = "123", FromJalali = "1405/07/01", ToJalali = "1405/07/30", BeforeId = 20 }.ToSearch().IsFailure);
        Result<SearchMessagesRequest> next = new FindCustomerMessagesRequest
        {
            BillId = "123", FromJalali = "1405/07/01", ToJalali = "1405/07/30", BeforeDate = "1405/07/20", BeforeId = 25,
        }.ToSearch();
        Assert.True(next.IsSuccess);
        Assert.Equal(new MessageSearchCursor("1405/07/20", 25), next.Value.Before);
        Assert.True(new FindCustomerMessagesRequest { BillId = "123" }.ToSearch().IsFailure);
    }

    [Fact]
    public async Task Missing_key_is_unauthorized_before_query_validation_or_database_access()
    {
        Db db = new("Server=unused;Database=unused");
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        await using ServiceProvider provider = services.BuildServiceProvider();
        CustomerMessagesController controller = new(new ApiKeyAuthenticator(db, TimeProvider.System), new SearchMessagesHandler(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = provider } },
        };
        ObjectResult response = Assert.IsType<ObjectResult>(await controller.Find(new FindCustomerMessagesRequest(), CancellationToken.None));
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.Equal("auth.missing_key", Assert.IsType<ApiResponse<object>>(response.Value).Code);
    }

    [Fact]
    public async Task Customer_handler_rejects_administrator_keys_before_database_access()
    {
        SearchMessagesHandler handler = new(new Db("Server=unused;Database=unused"));
        Result<MessageSearchPage> result = await handler.HandleForCustomer(Request("123", null).ToSearch().Value,
            new ApiKeyIdentity(1, 1, "prefix", ApiKeyAccessLevel.Administrator), CancellationToken.None);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    private static FindCustomerMessagesRequest Request(string? bill, string? phone) => new()
    {
        BillId = bill, MobileNumber = phone, FromJalali = "۱۴۰۵/۰۷/۰۱", ToJalali = "۱۴۰۵/۰۷/۳۰",
    };
}
