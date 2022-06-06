namespace SarsaWidgets;

public interface ISampleLibrary
{

    public IEnumerable<(string Title, string Description)> GetListing();

    public string GetSample(string title);

}
