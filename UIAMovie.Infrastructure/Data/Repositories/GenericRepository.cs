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

    public async Task<T?> GetByIdAsync(Guid id) =>
        await _dbContext.Set<T>().FindAsync(id);

    public async Task<IEnumerable<T>> GetAllAsync() =>
        await _dbContext.Set<T>().ToListAsync();

    // ✅ Expression → SQL WHERE, không load hết lên memory
    public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate) =>
        await _dbContext.Set<T>().Where(predicate).ToListAsync();

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate) =>
        await _dbContext.Set<T>().FirstOrDefaultAsync(predicate);

    public async Task AddAsync(T entity) =>
        await _dbContext.Set<T>().AddAsync(entity);

    public async Task AddRangeAsync(IEnumerable<T> entities) =>
        await _dbContext.Set<T>().AddRangeAsync(entities);

    public void Update(T entity) =>
        _dbContext.Set<T>().Update(entity);

    public void Remove(T entity) =>
        _dbContext.Set<T>().Remove(entity);

    public void RemoveRange(IEnumerable<T> entities) =>
        _dbContext.Set<T>().RemoveRange(entities);

    public async Task SaveChangesAsync() =>
        await _dbContext.SaveChangesAsync();
}