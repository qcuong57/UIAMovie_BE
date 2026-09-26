using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class NotificationRepository : Repository<Notification>, INotificationRepository
{
    private readonly MovieDbContext _db;

    public NotificationRepository(MovieDbContext db) : base(db)
    {
        _db = db;
    }

    public async Task<(List<(Notification Notif, bool IsRead)> Items, int TotalCount)> GetUserNotificationsPagedAsync(
        Guid userId, int page, int pageSize, string? excludeType = null, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize > 100) pageSize = 100;

        // ID các broadcast notification user này đã đọc
        var readSystemNotifIds = _db.NotificationReads
            .Where(nr => nr.UserId == userId && !nr.IsDismissed)
            .Select(nr => nr.NotificationId);

        // ID các broadcast notification user này đã dismiss (ẩn/xóa)
        var dismissedSystemNotifIds = _db.NotificationReads
            .Where(nr => nr.UserId == userId && nr.IsDismissed)
            .Select(nr => nr.NotificationId);

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n =>
                (n.UserId == userId || n.UserId == null)
                && !dismissedSystemNotifIds.Contains(n.Id));

        // Lọc bỏ loại thông báo không mong muốn (ví dụ admin_announcement cho chuông)
        if (!string.IsNullOrEmpty(excludeType))
        {
            query = query.Where(n => n.Type != excludeType);
        }

        var totalCount = await query.CountAsync(ct);

        var rawList = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new
            {
                Notification = n,
                IsRead = n.UserId != null ? n.IsRead : readSystemNotifIds.Contains(n.Id)
            })
            .ToListAsync(ct);

        var items = rawList.Select(x => (x.Notification, x.IsRead)).ToList();
        return (items, totalCount);
    }

    public async Task<int> CountUnreadAsync(Guid userId, string? excludeType = null, CancellationToken ct = default)
    {
        // 1. Số noti cá nhân chưa đọc
        var personalQuery = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead);

        if (!string.IsNullOrEmpty(excludeType))
        {
            personalQuery = personalQuery.Where(n => n.Type != excludeType);
        }

        var unreadPersonal = await personalQuery.CountAsync(ct);

        // 2. Số noti hệ thống đã đọc (không tính dismissed)
        var readSystemQuery = from n in _db.Notifications
                              join nr in _db.NotificationReads on n.Id equals nr.NotificationId
                              where n.UserId == null && nr.UserId == userId && !nr.IsDismissed
                              select n;

        if (!string.IsNullOrEmpty(excludeType))
        {
            readSystemQuery = readSystemQuery.Where(n => n.Type != excludeType);
        }

        var readSystemCount = await readSystemQuery.CountAsync(ct);

        // 3. Tổng broadcast chưa bị dismiss
        var dismissedSystemNotifIds = _db.NotificationReads
            .Where(nr => nr.UserId == userId && nr.IsDismissed)
            .Select(nr => nr.NotificationId);

        var totalSystemQuery = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == null && !dismissedSystemNotifIds.Contains(n.Id));

        if (!string.IsNullOrEmpty(excludeType))
        {
            totalSystemQuery = totalSystemQuery.Where(n => n.Type != excludeType);
        }

        var totalSystemNotifs = await totalSystemQuery.CountAsync(ct);
        var unreadSystem = Math.Max(0, totalSystemNotifs - readSystemCount);

        return unreadPersonal + unreadSystem;
    }

    public async Task<(List<Notification> Items, int TotalCount)> GetPublicAnnouncementsPagedAsync(
        int page, int pageSize, string? type = null, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize > 100) pageSize = 100;

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == null); // Chỉ thông báo toàn hệ thống

        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(n => n.Type == type);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var notif = await _db.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == notificationId, ct);

        if (notif == null) return false;

        if (notif.UserId == userId)
        {
            var affected = await _db.Notifications
                .Where(n => n.Id == notificationId && n.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
            return affected > 0;
        }

        if (notif.UserId == null)
        {
            var existing = await _db.NotificationReads
                .FirstOrDefaultAsync(nr => nr.NotificationId == notificationId && nr.UserId == userId, ct);

            if (existing == null)
            {
                _db.NotificationReads.Add(new NotificationRead
                {
                    NotificationId = notificationId,
                    UserId = userId,
                    ReadAt = DateTime.UtcNow,
                    IsDismissed = false
                });
                await _db.SaveChangesAsync(ct);
            }
            return true;
        }

        return false;
    }

    public async Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
    {
        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

        var unreadSystemIds = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == null
                && !_db.NotificationReads.Any(nr => nr.NotificationId == n.Id && nr.UserId == userId))
            .Select(n => n.Id)
            .ToListAsync(ct);

        if (unreadSystemIds.Any())
        {
            var reads = unreadSystemIds.Select(id => new NotificationRead
            {
                NotificationId = id,
                UserId = userId,
                ReadAt = DateTime.UtcNow,
                IsDismissed = false
            });
            _db.NotificationReads.AddRange(reads);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var notif = await _db.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == notificationId, ct);

        if (notif == null) return false;

        if (notif.UserId == userId)
        {
            var affected = await _db.Notifications
                .Where(n => n.Id == notificationId && n.UserId == userId)
                .ExecuteDeleteAsync(ct);
            return affected > 0;
        }

        // Với broadcast: chỉ đánh dấu ẩn đối với riêng user này
        if (notif.UserId == null)
        {
            var existing = await _db.NotificationReads
                .FirstOrDefaultAsync(nr => nr.NotificationId == notificationId && nr.UserId == userId, ct);

            if (existing != null)
            {
                existing.IsDismissed = true;
            }
            else
            {
                _db.NotificationReads.Add(new NotificationRead
                {
                    NotificationId = notificationId,
                    UserId = userId,
                    ReadAt = DateTime.UtcNow,
                    IsDismissed = true
                });
            }

            await _db.SaveChangesAsync(ct);
            return true;
        }

        return false;
    }

    public async Task<bool> AdminUpdateAsync(
        Guid id, string title, string message, string? linkUrl, string? thumbnailUrl, string type, CancellationToken ct = default)
    {
        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (notif == null) return false;

        notif.Title = title;
        notif.Message = message;
        notif.LinkUrl = linkUrl;
        notif.ThumbnailUrl = thumbnailUrl;
        notif.Type = type;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> AdminHardDeleteAsync(Guid id, CancellationToken ct = default)
    {
        // Xóa các bản ghi đã đọc liên quan trước
        var reads = _db.NotificationReads.Where(nr => nr.NotificationId == id);
        _db.NotificationReads.RemoveRange(reads);

        // Xóa thông báo vĩnh viễn
        var affected = await _db.Notifications
            .Where(n => n.Id == id)
            .ExecuteDeleteAsync(ct);

        return affected > 0;
    }

    public Task<int> DeleteOlderThanAsync(DateTime threshold, CancellationToken ct = default)
    {
        return _db.Notifications
            .Where(n => n.CreatedAt < threshold)
            .ExecuteDeleteAsync(ct);
    }
}