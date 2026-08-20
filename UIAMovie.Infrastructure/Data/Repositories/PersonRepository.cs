// UIAMovie.Infrastructure/Data/Repositories/PersonRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class PersonRepository : Repository<Person>, IPersonRepository
{
    private readonly MovieDbContext _context;

    public PersonRepository(MovieDbContext context) : base(context)
    {
        _context = context;
    }

    /// <summary>
    /// Cùng pattern với MovieRepository.GetPagedAsync: build IQueryable, để EF dịch
    /// sang SQL WHERE ILIKE + ORDER BY + OFFSET/FETCH — không kéo cả bảng Person về RAM.
    /// </summary>
    public async Task<(IEnumerable<Person> Items, int TotalCount)> SearchPagedAsync(
        string? query, int page, int pageSize)
    {
        var q = _context.Persons.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            q = q.Where(p => EF.Functions.ILike(p.Name, pattern));
        }

        q = q.OrderBy(p => p.Name);

        var totalCount = await q.CountAsync();

        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Person?> GetByIdWithImagesAsync(Guid id)
    {
        return await _context.Persons
            .AsNoTracking()
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>
    /// GROUP BY tên đã chuẩn hóa NGAY TRÊN DB (Trim + ToLower dịch được sang SQL),
    /// HAVING COUNT > 1 để chỉ lấy các nhóm thực sự trùng — rồi mới load full Person
    /// của các Id đó (kèm Images) để trả về cho FE hiển thị so sánh.
    /// </summary>
    public async Task<IEnumerable<IGrouping<string, Person>>> FindDuplicatesByNameAsync()
    {
        var duplicateKeys = await _context.Persons
            .AsNoTracking()
            .GroupBy(p => p.Name.Trim().ToLower())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync();

        if (duplicateKeys.Count == 0)
            return Enumerable.Empty<IGrouping<string, Person>>();

        var people = await _context.Persons
            .AsNoTracking()
            .Include(p => p.Images)
            .Where(p => duplicateKeys.Contains(p.Name.Trim().ToLower()))
            .ToListAsync();

        return people.GroupBy(p => p.Name.Trim().ToLower());
    }

    public async Task<bool> IsReferencedByAnyMovieAsync(Guid personId)
    {
        var inCast = await _context.MovieCasts.AsNoTracking().AnyAsync(c => c.PersonId == personId);
        if (inCast) return true;

        return await _context.MovieDirectors.AsNoTracking().AnyAsync(d => d.PersonId == personId);
    }
}