using FlaxMcp.Tools;

namespace FlaxMcp.Configuration;

/// <summary>
/// The tool classes of the live fork that belong to a toolset of upstream's registry: a toolset gives its main tool
/// class first, then its companions.
/// </summary>
internal static class ToolsetCompanions
{
    /// <summary>The companion tool classes of each main tool class that has some.</summary>
    private static readonly Dictionary<Type, Type[]> Companions = new()
    {
        [typeof(EditorTools)] = [typeof(EditorLiveTools), typeof(EditorObjectTools)]
    };

    /// <summary>Returns a toolset's main tool class followed by its companion tool classes.</summary>
    /// <param name="mainType">The tool class that the toolset registry names.</param>
    /// <returns>The main tool class, then its companions in registration order.</returns>
    public static IEnumerable<Type> WithCompanions(Type mainType)
    {
        List<Type> types = [mainType];
        if (Companions.TryGetValue(mainType, out Type[]? companions))
        {
            types.AddRange(companions);
        }

        return types;
    }
}
