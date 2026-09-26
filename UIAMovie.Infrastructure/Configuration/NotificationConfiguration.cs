using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Configuration;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);

        // Tối ưu paging thông báo theo User và thời gian
        builder.HasIndex(n => new { n.UserId, n.CreatedAt })
            .IsDescending(false, true);

        // Filtered index tối ưu CountUnreadAsync
        builder.HasIndex(n => new { n.UserId, n.IsRead });
    }
}