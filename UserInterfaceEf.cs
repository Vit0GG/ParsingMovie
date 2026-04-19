using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Movie.Data;

namespace Movie;

public class UserInterfaceEf
{
    private readonly MovieDbContext _db;

    public UserInterfaceEf(MovieDbContext db)
    {
        _db = db;
    }

    public void Run()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== МЕНЮ ===");
            Console.WriteLine("1. Поиск фильма");
            Console.WriteLine("2. Фильмы по актёру");
            Console.WriteLine("3. Фильмы по тегу");
            Console.WriteLine("4. Топ-5 по рейтингу");
            Console.WriteLine("5. Статистика");
            Console.WriteLine("6. Проверка связей (debug)");
            Console.WriteLine("7. Выход");

            Console.Write("\nВыбор: ");
            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1": SearchMovie(); break;
                case "2": SearchByActor(); break;
                case "3": SearchByTag(); break;
                case "4": ShowTopMovies(); break;
                case "5": ShowStats(); break;
                case "6": DebugRelations(); break;
                case "7": return;
                default:
                    Console.WriteLine("Неверный выбор");
                    Pause();
                    break;
            }
        }
    }

    private void SearchMovie()
    {
        Console.Write("Название фильма: ");
        var query = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(query))
            return;

        var movie = _db.Movies
            .AsNoTracking()
            .FirstOrDefault(m => EF.Functions.Like(m.Title.ToLower(), $"%{query.ToLower()}%"));

        if (movie == null)
        {
            Console.WriteLine("Фильм не найден");
            Pause();
            return;
        }

        Console.WriteLine($"\n=== {movie.Title} ===");
        Console.WriteLine($"Рейтинг: {movie.Rating:F1}");
        Console.WriteLine($"Режиссёр: {(string.IsNullOrEmpty(movie.Director) ? "Неизвестен" : movie.Director)}");

        var actors = _db.MovieActors
            .AsNoTracking()
            .Where(ma => ma.MovieId == movie.Id)
            .Select(ma => ma.Person.Name)
            .Distinct()
            .Take(20)
            .ToList();

        Console.WriteLine($"\nАктёры ({actors.Count}):");
        if (actors.Any())
        {
            foreach (var actor in actors)
                Console.WriteLine($"  • {actor}");
        }
        else
        {
            Console.WriteLine("  (нет данных)");
        }

        var tags = _db.MovieTags
            .AsNoTracking()
            .Where(mt => mt.MovieId == movie.Id)
            .Select(mt => mt.Tag.Name)
            .Distinct()
            .ToList();

        Console.WriteLine($"\nТеги ({tags.Count}):");
        if (tags.Any())
        {
            foreach (var tag in tags)
                Console.WriteLine($"  • {tag}");
        }
        else
        {
            Console.WriteLine("  (нет данных)");
        }

        Pause();
    }

    private void SearchByActor()
    {
        Console.Write("Имя актёра: ");
        var name = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(name))
            return;

        var movies = _db.MovieActors
            .AsNoTracking()
            .Where(ma => EF.Functions.Like(ma.Person.Name.ToLower(), $"%{name.ToLower()}%"))
            .Select(ma => new { ma.Movie.Title, ma.Movie.Rating })
            .Distinct()
            .OrderByDescending(m => m.Rating)
            .Take(20)
            .ToList();

        if (!movies.Any())
        {
            Console.WriteLine("Ничего не найдено");
            Pause();
            return;
        }

        Console.WriteLine($"\nФильмы с актёром '{name}':");
        int i = 1;
        foreach (var m in movies)
            Console.WriteLine($"{i++}. {m.Title} ({m.Rating:F1})");

        Pause();
    }

    private void SearchByTag()
    {
        Console.Write("Тег: ");
        var tag = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(tag))
            return;

        var movies = _db.MovieTags
            .AsNoTracking()
            .Where(mt => EF.Functions.Like(mt.Tag.Name.ToLower(), $"%{tag.ToLower()}%"))
            .Select(mt => new { mt.Movie.Title, mt.Movie.Rating })
            .Distinct()
            .OrderByDescending(m => m.Rating)
            .Take(20)
            .ToList();

        if (!movies.Any())
        {
            Console.WriteLine("Ничего не найдено");
            Pause();
            return;
        }

        Console.WriteLine($"\nФильмы с тегом '{tag}':");
        int i = 1;
        foreach (var m in movies)
            Console.WriteLine($"{i++}. {m.Title} ({m.Rating:F1})");

        Pause();
    }

    private void ShowTopMovies()
    {
        var movies = _db.Movies
            .AsNoTracking()
            .Where(m => m.Rating > 0)
            .OrderByDescending(m => m.Rating)
            .Take(10)
            .ToList();

        Console.WriteLine("\n=== Топ-10 по рейтингу ===");
        int i = 1;
        foreach (var m in movies)
            Console.WriteLine($"{i++}. {m.Title} ({m.Rating:F1})");

        Pause();
    }

    private void ShowStats()
    {
        Console.WriteLine("\n=== СТАТИСТИКА ===");
        Console.WriteLine($"Фильмов: {_db.Movies.Count()}");
        Console.WriteLine($"Персон: {_db.Persons.Count()}");
        Console.WriteLine($"Тегов: {_db.Tags.Count()}");
        Console.WriteLine($"Связей актёр-фильм: {_db.MovieActors.Count()}");
        Console.WriteLine($"Связей тег-фильм: {_db.MovieTags.Count()}");
        Pause();
    }

    
    private void DebugRelations()
    {
        Console.WriteLine("\n=== DEBUG: Проверка связей ===\n");

        var movieWithActors = _db.Movies
            .AsNoTracking()
            .FirstOrDefault(m => _db.MovieActors.Any(ma => ma.MovieId == m.Id));

        if (movieWithActors != null)
        {
            Console.WriteLine($"Фильм: {movieWithActors.Title} (ID={movieWithActors.Id})");

            var actorCount = _db.MovieActors.Count(ma => ma.MovieId == movieWithActors.Id);
            Console.WriteLine($"Связей MovieActor: {actorCount}");

            var actors = _db.MovieActors
                .Where(ma => ma.MovieId == movieWithActors.Id)
                .Select(ma => ma.Person.Name)
                .Take(5)
                .ToList();

            Console.WriteLine("Актёры: " + string.Join(", ", actors));
        }
        else
        {
            Console.WriteLine("Нет фильмов со связями MovieActor!");
        }

        Console.WriteLine();

        var movieWithTags = _db.Movies
            .AsNoTracking()
            .FirstOrDefault(m => _db.MovieTags.Any(mt => mt.MovieId == m.Id));

        if (movieWithTags != null)
        {
            Console.WriteLine($"Фильм: {movieWithTags.Title} (ID={movieWithTags.Id})");

            var tagCount = _db.MovieTags.Count(mt => mt.MovieId == movieWithTags.Id);
            Console.WriteLine($"Связей MovieTag: {tagCount}");

            var tags = _db.MovieTags
                .Where(mt => mt.MovieId == movieWithTags.Id)
                .Select(mt => mt.Tag.Name)
                .Take(5)
                .ToList();

            Console.WriteLine("Теги: " + string.Join(", ", tags));
        }
        else
        {
            Console.WriteLine("Нет фильмов со связями MovieTag!");
        }

        Pause();
    }

    private void Pause()
    {
        Console.WriteLine("\nНажмите любую клавишу...");
        Console.ReadKey();
    }
}