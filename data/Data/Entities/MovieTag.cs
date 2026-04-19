namespace Movie.Data.Entities;

public class MovieTag
{
    public int MovieId { get; set; }
    public MovieEntity Movie { get; set; } = null!;

    public int TagId { get; set; }
    public TagEntity Tag { get; set; } = null!;
}
