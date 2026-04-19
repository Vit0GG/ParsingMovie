using Microsoft.EntityFrameworkCore;
using Movie.Data.Entities;

namespace Movie.Data;

public static class DatabaseLoader
{
    public static void Load(MovieDataProcessor processor, MovieDbContext db)
    {
        var movies = db.Movies.AsNoTracking()
            .Include(m => m.MovieActors).ThenInclude(ma => ma.Person)
            .Include(m => m.MovieTags).ThenInclude(mt => mt.Tag)
            .ToList();

        foreach (var entity in movies)
        {
            var movie = new Movie
            {
                Title = entity.Title,
                Rating = entity.Rating,
                Director = entity.Director
            };

            foreach (var ma in entity.MovieActors)
                movie.Actors.Add(ma.Person.Name);

            foreach (var mt in entity.MovieTags)
                movie.Tags.Add(mt.Tag.Name);

            processor.MoviesByTitle[movie.Title] = movie;

            foreach (var actor in movie.Actors)
                processor.MoviesByPerson
                    .GetOrAdd(actor, _ => new HashSet<Movie>())
                    .Add(movie);

            foreach (var tag in movie.Tags)
                processor.MoviesByTag
                    .GetOrAdd(tag, _ => new HashSet<Movie>())
                    .Add(movie);
        }

        processor.BuildAllTop10();
    }
}
