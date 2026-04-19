using Movie.Data.Entities;

namespace Movie.Data;

public static class DatabaseSeeder
{
    public static void Save(MovieDataProcessor processor, MovieDbContext db)
    {
        Console.WriteLine("Сохранение данных в БД...");

        var movieMap = new Dictionary<Movie, MovieEntity>();
        var personMap = new Dictionary<string, PersonEntity>();
        var tagMap = new Dictionary<string, TagEntity>();

        Console.WriteLine("  Добавление фильмов...");
        foreach (var movie in processor.MoviesByTitle.Values)
        {
            var entity = new MovieEntity
            {
                Title = movie.Title,
                Rating = movie.Rating,
                Director = movie.Director ?? ""
            };
            db.Movies.Add(entity);
            movieMap[movie] = entity;
        }
        db.SaveChanges();
        Console.WriteLine($"  Фильмов добавлено: {db.Movies.Count()}");

        Console.WriteLine("  Добавление персон...");
        var allActors = processor.MoviesByTitle.Values
            .SelectMany(m => m.Actors)
            .Distinct()
            .ToList();

        foreach (var actorName in allActors)
        {
            var person = new PersonEntity { Name = actorName };
            db.Persons.Add(person);
            personMap[actorName] = person;
        }
        db.SaveChanges();
        Console.WriteLine($"  Персон добавлено: {db.Persons.Count()}");

        Console.WriteLine("  Добавление тегов...");
        var allTags = processor.MoviesByTitle.Values
            .SelectMany(m => m.Tags)
            .Distinct()
            .ToList();

        foreach (var tagName in allTags)
        {
            var tag = new TagEntity { Name = tagName };
            db.Tags.Add(tag);
            tagMap[tagName] = tag;
        }
        db.SaveChanges();
        Console.WriteLine($"  Тегов добавлено: {db.Tags.Count()}");

        Console.WriteLine("  Создание связей актёр-фильм...");
        int actorLinksCount = 0;
        foreach (var movie in processor.MoviesByTitle.Values)
        {
            var movieEntity = movieMap[movie];

            foreach (var actorName in movie.Actors)
            {
                if (personMap.TryGetValue(actorName, out var person))
                {
                    db.MovieActors.Add(new MovieActor
                    {
                        MovieId = movieEntity.Id,
                        PersonId = person.Id
                    });
                    actorLinksCount++;
                }
            }
        }
        db.SaveChanges();
        Console.WriteLine($"  Связей актёр-фильм: {actorLinksCount}");

        Console.WriteLine("  Создание связей тег-фильм...");
        int tagLinksCount = 0;
        foreach (var movie in processor.MoviesByTitle.Values)
        {
            var movieEntity = movieMap[movie];

            foreach (var tagName in movie.Tags)
            {
                if (tagMap.TryGetValue(tagName, out var tag))
                {
                    db.MovieTags.Add(new MovieTag
                    {
                        MovieId = movieEntity.Id,
                        TagId = tag.Id
                    });
                    tagLinksCount++;
                }
            }
        }
        db.SaveChanges();
        Console.WriteLine($"  Связей тег-фильм: {tagLinksCount}");

        Console.WriteLine("\n=== ИТОГО В БД ===");
        Console.WriteLine($"  Фильмов: {db.Movies.Count()}");
        Console.WriteLine($"  Персон: {db.Persons.Count()}");
        Console.WriteLine($"  Тегов: {db.Tags.Count()}");
        Console.WriteLine($"  MovieActors: {db.MovieActors.Count()}");
        Console.WriteLine($"  MovieTags: {db.MovieTags.Count()}");
    }
}