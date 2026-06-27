using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

[ApiController]
[Route("api/messages")]
public sealed class SendMessagesController : ControllerBase
{
    private readonly SendMessagesHandler _handler;

    public SendMessagesController(SendMessagesHandler handler) => _handler = handler;

    /// <summary>Accept a batch of messages for asynchronous sending.</summary>
    [HttpPost]
    public IActionResult Send([FromBody] SendMessagesRequest request) =>
        _handler.Handle(request).ToActionResult(StatusCodes.Status202Accepted);
}
