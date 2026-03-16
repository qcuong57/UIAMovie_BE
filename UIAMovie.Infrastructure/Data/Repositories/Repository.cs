// UIAMovie.Infrastructure/Data/Repositories/Repository.cs
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    private readonly MovieDbContext _context;
    private readonly DbSet<T> _dbSet;

    public Repository(MovieDbContext context)
    {
        _context = context;
        _dbSet   = context.Set<T>();
    }

    public async Task<IEnumerable<T>> GetAllAsync() =>
        await _dbSet.ToListAsync();

    public async Task<T?> GetByIdAsync(Guid id) =>
        await _dbSet.FindAsync(id);

    // ✅ Expression → EF Core dịch thành SQL WHERE
    public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate) =>
        await _dbSet.Where(predicate).ToListAsync();

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> predicate) =>
        await _dbSet.FirstOrDefaultAsync(predicate);

    public async Task AddAsync(T entity) =>
        await _dbSet.AddAsync(entity);

    public async Task AddRangeAsync(IEnumerable<T> entities) =>
        await _dbSet.AddRangeAsync(entities);

    public void Update(T entity) =>
        _dbSet.Update(entity);

    public void Remove(T entity) =>
        _dbSet.Remove(entity);

    public void RemoveRange(IEnumerable<T> entities) =>
        _dbSet.RemoveRange(entities);

    public async Task SaveChangesAsync() =>
        await _context.SaveChangesAsync();
}