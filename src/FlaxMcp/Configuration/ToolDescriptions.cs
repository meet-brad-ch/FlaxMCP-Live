using System.Reflection;
using ModelContextProtocol.Server;

namespace FlaxMcp.Configuration;

/// <summary>
/// The fork's descriptions of upstream tools. Each text replaces the [Description] attribute text of that tool in its
/// upstream file, so the upstream file stays unchanged for merges. The texts tell an agent when to use a tool instead
/// of a similar tool.
/// </summary>
internal static class ToolDescriptions
{
    /// <summary>The replacement descriptions, by tool name.</summary>
    public static IReadOnlyDictionary<string, string> Overrides { get; } = new Dictionary<string, string>
    {
        ["scene_find_actor"] =
            "Searches the actors of a saved .scene or .prefab file on disk. Name matches a part of the actor name, " +
            "TypeName matches the full type name, and both ignore case. The tool does not need the editor and does not " +
            "see unsaved changes. It returns the first 100 matches. For the scenes in the running editor, use " +
            "editor_find_actors.",
        ["scene_outline"] =
            "Reads the actor tree of a saved .scene or .prefab file on disk. The tree comes from the ParentID links, not " +
            "from the file order. The scripts of an actor are in its Scripts list, not in its child nodes. Truncated is " +
            "true when the tool reached the depth or node limit. The tool does not see unsaved changes. For the live " +
            "tree in the running editor, use editor_scene_graph.",
        ["editor_scene_graph"] =
            "Returns the live actor tree of every scene that is open in the running Flax Editor, with the unsaved " +
            "changes. Truncated is true when the tree has more than 500 nodes or 32 levels. For a saved .scene or " +
            ".prefab file without the editor, use scene_outline. To find actors by name or type, use editor_find_actors.",
        ["build_compile_scripts"] =
            "Compiles the game scripts in a separate headless Flax Editor process, with a time limit of 2 minutes. " +
            "Returns structured compiler errors (file, line, code, message) from the engine log. The tool fails when a " +
            "Flax Editor is open on the project. With the editor open, use build_check_scripts to find errors, or " +
            "editor_compile to compile and reload.",
        ["content_create_material"] =
            "Creates a surface Material asset through the live Flax Editor and saves it. relativePath is relative to " +
            "Content/ and ends in .flax. The tool does not overwrite: it fails when an asset exists at that path. It " +
            "creates missing folders. The editor does not record an Undo step. Texture ids are asset GUIDs of 32 hex " +
            "digits: get them from content_search. normalTextureId must be a texture that is imported as a normal map. " +
            "For glass, water or film, use content_create_material_transparent. For a variant of an existing material, " +
            "use content_create_material_instance.",
        ["editor_duplicate_actor"] =
            "Duplicates a loaded actor, with its children and scripts, into a loaded scene or under a loaded parent " +
            "actor. Returns the live details of the duplicate. Set exactly one of sceneId and parentId, and set the " +
            "other to null. name is the name of the duplicate. Its children keep their names. translation, orientation " +
            "and scale set the world transform of the duplicate. The source actor does not change. The editor records " +
            "an Undo step. The scene stays unsaved until editor_save. To make a new actor of a type, use " +
            "editor_create_actor. To move an actor to a different parent, use editor_reparent_actor.",
        ["editor_screenshot"] =
            "Captures the Scene viewport of the running Flax Editor to a PNG file. The Scene tab must be visible: the " +
            "capture fails when another tab covers it. The output folder must exist. The capture is not available in " +
            "headless mode. To render from a camera without the Scene tab, use editor_camera_screenshot.",
    };

    /// <summary>The replacement description of a tool method.</summary>
    /// <param name="method">A method with the McpServerTool attribute.</param>
    /// <returns>The replacement, or null when the tool keeps its attribute text.</returns>
    public static string? For(MethodInfo method)
    {
        var toolName = method.GetCustomAttribute<McpServerToolAttribute>()?.Name;
        if (toolName is null)
        {
            return null;
        }

        return Overrides.GetValueOrDefault(toolName);
    }
}
