namespace Movie.Data.Entities;

public class MovieEntity
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public double Rating { get; set; }
    public string Director { get; set; } = "";
    public ICollection<MovieActor> MovieActors { get; set; } = new List<MovieActor>();
    public ICollection<MovieTag> MovieTags { get; set; } = new List<MovieTag>();

}
