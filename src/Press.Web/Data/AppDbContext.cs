using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Press.Web.Domain;

namespace Press.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<MaterialType> MaterialTypes => Set<MaterialType>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialLink> MaterialLinks => Set<MaterialLink>();
    public DbSet<MaterialRelation> MaterialRelations => Set<MaterialRelation>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<MonitoredItem> MonitoredItems => Set<MonitoredItem>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Resource>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.DefaultMaterialType).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<MaterialType>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Material>(e =>
        {
            e.HasIndex(x => x.PublishedAt);
            e.HasIndex(x => x.IsOwn);
            e.HasMany(x => x.Types).WithMany(t => t.Materials).UsingEntity("MaterialTypeAssignments");
        });

        b.Entity<MaterialLink>(e =>
        {
            e.HasIndex(x => x.UrlKey).IsUnique();
            e.HasOne(x => x.Material).WithMany(m => m.Links).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Resource).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<MaterialRelation>(e =>
        {
            e.HasIndex(x => new { x.SourceMaterialId, x.DerivedMaterialId }).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.SourceMaterial).WithMany(m => m.Derived)
                .HasForeignKey(x => x.SourceMaterialId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DerivedMaterial).WithMany(m => m.Sources)
                .HasForeignKey(x => x.DerivedMaterialId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppUser>(e => e.HasIndex(x => x.Login).IsUnique());

        b.Entity<MonitoredItem>(e =>
        {
            e.HasIndex(x => x.UrlKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.PublishedAt });
            e.HasOne(x => x.Resource).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SuggestedMaterial).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.DerivedMaterial).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AppSetting>(e => e.HasKey(x => x.Key));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void Stamp()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Material>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var m = entry.Entity;
            if (entry.State == EntityState.Added && m.CreatedAt == default) m.CreatedAt = now;
            m.UpdatedAt = now;
            m.SearchKey = SearchText.Normalize(m.Title);
        }
        foreach (var entry in ChangeTracker.Entries<MaterialLink>())
        {
            if (entry.State == EntityState.Added && entry.Entity.AddedAt == default) entry.Entity.AddedAt = now;
        }
    }
}

public static class SearchText
{
    /// <summary>Нижний регистр, «ё» → «е» — чтобы поиск не зависел от написания.</summary>
    public static string Normalize(string? s) =>
        (s ?? "").ToLowerInvariant().Replace('ё', 'е');
}
