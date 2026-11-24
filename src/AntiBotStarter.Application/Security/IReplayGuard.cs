using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Application.Security;

/// <summary>Remembers single-use nonces (form tokens) so they cannot be replayed.</summary>
public interface IReplayGuard
{
    /// <summary>true the first time a nonce is seen; false on every later attempt (replay).</summary>
    Task<bool> TryConsumeAsync(string nonce, TimeSpan ttl);
}
