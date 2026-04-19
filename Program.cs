using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Movie;
using Movie.Data;
using Microsoft.EntityFrameworkCore;
namespace Movie;

public class Movie
{
    public string Title { get; set; } = "";
    public HashSet<string> Actors { get; } = new();
    public string Director { get; set; } = "";
    public HashSet<string> Tags { get; } = new();
    public double Rating { get; set; }
    public List<(Movie movie, double score)> Top10SimilarMovies { get; set; } = new();

    public override string ToString()
    {
        var director = string.IsNullOrEmpty(Director) ? "Неизвестно" : Director;
        return $"{Title} (Рейтинг: {Rating:F1})\n" +
               $"Режиссер: {director}\n" +
               $"Актеры: {string.Join(", ", Actors.Take(5))}\n" +
               $"Тэги: {string.Join(", ", Tags.Take(5))}";
    }

    public double ComputeSimilarity(Movie other)
    {
        if (other == null || other == this) return 0;

        int commonActors = Actors.Intersect(other.Actors).Count();
        
        int sameDirector = (!string.IsNullOrEmpty(Director) && Director == other.Director) ? 1 : 0;
        
        int commonTags = Tags.Intersect(other.Tags).Count();

        double overlapScore = (commonActors + sameDirector + commonTags) / 21.0 * 0.5;

        double ratingScore = Math.Min(other.Rating / 10.0, 1.0) * 0.5;

        return Math.Min(1.0, overlapScore + ratingScore);
    }

    public void BuildTop10(MovieDataProcessor processor)
    {
        var candidates = new HashSet<Movie>();

        foreach (var actor in Actors)
            if (processor.MoviesByPerson.TryGetValue(actor, out var movies))
                candidates.UnionWith(movies);

        if (!string.IsNullOrEmpty(Director))
            if (processor.MoviesByPerson.TryGetValue(Director, out var dirMovies))
                candidates.UnionWith(dirMovies);

        foreach (var tag in Tags)
            if (processor.MoviesByTag.TryGetValue(tag, out var tagMovies))
                candidates.UnionWith(tagMovies);

        candidates.Remove(this);

        Top10SimilarMovies = candidates
            .Select(c => (movie: c, score: ComputeSimilarity(c)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(10)
            .ToList();
    }
}

public class MovieDataProcessor
{
    public ConcurrentDictionary<string, Movie> MoviesByTitle { get; } = new();
    public ConcurrentDictionary<string, HashSet<Movie>> MoviesByPerson { get; } = new();
    public ConcurrentDictionary<string, HashSet<Movie>> MoviesByTag { get; } = new();


    private readonly ConcurrentDictionary<string, string> _imdbToMovieLens = new();
    private readonly ConcurrentDictionary<string, string> _movieLensToImdb = new();
    private readonly ConcurrentDictionary<string, string> _personCodesToNames = new();
    private readonly ConcurrentDictionary<string, string> _tagCodesToNames = new();
    private readonly ConcurrentDictionary<string, string> _movieCodesToTitles = new();

    private CancellationTokenSource _cts = new();
    private readonly int _consumerCount = Math.Max(2, Environment.ProcessorCount - 1);

    public void CancelProcessing() => _cts.Cancel();

    
    public void BuildAllTop10()
    {
        Console.WriteLine("Строим топ-10 похожих фильмов...");
        var sw = Stopwatch.StartNew();
        
        Parallel.ForEach(MoviesByTitle.Values, movie => movie.BuildTop10(this));
        
        Console.WriteLine($"Топ-10 построены за {sw.ElapsedMilliseconds} мс");
    }

    public List<(Movie movie, double score)> BuildRecommendationsFromMany(List<Movie> selectedMovies)
    {
        if (selectedMovies == null || selectedMovies.Count == 0)
            return new List<(Movie, double)>();

        var candidates = selectedMovies
            .SelectMany(m => m.Top10SimilarMovies.Select(x => x.movie))
            .Except(selectedMovies)
            .Distinct()
            .ToList();

        return candidates
            .Select(c => (movie: c, avgScore: selectedMovies.Average(sel => sel.ComputeSimilarity(c))))
            .Where(x => x.avgScore > 0)
            .OrderByDescending(x => x.avgScore)
            .Take(10)
            .ToList();
    }
    public async Task ProcessAllDataAsync(string dataFolderPath)
    {
        var totalSw = Stopwatch.StartNew();
        Console.WriteLine($"Начинаем обработку ({_consumerCount} потоков-обработчиков)...\n");

        var filesToProcess = new[]
        {
            ("links_IMDB_MovieLens", (Action<string, bool>)ProcessLinksLine),
            ("ActorsDirectorsNames_IMDB", ProcessNamesLine),
            ("TagCodes_MovieLens", ProcessTagCodesLine),
            ("MovieCodes_IMDB", ProcessMoviesLine),
            ("ActorsDirectorsCodes_IMDB", ProcessRolesLine),
            ("Ratings_IMDB", ProcessRatingsLine),
            ("TagScores_MovieLens", ProcessTagScoresLine)
        };

        foreach (var (fileName, processor) in filesToProcess)
        {
            _cts.Token.ThrowIfCancellationRequested();
            
            var filePath = FindFile(dataFolderPath, fileName);
            if (string.IsNullOrEmpty(filePath)) continue;

            await ProcessFileWithPipelineAsync(filePath, processor);
        }

        Console.WriteLine("\nФинализация данных...");
        BuildAllTop10();
        ClearTempData();

        Console.WriteLine($"\n{'=',-50}");
        Console.WriteLine($"ИТОГО: {totalSw.ElapsedMilliseconds} мс");
        Console.WriteLine($"Фильмов: {MoviesByTitle.Count:N0}");
        Console.WriteLine($"Актёров/режиссёров: {MoviesByPerson.Count:N0}");
        Console.WriteLine($"Тэгов: {MoviesByTag.Count:N0}");
        Console.WriteLine($"{'=',-50}");
    }

    private async Task ProcessFileWithPipelineAsync(string filePath, Action<string, bool> lineProcessor)
    {
        var fileName = Path.GetFileName(filePath);
        var sw = Stopwatch.StartNew();
        Console.WriteLine($"[{fileName}] Начало обработки...");

        bool isCsv = filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        
        using var lineQueue = new BlockingCollection<string>(boundedCapacity: 10000);
        
        var processedCount = 0;

        var producerTask = Task.Run(() =>
        {
            try
            {
                bool isFirstLine = true;
                foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    
                    if (isFirstLine) { isFirstLine = false; continue; }
                    
                    if (!string.IsNullOrWhiteSpace(line))
                        lineQueue.Add(line, _cts.Token);
                }
            }
            finally
            {
                lineQueue.CompleteAdding();
            }
        }, _cts.Token);

        var consumerTasks = Enumerable.Range(0, _consumerCount)
            .Select(_ => Task.Run(() =>
            {
                foreach (var line in lineQueue.GetConsumingEnumerable(_cts.Token))
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    
                    try
                    {
                        lineProcessor(line, isCsv);
                        Interlocked.Increment(ref processedCount);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Ошибка обработки строки: {ex.Message}");
                    }
                }
            }, _cts.Token))
            .ToArray();

        await producerTask;
        
        await Task.WhenAll(consumerTasks);

        Console.WriteLine($"[{fileName}] Готово: {processedCount:N0} строк за {sw.ElapsedMilliseconds} мс");
    }

//links_IMDB_MovieLens
    private void ProcessLinksLine(string line, bool isCsv)
    {
        var parts = isCsv ? FastSplitCsv(line, 2) : FastSplitTsv(line, 2);
        if (parts.Length < 2) return;

        var mlCode = parts[0];
        var imdbCode = NormalizeImdbCode(parts[1]);

        if (IsValidCode(mlCode) && IsValidCode(imdbCode))
        {
            _imdbToMovieLens[imdbCode] = mlCode;
            _movieLensToImdb[mlCode] = imdbCode;
        }
    }
//ActorsDirectorsNames_IMDB
    private void ProcessNamesLine(string line, bool isCsv)
    {
        int firstTab = line.IndexOf('\t');
        if (firstTab < 0) return;
        
        int secondTab = line.IndexOf('\t', firstTab + 1);
        if (secondTab < 0) secondTab = line.Length;

        var personCode = line.Substring(0, firstTab);
        var personName = line.Substring(firstTab + 1, secondTab - firstTab - 1);

        if (IsValidCode(personCode) && IsValidCode(personName))
            _personCodesToNames[personCode] = personName;
    }
//TagCodes_MovieLens
    private void ProcessTagCodesLine(string line, bool isCsv)
    {
        
        var parts = isCsv ? FastSplitCsv(line, 2) : FastSplitTsv(line, 2);
        if (parts.Length < 2) return;

        var tagCode = parts[0];
        var tagName = parts[1];

        if (!string.IsNullOrEmpty(tagCode) && !string.IsNullOrEmpty(tagName))
            _tagCodesToNames[tagCode] = tagName;
    }
//MovieCodes_IMDB
    private void ProcessMoviesLine(string line, bool isCsv)
    {
       
        bool hasEnglish = line.Contains("\ten\t") || line.Contains("\tEN\t") || 
                          line.Contains("\tUS\t") || line.Contains("\tGB\t");
        bool hasRussian = line.Contains("\tru\t") || line.Contains("\tRU\t");
        
        if (!hasEnglish && !hasRussian) return;

        var parts = FastSplitTsv(line, 5);
        if (parts.Length < 5) return;

        var code = parts[0];
        var title = parts[2];
        var region = parts[3].ToUpperInvariant();
        var lang = parts[4].ToLowerInvariant();

        if (!IsValidCode(code) || !IsValidCode(title)) return;

        bool validLang = lang == "en" || lang == "ru" || lang == "\\n" || string.IsNullOrEmpty(lang);
        bool validRegion = region == "US" || region == "GB" || region == "RU" || 
                           region == "\\N" || string.IsNullOrEmpty(region);

        if (!validLang || !validRegion) return;

        _movieCodesToTitles[code] = title;
        MoviesByTitle.TryAdd(title, new Movie { Title = title });
    }
//ActorsDirectorsCodes_IMDB
    private void ProcessRolesLine(string line, bool isCsv)
    {
        var parts = FastSplitTsv(line, 4);
        if (parts.Length < 4) return;

        var movieCode = parts[0];
        var personCode = parts[2];
        var role = parts[3];

        if (!IsValidCode(movieCode) || !IsValidCode(personCode)) return;

        
        if (string.IsNullOrEmpty(role)) return;
        char firstChar = char.ToLower(role[0]);
        bool isDirector = firstChar == 'd' && role.StartsWith("director", StringComparison.OrdinalIgnoreCase);
        bool isActor = firstChar == 'a' && (role.StartsWith("actor", StringComparison.OrdinalIgnoreCase) || 
                                             role.StartsWith("actress", StringComparison.OrdinalIgnoreCase));

        if (!isDirector && !isActor) return;

        if (!_movieCodesToTitles.TryGetValue(movieCode, out var title)) return;
        if (!_personCodesToNames.TryGetValue(personCode, out var personName)) return;
        if (!MoviesByTitle.TryGetValue(title, out var movie)) return;

        lock (movie)
        {
            if (isActor)
                movie.Actors.Add(personName);
            else if (isDirector)
                movie.Director = string.IsNullOrEmpty(movie.Director) 
                    ? personName 
                    : $"{movie.Director}, {personName}";
        }

        MoviesByPerson.AddOrUpdate(personName,
            _ => new HashSet<Movie> { movie },
            (_, set) => { lock (set) set.Add(movie); return set; });
    }
//Ratings_IMDB
    private void ProcessRatingsLine(string line, bool isCsv)
    {
        
        int firstTab = line.IndexOf('\t');
        if (firstTab < 0) return;
        
        int secondTab = line.IndexOf('\t', firstTab + 1);
        if (secondTab < 0) secondTab = line.Length;

        var movieCode = line.Substring(0, firstTab);
        var ratingStr = line.Substring(firstTab + 1, secondTab - firstTab - 1);

        if (!_movieCodesToTitles.TryGetValue(movieCode, out var title)) return;
        if (!MoviesByTitle.TryGetValue(title, out var movie)) return;

        if (double.TryParse(ratingStr.Replace(',', '.'), 
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var rating))
        {
            movie.Rating = rating;
        }
    }
//
    private void ProcessTagScoresLine(string line, bool isCsv)
    {
        
        var parts = isCsv ? FastSplitCsv(line, 3) : FastSplitTsv(line, 3);
        if (parts.Length < 3) return;

        var mlCode = parts[0];
        var tagCode = parts[1];
        var scoreStr = parts[2];

        
        if (!double.TryParse(scoreStr.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var score) || score <= 0.5)
            return;

        if (!_movieLensToImdb.TryGetValue(mlCode, out var imdbCode)) return;
        if (!_tagCodesToNames.TryGetValue(tagCode, out var tagName)) return;
        if (!_movieCodesToTitles.TryGetValue(imdbCode, out var title)) return;
        if (!MoviesByTitle.TryGetValue(title, out var movie)) return;

        lock (movie) movie.Tags.Add(tagName);

        MoviesByTag.AddOrUpdate(tagName,
            _ => new HashSet<Movie> { movie },
            (_, set) => { lock (set) set.Add(movie); return set; });
    }

    private static string[] FastSplitTsv(string line, int maxFields)
    {
        var result = new List<string>(maxFields);
        int start = 0;
        
        for (int i = 0; i < line.Length && result.Count < maxFields; i++)
        {
            if (line[i] == '\t')
            {
                result.Add(line.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        
        if (result.Count < maxFields)
            result.Add(line.Substring(start).Trim());

        return result.ToArray();
    }

    
    private static string[] FastSplitCsv(string line, int maxFields)
    {
        var result = new List<string>(maxFields);
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length && result.Count < maxFields; i++)
        {
            char c = line[i];
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim().Trim('"'));
                current.Clear();
            }
            else
                current.Append(c);
        }

        if (result.Count < maxFields)
            result.Add(current.ToString().Trim().Trim('"'));

        return result.ToArray();
    }

    private static string NormalizeImdbCode(string code)
    {
        code = code.Trim();
        return code.StartsWith("tt") ? code : "tt" + code.PadLeft(7, '0');
    }

    private static bool IsValidCode(string value) =>
        !string.IsNullOrEmpty(value) && value != "\\N";

    private static string FindFile(string folder, string name)
    {
        if (!Directory.Exists(folder))
        {
            Console.WriteLine($"Папка не найдена: {folder}");
            return null;
        }

        foreach (var ext in new[] { ".tsv", ".csv", ".txt", "" })
        {
            var files = Directory.GetFiles(folder, name + ext);
            if (files.Length > 0) return files[0];
        }

        Console.WriteLine($"Файл не найден: {name}");
        return null;
    }

    private void ClearTempData()
    {
        _imdbToMovieLens.Clear();
        _movieLensToImdb.Clear();
        _personCodesToNames.Clear();
        _tagCodesToNames.Clear();
        _movieCodesToTitles.Clear();
        GC.Collect();
    }

   
}

public class UserInterface
{
    private readonly MovieDataProcessor _processor;

    public UserInterface(MovieDataProcessor processor) => _processor = processor;

    public void Run()
    {
        if (_processor.MoviesByTitle.IsEmpty)
        {
            Console.WriteLine("Нет данных. Нажмите любую клавишу...");
            Console.ReadKey();
            return;
        }

        var menu = new (string name, Action action)[]
        {
            ("Поиск информации о фильме", SearchMovie),
            ("Поиск фильмов по актеру/режиссеру", SearchByPerson),
            ("Поиск фильмов по тэгу", SearchByTag),
            ("Похожие фильмы (топ-10)", ShowSimilar),
            ("Рекомендации по нескольким фильмам", ShowMultiRecommendations),
            ("Статистика", ShowStats),
            ("Выход", null)
        };

        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== МЕНЮ ===\n");
            
            for (int i = 0; i < menu.Length; i++)
                Console.WriteLine($"{i + 1}. {menu[i].name}");

            Console.Write("\nВыбор: ");
            
            if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > menu.Length)
            {
                Console.WriteLine("Неверный выбор!");
                Thread.Sleep(500);
                continue;
            }

            if (choice == menu.Length) return; // Выход

            menu[choice - 1].action?.Invoke();
            
            Console.WriteLine("\nНажмите любую клавишу...");
            Console.ReadKey();
        }
    }

    private Movie FindMovie(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        
        if (_processor.MoviesByTitle.TryGetValue(query, out var exact))
            return exact;

        return _processor.MoviesByTitle
            .FirstOrDefault(m => m.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private void SearchMovie()
    {
        Console.Write("\nНазвание фильма: ");
        var movie = FindMovie(Console.ReadLine());
        
        if (movie != null)
        {
            Console.WriteLine($"\n=== ИНФОРМАЦИЯ О ФИЛЬМЕ ===\n{movie}");
            Console.WriteLine($"\nВсего актёров: {movie.Actors.Count}");
            Console.WriteLine($"Всего тэгов: {movie.Tags.Count}");
        }
        else
            Console.WriteLine("Фильм не найден!");
    }

    private void SearchByPerson()
    {
        Console.Write("\nИмя актера/режиссера: ");
        var query = Console.ReadLine() ?? "";
        
        var entry = _processor.MoviesByPerson
            .FirstOrDefault(p => p.Key.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (entry.Value != null && entry.Value.Any())
        {
            Console.WriteLine($"\n=== ФИЛЬМЫ: {entry.Key.ToUpper()} ===");
            ShowMovieList(entry.Value);
        }
        else
            Console.WriteLine("Не найден!");
    }

    private void SearchByTag()
    {
        Console.Write("\nТэг: ");
        var query = Console.ReadLine() ?? "";
        
        var entry = _processor.MoviesByTag
            .FirstOrDefault(t => t.Key.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (entry.Value != null && entry.Value.Any())
        {
            Console.WriteLine($"\n=== ФИЛЬМЫ С ТЭГОМ '{entry.Key}' ===");
            ShowMovieList(entry.Value);
        }
        else
            Console.WriteLine("Тэг не найден!");
    }

    private void ShowSimilar()
    {
        Console.Write("\nНазвание фильма: ");
        var movie = FindMovie(Console.ReadLine());
        
        if (movie == null) { Console.WriteLine("Фильм не найден!"); return; }

        Console.WriteLine($"\n=== ТОП-10 ПОХОЖИХ НА \"{movie.Title}\" ===");
        
        if (!movie.Top10SimilarMovies.Any())
        {
            Console.WriteLine("Нет похожих фильмов.");
            return;
        }

        int i = 1;
        foreach (var (m, score) in movie.Top10SimilarMovies)
        {
            Console.WriteLine($"{i++}. {m.Title}");
            Console.WriteLine($"   Похожесть: {score:F3} | Рейтинг: {m.Rating:F1}");
        }
    }

    private void ShowMultiRecommendations()
    {
        Console.WriteLine("\nВведите названия фильмов (пустая строка — завершить):\n");
        var selected = new List<Movie>();

        while (true)
        {
            Console.Write($"Фильм {selected.Count + 1}: ");
            var title = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(title)) break;

            var movie = FindMovie(title);
            if (movie != null)
            {
                selected.Add(movie);
                Console.WriteLine($" Добавлен: {movie.Title}");
            }
            else
                Console.WriteLine("  Не найден");
        }

        if (!selected.Any())
        {
            Console.WriteLine("\nНе выбрано ни одного фильма.");
            return;
        }

        Console.WriteLine($"\nВыбрано фильмов: {selected.Count}");
        Console.WriteLine("Строим рекомендации...\n");

        var recommendations = _processor.BuildRecommendationsFromMany(selected);

        if (!recommendations.Any())
        {
            Console.WriteLine("Нет рекомендаций.");
            return;
        }

        Console.WriteLine("=== РЕКОМЕНДАЦИИ ===");
        int i = 1;
        foreach (var (movie, score) in recommendations)
        {
            Console.WriteLine($"{i++}. {movie.Title}");
            Console.WriteLine($"   Средняя похожесть: {score:F3} | Рейтинг: {movie.Rating:F1}");
        }
    }

    private void ShowStats()
    {
        Console.WriteLine("\n=== СТАТИСТИКА ===");
        Console.WriteLine($"Всего фильмов: {_processor.MoviesByTitle.Count:N0}");
        Console.WriteLine($"Всего актёров/режиссёров: {_processor.MoviesByPerson.Count:N0}");
        Console.WriteLine($"Всего тэгов: {_processor.MoviesByTag.Count:N0}");

        Console.WriteLine("\n--- Топ-5 фильмов по рейтингу ---");
        int i = 1;
        foreach (var m in _processor.MoviesByTitle.Values
            .Where(m => m.Rating > 0)
            .OrderByDescending(m => m.Rating)
            .Take(5))
        {
            Console.WriteLine($"{i++}. {m.Title}: {m.Rating:F1}");
        }

        Console.WriteLine("\n--- Топ-5 актёров/режиссёров ---");
        i = 1;
        foreach (var p in _processor.MoviesByPerson
            .OrderByDescending(p => p.Value.Count)
            .Take(5))
        {
            Console.WriteLine($"{i++}. {p.Key}: {p.Value.Count} фильмов");
        }
    }

    private static void ShowMovieList(IEnumerable<Movie> movies)
    {
        var list = movies.OrderByDescending(m => m.Rating).Take(10).ToList();
        int i = 1;
        
        foreach (var m in list)
            Console.WriteLine($"{i++}. {m.Title} (Рейтинг: {m.Rating:F1})");

        int total = movies.Count();
        if (total > 10)
            Console.WriteLine($"... и ещё {total - 10} фильмов");
    }
}

internal class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Использование:");
            Console.WriteLine("  dotnet run parse  — парсинг файлов в БД");
            Console.WriteLine("  dotnet run run    — запуск приложения (из БД)");
            return;
        }

        if (args[0] == "parse")
        {
            await RunParse();
        }
        else if (args[0] == "run")
        {
            RunFromDatabase();
        }
        else
        {
            Console.WriteLine("Неизвестная команда");
        }
    }

    static async Task RunParse()
    {
        Console.WriteLine("=== PARSE: файлы → EF → SQLite ===");

        string dataPath = @"/Users/mihailtupcij/Downloads/ml-latest";
        if (!Directory.Exists(dataPath))
        {
            Console.WriteLine($"Папка не найдена: {dataPath}");
            return;
        }
        var processor = new MovieDataProcessor();
        await processor.ProcessAllDataAsync(dataPath);

        using var db = new MovieDbContext();
        db.Database.EnsureDeleted();  
        db.Database.EnsureCreated();

        DatabaseSeeder.Save(processor, db);


        Console.WriteLine("Парсинг завершён, данные сохранены в БД");
    }

    static void RunFromDatabase()
    {
        Console.WriteLine("=== RUN: работа напрямую с БД ===");

        using var db = new MovieDbContext();
        db.Database.EnsureCreated();

        var ui = new UserInterfaceEf(db);
        ui.Run();
    }
}