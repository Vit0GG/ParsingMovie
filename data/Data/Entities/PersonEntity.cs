namespace Movie.Data.Entities;

public class PersonEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public ICollection<MovieActor> MovieActors { get; set; } = new List<MovieActor>();
}
