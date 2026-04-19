namespace Movie.Data.Entities;

public class MovieActor
{
    public int MovieId { get; set; }
    public MovieEntity Movie { get; set; } = null!;

    public int PersonId { get; set; }
    public PersonEntity Person { get; set; } = null!;
}
