using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PickleballClub.Services;

public sealed class GeminiFunctionCall
{
    public string Name { get; init; } = string.Empty;
    public JsonElement Args { get; init; }
    public JsonElement? RawPart { get; init; }
}

public sealed class GeminiClientResult
{
    public bool Success { get; init; }
    public int HttpStatusCode { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Text { get; init; }
    public List<GeminiFunctionCall> FunctionCalls { get; init; } = new();
    public JsonElement? ModelContentElement { get; init; }
}

public interface IGeminiClient
{
    Task<GeminiClientResult> GenerateAsync(
        string systemInstruction,
        IReadOnlyList<object> contents,
        object? toolsDeclaration,
        CancellationToken cancellationToken = default);
}

public sealed class AssistantLinkDto
{
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed class AssistantChatRequest
{
    public string Message { get; init; } = string.Empty;
    public string? ConversationId { get; init; }
    public string? BookingCode { get; init; }
    public string? GuestAccessToken { get; init; }
}

public sealed class AssistantChatResponse
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
    public string Reply { get; init; } = string.Empty;
    public string Pose { get; init; } = "idle";
    public string ConversationId { get; init; } = string.Empty;
    public List<string> SuggestedQuestions { get; init; } = new();
    public List<AssistantLinkDto> Links { get; init; } = new();
    public List<string> ToolsUsed { get; init; } = new();
}

public interface IAiAssistantService
{
    Task<AssistantChatResponse> ChatAsync(
        AssistantChatRequest request,
        AssistantActorContext actor,
        string sessionPartitionKey,
        CancellationToken cancellationToken = default);

    void ResetConversation(string sessionPartitionKey, string? conversationId);
}
