using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataTrustHub.Infrastructure.Persistance.Configurations
{
    public class DbAuditConfiguration : IEntityTypeConfiguration<DbAudit>
    {
        public void Configure(EntityTypeBuilder<DbAudit> builder)
        {
            builder.ToTable("Audits");
            
            builder.HasKey(a => a.Id);
            
            builder.Property(a => a.Action)
                .IsRequired()
                .HasMaxLength(50);
            
            builder.Property(a => a.EntityType)
                .IsRequired()
                .HasMaxLength(100);
            
            builder.Property(a => a.EntityId)
                .IsRequired();
            
            builder.Property(a => a.Timestamp)
                .IsRequired();
            
            builder.Property(a => a.UserId)
                .HasMaxLength(450);
            
            builder.Property(a => a.IpAddress)
                .HasMaxLength(45);
            
            builder.Property(a => a.UserAgent)
                .HasMaxLength(500);
            
            builder.Property(a => a.RequestPath)
                .HasMaxLength(500);
            
            builder.Property(a => a.RequestMethod)
                .HasMaxLength(10);
            
            builder.HasIndex(a => a.EntityType);
            builder.HasIndex(a => a.EntityId);
            builder.HasIndex(a => a.Timestamp);
        }
    }
}

