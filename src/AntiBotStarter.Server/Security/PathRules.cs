namespace AntiBotStarter.Server.Security;

internal static class PathRules
{
    public static bool IsUnder(PathString path, IEnumerable<string> prefixes) =>
        prefixes.Any(p => !string.IsNullOrWhiteSpace(p) &&
                          path.StartsWithSegments(new PathString(p), StringComparison.OrdinalIgnoreCase));

    public static async Task RejectAsync(HttpContext ctx, int status)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync(status == StatusCodes.Status404NotFound ? "Not Found" : "Forbidden");
    }
}
