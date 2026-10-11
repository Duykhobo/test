# Dựng lại Auth — .NET 10

AI dựng contract/scaffold và TODO blueprint; developer viết nghiệp vụ. Mỗi nhịp tối đa 2–3 file, build rồi dừng để developer commit. Jira chưa được cung cấp: LV-XXX là placeholder, không phải mã issue thực.

## Trạng thái Domain hiện tại

User đã triển khai NormalizeEmail/Create và các method lockout. RefreshToken đã triển khai factory, validation, expiry và revoke idempotent. Các phần này giữ schema hiện có; không cần migration mới cho thay đổi logic trong RAM. Policy hiện tại khóa 15 phút sau 5 lần sai.

DbInitializer có thể gọi password hasher và User.Create để seed khi database chưa có user. Seed chỉ được gọi ở Development và không ghi mật khẩu vào log. AuthChecks đã bao phủ validation/DTO, lockout và lifecycle refresh token trong RAM; chưa chứng minh persistence an toàn khi request đồng thời.

Thứ tự còn lại: AuthService và persistence concurrency/transaction → HTTP security → integration checks. Không viết test coi NotImplementedException là nghiệp vụ đúng.

## Trạng thái Security hiện tại

Argon2PasswordHasher.Hash/Verify đã triển khai bằng package có sẵn, giữ định dạng hash cũ và whitelist bộ tham số hiện tại. Tham số serialize và độ dài Base64 được tính từ một nguồn cấu hình. Khi nâng tham số phải hỗ trợ hash cũ trước khi triển khai. PasswordHasherChecks chạy riêng để kiểm tra hành vi này.

JwtTokenService đã triển khai ký HS256, claims sub/jti/role, expiry chuẩn hóa đến giây và refresh token CSPRNG 64 byte kèm hash SHA-256. JwtTokenServiceChecks đã chạy pass và được thêm vào workflow; không đọc signing key thật. AuthService đã nhận dependency qua constructor và triển khai GetCurrentUserAsync: kiểm tra principal, tải user read-only, kiểm tra Active và map UserDto. Login, refresh và logout vẫn là scaffold. CurrentSessionChecks kiểm tra guard không cần SQL; CurrentSessionSqlChecks đã pass trên SQL Server development local cho Active/Pending/Inactive, user đã xóa, DTO, no-tracking và cancellation. Fixture được rollback trong transaction. Bước tiếp theo là concurrency/transaction của persistence trước khi triển khai login và rotation. Seed thành công và checks Domain/security pass chưa chứng minh endpoint Auth hoạt động.

Checks password hasher hiện có: round-trip Unicode, mật khẩu sai, salt khác nhau, hash bị sửa, Base64 lỗi, tham số không hỗ trợ, giới hạn input và culture. Chưa có fixture hash lịch sử độc lập để chứng minh tương thích ngoài định dạng/bộ tham số hiện tại. Khi triển khai token service, thêm checks signature/issuer/audience/expiry, secret claims, refresh randomness/encoding và hash deterministic. Test JWT dùng signing key fixture riêng, không đọc hoặc log user-secrets thật.

## 1. Use case và endpoint

Các route và DTO đã có; kết quả thành công trong bảng là mục tiêu sau khi hoàn thiện use case.

| Mã | Endpoint hiện có | Contract mục tiêu |
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

Ma trận trên ghi lại các nhịp scaffold ban đầu. User, RefreshToken và password hasher đã được triển khai theo hướng dẫn/review của chủ module; chỉ những method chưa triển khai mới giữ TODO blueprint và exception rõ ràng. Giữ migration hiện có làm baseline, mọi thay đổi schema phát sinh phải qua migration mới. Không xóa database để dựng lại code.

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

Kiểm tra contract/validation và Domain bằng BCL; kiểm tra hasher dùng package đã có. Cả hai bộ không cần SQL Server, signing key thật hoặc user-secrets:

```powershell
dotnet run --project tests/LegacyVault.AuthChecks --configuration Release
dotnet run --file tests/security/PasswordHasherChecks.cs --configuration Release
dotnet run --file tests/security/JwtOptionsChecks.cs --configuration Release
dotnet run --file tests/security/JwtTokenServiceChecks.cs --configuration Release
```

Đây là console checks, không được `dotnet test` tự khám phá. Workflow `.github/workflows/backend-checks.yml` chạy khi push hoặc mở/cập nhật PR: cài SDK từ global.json, restore, build Release và chạy rõ các bộ checks Domain, password hasher, JWT options và JWT token service. Exit code khác zero làm job thất bại. Workflow không khởi động API hoặc seed database. Cần chọn job này làm required status check trong branch protection nếu muốn bắt buộc trước merge; file workflow không tự bật branch protection.

AuthChecks bao phủ 11 tình huống input, JSON whitelist/camelCase của hai response session, lockout và refresh lifecycle. PasswordHasherChecks bao phủ hành vi mô tả ở phần Security. Chưa kiểm thử middleware, cookie, database, concurrency hoặc use case Auth; service vẫn là scaffold. Lần chạy đầu của file-based check cần restore project references.

Nhịp đồng bộ tài liệu/CI sửa đúng ba file: `docs/AUTH_REBUILD.md` (MODIFY), `http/auth.http` (MODIFY), `.github/workflows/backend-checks.yml` (CREATE). Không thay đổi endpoint, DTO hoặc schema trong nhịp này.

Nhịp 1: `dotnet build LegacyVault.sln --no-restore` pass. Validation kiểm tra thiếu/sai email, mật khẩu rỗng, chuỗi vượt giới hạn; mật khẩu ngắn vẫn được gửi để xác thực tài khoản hiện hữu. Compile pass chưa chứng minh nghiệp vụ đúng.

Sau khi developer hoàn thiện: chạy `dotnet run --project src/LegacyVault.Api`; dùng http/auth.http để kiểm tra anonymous session 401 → login 200 → session 200 → refresh 200 → logout 204 → session 401. Thử payload sai 400, inactive 403 và policy lockout đã chốt. Kiểm tra hai refresh cùng token đồng thời chỉ có một lần tiêu thụ thành công; thử đăng nhập sai đồng thời không mất bộ đếm; token hết hạn/revoke không được logout mọi thiết bị. Kiểm tra rate limit 429 và Retry-After theo policy.

Manual UI khi đã tích hợp FE: đăng nhập → F5 khôi phục phiên → hết access token và refresh → đăng xuất; lỗi form hiển thị inline, lỗi API có thông báo, không tạo vòng lặp refresh. Trong giai đoạn scaffold, endpoint chưa triển khai phải được nhận diện rõ và chưa đủ điều kiện nghiệm thu chức năng.

## Integration checks session trên SQL Server development

Database phải có migration hiện tại. Chỉ chạy trên database development; không tự tạo/xóa database hoặc chạy migration trong check. Đặt biến LEGACYVAULT_TEST_CONNECTION_STRING bằng connection string development rồi chạy:

```powershell
dotnet run --file tests/security/CurrentSessionSqlChecks.cs --configuration Release
```

Check dùng signing key fixture, không đọc user-secrets. Các user fixture có email ngẫu nhiên được thêm/xóa trong transaction và luôn rollback khi kết thúc, kể cả khi assertion thất bại. File tắt PublishAot để EF Core dựng model ở runtime. Check này chưa nằm trong CI vì workflow hiện chưa cấp SQL Server; các checks principal không cần SQL vẫn chạy trong CI. Đây là kiểm tra use case/persistence, chưa kiểm tra HTTP middleware hoặc cookie.
## Checkpoint nhánh đăng nhập sai

LoginAsync đã chuẩn hóa email, verify bằng hash thật/dummy hash và xử lý nhánh mật khẩu sai: ghi bộ đếm, giữ khóa hiện tại, retry tối đa ba lần ghi khi conflict. Sau reload, user đã xóa hoặc đổi password hash bị từ chối; conflict cuối cùng và cancellation được truyền lên. Nhánh cấp phiên còn TODO; refresh/logout chưa triển khai.

LoginFailureSqlChecks chạy implementation AuthService thật trên database development đã áp dụng migration row_version:

```powershell
dotnet run --file tests/security/LoginFailureSqlChecks.cs --configuration Release
```

Cần đặt LEGACYVAULT_TEST_CONNECTION_STRING trước khi chạy. Check bao phủ email không tồn tại, bộ đếm lưu ở DB, khóa không bị kéo dài, một stale-version dẫn đến reload/retry, giới hạn ba lần ghi và cancellation. Interceptor chỉ thay original rowversion để tạo xung đột SQL có kiểm soát; đây không phải kiểm thử tải hoặc hai request HTTP chạy song song. Fixture luôn rollback; không đọc signing key thật. Chưa bao phủ user bị xóa hoặc password đổi đúng lúc conflict. Check SQL chưa nằm trong CI vì workflow chưa cấp SQL Server. Việc email không tồn tại dùng dummy hash không bảo đảm mọi nhánh có cùng thời gian phản hồi; nhánh sai/hợp lệ còn chưa đồng nhất đầy đủ trong giai đoạn scaffold.
## Checkpoint cấp phiên Owner/Verifier

LoginAsync đã cấp phiên cho Owner/Verifier sau khi mật khẩu đúng, user Active và không bị khóa. Guard Admin nằm trước transaction: challenge 2FA vẫn là TODO, hiện ném NotImplementedException và chưa có login Admin hoạt động. User và refresh hash được lưu trong transaction; ký access token, dựng DTO rồi commit trước khi trả AuthResult. Access token 15 phút, refresh token 7 ngày; RememberMe quyết định cookie persistent, không đổi lifetime server. Conflict ở nhánh cấp phiên được truyền lên và rollback, chưa tự retry. Refresh/logout còn scaffold.

```powershell
dotnet run --file tests/security/LoginSessionSqlChecks.cs --configuration Release
```

Cần đặt LEGACYVAULT_TEST_CONNECTION_STRING tới SQL Server development đã áp dụng migrations. Check dùng signing key fixture, không đọc secrets. Bao phủ Owner/Verifier commit phiên, JWT claims/expiry, refresh hash/lifetime, DTO và RememberMe; Admin bị chặn; lỗi ký JWT và stale-version dẫn đến rollback cả user/refresh token. Check tạo user riêng và thực sự commit phiên để kiểm tra persistence, sau đó cleanup đúng các fixture ID trong finally. Nếu tiến trình bị kill trước cleanup, fixture có thể còn trong DB development. Đây chưa phải HTTP/cookie test, chưa mô phỏng lỗi mạng lúc commit hoặc chứng minh mọi kịch bản request đồng thời. Check SQL chưa chạy trong CI vì workflow chưa cấp SQL Server.