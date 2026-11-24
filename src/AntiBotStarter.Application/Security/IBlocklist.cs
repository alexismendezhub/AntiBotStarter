using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Application.Security;

public interface IBlocklist
{
    Task BlockAsync(string clientKey, TimeSpan duration, string reason);
    Task<bool> IsBlockedAsync(string clientKey);
}
