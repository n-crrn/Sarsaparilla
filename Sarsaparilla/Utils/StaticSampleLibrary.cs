using SarsaWidgets;
using StatefulHorn;

namespace Sarsaparilla.Utils;

/// <summary>
/// Provides access to sample libraries where the models or code is stored as
/// string attached to a class through static members.
/// </summary>
public class StaticSampleLibrary : ISampleLibrary
{

    public const string ExpectedListName = "Models";

    private readonly IReadOnlyList<(string Title, string Description, string Sample)> Models;

    public StaticSampleLibrary(Type t)
    {
        Models = (IReadOnlyList<(string Title, string Description, string Sample)>)(t.GetField(ExpectedListName)!.GetValue(null)!);
    }

    public IEnumerable<(string Title, string Description)> GetListing()
    {
        foreach ((string title, string desc, string _) in Models)
        {
            yield return (title, desc);
        }
    }

    public string GetSample(string title)
    {
        foreach ((string entryTitle, string _, string sample) in Models)
        {
            if (entryTitle.Equals(title))
            {
                return sample;
            }
        }
        return "";
    }

}
