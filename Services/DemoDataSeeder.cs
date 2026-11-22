using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Services;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(
        PickleballContext db,
        PasswordService passwordService,
        QrTokenService qrTokenService,
        ILogger logger)
    {
        if (await db.TbCourts.AnyAsync())
        {
            return;
        }

        DateTime vnNow = BookingService.GetVietnamNow();
        string today = vnNow.ToString("yyyy-MM-dd");
        string tomorrow = vnNow.AddDays(1).ToString("yyyy-MM-dd");
        string yesterday = vnNow.AddDays(-1).ToString("yyyy-MM-dd");

        // 1. Roles
        db.TbRoles.AddRange(
            new TbRole { RoleId = 1, RoleName = "Admin" },
            new TbRole { RoleId = 2, RoleName = "Customer" },
            new TbRole { RoleId = 3, RoleName = "Staff" }
        );

        // 2. Order Statuses
        db.TbOrderStatuses.AddRange(
            new TbOrderStatus { OrderStatusId = 1, Name = "Chờ xác nhận", Description = "Đơn mới chờ xác nhận" },
            new TbOrderStatus { OrderStatusId = 2, Name = "Đã xác nhận", Description = "Đã xác nhận lịch" },
            new TbOrderStatus { OrderStatusId = 3, Name = "Đang thi đấu", Description = "Khách đã check-in vào sân" },
            new TbOrderStatus { OrderStatusId = 4, Name = "Hoàn thành", Description = "Đã hoàn tất ca đấu" },
            new TbOrderStatus { OrderStatusId = 5, Name = "Đã hủy", Description = "Đơn đã hủy" }
        );

        // 3. Top Navigation Menus
        db.TbMenus.AddRange(
            new TbMenu { MenuId = 1, Title = "Trang chủ", Alias = "", Levels = 1, ParentId = 0, Position = 1, IsActive = true },
            new TbMenu { MenuId = 2, Title = "Đặt sân", Alias = "Booking", Levels = 1, ParentId = 0, Position = 2, IsActive = true },
            new TbMenu { MenuId = 3, Title = "Pro Shop", Alias = "Product", Levels = 1, ParentId = 0, Position = 3, IsActive = true },
            new TbMenu { MenuId = 4, Title = "Tra cứu vé", Alias = "Lookup", Levels = 1, ParentId = 0, Position = 4, IsActive = true }
        );

        // 4. CMS Configs & Pricing
        var cmsEntries = new (string Key, string Value, string Desc)[]
        {
            ("clubName", "SPIKE PICKLEBALL CLUB (DEMO)", "Tên câu lạc bộ"),
            ("heroTag", "CỤM SÂN PICKLEBALL TIÊU CHUẨN THI ĐẤU • DEMO", "Nhãn nổi bật trang chủ"),
            ("heroSubtag", "Mặt sân Cushion giảm chấn · Bản xem trước không kết nối CSDL thật", "Dòng phụ trang chủ"),
            ("heroTitle", "Đặt sân Pickleball rõ lịch, đúng giá chỉ trong 30 giây.", "Tiêu đề chính"),
            ("heroSubtitle", "Tổ hợp 4 sân trong nhà và ngoài trời mặt thảm Acrylic Cushion 8 lớp, đèn LED chống chói và vé QR mẫu.", "Mô tả chi tiết"),
            ("courtPrice", "120000", "Đơn giá tiêu chuẩn"),
            ("peakSurcharge", "30000", "Phụ thu giờ vàng"),
            ("peakStart", "17:00", "Giờ bắt đầu giờ vàng"),
            ("openTime", "06:00", "Giờ mở cửa"),
            ("closeTime", "22:00", "Giờ đóng cửa"),
            ("hotline", "0901 234 567", "Hotline mẫu"),
            ("address", "Khu Thảo Điền, TP. Thủ Đức, TP.HCM", "Địa chỉ mẫu"),
            ("clubTitle", "Đặc Quyền Hội Viên CLB (Demo)", "Tiêu đề hội viên"),
            ("clubDesc", "Tích lũy chi tiêu mô phỏng để xem trước hạng thẻ Bạc (-5%), Vàng (-10%), VIP (-15%).", "Mô tả hội viên"),
            ("checkInEarlyMinutes", "30", "Số phút check-in sớm"),
            ("cancelCutoffMinutes", "120", "Giới hạn tự hủy"),
            ("pendingHoldMinutes", "30", "Thời gian giữ chỗ")
        };
        foreach (var (k, v, d) in cmsEntries)
        {
            db.TbCmsConfigs.Add(new TbCmsConfig
            {
                ConfigKey = k,
                ConfigValue = v,
                Description = d,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "DemoSeeder"
            });
        }

        // 5. Courts (4 courts)
        var c1 = new TbCourt
        {
            CourtId = 1,
            CourtName = "Sân VIP 01 — Trong nhà",
            Alias = "san-vip-01",
            CourtType = "Indoor",
            TypeLabel = "Indoor VIP",
            Surface = "Acrylic Cushion 8 lớp + Mái vòm",
            Image = "/assets/court-vip-arena.png",
            BasePrice = 120000,
            Surcharge = 20000,
            Description = "Sân trung tâm mái vòm chống nắng mưa, đèn LED chống chói chuẩn thi đấu USAPA.",
            IsActive = true
        };
        var c2 = new TbCourt
        {
            CourtId = 2,
            CourtName = "Sân VIP 02 — Trong nhà",
            Alias = "san-vip-02",
            CourtType = "Indoor",
            TypeLabel = "Indoor VIP",
            Surface = "Acrylic Cushion 8 lớp + Mái vòm",
            Image = "/assets/court-vip-arena.png",
            BasePrice = 120000,
            Surcharge = 20000,
            Description = "Khoảng lùi cuối sân rộng 3m, phù hợp thi đấu đôi và quay highlight.",
            IsActive = true
        };
        var c3 = new TbCourt
        {
            CourtId = 3,
            CourtName = "Sân Chuẩn 03 — Ngoài trời",
            Alias = "san-chuan-03",
            CourtType = "Outdoor",
            TypeLabel = "Outdoor Pro",
            Surface = "Laykold Pro Acrylic",
            Image = "/assets/hero-spike-pickleball.png",
            BasePrice = 120000,
            Surcharge = 0,
            Description = "Sân ngoài trời thoáng gió, lưới chắn gió quanh viền và ghế chờ mát mẻ.",
            IsActive = true
        };
        var c4 = new TbCourt
        {
            CourtId = 4,
            CourtName = "Sân Chuẩn 04 — Ngoài trời",
            Alias = "san-chuan-04",
            CourtType = "Outdoor",
            TypeLabel = "Outdoor Pro",
            Surface = "Laykold Pro Acrylic",
            Image = "/assets/hero-spike-pickleball.png",
            BasePrice = 120000,
            Surcharge = 0,
            Description = "Không gian lý tưởng cho giao lưu cộng đồng và tập luyện buổi chiều tối.",
            IsActive = true
        };
        db.TbCourts.AddRange(c1, c2, c3, c4);

        // 6. Product Categories & Pro Shop Products (using real files in wwwroot/uploads/products/)
        db.TbProductCategories.AddRange(
            new TbProductCategory { CategoryProductId = 1, Title = "Đồ uống", Alias = "drink", IsActive = true },
            new TbProductCategory { CategoryProductId = 2, Title = "Đồ ăn", Alias = "food", IsActive = true },
            new TbProductCategory { CategoryProductId = 3, Title = "Phụ kiện", Alias = "other", IsActive = true }
        );

        var demoProducts = new TbProduct[]
        {
            new() { ProductId = 1, Title = "Nước khoáng Lavie 500ml", Alias = "lavie-500ml", CategoryProductId = 1, Description = "Nước khoáng thiên nhiên ướp lạnh", Image = "/uploads/products/1ebf8fe0bd0b4a8ab5d8350fe2e50a97.webp", Price = 10000, PriceDec = 10000, Quantity = 96, UnitInStock = 96, IsActive = true, IsBestSeller = false },
            new() { ProductId = 2, Title = "Bù điện giải Revive Chanh Muối", Alias = "revive-chanh-muoi", CategoryProductId = 1, Description = "Bù nước & ion tức thì giữa set đấu", Image = "/uploads/products/1cc9f7b37cc44af3be722cf01fc3d3b3.webp", Price = 15000, PriceDec = 15000, Quantity = 64, UnitInStock = 64, IsActive = true, IsBestSeller = true },
            new() { ProductId = 3, Title = "Trà xanh Ô Long Tea+ 450ml", Alias = "olong-tea-plus", CategoryProductId = 1, Description = "Thanh mát giải nhiệt sau trận đấu", Image = "/uploads/products/3ecf116c21c644449744e820dd01f03e.webp", Price = 18000, PriceDec = 18000, Quantity = 42, UnitInStock = 42, IsActive = true },
            new() { ProductId = 4, Title = "Nước tăng lực RedBull Lon 250ml", Alias = "redbull-250ml", CategoryProductId = 1, Description = "Tăng tập trung cho các trận đấu tối", Image = "/uploads/products/455f2a0677fa465bb0d0143ee465f7d0.webp", Price = 20000, PriceDec = 20000, Quantity = 38, UnitInStock = 38, IsActive = true },
            new() { ProductId = 5, Title = "Monster Energy Ultra White", Alias = "monster-ultra", CategoryProductId = 1, Description = "Không đường, vị cam chanh mát lạnh", Image = "/uploads/products/sku-05-monster.png", Price = 35000, PriceDec = 35000, Quantity = 24, UnitInStock = 24, IsActive = true },
            new() { ProductId = 6, Title = "Chuối tươi Đà Lạt (2 trái)", Alias = "chuoi-da-lat", CategoryProductId = 2, Description = "Bổ sung Kali chống chuột rút tự nhiên", Image = "/uploads/products/5c4732df2473465ea67bd564b76d09ae.webp", Price = 15000, PriceDec = 15000, Quantity = 30, UnitInStock = 30, IsActive = true },
            new() { ProductId = 7, Title = "Thanh năng lượng Protein Bar", Alias = "protein-bar", CategoryProductId = 2, Description = "20g Protein phục hồi cơ nhanh", Image = "/uploads/products/7c5ff589d64d421db1882da38d9fe56a.webp", Price = 45000, PriceDec = 45000, Quantity = 28, UnitInStock = 28, IsActive = true, IsBestSeller = true },
            new() { ProductId = 8, Title = "Bánh Sandwich Gà Xé Sốt Mè", Alias = "sandwich-ga-xe", CategoryProductId = 2, Description = "Nạp năng lượng nhẹ trước giờ thi đấu", Image = "/uploads/products/sku-08-sandwich.png", Price = 42000, PriceDec = 42000, Quantity = 16, UnitInStock = 16, IsActive = true },
            new() { ProductId = 9, Title = "Thuê vợt Carbon T700 Pro", Alias = "thue-vot-t700", CategoryProductId = 3, Description = "Mặt nhám Carbon T700 16mm (theo ca)", Image = "/uploads/products/7fbf01d462d74a9397e5a9ac6430605f.webp", Price = 50000, PriceDec = 50000, Quantity = 14, UnitInStock = 14, IsActive = true, IsBestSeller = true },
            new() { ProductId = 10, Title = "Quấn cán vợt Joola Feel-Tec", Alias = "quan-can-joola", CategoryProductId = 3, Description = "Thấm hút mồ hôi, bám tay chắc chắn", Image = "/uploads/products/sku-10-joola-grip.png", Price = 35000, PriceDec = 35000, Quantity = 45, UnitInStock = 45, IsActive = true },
            new() { ProductId = 11, Title = "Ống 3 bóng Franklin X-40 USAPA", Alias = "bong-franklin-x40", CategoryProductId = 3, Description = "Bóng thi đấu chính thức 40 lỗ ngoài trời", Image = "/uploads/products/9d9df3b023f349b5a6764977db173cdb.webp", Price = 165000, PriceDec = 165000, Quantity = 22, UnitInStock = 22, IsActive = true },
            new() { ProductId = 12, Title = "Khăn lạnh thể thao Microfiber", Alias = "khan-the-thao", CategoryProductId = 3, Description = "Mềm mịn, giữ mát lâu khi thi đấu", Image = "/uploads/products/b17f1a7f1c264970ab81f385e3545d2e.webp", Price = 25000, PriceDec = 25000, Quantity = 34, UnitInStock = 34, IsActive = true },
            new() { ProductId = 13, Title = "Băng đô & Băng cổ tay Spike Pro", Alias = "bang-co-tay-spike", CategoryProductId = 3, Description = "Combo thấm mồ hôi cổ tay co giãn", Image = "/uploads/products/c9abce01398a494aab29cc5a0c359b68.webp", Price = 55000, PriceDec = 55000, Quantity = 19, UnitInStock = 19, IsActive = true },
            new() { ProductId = 14, Title = "Hộp 6 bóng thi đấu Pro Outdoor", Alias = "hop-6-bong-pro", CategoryProductId = 3, Description = "Bền bỉ, độ nảy chuẩn giải CLB", Image = "/uploads/products/fd1d0411c69c486a90693bcdcdc01a12.webp", Price = 290000, PriceDec = 290000, Quantity = 12, UnitInStock = 12, IsActive = true }
        };
        db.TbProducts.AddRange(demoProducts);

        // 7. Synthetic Demo Accounts ONLY (ephemeral non-guessable password hashes, zero real user data)
        string ephemeralSeedSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var demoAdmin = new TbAccount
        {
            AccountId = 1,
            Username = "demo_admin",
            FullName = "Quản trị Demo (Admin)",
            Phone = "0900000001",
            Email = "demo-admin@example.invalid",
            Avatar = "/assets/avatars/ace.png",
            MemberTier = "Hội viên Vàng VIP",
            SkillLevel = "Bán chuyên (DUPR 4.0+)",
            DominantHand = "Tay phải",
            PaddleBrand = "Joola Perseus 16mm",
            PlayStyle = "Đánh đôi phối hợp",
            Address = "Thảo Điền, TP. Thủ Đức (Demo)",
            Bio = "Tài khoản mô phỏng vai trò Quản trị viên CLB trong DemoMode.",
            RoleId = 1,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        demoAdmin.Password = passwordService.HashPassword(demoAdmin, ephemeralSeedSecret + "-admin");

        var demoStaff = new TbAccount
        {
            AccountId = 2,
            Username = "demo_staff",
            FullName = "Nhân viên Demo (Staff)",
            Phone = "0900000002",
            Email = "demo-staff@example.invalid",
            Avatar = "/assets/avatars/sunny.png",
            MemberTier = "Hội viên Bạc",
            SkillLevel = "Giao lưu (DUPR 3.0)",
            DominantHand = "Tay phải",
            PaddleBrand = "Selkirk Vanguard Control",
            PlayStyle = "Kiểm soát bếp & Dink",
            Address = "An Phú, TP. Thủ Đức (Demo)",
            Bio = "Tài khoản mô phỏng nhân viên lễ tân / POS trong DemoMode.",
            RoleId = 3,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        demoStaff.Password = passwordService.HashPassword(demoStaff, ephemeralSeedSecret + "-staff");

        var demoMember = new TbAccount
        {
            AccountId = 101,
            Username = "demo_member",
            FullName = "Nguyễn Minh Khôi (Demo)",
            Phone = "0909123456",
            Email = "demo-member@example.invalid",
            Avatar = "/assets/avatars/leo.png",
            MemberTier = "Hội viên Vàng VIP",
            SkillLevel = "Khá (DUPR 3.5+)",
            DominantHand = "Tay phải",
            PaddleBrand = "Joola Perseus 16mm",
            PlayStyle = "Tấn công tốc độ",
            Address = "Thảo Điền, TP. Thủ Đức (Demo)",
            Bio = "Tài khoản Hội viên mẫu để xem trước hồ sơ, vé QR và lịch sử đặt sân.",
            RoleId = 2,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        demoMember.Password = passwordService.HashPassword(demoMember, ephemeralSeedSecret + "-member");

        var demoMember2 = new TbAccount
        {
            AccountId = 102,
            Username = "demo_member2",
            FullName = "Trần Thảo Vy (Demo)",
            Phone = "0912345678",
            Email = "demo-member2@example.invalid",
            Avatar = "/assets/avatars/maya.png",
            MemberTier = "Hội viên Bạc",
            SkillLevel = "Giao lưu (DUPR 3.0)",
            DominantHand = "Tay phải",
            PaddleBrand = "Six Zero DBD 16mm",
            PlayStyle = "Kiểm soát bếp & Dink",
            Address = "Bình Thạnh, TP.HCM (Demo)",
            RoleId = 2,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        demoMember2.Password = passwordService.HashPassword(demoMember2, ephemeralSeedSecret + "-member2");

        db.TbAccounts.AddRange(demoAdmin, demoStaff, demoMember, demoMember2);

        // 8. Sample Bookings (relative to today Vietnam timezone)
        DateTime expSoon = DateTime.UtcNow.AddDays(2);
        var bookings = new List<TbBooking>
        {
            new()
            {
                BookingId = 1, BookingCode = "PB-DEMO01", CourtId = 1, AccountId = 101,
                CustomerName = "Nguyễn Minh Khôi (Demo)", Phone = "0909123456",
                BookingDate = today, StartTime = "08:00", Duration = 90, Players = 4,
                SkillLevel = "Khá (DUPR 3.5+)", PaymentMethod = "cash",
                CourtFee = 210000, DiscountAmount = 31500, PosTotal = 45000, AmountDue = 223500, PaidAmount = 223500, TotalPaid = 223500,
                PaymentStatus = "paid", Status = "completed",
                PosItemsJson = "[{\"id\":2,\"name\":\"Bù điện giải Revive Chanh Muối\",\"qty\":3,\"price\":15000}]",
                CheckInToken = qrTokenService.GenerateCheckInToken("PB-DEMO01", today, expSoon),
                CreatedDate = DateTime.UtcNow.AddHours(-4)
            },
            new()
            {
                BookingId = 2, BookingCode = "PB-DEMO02", CourtId = 2, AccountId = 102,
                CustomerName = "Trần Thảo Vy (Demo)", Phone = "0912345678",
                BookingDate = today, StartTime = "17:00", Duration = 60, Players = 4,
                SkillLevel = "Giao lưu (DUPR 3.0)", PaymentMethod = "paypal",
                CourtFee = 170000, DiscountAmount = 8500, PosTotal = 0, AmountDue = 161500, PaidAmount = 161500, TotalPaid = 161500,
                PaymentStatus = "paid", Status = "approved",
                CheckInToken = qrTokenService.GenerateCheckInToken("PB-DEMO02", today, expSoon),
                CreatedDate = DateTime.UtcNow.AddHours(-2)
            },
            new()
            {
                BookingId = 3, BookingCode = "PB-DEMO03", CourtId = 3, AccountId = null,
                CustomerName = "Lê Hoàng Nam (Demo)", Phone = "0938765432",
                BookingDate = today, StartTime = "19:00", Duration = 60, Players = 4,
                SkillLevel = "Bán chuyên", PaymentMethod = "cash",
                CourtFee = 150000, DiscountAmount = 0, PosTotal = 0, AmountDue = 150000, PaidAmount = 0, TotalPaid = 0,
                PaymentStatus = "unpaid", Status = "pending",
                HoldExpiresAtUtc = DateTime.UtcNow.AddMinutes(25),
                CheckInToken = qrTokenService.GenerateCheckInToken("PB-DEMO03", today, expSoon),
                CreatedDate = DateTime.UtcNow.AddMinutes(-20)
            },
            new()
            {
                BookingId = 4, BookingCode = "PB-DEMO04", CourtId = 1, AccountId = 101,
                CustomerName = "Nguyễn Minh Khôi (Demo)", Phone = "0909123456",
                BookingDate = tomorrow, StartTime = "18:00", Duration = 90, Players = 4,
                SkillLevel = "Khá (DUPR 3.5+)", PaymentMethod = "paypal",
                CourtFee = 255000, DiscountAmount = 38250, PosTotal = 0, AmountDue = 216750, PaidAmount = 216750, TotalPaid = 216750,
                PaymentStatus = "paid", Status = "approved",
                CheckInToken = qrTokenService.GenerateCheckInToken("PB-DEMO04", tomorrow, expSoon),
                CreatedDate = DateTime.UtcNow.AddMinutes(-45)
            },
            new()
            {
                BookingId = 5, BookingCode = "PB-DEMO05", CourtId = 4, AccountId = 102,
                CustomerName = "Trần Thảo Vy (Demo)", Phone = "0912345678",
                BookingDate = yesterday, StartTime = "16:00", Duration = 60, Players = 2,
                SkillLevel = "Giao lưu (DUPR 3.0)", PaymentMethod = "cash",
                CourtFee = 120000, DiscountAmount = 6000, PosTotal = 0, AmountDue = 114000, PaidAmount = 114000, TotalPaid = 114000,
                PaymentStatus = "paid", Status = "completed",
                CheckInToken = qrTokenService.GenerateCheckInToken("PB-DEMO05", yesterday, expSoon),
                CreatedDate = DateTime.UtcNow.AddDays(-1)
            }
        };
        db.TbBookings.AddRange(bookings);

        // 9. Sample Court Lock
        db.TbCourtLocks.Add(new TbCourtLock
        {
            LockId = 1,
            LockCode = "LCK-DEMO01",
            CourtId = 4,
            LockDate = today,
            StartTime = "13:00",
            Duration = 60,
            Reason = "Bảo trì lưới & vệ sinh mặt sân định kỳ (Demo)",
            CreatedDate = DateTime.UtcNow.AddHours(-3)
        });

        // 10. Sample Pro Shop Order & Ledger Transactions
        db.TbOrders.Add(new TbOrder
        {
            OrderId = 1,
            Code = "ORD-DEMO01",
            AccountId = 101,
            CustomerName = "Nguyễn Minh Khôi (Demo)",
            Phone = "0909123456",
            Address = "Tại quầy Pro Shop CLB (Demo)",
            TotalAmount = 195000,
            TotalAmountDec = 195000,
            PaidAmount = 195000,
            PaidAmountDec = 195000,
            PaymentStatus = "paid",
            Status = "completed",
            FulfillmentStatus = "delivered",
            PaymentMethod = "cash",
            Quanlity = 2,
            CreatedDate = DateTime.UtcNow.AddHours(-1)
        });
        db.TbOrderDetails.AddRange(
            new TbOrderDetail { OrderDetailId = 1, OrderId = 1, ProductId = 11, Price = 165000, Quantity = 1 },
            new TbOrderDetail { OrderDetailId = 2, OrderId = 1, ProductId = 2, Price = 15000, Quantity = 2 }
        );

        db.TbPaymentTransactions.AddRange(
            new TbPaymentTransaction
            {
                TransactionId = 1, BookingId = 1, BookingCode = "PB-DEMO01",
                Amount = 223500, AmountDec = 223500, Currency = "VND",
                TransactionType = "payment", PaymentMethod = "cash", Status = "completed",
                ReferenceCode = "DEMO-TX-01", Note = "Thu tiền ca sáng (Demo)",
                RecordedBy = "Nhân viên Demo", CreatedAt = DateTime.UtcNow.AddHours(-3)
            },
            new TbPaymentTransaction
            {
                TransactionId = 2, BookingId = 2, BookingCode = "PB-DEMO02",
                Amount = 161500, AmountDec = 161500, Currency = "VND",
                TransactionType = "payment", PaymentMethod = "paypal", Status = "completed",
                ReferenceCode = "DEMO-PP-01", Note = "PayPal Sandbox Demo Ledger",
                RecordedBy = "Demo System", CreatedAt = DateTime.UtcNow.AddHours(-2)
            },
            new TbPaymentTransaction
            {
                TransactionId = 3, OrderId = 1, BookingCode = "ORD-DEMO01",
                Amount = 195000, AmountDec = 195000, Currency = "VND",
                TransactionType = "payment", PaymentMethod = "cash", Status = "completed",
                ReferenceCode = "DEMO-ORD-01", Note = "Đơn Pro Shop tại quầy (Demo)",
                RecordedBy = "Nhân viên Demo", CreatedAt = DateTime.UtcNow.AddHours(-1)
            }
        );

        await db.SaveChangesAsync();
        logger.LogInformation("DemoMode InMemory sample dataset seeded (Courts=4, Products={ProductCount}, Bookings={BookingCount})",
            demoProducts.Length, bookings.Count);
    }
}
