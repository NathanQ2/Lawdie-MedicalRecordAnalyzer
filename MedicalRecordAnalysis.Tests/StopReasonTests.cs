using Anthropic.Models.Messages;
using MedicalRecordAnalysis.LLM.Providers;

namespace MedicalRecordAnalysis.Tests;

public class StopReasonTests
{
    [Fact]
    public void AcceptsResponsesThatFinished()
    {
        AnthropicProvider.CheckComplete(StopReason.EndTurn);
    }

    [Fact]
    public void RejectsResponsesCutOffByTheTokenLimit()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AnthropicProvider.CheckComplete(StopReason.MaxTokens));

        Assert.Contains("token limit", exception.Message);
    }

    [Fact]
    public void RejectsDeclinedResponses()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AnthropicProvider.CheckComplete(StopReason.Refusal));

        Assert.Contains("declined", exception.Message);
    }
}
