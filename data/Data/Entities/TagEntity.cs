namespace Movie.Data.Entities;

public class TagEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public ICollection<MovieTag> MovieTags { get; set; } = new List<MovieTag>();
}