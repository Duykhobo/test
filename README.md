# LegacyVault Backend — Bước 1: Đăng nhập / Đăng xuất

ASP.NET Core Web API (.NET 10) theo kiến trúc 4 lớp của nhóm. Bước này chỉ làm **xác thực**: đăng nhập, xem phiên, làm mới phiên, đăng xuất. Các module khác (Vaults, Assets, …) sẽ thêm dần theo đúng khuôn này.

Đọc theo thứ tự: chạy được trước (mục 1) → hiểu cấu trúc (mục 2) → lần theo một request (mục 3) → tự làm bài tập (mục 6).

---

## 1. Chạy thử

Cần: [.NET 10 SDK](https://dotnet.microsoft.com/download), Docker Desktop.

```bash
# 1) Bật SQL Server
docker compose up -d

# 2) Cài công cụ migration của EF Core (một lần trên mỗi máy)
dotnet tool install --global dotnet-ef

# 3) Đặt khóa ký JWT vào kho bí mật cục bộ (KHÔNG nằm trong git)
cd src/LegacyVault.Api
dotnet user-secrets set "Jwt:SigningKey" "thay-bang-chuoi-ngau-nhien-dai-it-nhat-32-ky-tu"
cd ../..

# 4) Tạo migration đầu tiên (sinh code tạo bảng Users, RefreshTokens)
dotnet ef migrations add InitialCreate \
  --project src/LegacyVault.Infrastructure \
  --startup-project src/LegacyVault.Api \
  --output-dir Persistence/Migrations

# 5) Chạy API → http://localhost:5000
dotnet run --project src/LegacyVault.Api
```

Lần chạy đầu, API tự áp migration và tạo 3 tài khoản demo (mật khẩu chung `Password123!`):

| Email | Role | Ghi chú |
| --- | --- | --- |
| `owner@legacyvault.local` | OWNER | Tài khoản thường |
| `admin@legacyvault.local` | ADMIN | |
| `inactive@legacyvault.local` | OWNER | Bị vô hiệu → đăng nhập nhận 403 |

Thử API: mở `http/auth.http` trong VS Code (extension REST Client), Rider hoặc Visual Studio và bấm "Send Request" từng mục. Hoặc chạy FE (`npm run dev` ở cổng 5173) và đăng nhập trên giao diện.

**Lỗi hay gặp**
- `Jwt:SigningKey phải dài ít nhất 32 ký tự` → chưa làm bước 3.
- `pending changes … Add a new migration` → chưa làm bước 4, hoặc vừa sửa entity mà chưa tạo migration mới.
- `A network-related or instance-specific error` → SQL Server chưa chạy (bước 1), hoặc mật khẩu không khớp `appsettings.Development.json`.

---

## 2. Cấu trúc

```
legacyvault-be/
├── Directory.Build.props        Cấu hình chung cho mọi project (.NET 10, nullable)
├── Directory.Packages.props     Version NuGet khai báo một chỗ
├── docker-compose.yml           SQL Server cho dev
├── http/auth.http               Kịch bản thử API bằng tay
└── src/
    ├── LegacyVault.Domain/              ① LÕI — không phụ thuộc gì
    │   ├── Common/                      Entity, Result, Error
    │   └── Identity/                    User, RefreshToken, UserRole, UserStatus
    ├── LegacyVault.Application/         ② USE CASE — chỉ biết Domain + interface
    │   ├── Common/Interfaces/           IAppDbContext, IPasswordHasher, ITokenService, ICurrentUser
    │   ├── Features/Identity/           AuthService, AuthDtos, AuthErrors
    │   └── DependencyInjection.cs
    ├── LegacyVault.Infrastructure/      ③ CÀI ĐẶT THẬT cho các interface
    │   ├── Persistence/                 AppDbContext, Configurations/, DbInitializer
    │   ├── Security/                    Argon2PasswordHasher, JwtTokenService, CurrentUser
    │   └── DependencyInjection.cs       Đăng ký DB, JWT
    └── LegacyVault.Api/                 ④ HTTP — controller mỏng, lắp ráp mọi thứ
        ├── Common/                      ApiControllerBase, ApiResponse, AuthCookieWriter, GlobalExceptionHandler
        ├── Controllers/V1/              AuthController
        ├── Extensions/                  Controller, CORS, chuẩn lỗi
        └── Program.cs
```

**Chiều phụ thuộc** (mũi tên = "biết đến"):

```
Api ──► Application ──► Domain
 │           ▲
 └──► Infrastructure
```

Quy tắc duy nhất cần nhớ: **lớp trong không bao giờ biết lớp ngoài.**
- Domain không biết có database hay HTTP. `User.RegisterFailedLogin()` chỉ là C# thuần — test được không cần gì.
- Application cần lưu dữ liệu nhưng chỉ biết `IAppDbContext` (interface). Không biết đó là SQL Server.
- Infrastructure cài đặt interface đó bằng EF Core + SQL Server. Đổi sang PostgreSQL chỉ sửa ở đây.
- Api là nơi duy nhất "lắp" mọi thứ lại (`Program.cs`).

Muốn biết một dòng code nên đặt ở đâu? Hỏi: *"Nó là luật nghiệp vụ, là điều phối, là chi tiết kỹ thuật, hay là chuyện HTTP?"* → Domain / Application / Infrastructure / Api.

---

## 3. Lần theo một request đăng nhập

Mở từng file theo thứ tự dưới đây khi đọc:

```
FE: POST /api/v1/auth/login { email, password, rememberMe }
 │
 ├─ Program.cs ............... CORS → Authentication → Authorization → Controller
 │
 ├─ AuthController.Login ..... [ApiController] đã tự kiểm tra [Required], [EmailAddress]
 │                             trong LoginRequest. Sai → 400 VALIDATION_ERROR, không vào tới đây.
 │
 ├─ AuthService.LoginAsync
 │    1. Tìm user theo email đã chuẩn hóa          (EF → SELECT … WHERE Email = @p)
 │    2. Hỏi entity: user.IsLockedOut(now)?       → 423 ACCOUNT_LOCKED
 │    3. passwordHasher.Verify(...)  sai?         → user.RegisterFailedLogin(now) → 401 + attemptsRemaining
 │    4. user.Status != Active?                    → 403 ACCOUNT_NOT_ACTIVE
 │    5. user.RegisterSuccessfulLogin(now)
 │    6. Tạo access token (JWT 15') + refresh token (7 ngày, lưu HASH vào DB)
 │    7. SaveChangesAsync() — MỘT lần cho mọi thay đổi
 │    → trả Result<AuthResult>
 │
 ├─ AuthController ........... Thất bại → Fail(error) → ProblemDetails
 │                             Thành công → AuthCookieWriter ghi 2 cookie HttpOnly
 │
 └─ Response 200 { "data": { userId, expiresIn, user } } + Set-Cookie: lv_access, lv_refresh
```

Các request sau đó: trình duyệt tự gửi cookie `lv_access` → JwtBearer (cấu hình ở `Infrastructure/DependencyInjection.cs`) đọc cookie, kiểm tra chữ ký + hạn → điền `HttpContext.User` → `CurrentUser.UserId` đọc ra.

### Vì sao trả `Result` thay vì `throw`?

Sai mật khẩu là chuyện **bình thường**, xảy ra hàng ngày. Exception dành cho chuyện **bất thường** (mất kết nối DB). `Result` buộc người gọi xử lý cả hai nhánh, và chữ ký method nói rõ "có thể thất bại". Exception thật sự bất ngờ thì `GlobalExceptionHandler` bắt hết và trả 500.

---

## 4. Xác thực hoạt động thế nào

| | Access token | Refresh token |
| --- | --- | --- |
| Là gì | JWT có chữ ký | Chuỗi ngẫu nhiên 64 byte |
| Sống | 15 phút | 7 ngày |
| Lưu ở server | Không | Có (chỉ lưu SHA-256) |
| Cookie | `lv_access`, path `/api` | `lv_refresh`, path `/api/v1/auth` |
| Thu hồi được? | Không (tự hết hạn) | Có — đó là cách đăng xuất |

**Vì sao cookie HttpOnly mà không để FE giữ token?** JavaScript không đọc được cookie HttpOnly, nên nếu trang bị chèn mã độc (XSS), mã đó cũng không lấy được token. FE của nhóm đã thiết kế theo hướng này (`withCredentials: true`, không đọc token).

**Xoay vòng refresh token:** mỗi lần `/auth/refresh`, token cũ bị thu hồi và thay bằng token mới. Nếu ai đó dùng lại token cũ — dấu hiệu bị đánh cắp — cả chuỗi phiên đó bị thu hồi. Bạn có thể tự thử: lưu cookie trước khi refresh, refresh, rồi gửi lại cookie cũ → 401, và cookie mới cũng chết theo.

**Giới hạn cần biết:** sau khi đăng xuất, access token cũ (nếu ai đó đã chép được) vẫn dùng được tối đa 15 phút vì server không lưu nó. Đó là lý do nó phải sống ngắn.

**Các biện pháp nhỏ đã có:** băm mật khẩu Argon2id; khóa 15 phút sau 5 lần sai; email không tồn tại vẫn chạy băm để không lộ qua thời gian phản hồi; kiểm tra trạng thái tài khoản *sau* khi mật khẩu đúng; so sánh hash thời gian cố định; khóa JWT nằm trong user-secrets.

---

## 5. Hợp đồng API

Thành công: `{ "data": … }`. Lỗi: ProblemDetails có `code`.

| Method | URL | Quyền | Kết quả |
| --- | --- | --- | --- |
| POST | `/api/v1/auth/login` | Ai cũng gọi được | 200 `{ data: { userId, expiresIn, user } }` + cookie |
| GET | `/api/v1/auth/session` | Cần đăng nhập | 200 `{ data: { user } }` / 401 |
| POST | `/api/v1/auth/refresh` | Cookie refresh | 200 như login + cookie mới / 401 `SESSION_EXPIRED` |
| POST | `/api/v1/auth/logout` | Ai cũng gọi được | 204, xóa cookie. Body tùy chọn `{ "revokeAllDevices": true }` |

| HTTP | `code` | Khi nào | Trường thêm |
| --- | --- | --- | --- |
| 400 | `VALIDATION_ERROR` | Thiếu/sai định dạng | `errors: { email: [...] }` |
| 401 | `INVALID_CREDENTIALS` | Sai email/mật khẩu | `attemptsRemaining` (chỉ khi email tồn tại) |
| 423 | `ACCOUNT_LOCKED` | Sai 5 lần | `attemptsRemaining: 0`, `remainingSeconds` |
| 403 | `ACCOUNT_NOT_ACTIVE` | Tài khoản bị vô hiệu | |
| 401 | `SESSION_EXPIRED` | Refresh token hết hạn/bị thu hồi | |
| 500 | `INTERNAL_ERROR` | Lỗi bất ngờ (xem log theo `traceId`) | |

### Ghi chú cho FE

1. `AuthSession` trong `auth.types.ts` khai báo `accessToken`, `refreshToken` bắt buộc — BE cố ý không trả hai trường này (đã nằm trong cookie). Nên đổi thành tùy chọn hoặc xóa.
2. Access token hết sau 15 phút. FE cần thêm vào response interceptor của axios: gặp 401 → gọi `POST /auth/refresh` một lần → thử lại request; refresh cũng 401 → về trang đăng nhập. Không có bước này, F5 sau 15 phút sẽ bị đăng xuất.
3. FE dùng role `NOTARY`, tài liệu kiến trúc dùng `VERIFIER`. BE đang theo tài liệu (`OWNER`, `VERIFIER`, `ADMIN`) — nhóm cần chốt một tên.

---

## 6. Học tiếp: tự làm theo khuôn

Mỗi tính năng mới đi đúng 6 bước (giống mục 7 tài liệu kiến trúc). Thử tự làm theo thứ tự, mỗi bài dựa trên bài trước:

**Bài 1 — Đăng ký (`POST /api/v1/auth/register`)**
FE đã có `RegisterRequest`. Gợi ý: thêm `RegisterRequest` vào `AuthDtos.cs` (kèm `[Compare]` cho `confirmPassword`), thêm `AuthErrors.EmailAlreadyExists` (409), thêm `RegisterAsync` vào `AuthService` dùng `User.Create(..., UserStatus.Pending, ...)`. Không cần migration mới vì bảng không đổi. Câu hỏi để tự nghĩ: nếu hai người đăng ký cùng email cùng lúc, cái gì chặn lại? (Gợi ý: xem `UserConfiguration`.)

**Bài 2 — Unit test cho Domain**
Tạo project `tests/LegacyVault.UnitTests` (xUnit), viết test cho `User.RegisterFailedLogin`: sai 4 lần còn 1 lượt; lần 5 bị khóa; sau 15 phút được thử lại. Domain không phụ thuộc gì nên test chỉ là `new` và `Assert`.

**Bài 3 — Phân quyền theo role**
Tạo `AdminController` với một endpoint `[Authorize(Roles = "ADMIN")]`. Đăng nhập bằng owner → 403; bằng admin → 200. Đọc lại `RoleClaimType` trong `Infrastructure/DependencyInjection.cs` để hiểu vì sao nó chạy.

**Bài 4 — Module đầu tiên có bảng mới**
Đây là lúc đi đủ 6 bước, kể cả `dotnet ef migrations add`: entity trong `Domain/<Module>/`, `<X>Configuration.cs`, thêm `DbSet` vào `IAppDbContext` và `AppDbContext`, service + DTO + errors, controller, migration.

Các phần còn lại của bảo mật (TOTP cho Admin, đăng nhập Google, rate limit) làm sau khi đã quen khuôn.
