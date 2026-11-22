using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Controllers;

[ApiController]
[Route("api/assistant")]
public class AssistantController : ControllerBase
{
    private const string AssistantSessionCookieName = "spike_ai_sid";

    private readonly IAiAssistantService _assistantService;
    private readonly GeminiOptions _options;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(
        IAiAssistantService assistantService,
        IOptions<GeminiOptions> options,
        ILogger<AssistantController>? logger = null)
    {
        _assistantService = assistantService;
        _options = options.Value;
        _logger = logger ?? NullLogger<AssistantController>.Instance;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var (isValid, errorCode, _) = _options.Validate();
        return Ok(new
        {
            ok = true,
            enabled = _options.Enabled,
            ready = isValid,
            statusCode = isValid ? "READY" : errorCode,
            welcomeMessage = "Xin chào! 🏓 Mình là Spike Assistant. Mình có thể giúp bạn tìm sân, xem giá, tìm sản phẩm và hướng dẫn sử dụng Spike OS.",
            privacyNotice = "Trợ lý AI dùng dữ liệu Spike OS. Không nhập mật khẩu hoặc mã OTP.",
            suggestedQuestions = GeminiAssistantService.GetDefaultSuggestions(false)
        });
    }

    [HttpPost("chat")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AssistantLimiter")]
    public async Task<IActionResult> Chat([FromBody] AssistantChatRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new
            {
                ok = false,
                errorCode = "EMPTY_MESSAGE",
                error = "Vui lòng nhập nội dung câu hỏi.",
                pose = "idle"
            });
        }

        var actor = BuildActorContext(request);
        string partitionKey = ResolveSessionPartitionKey(actor);

        var result = await _assistantService.ChatAsync(request, actor, partitionKey, HttpContext.RequestAborted);
        if (!result.Ok)
        {
            int statusCode = result.ErrorCode switch
            {
                "EMPTY_MESSAGE" or "MESSAGE_TOO_LONG" => StatusCodes.Status400BadRequest,
                "DUPLICATE_IN_FLIGHT" => StatusCodes.Status429TooManyRequests,
                "ASSISTANT_DISABLED" or "GEMINI_API_KEY_MISSING" or "GEMINI_INVALID_BASE_URL" or "GEMINI_MODEL_MISSING" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status503ServiceUnavailable
            };

            return StatusCode(statusCode, new
            {
                ok = false,
                errorCode = result.ErrorCode,
                error = result.Error,
                pose = result.Pose,
                conversationId = result.ConversationId,
                suggestedQuestions = result.SuggestedQuestions,
                links = result.Links
            });
        }

        return Ok(new
        {
            ok = true,
            reply = result.Reply,
            pose = result.Pose,
            conversationId = result.ConversationId,
            suggestedQuestions = result.SuggestedQuestions,
            links = result.Links,
            toolsUsed = result.ToolsUsed
        });
    }

    [HttpPost("reset")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AssistantLimiter")]
    public IActionResult Reset([FromBody] AssistantChatRequest? request)
    {
        var actor = BuildActorContext(request);
        string partitionKey = ResolveSessionPartitionKey(actor);
        _assistantService.ResetConversation(partitionKey, request?.ConversationId);

        return Ok(new
        {
            ok = true,
            pose = "wave",
            conversationId = "conv-" + Guid.NewGuid().ToString("N")[..12],
            suggestedQuestions = GeminiAssistantService.GetDefaultSuggestions(false)
        });
    }

    private AssistantActorContext BuildActorContext(AssistantChatRequest? request)
    {
        bool isLoggedIn = Function.IsLogin();
        string? headerGuestToken = Request.Headers["X-Booking-Access-Token"].FirstOrDefault();
        string? guestToken = !string.IsNullOrWhiteSpace(request?.GuestAccessToken)
            ? request!.GuestAccessToken!.Trim()
            : headerGuestToken?.Trim();

        return new AssistantActorContext
        {
            IsAuthenticated = isLoggedIn,
            AccountId = isLoggedIn ? Function._AccountId : null,
            FullName = isLoggedIn ? Function._FullName : null,
            GuestAccessToken = guestToken
        };
    }

    private string ResolveSessionPartitionKey(AssistantActorContext actor)
    {
        if (actor.IsAuthenticated && actor.AccountId is > 0)
        {
            return $"acc:{actor.AccountId.Value}";
        }

        if (Request.Cookies.TryGetValue(AssistantSessionCookieName, out var existingSid) &&
            !string.IsNullOrWhiteSpace(existingSid) &&
            Regex.IsMatch(existingSid, @"^[A-Za-z0-9_-]{16,48}$"))
        {
            return $"guest:{existingSid}";
        }

        string newSid = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(AssistantSessionCookieName, newSid, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddHours(4)
        });

        return $"guest:{newSid}";
    }
}
