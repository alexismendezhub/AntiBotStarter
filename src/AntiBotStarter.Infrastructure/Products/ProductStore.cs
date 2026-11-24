using AntiBotStarter.Domain.Products;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Infrastructure.Products;

/// <summary>Sample data. Random GUID ids → cannot be enumerated with /products/1, /2, /3…</summary>
public sealed class ProductStore
{
    private readonly List<ProductDto> _items;
    private readonly Dictionary<Guid, ProductDto> _byId;

    public ProductStore()
    {
        var rnd = new Random(42);
        _items = Enumerable.Range(1, 500).Select(i =>
        {
            var bytes = new byte[16];
            rnd.NextBytes(bytes);
            return new ProductDto(new Guid(bytes), $"Product {i:000}", Math.Round((decimal)(rnd.NextDouble() * 500 + 5), 2));
        }).ToList();
        _byId = _items.ToDictionary(p => p.Id);
    }

    public int Count => _items.Count;
    public IReadOnlyList<ProductDto> Page(int page, int size) => _items.Skip((page - 1) * size).Take(size).ToList();
    public ProductDto? Find(Guid id) => _byId.GetValueOrDefault(id);
}
