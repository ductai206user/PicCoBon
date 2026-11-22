using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Services;

public sealed class DemoAiAssistantService : IAiAssistantService
{
    private readonly PickleballContext _context;
    private readonly ClubSettingsService _settingsService;

    public DemoAiAssistantService(PickleballContext context, ClubSettingsService settingsService)
    {
        _context = context;
        _settingsService = settingsService;
    }

    public async Task<AssistantChatResponse> ChatAsync(
        AssistantChatRequest request,
        AssistantActorContext actor,
        string sessionPartitionKey,
        CancellationToken cancellationToken = default)
    {
        string raw = (request.Message ?? string.Empty).Trim();
        string q = raw.ToLowerInvariant();
        string convId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? "demo-conv-" + Guid.NewGuid().ToString("N")[..8]
            : request.ConversationId;

        var settings = await _settingsService.GetSettingsAsync(_context);
        var courts = await _context.TbCourts.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.CourtId).ToListAsync(cancellationToken);
        var products = await _context.TbProducts.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.ProductId).Take(5).ToListAsync(cancellationToken);

        string reply;
        string pose;
        List<AssistantLinkDto> links;

        if (q.Contains("giá") || q.Contains("bao nhiêu") || q.Contains("bảng giá") || q.Contains("giờ vàng") || q.Contains("hội viên"))
        {
            pose = "idea";
            int peakTotal = settings.BaseCourtPrice + settings.PeakSurchargePerHour;
            reply = $"**[AI DEMO — Phản hồi giả lập]**\n" +
                    $"**Bảng giá niêm yết trên Spike OS (Demo):**\n" +
                    $"• Giờ tiêu chuẩn ({settings.OpenTime} – {settings.PeakStart}): **{settings.BaseCourtPrice:N0} đ/giờ**\n" +
                    $"• Giờ vàng ({settings.PeakStart} – {settings.CloseTime}): **{peakTotal:N0} đ/giờ** (+{settings.PeakSurchargePerHour:N0} đ/giờ)\n" +
                    $"• Sân Indoor VIP phụ thu thêm **20.000 đ/giờ**.\n" +
                    $"• Ưu đãi thẻ Hội viên Demo: Bạc (-5%), Vàng VIP (-15%).";
            links = new()
            {
                new() { Label = "Đặt sân & xem báo giá", Url = "/Booking" },
                new() { Label = "Hồ sơ hội viên Demo", Url = "/Account/Profile" }
            };
        }
        else if (q.Contains("trống") || q.Contains("sân") || q.Contains("19:00") || q.Contains("18:00") || q.Contains("lịch"))
        {
            pose = "thinking";
            string courtList = string.Join("\n", courts.Select(c => $"• **{c.CourtName}** ({c.TypeLabel ?? c.CourtType})"));
            reply = $"**[AI DEMO — Phản hồi giả lập]**\n" +
                    $"CLB hiện hiển thị **{courts.Count} sân mẫu** hoạt động từ **{settings.OpenTime} – {settings.CloseTime}**:\n{courtList}\n" +
                    $"Bạn có thể mở trang Đặt sân để xem timeline trống từng block 30 phút (các thao tác đặt thật đã được tắt trong DemoMode).";
            links = new()
            {
                new() { Label = "Xem ma trận sân", Url = "/Booking" },
                new() { Label = "Tra cứu vé mẫu", Url = "/Lookup" }
            };
        }
        else if (q.Contains("pro shop") || q.Contains("sản phẩm") || q.Contains("nước") || q.Contains("vợt") || q.Contains("bóng"))
        {
            pose = "happy";
            string sample = string.Join("\n", products.Select(p => $"• **{p.Title}** — {(p.Price ?? 0):N0} đ (Còn {p.UnitInStock ?? 0})"));
            reply = $"**[AI DEMO — Phản hồi giả lập]**\n" +
                    $"Một số mặt hàng tiêu biểu tại **Pro Shop (Dữ liệu mẫu)**:\n{sample}\n" +
                    $"Bạn có thể lọc danh mục và thêm vào giỏ hàng xem trước tại trang Pro Shop.";
            links = new()
            {
                new() { Label = "Mở Pro Shop", Url = "/Product" },
                new() { Label = "Trang chủ", Url = "/" }
            };
        }
        else if (q.Contains("qr") || q.Contains("vé") || q.Contains("đặt") || q.Contains("paypal") || q.Contains("check-in"))
        {
            pose = "idea";
            reply = $"**[AI DEMO — Phản hồi giả lập]**\n" +
                    $"Trong **DemoMode**, hệ thống dùng dữ liệu giả lập `InMemory` và **đã vô hiệu hóa** đặt sân thật, thanh toán PayPal thật, hoàn tiền và check-in thật.\n" +
                    $"Bạn vẫn có thể xem trước mã vé mẫu `PB-DEMO02` tại trang **Tra cứu vé QR** hoặc chuyển sang vai trò **Staff / Admin Demo** để xem giao diện quản trị.";
            links = new()
            {
                new() { Label = "Tra cứu vé QR mẫu", Url = "/Lookup" },
                new() { Label = "Đặt sân trực tuyến", Url = "/Booking" }
            };
        }
        else
        {
            pose = "chat";
            reply = $"**[AI DEMO — Phản hồi giả lập]**\n" +
                    $"Mình đang chạy ở chế độ xem trước không gọi Gemini API (`\"{raw}\"`).\n" +
                    $"Hãy hỏi mình về **sân trống**, **bảng giá**, **Pro Shop**, hoặc **vé QR mẫu** — hoặc dùng các nút gợi ý bên dưới nhé!";
            links = new()
            {
                new() { Label = "Đặt sân", Url = "/Booking" },
                new() { Label = "Pro Shop", Url = "/Product" },
                new() { Label = "Tra cứu vé", Url = "/Lookup" }
            };
        }

        return new AssistantChatResponse
        {
            Ok = true,
            Reply = reply,
            Pose = pose,
            ConversationId = convId,
            SuggestedQuestions = new List<string>(GeminiAssistantService.GetDefaultSuggestions(actor.IsAuthenticated)),
            Links = links,
            ToolsUsed = new List<string> { "demo_mock_responder" }
        };
    }

    public void ResetConversation(string sessionPartitionKey, string? conversationId)
    {
        // Stateless simulated chat in DemoMode
    }
}
