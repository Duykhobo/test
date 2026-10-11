# LegacyVault Backend — Auth trên .NET 10

Backend gồm bốn lớp: Domain, Application, Infrastructure và Api. Repo đang được hoàn thiện theo từng checkpoint; build thành công chưa chứng minh các endpoint Auth hoạt động.

## Trạng thái hiện tại

| Thành phần | Trạng thái |
| --- | --- |
| User: tạo tài khoản, chuẩn hóa email, lockout | Đã triển khai; có checks lockout trong RAM |
| RefreshToken: validation, hết hạn, revoke idempotent | Đã triển khai; có checks lifecycle trong RAM |
| Argon2PasswordHasher | Đã triển khai; có checks riêng |
| DTO, controller và cookie writer | Đã dựng contract/wiring; chưa kiểm thử HTTP end-to-end |
| AuthService | Scaffold + TODO; các use case ném NotImplementedException |
| JwtTokenService | Scaffold + TODO; các hàm sinh/hash token và lifetime chưa triển khai |
| Rate limiting, CSRF, concurrency và correlation ID thống nhất | Chưa triển khai |
| CI chạy các checks | Chưa cấu hình trong repo |

Login, refresh, session đã xác thực và logout chưa thể nghiệm thu. Khi request đi tới use case chưa triển khai, API trả lỗi server qua exception handler.

## Chuẩn bị và chạy

Cần SDK theo `global.json`, SQL Server và khóa JWT trong user-secrets. `docker-compose.yml` cung cấp SQL Server cho Development ở cổng 1433; có thể dùng SQL Server native với connection string cục bộ phù hợp.

Chạy tại thư mục chứa `LegacyVault.sln`:

```bash
dotnet restore LegacyVault.sln
dotnet build LegacyVault.sln --no-restore
```

Nếu dùng SQL Server Docker:

```bash
docker compose up -d
```

Đặt `Jwt:SigningKey` bằng user-secrets cho project `src/LegacyVault.Api`: dùng khóa ngẫu nhiên đủ mạnh, không commit khóa thật. Validator hiện yêu cầu ít nhất 32 byte UTF-8. Có thể ghi đè `ConnectionStrings:Default` trong cùng kho bí mật để dùng SQL Server native.

```bash
dotnet run --project src/LegacyVault.Api
```

Profile Development dùng `http://localhost:5000`. Khi khởi động, ứng dụng áp migrations đã có và seed nếu database chưa có user. Migration `InitialCreate` đã nằm trong repo: không tạo lại migration đó khi clone. Chỉ thêm migration mới khi model/schema thực sự thay đổi.

Các tài khoản demo dùng mật khẩu fixture công khai `Password123!`, chỉ dành cho Development. Seed thành công không có nghĩa login đã chạy được.

| Email | Role | Trạng thái |
| --- | --- | --- |
| owner@legacyvault.local | OWNER | ACTIVE |
| admin@legacyvault.local | ADMIN | ACTIVE |
| inactive@legacyvault.local | OWNER | INACTIVE |

Lỗi kết nối SQL cần kiểm tra server, cổng và connection string. Lỗi khóa JWT cần kiểm tra user-secrets. Không xóa database hoặc sửa migration đã áp chỉ để bỏ qua lỗi.

## Kiểm tra tự động

```bash
dotnet run --project tests/LegacyVault.AuthChecks --no-restore
dotnet run --file tests/security/PasswordHasherChecks.cs
```

Đây là các console checks, không được tự khám phá bằng `dotnet test`. CI phải gọi rõ cả hai lệnh và kiểm tra exit code. Lần chạy đầu của file-based check cần restore các project references bằng SDK .NET 10.

AuthChecks kiểm tra 11 tình huống validation, hai public response contracts, lockout và lifecycle refresh token. PasswordHasherChecks kiểm tra salt ngẫu nhiên, Unicode, mật khẩu sai, hash bị sửa, định dạng, giới hạn đầu vào và culture.

Các checks này chưa xác minh JWT, middleware, cookie qua HTTP, SQL concurrency, transaction rollback, CSRF hoặc rate limiting.

## Kiến trúc và quy tắc triển khai

- Domain giữ entity và invariant, không phụ thuộc EF Core hoặc HTTP.
- Application chứa DTO, use case và interface. `IAppDbContext` sử dụng EF Core theo lựa chọn hiện tại; không thêm Repository riêng chỉ để bọc DbSet.
- Infrastructure triển khai persistence/security; mapping dùng chung giới hạn Domain.
- Api nhận request, gọi use case và ghi cookie; không serialize `AuthResult` chứa token ra body.

Giới hạn email lấy từ `UserLimits.Email`. Không trim hoặc đổi casing mật khẩu. Không log mật khẩu, token hoặc khóa ký. Hash mật khẩu giữ định dạng cũ và bộ tham số Argon2 đã phê duyệt; khi nâng tham số phải hỗ trợ hash cũ trước khi triển khai.

## Contract Auth mục tiêu

Các endpoint đã có route/controller, nhưng kết quả thành công dưới đây còn phụ thuộc việc hoàn thiện use case và token service.

| Method | Endpoint | Kết quả mục tiêu |
| --- | --- | --- |
| POST | /api/v1/auth/login | 200 `{ data: { userId, expiresIn, user } }` và cookie |
| GET | /api/v1/auth/session | 200 `{ data: { user } }`; cần principal hợp lệ |
| POST | /api/v1/auth/refresh | 200 session mới; tiêu thụ refresh token một lần |
| POST | /api/v1/auth/logout | 204 sau khi lưu thu hồi thành công; body revokeAllDevices tùy chọn |

Token gốc chỉ gửi qua cookie HttpOnly, không nằm trong body session. Cookie refresh dùng path `/api/v1/auth`; access dùng `/api`. HttpOnly hạn chế JavaScript đọc token, không ngăn mã XSS gửi request bằng phiên hiện tại. CORS và SameSite không thay thế toàn bộ chính sách CSRF.

| HTTP | Code | Chính sách phản hồi mục tiêu |
| --- | --- | --- |
| 400 | VALIDATION_ERROR | Lỗi theo field |
| 401 | INVALID_CREDENTIALS | Email không tồn tại và mật khẩu sai có cùng cấu trúc; không trả attemptsRemaining |
| 423 | ACCOUNT_LOCKED | Chỉ công khai sau khi chứng minh mật khẩu đúng; có remainingSeconds và attemptsRemaining bằng zero |
| 403 | ACCOUNT_NOT_ACTIVE | Chỉ công khai sau khi chứng minh mật khẩu đúng |
| 401 | SESSION_EXPIRED | Lỗi chung cho phiên không hợp lệ |
| 500 | INTERNAL_ERROR | Thông báo an toàn; không trả stack trace |

Login cần dummy hash cho user không tồn tại, cập nhật bộ đếm sai nguyên tử và chỉ cấp cookie sau khi commit. Refresh cần phối hợp rotation/replay/logout trong persistence; một lần SaveChanges không tự bảo vệ bước SELECT trước đó. Thu hồi refresh không tự vô hiệu access JWT đã cấp; phải xác định chính sách này khi triển khai.

## Thứ tự hoàn thiện và nghiệm thu

1. Đồng bộ tài liệu, XML docs và constants; bổ sung gate CI cho các checks hiện có.
2. Hoàn thiện JwtTokenService với tests chữ ký, issuer, audience, expiry và refresh token.
3. Hoàn thiện AuthService theo TODO blueprint, bảo vệ concurrency và transaction; mọi đổi schema qua migration mới.
4. Triển khai rate limit trước công việc băm, CSRF theo deployment và correlation ID liên kết response/log.
5. Kiểm thử HTTP: anonymous session 401, login 200, session 200, refresh 200, logout 204; thử token hết hạn/revoked, replay, hai refresh đồng thời và login sai đồng thời.
6. Đối chiếu DTO/role với frontend trước tích hợp; không suy ra contract đã chốt từ ghi chú frontend cũ.

`http/auth.http` là bộ tình huống thủ công mục tiêu; phần chú thích cũ còn cần đồng bộ. `docs/AUTH_REBUILD.md` giữ lịch sử dựng scaffold và còn cần cập nhật trạng thái Domain/security hiện tại. Các luồng đăng ký, xác minh email, reset mật khẩu và challenge Admin phải chốt contract trước khi triển khai.
