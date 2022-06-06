using SarsaWidgets;
using StatefulHorn;

namespace Sarsaparilla.Utils;

/// <summary>
/// Provides access to the Stateful Horn library of models.
/// </summary>
public class AppKnowledgeSampleLibrary : ISampleLibrary
{

    public IEnumerable<(string Title, string Description)> GetListing()
    {
        foreach ((string title, string desc, string _) in KnowledgeSampleLibrary.Models)
        {
            yield return (title, desc);
        }
    }

    public string GetSample(string title)
    {
        foreach ((string entryTitle, string _, string sample) in KnowledgeSampleLibrary.Models)
        {
            if (entryTitle.Equals(title))
            {
                return sample;
            }
        }
        return "";
    }

}
