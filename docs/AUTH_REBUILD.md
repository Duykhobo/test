# Dựng lại Auth — .NET 10

AI dựng contract/scaffold và TODO blueprint; developer viết nghiệp vụ. Mỗi nhịp tối đa 2–3 file, build rồi dừng để developer commit. Jira chưa được cung cấp: LV-XXX là placeholder, không phải mã issue thực.

## Trạng thái Domain scaffold

User và RefreshToken giữ nguyên property/schema, nhưng factory và method nghiệp vụ hiện ném NotImplementedException kèm tên method. EF Core vẫn materialize được bản ghi đã lưu bằng constructor private; không cần migration mới cho nhịp này. Policy 5 lần sai/15 phút vẫn là giá trị mẫu cũ, chủ module quyết định trước khi hoàn thiện.

DbInitializer vẫn gọi User.Create khi database chưa có user. Vì vậy chạy trên database mới/rỗng sẽ dừng ở bước seed cho đến khi developer hoàn thiện factory; không bỏ qua lỗi để giả báo khởi tạo thành công. Database hiện có không bị xóa. Bộ AuthChecks chỉ xác minh DTO/validation, không chứng minh các method Domain đã hoạt động.

Thứ tự developer đề xuất: User.NormalizeEmail/Create → các method lockout → RefreshToken factory/lifecycle → Infrastructure security → AuthService → kiểm tra HTTP và concurrency. Thêm kiểm tra hành vi Domain khi triển khai; không viết test coi NotImplementedException là nghiệp vụ đúng.

## Trạng thái Security scaffold

Argon2PasswordHasher.Hash/Verify và JwtTokenService.CreateAccessToken/CreateRefreshToken/HashRefreshToken/RefreshTokenLifetime hiện là TODO blueprint và ném NotImplementedException. Helper CreateSigningKey được giữ nguyên vì JwtBearer dùng để cấu hình validator; helper này chỉ bọc khóa đã cấu hình, không sinh hoặc ký token. Không thêm dependencies. Các constructor options/TimeProvider của token service cần được developer khôi phục khi triển khai theo blueprint.

DbInitializer trên database rỗng gọi Hash trước khi gọi User.Create, nên hiện sẽ dừng ngay tại password hasher chưa triển khai. Database hiện hữu không bị sửa/xóa bởi nhịp scaffold. Không coi build/DTO checks pass là bằng chứng mật mã hoặc xác thực đã hoạt động.

Khi triển khai security, bổ sung kiểm tra: hash/verify round-trip, mật khẩu sai, salt khác nhau, hash malformed/parameters vượt bounds; JWT signature/issuer/audience/expiry và không lộ secret claims; refresh token randomness/encoding và hash deterministic. Test JWT dùng signing key fixture riêng, không đọc hoặc log user-secrets thật. Chốt policy và tương thích hash đã lưu với chủ module trước khi đổi format/tham số.

## 1. Use case và endpoint

| Mã | Endpoint hiện có | Contract hiện có |
| --- | --- | --- |
| AUTH-LOGIN | POST /api/v1/auth/login | LoginRequest → data: AuthSessionDto + cookie HttpOnly |
| AUTH-SESSION | GET /api/v1/auth/session | data: CurrentSessionDto; cần xác thực |
| AUTH-REFRESH | POST /api/v1/auth/refresh | Refresh cookie → data: AuthSessionDto + cookie mới |
| AUTH-LOGOUT | POST /api/v1/auth/logout | LogoutRequest tùy chọn → 204 + xóa cookie |

Đăng ký, xác minh email, quên/reset mật khẩu, Google, 2FA và passkey thuộc các luồng Auth mở rộng trong docs/FE_BE_INTEGRATION_REQUEST.md ở workspace cha. Chưa có contract C# đã chốt cho các luồng này. Cần chốt payload/response và challenge policy trước khi dựng DTO, không suy ra contract từ FE legacy. Không nhận role đặc quyền từ request đăng ký.

## 2. Phân tầng

Backend: Domain chứa entity → Application chứa DTO và interface use case → Infrastructure cài DB/security → Api lắp DI và HTTP. Không áp tên thư mục FSD frontend vào C#.

Khi tích hợp FE: shared contract/API → entities user/adapter → features auth service/hooks/UI → app bootstrap. Đối chiếu DTO 1:1; refresh token chỉ nằm trong cookie HttpOnly.

## 3. Ma trận file

| Nhịp | Thao tác | File |
| --- | --- | --- |
| 1 | CREATE | src/LegacyVault.Application/Features/Identity/AuthValidation.cs |
| 1 | MODIFY | src/LegacyVault.Application/Features/Identity/AuthDtos.cs |
| 1 | CREATE | docs/AUTH_REBUILD.md |
| 2 | MODIFY | src/LegacyVault.Application/Features/Identity/AuthService.cs: thay nghiệp vụ bằng TODO blueprint, giữ interface tương thích controller |
| 2 | MODIFY | src/LegacyVault.Application/Features/Identity/AuthErrors.cs: tài liệu hóa lỗi và hướng dẫn phản hồi chống dò tài khoản |
| 3 | MODIFY | src/LegacyVault.Api/Controllers/V1/AuthController.cs |
| 3 | MODIFY | src/LegacyVault.Api/Extensions/ApiServiceExtensions.cs |
| 3 | MODIFY | src/LegacyVault.Api/Program.cs |
| 4 | CREATE/MODIFY | Bộ kiểm tra runnable cho validation/contract; đường dẫn chốt theo test tooling được phê duyệt |

Sau scaffold use case, tiếp tục Domain và Infrastructure security bằng từng nhịp riêng: User, RefreshToken, password hasher và token service. Mỗi method nghiệp vụ để TODO 5 mục và lỗi chưa triển khai rõ ràng; không trả thành công giả. Giữ migration hiện có làm baseline, mọi thay đổi schema phát sinh phải qua migration mới. Không xóa database để dựng lại code.

## 4. Micro-task

- Nhịp 1: giữ tên/cấu trúc DTO hiện có, gom giới hạn/thông báo validation, bổ sung XML docs. Không thêm package.
- Nhịp 2: blueprint cho login, refresh, logout và đọc session. Mỗi TODO có mục tiêu, input/output, thứ tự, thư viện/hàm, edge cases/exception.
- Nhịp 3: HTTP wiring, blueprint rate limit/CSRF và lỗi; không coi CORS là xác thực hay kiểm tra quyền.
- Nhịp 4: kiểm tra validation trước; developer hoàn thiện nghiệp vụ rồi chạy integration scenarios bên dưới.
- Khi làm FE: định nghĩa query key session, mutation và cache invalidation trong feature auth; không dùng useState cho server data.

## 5. Điều kiện biên và ngoại lệ

- Login: email/mật khẩu rỗng, vượt giới hạn, tài khoản không tồn tại/khóa/inactive; thống nhất phản hồi công khai để không dò email. Bộ đếm sai phải an toàn khi request đồng thời.
- Refresh: thiếu cookie, hết hạn, revoke, replay và request đồng thời. Tiêu thụ token nguyên tử; transaction SaveChanges một mình không bảo vệ bước đọc trước đó.
- Logout: idempotent; logout mọi thiết bị phải có phiên hợp lệ, token cũ không đủ quyền. Cookie xóa đúng path; xác định rõ chính sách thu hồi access token.
- Security: rate limit trước công việc băm tốn tài nguyên; CSRF theo mô hình triển khai thực; không tin X-Active-Role để cấp quyền.
- Không log secret/token/mật khẩu. Blueprint cần phân biệt lỗi validation, auth, concurrency, database và cancellation; không nuốt lỗi để trả thành công.

## 6. Definition of Done

Kiểm tra contract/validation bằng BCL, không cần SQL Server hoặc NuGet package mới:

```powershell
dotnet run --project tests/LegacyVault.AuthChecks
```

Đây là console check, không phải test project do `dotnet test` tự khám phá. Chạy lệnh trên riêng trong CI nếu muốn dùng làm gate. Exit code khác zero khi contract sai. Bao phủ 11 tình huống input và JSON whitelist/camelCase của hai response session. Chưa kiểm thử middleware, cookie, database, concurrency hoặc nghiệp vụ Auth; các hàm service vẫn là scaffold.

Nhịp 1: `dotnet build LegacyVault.sln --no-restore` pass. Validation kiểm tra thiếu/sai email, mật khẩu rỗng, chuỗi vượt giới hạn; mật khẩu ngắn vẫn được gửi để xác thực tài khoản hiện hữu. Compile pass chưa chứng minh nghiệp vụ đúng.

Sau khi developer hoàn thiện: chạy `dotnet run --project src/LegacyVault.Api`; dùng http/auth.http để kiểm tra anonymous session 401 → login 200 → session 200 → refresh 200 → logout 204 → session 401. Thử payload sai 400, inactive 403 và policy lockout đã chốt. Kiểm tra hai refresh cùng token đồng thời chỉ có một lần tiêu thụ thành công; thử đăng nhập sai đồng thời không mất bộ đếm; token hết hạn/revoke không được logout mọi thiết bị. Kiểm tra rate limit 429 và Retry-After theo policy.

Manual UI khi đã tích hợp FE: đăng nhập → F5 khôi phục phiên → hết access token và refresh → đăng xuất; lỗi form hiển thị inline, lỗi API có thông báo, không tạo vòng lặp refresh. Trong giai đoạn scaffold, endpoint chưa triển khai phải được nhận diện rõ và chưa đủ điều kiện nghiệm thu chức năng.
