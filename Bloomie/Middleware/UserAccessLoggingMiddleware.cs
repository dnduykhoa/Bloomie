using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Bloomie.Data;
using Bloomie.Models.Entities;

namespace Bloomie.Middleware
{
    public class UserAccessLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UserAccessLoggingMiddleware> _logger;

        public UserAccessLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, ILogger<UserAccessLoggingMiddleware> logger)
        {
            _next = next;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            _logger.LogInformation("Middleware called for request: {Path}", context.Request.Path);

            // Kiểm tra xem người dùng đã đăng nhập chưa
            if (context.User.Identity.IsAuthenticated)
            {
                // Kiểm tra xem request có phải đến trang chủ không
                var path = context.Request.Path.Value?.ToLower();
                if (path == "/" || path == "/home/index")
                {
                    var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (!string.IsNullOrEmpty(userId))
                    {
                        _logger.LogInformation("Authenticated user: {UserId} accessing homepage", userId);
                        try
                        {
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                                var log = new UserAccessLog
                                {
                                    UserId = userId,
                                    AccessTime = DateTime.Now,
                                    Url = context.Request.Path // Lưu URL của request
                                };
                                dbContext.UserAccessLogs.Add(log);
                                await dbContext.SaveChangesAsync();
                                _logger.LogInformation("Logged access for user: {UserId} to {Url}", userId, log.Url);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to log access for user: {UserId}", userId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("User authenticated but no user ID found.");
                    }
                }
                else
                {
                    _logger.LogInformation("Authenticated user accessed non-homepage: {Path}", context.Request.Path);
                }
            }
            else
            {
                _logger.LogInformation("User not authenticated for request: {Path}", context.Request.Path);
            }

            await _next(context);
        }
    }
}