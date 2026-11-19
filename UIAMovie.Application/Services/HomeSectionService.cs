// UIAMovie.Application/Services/HomeSectionService.cs

using System.Text.Json;
using System.Text.RegularExpressions;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Application.AutoRules;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public interface IHomeSectionService
{
    /// <summary>Public: section active + trong khung StartAt/EndAt + có ít nhất 1 item. Có cache.</summary>
    Task<List<HomeSectionDTO>> GetPublicAsync();

    /// <summary>Admin: tất cả section (kể cả tắt / hết hạn), không cache.</summary>
    Task<List<HomeSectionDTO>> GetAllForAdminAsync();
    Task<HomeSectionDTO?> GetByIdAsync(Guid id);
    Task<Guid> CreateAsync(UpsertHomeSectionDTO dto);
    Task<bool> UpdateAsync(Guid id, UpsertHomeSectionDTO dto);
    Task<bool> DeleteAsync(Guid id);
    Task ReorderAsync(List<Guid> sectionIds);
    Task<bool> SetActiveAsync(Guid id, bool isActive);
    HomeSectionMetaDTO GetMeta();
}

public class HomeSectionService : IHomeSectionService
{
    private readonly IHomeSectionRepository _repo;
    private readonly IMovieRepository _movieRepository;
    private readonly ITvShowRepository _tvShowRepository;
    private readonly ICacheService _cacheService;
    private readonly IClock _clock;
    private readonly Dictionary<string, IAutoRule> _rules;

    private const string CACHE_KEY = "home:sections";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public HomeSectionService(
        IHomeSectionRepository repo,
        IMovieRepository movieRepository,
        ITvShowRepository tvShowRepository,
        ICacheService cacheService,
        IClock clock,
        IEnumerable<IAutoRule> autoRules)
    {
        _repo = repo;
        _movieRepository = movieRepository;
        _tvShowRepository = tvShowRepository;
        _cacheService = cacheService;
        _clock = clock;
        _rules = autoRules.ToDictionary(r => r.Key);
    }

    // ═══════════════════════════════════════════════════════════════════
    // READ
    // ═══════════════════════════════════════════════════════════════════

    public async Task<List<HomeSectionDTO>> GetPublicAsync()
    {
        // Cache TẤT CẢ section active (đã resolve). Lọc khung giờ SAU khi đọc cache
        // để hẹn giờ luôn chính xác mà không phải đợi cache hết hạn.
        var all = await _cacheService.GetAsync<List<HomeSectionDTO>>(CACHE_KEY);
        if (all == null)
        {
            var sections = await _repo.GetAllWithItemsAsync(onlyActive: true);
            all = await BuildDtosAsync(sections);
            await _cacheService.SetAsync(CACHE_KEY, all, CacheTtl);
        }

        var now = DateTime.UtcNow;
        return all
            .Where(s => (!s.StartAt.HasValue || s.StartAt <= now) && (!s.EndAt.HasValue || s.EndAt >= now))
            .Where(s => s.Items.Count > 0)
            .ToList();
    }

    public async Task<List<HomeSectionDTO>> GetAllForAdminAsync()
    {
        var sections = await _repo.GetAllWithItemsAsync(onlyActive: false);
        // Trang admin chỉ cần danh sách cấu hình. KHÔNG resolve khối Auto (mỗi khối = vài query
        // nặng: trending/paged + genres + videos) — đó là nguyên nhân chính làm lưu xong phải chờ lâu.
        return await BuildDtosAsync(sections, resolveAuto: false);
    }

    public async Task<HomeSectionDTO?> GetByIdAsync(Guid id)
    {
        var section = await _repo.GetByIdWithItemsAsync(id);
        if (section == null) return null;
        return (await BuildDtosAsync(new List<HomeSection> { section })).First();
    }

    public HomeSectionMetaDTO GetMeta() => new()
    {
        Layouts = HomeSectionConstants.Layouts.ToList(),
        AutoRules = _rules.Keys.ToList(),
        AutoRuleDetails = _rules.Values.Select(r => new AutoRuleMetaDTO
        {
            Key = r.Key, Label = r.Label, ParamKind = r.ParamKind.ToString().ToLowerInvariant()
        }).ToList(),
        LayoutLimits = HomeSectionConstants.LayoutLimits,
        ContentTypes = HomeSectionConstants.ContentTypes.ToList(),
        AutoContentTypes = HomeSectionConstants.AutoContentTypes.ToList(),
        MaxManualItems = HomeSectionConstants.MaxManualItems
    };

    // ═══════════════════════════════════════════════════════════════════
    // WRITE
    // ═══════════════════════════════════════════════════════════════════

    public async Task<Guid> CreateAsync(UpsertHomeSectionDTO dto)
    {
        var items = await ValidateAndNormalizeAsync(dto);

        var section = new HomeSection
        {
            SortOrder = await _repo.GetMaxSortOrderAsync() + 1
        };
        ApplyFields(section, dto);
        foreach (var it in items)
        {
            it.HomeSectionId = section.Id;
            section.Items.Add(it);
        }

        await _repo.AddAsync(section);
        await _repo.SaveChangesAsync();

        await InvalidateCacheAsync();
        return section.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, UpsertHomeSectionDTO dto)
    {
        var section = await _repo.GetTrackedWithItemsAsync(id);
        if (section == null) return false;

        var items = await ValidateAndNormalizeAsync(dto);

        ApplyFields(section, dto);
        section.UpdatedAt = DateTime.UtcNow;

        foreach (var it in items) it.HomeSectionId = section.Id;
        _repo.ReplaceItems(section, items);

        await _repo.SaveChangesAsync();
        await InvalidateCacheAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var section = await _repo.GetByIdAsync(id);
        if (section == null) return false;

        _repo.Remove(section); // Items xoá theo cascade
        await _repo.SaveChangesAsync();

        await InvalidateCacheAsync();
        return true;
    }

    public async Task ReorderAsync(List<Guid> sectionIds)
    {
        if (sectionIds == null || sectionIds.Count == 0)
            throw new InvalidOperationException("Danh sách section không được rỗng.");

        var distinct = sectionIds.Distinct().ToList();
        var sections = await _repo.GetTrackedByIdsAsync(distinct);
        var byId = sections.ToDictionary(s => s.Id);

        var order = 0;
        foreach (var id in distinct)
        {
            if (!byId.TryGetValue(id, out var s)) continue;
            s.SortOrder = order++;
            s.UpdatedAt = DateTime.UtcNow;
        }

        await _repo.SaveChangesAsync();
        await InvalidateCacheAsync();
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive)
    {
        var section = await _repo.GetByIdAsync(id);
        if (section == null) return false;

        section.IsActive = isActive;
        section.UpdatedAt = DateTime.UtcNow;
        _repo.Update(section);
        await _repo.SaveChangesAsync();

        await InvalidateCacheAsync();
        return true;
    }

    private Task InvalidateCacheAsync() => _cacheService.RemoveAsync(CACHE_KEY);

    // ═══════════════════════════════════════════════════════════════════
    // VALIDATE + MAP INPUT
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<HomeSectionItem>> ValidateAndNormalizeAsync(UpsertHomeSectionDTO dto)
    {
        dto.Title = (dto.Title ?? "").Trim();
        if (dto.Title.Length == 0) throw new InvalidOperationException("Tên khối không được để trống.");
        if (dto.Title.Length > 120) throw new InvalidOperationException("Tên khối tối đa 120 ký tự.");

        dto.Subtitle = string.IsNullOrWhiteSpace(dto.Subtitle) ? null : dto.Subtitle.Trim();
        if (dto.Subtitle is { Length: > 250 }) throw new InvalidOperationException("Phụ đề tối đa 250 ký tự.");

        dto.Layout = (dto.Layout ?? "").Trim().ToLowerInvariant();
        if (!HomeSectionConstants.Layouts.Contains(dto.Layout))
            throw new InvalidOperationException(
                $"Layout '{dto.Layout}' không hợp lệ. Cho phép: {string.Join(", ", HomeSectionConstants.Layouts)}.");

        dto.StartAt = ToUtc(dto.StartAt);
        dto.EndAt = ToUtc(dto.EndAt);
        if (dto.StartAt.HasValue && dto.EndAt.HasValue && dto.StartAt >= dto.EndAt)
            throw new InvalidOperationException("StartAt phải nhỏ hơn EndAt.");

        if (dto.Config != null && JsonSerializer.Serialize(dto.Config).Length > HomeSectionConstants.MaxConfigLength)
            throw new InvalidOperationException("Config quá lớn.");

        // Màu nhấn: chỉ nhận #RRGGBB để không nhét chuỗi lạ vào style của trang chủ
        if (dto.Config != null && dto.Config.TryGetValue("accent", out var accentObj))
        {
            var accent = accentObj?.ToString();
            if (string.IsNullOrWhiteSpace(accent)) dto.Config.Remove("accent");
            else if (!Regex.IsMatch(accent, "^#[0-9a-fA-F]{6}$"))
                throw new InvalidOperationException("Màu nhấn phải có dạng #RRGGBB.");
        }

        var sourceType = (dto.SourceType ?? "").Trim();
        if (sourceType.Equals("Manual", StringComparison.OrdinalIgnoreCase))
        {
            dto.SourceType = "Manual";
            dto.AutoRule = null; dto.AutoParam = null;
            return await ValidateManualItemsAsync(dto);
        }

        if (sourceType.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            dto.SourceType = "Auto";
            dto.AutoRule = (dto.AutoRule ?? "").Trim().ToLowerInvariant();
            if (!_rules.TryGetValue(dto.AutoRule, out var rule))
                throw new InvalidOperationException(
                    $"AutoRule '{dto.AutoRule}' không hợp lệ. Cho phép: {string.Join(", ", _rules.Keys)}.");

            dto.AutoContentType = (dto.AutoContentType ?? "all").Trim().ToLowerInvariant();
            if (!HomeSectionConstants.AutoContentTypes.Contains(dto.AutoContentType))
                throw new InvalidOperationException("AutoContentType phải là movie, tvshow hoặc all.");

            var paramError = rule.ValidateParam(dto.AutoParam);
            if (paramError != null) throw new InvalidOperationException(paramError);
            if (rule.ParamKind == AutoParamKind.None) dto.AutoParam = null;

            // Layout chỉ hiển thị N phim thì không cho lưu / query nhiều hơn N
            dto.ItemLimit = HomeSectionConstants.EffectiveLimit(dto.Layout, dto.ItemLimit);
            return new List<HomeSectionItem>();
        }

        throw new InvalidOperationException("SourceType phải là Manual hoặc Auto.");
    }

    private async Task<List<HomeSectionItem>> ValidateManualItemsAsync(UpsertHomeSectionDTO dto)
    {
        var seen = new HashSet<(string, Guid)>();
        var normalized = new List<(string Type, Guid Id)>();

        foreach (var i in dto.Items ?? new())
        {
            var type = (i.ContentType ?? "").Trim().ToLowerInvariant();
            if (!HomeSectionConstants.ContentTypes.Contains(type))
                throw new InvalidOperationException($"ContentType '{i.ContentType}' không hợp lệ (movie | tvshow).");
            if (i.ContentId == Guid.Empty)
                throw new InvalidOperationException("ContentId không hợp lệ.");
            if (seen.Add((type, i.ContentId))) normalized.Add((type, i.ContentId));
        }

        if (normalized.Count > HomeSectionConstants.MaxManualItems)
            throw new InvalidOperationException($"Tối đa {HomeSectionConstants.MaxManualItems} phim mỗi khối.");

        // Kiểm tra tồn tại (không FK cứng vì polymorphic)
        var movieIds = normalized.Where(n => n.Type == "movie").Select(n => n.Id).ToList();
        var tvIds = normalized.Where(n => n.Type == "tvshow").Select(n => n.Id).ToList();

        if (movieIds.Count > 0)
        {
            var found = (await _movieRepository.FindAsync(m => movieIds.Contains(m.Id))).Select(m => m.Id).ToHashSet();
            var missing = movieIds.Where(id => !found.Contains(id)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"Không tìm thấy phim: {string.Join(", ", missing)}");
        }
        if (tvIds.Count > 0)
        {
            var found = (await _tvShowRepository.FindAsync(t => tvIds.Contains(t.Id))).Select(t => t.Id).ToHashSet();
            var missing = tvIds.Where(id => !found.Contains(id)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"Không tìm thấy TV show: {string.Join(", ", missing)}");
        }

        return normalized
            .Select((n, idx) => new HomeSectionItem { ContentType = n.Type, ContentId = n.Id, Order = idx })
            .ToList();
    }

    private static void ApplyFields(HomeSection s, UpsertHomeSectionDTO dto)
    {
        s.Title = dto.Title;
        s.Subtitle = dto.Subtitle;
        s.Layout = dto.Layout;
        s.ConfigJson = dto.Config == null || dto.Config.Count == 0 ? null : JsonSerializer.Serialize(dto.Config);
        s.IsActive = dto.IsActive;
        s.StartAt = dto.StartAt;
        s.EndAt = dto.EndAt;
        s.SourceType = dto.SourceType;
        s.AutoRule = dto.AutoRule;
        s.AutoParam = dto.AutoParam;
        s.AutoContentType = dto.SourceType == "Auto" ? dto.AutoContentType : "all";
        s.ItemLimit = dto.SourceType == "Auto" ? dto.ItemLimit : 20;
    }

    private static DateTime? ToUtc(DateTime? d) => d switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } => d,
        { Kind: DateTimeKind.Local } => d.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d.Value, DateTimeKind.Utc)
    };

    // ═══════════════════════════════════════════════════════════════════
    // RESOLVE: entity → DTO có sẵn nội dung
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<HomeSectionDTO>> BuildDtosAsync(List<HomeSection> sections, bool resolveAuto = true)
    {
        // Gom id của TẤT CẢ khối Manual để chỉ tốn 2 query (movie + tvshow) cho cả trang chủ
        var manual = sections.Where(s => s.SourceType == "Manual").SelectMany(s => s.Items).ToList();
        var movieCards = await LoadMovieCardsAsync(manual.Where(i => i.ContentType == "movie").Select(i => i.ContentId).Distinct().ToList());
        var tvCards = await LoadTvCardsAsync(manual.Where(i => i.ContentType == "tvshow").Select(i => i.ContentId).Distinct().ToList());

        var result = new List<HomeSectionDTO>();
        foreach (var s in sections)
        {
            var dto = new HomeSectionDTO
            {
                Id = s.Id, Title = s.Title, Subtitle = s.Subtitle, Layout = s.Layout,
                Config = ParseConfig(s.ConfigJson), SortOrder = s.SortOrder, IsActive = s.IsActive,
                StartAt = s.StartAt, EndAt = s.EndAt, SourceType = s.SourceType,
                AutoRule = s.AutoRule, AutoParam = s.AutoParam, AutoContentType = s.AutoContentType,
                ItemLimit = s.ItemLimit
            };

            if (s.SourceType == "Auto")
            {
                // Admin list: bỏ qua resolve (Items rỗng). Public/GetById: resolve như cũ.
                if (resolveAuto)
                {
                    var cards = await ResolveAutoAsync(s);
                    dto.Items = cards.Select((c, i) => new HomeSectionItemDTO
                    {
                        Id = Guid.Empty, ContentType = c.ContentType, ContentId = c.Id, Order = i, Content = c
                    }).ToList();
                }
            }
            else
            {
                // Item trỏ tới phim đã xoá / chưa publish sẽ tự bị bỏ qua
                dto.Items = s.Items.OrderBy(i => i.Order)
                    .Select(i =>
                    {
                        var map = i.ContentType == "tvshow" ? tvCards : movieCards;
                        return map.TryGetValue(i.ContentId, out var card)
                            ? new HomeSectionItemDTO
                            {
                                Id = i.Id, ContentType = i.ContentType, ContentId = i.ContentId, Order = i.Order, Content = card
                            }
                            : null;
                    })
                    .Where(x => x != null).Select(x => x!).ToList();
            }

            result.Add(dto);
        }
        return result;
    }

    private async Task<Dictionary<Guid, HomeContentCardDTO>> LoadMovieCardsAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return new();
        // Mode Ids của GetPagedAsync không lọc IsPublished → tự lọc ở đây
        var (movies, _) = await _movieRepository.GetPagedAsync(new FilterMoviesDTO { Ids = ids });
        return movies.Where(m => m.IsPublished).ToDictionary(m => m.Id, m => HomeCardMapper.ToCard(m));
    }

    private async Task<Dictionary<Guid, HomeContentCardDTO>> LoadTvCardsAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return new();
        var (shows, _) = await _tvShowRepository.GetPagedAsync(new FilterTvShowsDTO { Ids = ids });
        return shows.Where(t => t.IsPublished).ToDictionary(t => t.Id, t => HomeCardMapper.ToCard(t));
    }

    // ── Auto sources ─────────────────────────────────────────────────────────

    private async Task<List<HomeContentCardDTO>> ResolveAutoAsync(HomeSection s)
    {
        if (!_rules.TryGetValue(s.AutoRule ?? "", out var rule)) return new();

        // Kẹp theo trần của layout (cũng áp cho dữ liệu cũ đã lưu limit lớn hơn)
        var limit = HomeSectionConstants.EffectiveLimit(s.Layout, s.ItemLimit);
        var wantMovie = s.AutoContentType is "movie" or "all";
        var wantTv = s.AutoContentType is "tvshow" or "all";

        var pool = await rule.ResolveAsync(new AutoRuleContext(s.AutoParam, wantMovie, wantTv, limit));
        return pool.OrderByDescending(p => p.Key).Take(limit).Select(p => p.Card).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════
    // MAPPERS
    // ═══════════════════════════════════════════════════════════════════

    private static Dictionary<string, object>? ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, object>>(json); }
        catch (JsonException) { return null; }
    }
}