using SmsHubNext.Features.Landing;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Landing;

public sealed class SaveFaqRequestTests
{
    [Theory]
    [InlineData("", "answer")]
    [InlineData("question", "  ")]
    public void Requires_question_and_answer(string question, string answer) =>
        Assert.True(new SaveFaqRequest { Question = question, Answer = answer }.Validate().IsFailure);

    [Fact]
    public void Enforces_storage_limits()
    {
        Assert.True(new SaveFaqRequest { Question = new string('q', 501), Answer = "a" }.Validate().IsFailure);
        Assert.True(new SaveFaqRequest { Question = "q", Answer = new string('a', 4001) }.Validate().IsFailure);
        Assert.True(new SaveFaqRequest { Question = new string('q', 500), Answer = new string('a', 4000) }.Validate().IsSuccess);
    }
}
