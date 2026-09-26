// UIAMovie.Infrastructure/Data/Repositories/GenericRepository.cs
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class GenericRepository<T> : IRepository<T> where T : class
{
    protected readonly MovieDbContext _dbContext;

    public GenericRepository(MovieDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _dbContext.Set<T>().FindAsync(new object?[] { id }, ct);

    public async Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default) =>
        await _dbContext.Set<T>().ToListAsync(ct);

    public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _dbContext.Set<T>().Where(predicate).ToListAsync(ct);

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await _dbContext.Set<T>().FirstOrDefaultAsync(predicate, ct);

    public async Task AddAsync(T entity, CancellationToken ct = default) =>
        await _dbContext.Set<T>().AddAsync(entity, ct);

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) =>
        await _dbContext.Set<T>().AddRangeAsync(entities, ct);

    public void Update(T entity) =>
        _dbContext.Set<T>().Update(entity);

    public void Remove(T entity) =>
        _dbContext.Set<T>().Remove(entity);

    public void RemoveRange(IEnumerable<T> entities) =>
        _dbContext.Set<T>().RemoveRange(entities);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _dbContext.SaveChangesAsync(ct);
}