using Microsoft.AspNetCore.Builder;

namespace Bloomie.Middleware
{
    public static class UserAccessLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseUserAccessLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<UserAccessLoggingMiddleware>();
        }
    }
}