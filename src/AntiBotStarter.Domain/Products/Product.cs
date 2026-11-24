namespace AntiBotStarter.Domain.Products;

public sealed record ProductDto(Guid Id, string Name, decimal Price);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasMore);
