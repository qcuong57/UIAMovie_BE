// UIAMovie.Application/Services/PersonService.cs
//
// Lưu ý quan trọng — ranh giới với MovieService:
//   MovieService đã có UpsertPersonAsync() riêng (private) dùng cho luồng import phim
//   (ưu tiên PersonId FE chọn -> TmdbPersonId -> match theo tên -> tạo mới). PersonService
//   ở đây KHÔNG đụng vào luồng đó — nó là service riêng phục vụ PersonsController:
//   search/CRUD trực tiếp trên Person, và 2 endpoint dedup/merge cho admin.
//   Cả hai cùng ghi qua chung MovieDbContext (qua các Repository khác nhau) nên không xung đột.

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

public interface IPersonService
{
    Task<PaginatedDTO<PersonDTO>> SearchPersonsAsync(string? query, int page, int pageSize);
    Task<PersonDTO?> GetPersonByIdAsync(Guid id);
    Task<Guid> CreatePersonAsync(CreatePersonDTO dto);
    Task<bool> UpdatePersonAsync(Guid id, UpdatePersonDTO dto);
    Task<bool> DeletePersonAsync(Guid id);
    Task<IEnumerable<PersonDuplicateGroupDTO>> FindDuplicatesByNameAsync();
    Task<bool> MergePersonsAsync(MergePersonsDTO dto);
}

public class PersonService : IPersonService
{
    private readonly IPersonRepository _personRepository;
    private readonly IRepository<PersonImage> _personImageRepository;
    private readonly IRepository<MovieCast> _castRepository;
    private readonly IRepository<MovieDirector> _directorRepository;

    public PersonService(
        IPersonRepository personRepository,
        IRepository<PersonImage> personImageRepository,
        IRepository<MovieCast> castRepository,
        IRepository<MovieDirector> directorRepository)
    {
        _personRepository = personRepository;
        _personImageRepository = personImageRepository;
        _castRepository = castRepository;
        _directorRepository = directorRepository;
    }

    // ─── Search / CRUD ──────────────────────────────────────────────────────

    public async Task<PaginatedDTO<PersonDTO>> SearchPersonsAsync(string? query, int page, int pageSize)
    {
        var (people, totalCount) = await _personRepository.SearchPagedAsync(query, page, pageSize);

        return new PaginatedDTO<PersonDTO>
        {
            Items = people.Select(MapToDTO).ToList(),
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public async Task<PersonDTO?> GetPersonByIdAsync(Guid id)
    {
        var person = await _personRepository.GetByIdWithImagesAsync(id);
        return person == null ? null : MapToDTO(person);
    }

    public async Task<Guid> CreatePersonAsync(CreatePersonDTO dto)
    {
        var person = new Person
        {
            Name = dto.Name,
            TmdbPersonId = dto.TmdbPersonId,
            ProfileUrl = dto.ProfileUrl,
            Biography = dto.Biography,
            Birthday = dto.Birthday,
            PlaceOfBirth = dto.PlaceOfBirth
        };

        await _personRepository.AddAsync(person);
        await _personRepository.SaveChangesAsync();

        if (dto.ProfileImages?.Any() == true)
        {
            foreach (var url in dto.ProfileImages.Where(u => !string.IsNullOrWhiteSpace(u)))
                await _personImageRepository.AddAsync(new PersonImage { PersonId = person.Id, Url = url });
            await _personImageRepository.SaveChangesAsync();
        }

        return person.Id;
    }

    public async Task<bool> UpdatePersonAsync(Guid id, UpdatePersonDTO dto)
    {
        var person = await _personRepository.GetByIdAsync(id);
        if (person == null) return false;

        if (dto.Name != null) person.Name = dto.Name;
        if (dto.ProfileUrl != null) person.ProfileUrl = dto.ProfileUrl;
        if (dto.Biography != null) person.Biography = dto.Biography;
        if (dto.Birthday != null) person.Birthday = dto.Birthday;
        if (dto.PlaceOfBirth != null) person.PlaceOfBirth = dto.PlaceOfBirth;

        _personRepository.Update(person);
        await _personRepository.SaveChangesAsync();

        // Nếu FE gửi ProfileImages -> thay toàn bộ ảnh cũ bằng danh sách mới
        if (dto.ProfileImages != null)
        {
            var oldImages = await _personImageRepository.FindAsync(i => i.PersonId == id);
            _personImageRepository.RemoveRange(oldImages);

            foreach (var url in dto.ProfileImages.Where(u => !string.IsNullOrWhiteSpace(u)))
                await _personImageRepository.AddAsync(new PersonImage { PersonId = id, Url = url });

            await _personImageRepository.SaveChangesAsync();
        }

        return true;
    }

    public async Task<bool> DeletePersonAsync(Guid id)
    {
        var person = await _personRepository.GetByIdAsync(id);
        if (person == null) return false;

        // Chặn xóa cứng nếu Person đang gắn với phim — PersonsController trả 400
        // "Đang gắn với phim, không thể xóa" đúng như message đã viết sẵn trong controller.
        if (await _personRepository.IsReferencedByAnyMovieAsync(id))
            return false;

        var images = await _personImageRepository.FindAsync(i => i.PersonId == id);
        _personImageRepository.RemoveRange(images);
        await _personImageRepository.SaveChangesAsync();

        _personRepository.Remove(person);
        await _personRepository.SaveChangesAsync();
        return true;
    }

    // ─── Dedup / Merge ──────────────────────────────────────────────────────

    public async Task<IEnumerable<PersonDuplicateGroupDTO>> FindDuplicatesByNameAsync()
    {
        var groups = await _personRepository.FindDuplicatesByNameAsync();

        return groups.Select(g => new PersonDuplicateGroupDTO
        {
            NormalizedName = g.Key,
            People = g.Select(MapToDTO).ToList()
        }).ToList();
    }

    /// <summary>
    /// Chuyển toàn bộ MovieCast/MovieDirector/PersonImage từ các Person trùng sang
    /// Primary, bù các field còn thiếu ở Primary, rồi xóa các Person trùng.
    /// </summary>
    public async Task<bool> MergePersonsAsync(MergePersonsDTO dto)
    {
        var primary = await _personRepository.GetByIdAsync(dto.PrimaryPersonId);
        if (primary == null) return false;

        foreach (var dupId in dto.DuplicatePersonIds.Where(id => id != dto.PrimaryPersonId))
        {
            var dup = await _personRepository.GetByIdAsync(dupId);
            if (dup == null) continue;

            if (!primary.TmdbPersonId.HasValue && dup.TmdbPersonId.HasValue) primary.TmdbPersonId = dup.TmdbPersonId;
            if (string.IsNullOrEmpty(primary.Biography)) primary.Biography = dup.Biography ?? primary.Biography;
            if (string.IsNullOrEmpty(primary.Birthday)) primary.Birthday = dup.Birthday ?? primary.Birthday;
            if (string.IsNullOrEmpty(primary.PlaceOfBirth)) primary.PlaceOfBirth = dup.PlaceOfBirth ?? primary.PlaceOfBirth;
            if (string.IsNullOrEmpty(primary.ProfileUrl)) primary.ProfileUrl = dup.ProfileUrl ?? primary.ProfileUrl;

            // Chuyển cast — nếu phim đó đã có primary trong cast rồi thì xóa bản dư (tránh trùng)
            foreach (var cast in await _castRepository.FindAsync(c => c.PersonId == dupId))
            {
                var exists = await _castRepository.FindOneAsync(c => c.MovieId == cast.MovieId && c.PersonId == primary.Id);
                if (exists != null) _castRepository.Remove(cast); else cast.PersonId = primary.Id;
            }

            foreach (var dir in await _directorRepository.FindAsync(d => d.PersonId == dupId))
            {
                var exists = await _directorRepository.FindOneAsync(d => d.MovieId == dir.MovieId && d.PersonId == primary.Id);
                if (exists != null) _directorRepository.Remove(dir); else dir.PersonId = primary.Id;
            }

            var primaryUrls = (await _personImageRepository.FindAsync(i => i.PersonId == primary.Id))
                .Select(i => i.Url).ToHashSet();
            foreach (var img in await _personImageRepository.FindAsync(i => i.PersonId == dupId))
            {
                if (primaryUrls.Contains(img.Url)) _personImageRepository.Remove(img); else img.PersonId = primary.Id;
            }

            _personRepository.Remove(dup);
        }

        _personRepository.Update(primary);
        await _castRepository.SaveChangesAsync();
        await _directorRepository.SaveChangesAsync();
        await _personImageRepository.SaveChangesAsync();
        await _personRepository.SaveChangesAsync();
        return true;
    }

    // ─── MapToDTO ───────────────────────────────────────────────────────────

    private static PersonDTO MapToDTO(Person p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        ProfileUrl = p.ProfileUrl,
        TmdbPersonId = p.TmdbPersonId,
        Biography = p.Biography,
        Birthday = p.Birthday,
        PlaceOfBirth = p.PlaceOfBirth,
        ProfileImages = p.Images?
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => i.Url)
            .ToList() ?? new()
    };
}