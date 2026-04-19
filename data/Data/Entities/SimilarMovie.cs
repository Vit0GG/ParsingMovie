namespace Movie.Data.Entities;

public class SimilarMovie
{
    public int MovieId { get; set; }
    public MovieEntity Movie { get; set; } = null!;

    public int SimilarMovieId { get; set; }
    public MovieEntity Similar { get; set; } = null!;

    public double Score { get; set; }
}