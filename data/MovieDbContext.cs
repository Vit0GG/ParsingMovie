using Microsoft.EntityFrameworkCore;
using Movie.Data.Entities;

namespace Movie.Data;

public class MovieDbContext : DbContext
{
    public DbSet<MovieActor> MovieActors => Set<MovieActor>();
    public DbSet<MovieTag> MovieTags => Set<MovieTag>();
    public DbSet<MovieEntity> Movies => Set<MovieEntity>();
    public DbSet<PersonEntity> Persons => Set<PersonEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=movies.db");
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<MovieActor>(entity =>
        {
            entity.HasKey(x => new { x.MovieId, x.PersonId });

            entity.HasOne(ma => ma.Movie)
                  .WithMany(m => m.MovieActors)
                  .HasForeignKey(ma => ma.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ma => ma.Person)
                  .WithMany(p => p.MovieActors)
                  .HasForeignKey(ma => ma.PersonId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<MovieTag>(entity =>
        {
            entity.HasKey(x => new { x.MovieId, x.TagId });

            entity.HasOne(mt => mt.Movie)
                  .WithMany(m => m.MovieTags)
                  .HasForeignKey(mt => mt.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(mt => mt.Tag)
                  .WithMany(t => t.MovieTags)
                  .HasForeignKey(mt => mt.TagId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<MovieEntity>()
            .HasIndex(m => m.Title);

        model.Entity<PersonEntity>()
            .HasIndex(p => p.Name);

        model.Entity<TagEntity>()
            .HasIndex(t => t.Name);
    }
}