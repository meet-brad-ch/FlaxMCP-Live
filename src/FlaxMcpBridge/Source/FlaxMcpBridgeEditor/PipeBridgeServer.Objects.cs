using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FlaxEngine;

namespace FlaxMcpBridge.Editor;

/// <summary>
/// Typed access to live objects, for what snippets were used for most: finding actors, reading and writing a script's
/// or an actor's members, calling a method, adding a script.
/// </summary>
internal sealed partial class PipeBridgeServer
{
    /// <summary>The most actors one find_actors request may return.</summary>
    private const int MaxFoundActors = 500;

    /// <summary>The deepest struct nesting that <see cref="Plain"/> follows field by field.</summary>
    private const int MaxStructDepth = 8;

    /// <summary>The most distinct loader errors one type_load_failed message names.</summary>
    private const int MaxReportedLoaderErrors = 3;

    /// <summary>Selects public and non-public instance members.</summary>
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>How JSON values are turned into member values: fields count, names ignore case, enums are names.</summary>
    private static readonly JsonSerializerOptions MemberValueOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Finds the loaded actors that match every given filter, in scene order, parents before children.</summary>
    /// <param name="nameContains">Part of the actor name, any case; null for no name filter.</param>
    /// <param name="actorType">Full or short type name of the actor; null for no type filter.</param>
    /// <param name="scriptType">Full or short type name of a script on the actor; null for no script filter.</param>
    /// <param name="limit">The most actors to return, from 1 to <see cref="MaxFoundActors"/>.</param>
    /// <returns>The task of the answer: the actors found and whether more matched than the limit.</returns>
    /// <exception cref="ArgumentException">The limit is outside 1 to <see cref="MaxFoundActors"/>.</exception>
    private static Task<object> FindActorsAsync(string? nameContains, string? actorType, string? scriptType, int limit)
    {
        if (limit < 1 || limit > MaxFoundActors)
        {
            throw new ArgumentException($"limit must be 1 to {MaxFoundActors}, not {limit}.");
        }

        var search = new ActorSearch(nameContains, actorType, scriptType, limit);
        return InvokeOnUpdateAsync(search.Run);
    }

    /// <summary>One actor to visit and its path from the scene name.</summary>
    /// <param name="Actor">The actor.</param>
    /// <param name="Path">Names from the scene down to the actor, separated by slashes.</param>
    private readonly record struct PendingActor(Actor Actor, string Path);

    /// <summary>A walk over all loaded scenes that collects the actors that match a filter. Run it on the main thread.</summary>
    private sealed class ActorSearch
    {
        /// <summary>Part of the actor name; null for no name filter.</summary>
        private readonly string? _nameContains;

        /// <summary>Actor type name; null for no type filter.</summary>
        private readonly string? _actorType;

        /// <summary>Script type name; null for no script filter.</summary>
        private readonly string? _scriptType;

        /// <summary>The most actors to collect.</summary>
        private readonly int _limit;

        /// <summary>The descriptions of the actors found so far.</summary>
        private readonly List<object> _found = new();

        /// <summary>Whether an actor matched after the limit was reached.</summary>
        private bool _truncated;

        /// <summary>Sets the filters and the limit.</summary>
        /// <param name="nameContains">Part of the actor name; null for no name filter.</param>
        /// <param name="actorType">Actor type name; null for no type filter.</param>
        /// <param name="scriptType">Script type name; null for no script filter.</param>
        /// <param name="limit">The most actors to collect.</param>
        public ActorSearch(string? nameContains, string? actorType, string? scriptType, int limit)
        {
            _nameContains = nameContains;
            _actorType = actorType;
            _scriptType = scriptType;
            _limit = limit;
        }

        /// <summary>Walks every loaded scene.</summary>
        /// <returns>The answer object: the main thread id, the actors found and the truncated flag.</returns>
        public object Run()
        {
            foreach (Scene scene in Level.Scenes)
            {
                VisitTree(scene);
            }

            return new { mainThreadId = Globals.MainThreadID, actors = _found, truncated = _truncated };
        }

        /// <summary>Visits an actor and its descendants, parents first. A matching actor beyond the limit is not entered.</summary>
        /// <param name="root">The top actor of the tree.</param>
        private void VisitTree(Actor root)
        {
            var pending = new Stack<PendingActor>();
            pending.Push(new PendingActor(root, root.Name));
            while (pending.Count > 0)
            {
                PendingActor current = pending.Pop();
                VisitActor(current, pending);
            }
        }

        /// <summary>Records an actor when it matches, then queues its children.</summary>
        /// <param name="current">The actor and its path.</param>
        /// <param name="pending">The actors still to visit; the children go on top, first child last pushed.</param>
        private void VisitActor(PendingActor current, Stack<PendingActor> pending)
        {
            if (Matches(current.Actor))
            {
                if (_found.Count >= _limit)
                {
                    _truncated = true;
                    return;
                }

                object description = Describe(current);
                _found.Add(description);
            }

            Actor[] children = current.Actor.Children;
            for (int index = children.Length - 1; index >= 0; index--)
            {
                Actor child = children[index];
                string childPath = current.Path + "/" + child.Name;
                pending.Push(new PendingActor(child, childPath));
            }
        }

        /// <summary>Applies the name, type and script filters.</summary>
        /// <param name="actor">The actor to test.</param>
        /// <returns>True when the actor passes every filter that is set.</returns>
        private bool Matches(Actor actor)
        {
            if (_nameContains is not null && !actor.Name.Contains(_nameContains, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_actorType is not null && !IsNamed(actor.GetType(), _actorType))
            {
                return false;
            }

            if (_scriptType is null)
            {
                return true;
            }

            string scriptType = _scriptType;
            return actor.Scripts.Any(script => IsNamed(script.GetType(), scriptType));
        }

        /// <summary>Describes a found actor for the answer.</summary>
        /// <param name="current">The actor and its path.</param>
        /// <returns>An object with the actor id, name, type name, path and script type names.</returns>
        private static object Describe(PendingActor current)
        {
            Actor actor = current.Actor;
            string?[] scripts = actor.Scripts.Select(script => script.GetType().FullName).ToArray();
            return new
            {
                id = actor.ID.ToString(),
                name = actor.Name,
                typeName = actor.GetType().FullName,
                path = current.Path,
                scripts,
            };
        }
    }

    /// <summary>Reads a field or property of an actor or of one of its scripts; a dotted path goes into nested members.</summary>
    /// <param name="actorId">The actor id, as a GUID.</param>
    /// <param name="scriptType">Full or short type name of a script on the actor; null to read the actor itself.</param>
    /// <param name="member">The member path, e.g. <c>LocalPosition.X</c>.</param>
    /// <returns>The task of the answer: the target, the member and its value as JSON.</returns>
    private static Task<object> GetMemberAsync(string actorId, string? scriptType, string member)
    {
        return InvokeOnUpdateAsync(() => ReadMember(actorId, scriptType, member));
    }

    /// <summary>Reads a member on the main thread.</summary>
    /// <param name="actorId">The actor id, as a GUID.</param>
    /// <param name="scriptType">Script type name; null for the actor itself.</param>
    /// <param name="member">The member path.</param>
    /// <returns>The answer object.</returns>
    private static object ReadMember(string actorId, string? scriptType, string member)
    {
        FlaxEngine.Object target = FindTarget(actorId, scriptType);
        MemberInfo[] path = MemberPath(target, member);
        object? value = ReadPath(target, path);
        return new
        {
            mainThreadId = Globals.MainThreadID,
            targetId = target.ID.ToString(),
            targetType = target.GetType().FullName,
            member,
            typeName = value?.GetType().FullName,
            value = MemberJson(value),
        };
    }

    /// <summary>Writes a field or property of an actor or of one of its scripts.</summary>
    /// <param name="actorId">The actor id, as a GUID.</param>
    /// <param name="scriptType">Full or short type name of a script on the actor; null to write the actor itself.</param>
    /// <param name="member">The member path, e.g. <c>LocalPosition.X</c>.</param>
    /// <param name="value">The new value as JSON.</param>
    /// <returns>The task of the answer: the member's value after the write and whether the undo recorded it.</returns>
    private static Task<object> SetMemberAsync(string actorId, string? scriptType, string member, JsonElement value)
    {
        return InvokeOnUpdateAsync(() => WriteMember(actorId, scriptType, member, value));
    }

    /// <summary>Writes a member on the main thread.</summary>
    /// <param name="actorId">The actor id, as a GUID.</param>
    /// <param name="scriptType">Script type name; null for the actor itself.</param>
    /// <param name="member">The member path.</param>
    /// <param name="value">The new value as JSON.</param>
    /// <returns>The answer object.</returns>
    private static object WriteMember(string actorId, string? scriptType, string member, JsonElement value)
    {
        FlaxEngine.Object target = FindTarget(actorId, scriptType);
        MemberInfo[] path = MemberPath(target, member);
        Type memberType = TypeOf(path[^1]);
        object? converted = ConvertJson(value, memberType, member);

        // In edit mode the change goes through the editor's undo (a snapshot before and after) and marks the scene
        // edited; in play mode it lasts until play ends.
        bool recordedUndo = !FlaxEditor.Editor.IsPlayMode;
        if (recordedUndo)
        {
            RecordMemberChange(target, member, path, converted);
        }
        else
        {
            SetPath(target, path, 0, converted);
        }

        object? now = ReadPath(target, path);
        return new
        {
            mainThreadId = Globals.MainThreadID,
            targetId = target.ID.ToString(),
            targetType = target.GetType().FullName,
            member,
            typeName = now?.GetType().FullName,
            value = MemberJson(now),
            recordedUndo,
        };
    }

    /// <summary>Applies a member change through the editor's undo and marks the scene of the target as edited.</summary>
    /// <param name="target">The actor or script that owns the first member of the path.</param>
    /// <param name="member">The member path, for the undo entry name.</param>
    /// <param name="path">The members from the target down to the one to write.</param>
    /// <param name="converted">The new value.</param>
    private static void RecordMemberChange(FlaxEngine.Object target, string member, MemberInfo[] path, object? converted)
    {
        FlaxEditor.Editor.Instance.Undo.RecordAction(target, "Set " + member, () => SetPath(target, path, 0, converted));
        Scene? scene = (target as Actor)?.Scene ?? (target as Script)?.Actor?.Scene;
        if (scene is not null)
        {
            FlaxEditor.Editor.Instance.Scene.MarkSceneEdited(scene);
        }
    }

    /// <summary>Follows a member path from a target and reads the last member.</summary>
    /// <param name="target">The object that owns the first member.</param>
    /// <param name="path">The members to follow.</param>
    /// <returns>The value of the last member; null when it is null.</returns>
    private static object? ReadPath(object target, MemberInfo[] path)
    {
        object? owner = target;
        foreach (MemberInfo info in path)
        {
            owner = ValueOf(info, owner!);
        }

        return owner;
    }

    /// <summary>Calls a method on an actor, on one of its scripts, or a static method of a loaded type.</summary>
    /// <param name="typeName">Full or short type name for a static method; ignored when <paramref name="actorId"/> is set.</param>
    /// <param name="method">The method name.</param>
    /// <param name="actorId">The actor id, as a GUID; null for a static method.</param>
    /// <param name="scriptType">Script type name on the actor; null to call on the actor itself.</param>
    /// <param name="args">The arguments, as a JSON array.</param>
    /// <returns>The task of the answer: the method that ran and its result as JSON.</returns>
    /// <exception cref="ArgumentException">The arguments are not a JSON array.</exception>
    private static Task<object> InvokeMethodAsync(string? typeName, string method, string? actorId, string? scriptType, JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("args must be a JSON array.");
        }

        return InvokeOnUpdateAsync(() => InvokeMethodOnMainThread(typeName, method, actorId, scriptType, args));
    }

    /// <summary>Finds the best matching overload and calls it on the main thread.</summary>
    /// <param name="typeName">Type name for a static method.</param>
    /// <param name="method">The method name.</param>
    /// <param name="actorId">The actor id; null for a static method.</param>
    /// <param name="scriptType">Script type name; null for the actor itself.</param>
    /// <param name="args">The arguments, as a JSON array.</param>
    /// <returns>The answer object.</returns>
    /// <exception cref="ArgumentException">A static call has no type name.</exception>
    /// <exception cref="BridgeRequestException">No overload takes the arguments, or the method threw.</exception>
    private static object InvokeMethodOnMainThread(string? typeName, string method, string? actorId, string? scriptType, JsonElement args)
    {
        object? instance = null;
        if (actorId is not null)
        {
            instance = FindTarget(actorId, scriptType);
        }

        Type type = ResolveInvocationType(instance, typeName);
        BindingFlags scope = instance is null ? BindingFlags.Static : BindingFlags.Instance;
        BindingFlags flags = scope | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        JsonElement[] arguments = args.EnumerateArray().ToArray();
        MethodInfo[] candidates = FindCandidates(type, flags, method, arguments);
        foreach (MethodInfo candidate in candidates)
        {
            object?[]? converted = TryConvertArguments(candidate, arguments);
            if (converted is null)
            {
                continue;
            }

            object? result = InvokeCandidate(candidate, instance, converted);
            return new
            {
                mainThreadId = Globals.MainThreadID,
                method = candidate.ToString(),
                typeName = result?.GetType().FullName,
                result = MemberJson(result),
            };
        }

        throw NoMatchingMethod(type, flags, method, instance is null, arguments.Length);
    }

    /// <summary>Lists the methods with the given name and parameter count, the best fit for the arguments first.</summary>
    /// <param name="type">The type to search.</param>
    /// <param name="flags">The binding flags of the search.</param>
    /// <param name="method">The method name.</param>
    /// <param name="arguments">The JSON arguments.</param>
    /// <returns>The candidates; equal fits stay in declaration order.</returns>
    private static MethodInfo[] FindCandidates(Type type, BindingFlags flags, string method, JsonElement[] arguments)
    {
        return type.GetMethods(flags)
            .Where(info => info.Name == method && info.GetParameters().Length == arguments.Length)
            .OrderBy(info => FitScore(info, arguments))
            .ToArray();
    }

    /// <summary>Picks the type whose method is called.</summary>
    /// <param name="instance">The target object; null for a static call.</param>
    /// <param name="typeName">The type name for a static call.</param>
    /// <returns>The type of the instance, or the type that the name selects.</returns>
    /// <exception cref="ArgumentException">There is no instance and no type name.</exception>
    private static Type ResolveInvocationType(object? instance, string? typeName)
    {
        if (instance is not null)
        {
            return instance.GetType();
        }

        if (typeName is null)
        {
            throw new ArgumentException("typeName is needed for a static method (no actorId).");
        }

        return ResolveType(typeName);
    }

    /// <summary>Adds up how well each JSON argument suits the matching parameter of a method (lower is better).</summary>
    /// <param name="info">The method.</param>
    /// <param name="arguments">The JSON arguments; as many as the method has parameters.</param>
    /// <returns>The sum of <see cref="Fit"/> over the arguments.</returns>
    private static int FitScore(MethodInfo info, JsonElement[] arguments)
    {
        ParameterInfo[] parameters = info.GetParameters();
        int total = 0;
        for (int index = 0; index < parameters.Length; index++)
        {
            total += Fit(arguments[index], parameters[index].ParameterType);
        }

        return total;
    }

    /// <summary>Converts the JSON arguments to the parameter types of a method.</summary>
    /// <param name="candidate">The method.</param>
    /// <param name="arguments">The JSON arguments; as many as the method has parameters.</param>
    /// <returns>The converted arguments; null when an argument does not fit its parameter, so another overload may fit.</returns>
    private static object?[]? TryConvertArguments(MethodInfo candidate, JsonElement[] arguments)
    {
        ParameterInfo[] parameters = candidate.GetParameters();
        var converted = new object?[arguments.Length];
        try
        {
            for (int index = 0; index < arguments.Length; index++)
            {
                ParameterInfo parameter = parameters[index];
                string parameterName = parameter.Name ?? "?";
                converted[index] = ConvertJson(arguments[index], parameter.ParameterType, parameterName);
            }
        }
        catch (BridgeRequestException)
        {
            // The arguments fit another overload, or none: the caller answers no_matching_method after the last one.
            return null;
        }

        return converted;
    }

    /// <summary>Calls a method and reports an exception of the method as a request error.</summary>
    /// <param name="candidate">The method.</param>
    /// <param name="instance">The target object; null for a static method.</param>
    /// <param name="converted">The converted arguments.</param>
    /// <returns>The return value of the method; null for void.</returns>
    /// <exception cref="BridgeRequestException">The method threw (code <c>method_failed</c>).</exception>
    private static object? InvokeCandidate(MethodInfo candidate, object? instance, object?[] converted)
    {
        try
        {
            return candidate.Invoke(instance, converted);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            string typeName = ex.InnerException.GetType().Name;
            throw new BridgeRequestException("method_failed", $"{typeName}: {ex.InnerException.Message}");
        }
    }

    /// <summary>Builds the error for a call that no overload fits.</summary>
    /// <param name="type">The type that was searched.</param>
    /// <param name="flags">The binding flags of the search.</param>
    /// <param name="method">The method name.</param>
    /// <param name="isStatic">Whether the call was static.</param>
    /// <param name="argumentCount">How many arguments the call gave.</param>
    /// <returns>The error, with code <c>no_matching_method</c>; the caller throws it.</returns>
    private static BridgeRequestException NoMatchingMethod(Type type, BindingFlags flags, string method, bool isStatic, int argumentCount)
    {
        string?[] overloads = type.GetMethods(flags).Where(info => info.Name == method).Select(info => info.ToString()).ToArray();
        if (overloads.Length == 0)
        {
            string kind = isStatic ? "static" : "instance";
            return new BridgeRequestException("no_matching_method", $"{type.FullName} has no {kind} method '{method}'.");
        }

        string list = string.Join("; ", overloads);
        return new BridgeRequestException(
            "no_matching_method",
            $"No overload of {type.FullName}.{method} takes these {argumentCount} arguments: {list}.");
    }

    /// <summary>Adds a script of a type to an actor; an actor that has the script already is left as it is.</summary>
    /// <param name="actorId">The actor id, as a GUID.</param>
    /// <param name="scriptType">Full or short type name of the script.</param>
    /// <param name="orderInParent">The position among the actor's scripts for a new script; null to keep the default.</param>
    /// <returns>The task of the answer: the script id and whether this call added it.</returns>
    private static Task<object> AddScriptAsync(string actorId, string scriptType, int? orderInParent)
    {
        return InvokeOnUpdateAsync(() => AddScript(actorId, scriptType, orderInParent));
    }

    /// <summary>Adds a script on the main thread.</summary>
    /// <param name="actorId">The actor id, as a GUID.</param>
    /// <param name="scriptType">Script type name.</param>
    /// <param name="orderInParent">The position among the actor's scripts; null to keep the default.</param>
    /// <returns>The answer object.</returns>
    /// <exception cref="BridgeRequestException">The type is not a script type that can be added (code <c>invalid_script_type</c>).</exception>
    private static object AddScript(string actorId, string scriptType, int? orderInParent)
    {
        Actor actor = FindActor(actorId);
        Type type = ResolveType(scriptType);
        bool isScript = typeof(Script).IsAssignableFrom(type);
        if (!isScript || type.IsAbstract)
        {
            throw new BridgeRequestException("invalid_script_type", $"{type.FullName} is not a script type that can be added.");
        }

        Script? script = actor.Scripts.FirstOrDefault(existing => existing.GetType() == type);
        bool added = script is null;
        if (script is null)
        {
            script = actor.AddScript(type);
            if (orderInParent is not null)
            {
                script.OrderInParent = orderInParent.Value;
            }

            if (!FlaxEditor.Editor.IsPlayMode)
            {
                FlaxEditor.Editor.Instance.Scene.MarkSceneEdited(actor.Scene);
            }
        }

        return new
        {
            mainThreadId = Globals.MainThreadID,
            actorId = actor.ID.ToString(),
            scriptId = script.ID.ToString(),
            scriptType = type.FullName,
            added,
            orderInParent = script.OrderInParent,
            scripts = actor.Scripts.Select(existing => existing.GetType().FullName).ToArray(),
        };
    }

    /// <summary>How well a JSON value suits a parameter type (lower is better): a number prefers int, then long, then double.</summary>
    /// <param name="value">The JSON argument.</param>
    /// <param name="parameterType">The parameter type; a nullable type counts as its underlying type.</param>
    /// <returns>0 for the best fit up to 3; 5 when the value has no direct fit.</returns>
    private static int Fit(JsonElement value, Type parameterType)
    {
        Type type = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        return value.ValueKind switch
        {
            JsonValueKind.Number when type == typeof(int) => 0,
            JsonValueKind.Number when type == typeof(long) => 1,
            JsonValueKind.Number when type == typeof(double) => 2,
            JsonValueKind.Number when type == typeof(float) => 3,
            JsonValueKind.String when type == typeof(string) => 0,
            JsonValueKind.True or JsonValueKind.False when type == typeof(bool) => 0,
            _ => 5,
        };
    }

    /// <summary>
    /// A member's value as JSON: an engine object as its id, type and name; a struct (e.g. Vector3) by its fields, so
    /// properties that make new structs (Normalized) are not followed; anything else as the snippets serialize it.
    /// </summary>
    private static JsonElement? MemberJson(object? value)
    {
        if (value is null)
        {
            return null;
        }

        object? plain = Plain(value, 0);
        return JsonSerializer.SerializeToElement(plain);
    }

    /// <summary>Turns a member value into plain data for JSON: names for enums, id/type/name for engine objects, fields for structs.</summary>
    /// <param name="value">The value.</param>
    /// <param name="depth">How many structs deep the value is; the walk stops at <see cref="MaxStructDepth"/> (a bound on the recursion).</param>
    /// <returns>Plain data that the JSON serializer handles.</returns>
    private static object? Plain(object? value, int depth)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Enum)
        {
            return value.ToString();
        }

        if (value is string or bool)
        {
            return value;
        }

        if (value is FlaxEngine.Object engineObject)
        {
            return EngineObjectJson(engineObject);
        }

        Type type = value.GetType();
        if (type.IsPrimitive || value is decimal)
        {
            return value;
        }

        if (type.IsValueType && depth < MaxStructDepth)
        {
            return StructJson(value, type, depth);
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return NormalizeDynamicResult(value, visited, 0);
    }

    /// <summary>Describes an engine object by its id, type and (for an actor) name.</summary>
    /// <param name="engineObject">The object.</param>
    /// <returns>A dictionary with the keys id, type and name.</returns>
    private static Dictionary<string, object?> EngineObjectJson(FlaxEngine.Object engineObject)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = engineObject.ID.ToString(),
            ["type"] = engineObject.GetType().FullName,
            ["name"] = (engineObject as Actor)?.Name,
        };
    }

    /// <summary>Describes a struct by its public instance fields (properties are not followed, they may make new structs).</summary>
    /// <param name="value">The boxed struct.</param>
    /// <param name="type">The struct type.</param>
    /// <param name="depth">How many structs deep the value is.</param>
    /// <returns>A dictionary from field name to plain field value.</returns>
    private static Dictionary<string, object?> StructJson(object value, Type type, int depth)
    {
        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        var result = new Dictionary<string, object?>();
        foreach (FieldInfo field in fields)
        {
            object? fieldValue = field.GetValue(value);
            result.Add(field.Name, Plain(fieldValue, depth + 1));
        }

        return result;
    }

    /// <summary>Reads an optional integer from the request's <c>params</c>.</summary>
    /// <param name="root">The request.</param>
    /// <param name="name">The parameter name.</param>
    /// <returns>The integer; null when <c>params</c> or the parameter is missing or null.</returns>
    /// <exception cref="ArgumentException">The parameter is not an integer.</exception>
    private static int? OptionalIntParam(JsonElement root, string name)
    {
        if (!root.TryGetProperty("params", out JsonElement parameters))
        {
            return null;
        }

        if (!parameters.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!value.TryGetInt32(out int number))
        {
            throw new ArgumentException($"params.{name} must be an integer");
        }

        return number;
    }

    /// <summary>Finds the actor, or one of its scripts.</summary>
    /// <param name="actorId">The actor id, as a GUID.</param>
    /// <param name="scriptType">Full or short script type name; null for the actor itself.</param>
    /// <returns>The actor, or its first script of that type.</returns>
    /// <exception cref="BridgeRequestException">The actor has no such script (code <c>unknown_script</c>).</exception>
    private static FlaxEngine.Object FindTarget(string actorId, string? scriptType)
    {
        Actor actor = FindActor(actorId);
        if (scriptType is null)
        {
            return actor;
        }

        Script? script = actor.Scripts.FirstOrDefault(candidate => IsNamed(candidate.GetType(), scriptType));
        if (script is not null)
        {
            return script;
        }

        IEnumerable<string?> scriptNames = actor.Scripts.Select(existing => existing.GetType().FullName).DefaultIfEmpty("none");
        string scriptList = string.Join(", ", scriptNames);
        throw new BridgeRequestException("unknown_script", $"{actor.Name} has no {scriptType} script. Scripts: {scriptList}.");
    }

    /// <summary>Tells whether a type has the given full or short name.</summary>
    /// <param name="type">The type.</param>
    /// <param name="name">The full or short name.</param>
    /// <returns>True when the full name or the short name equals the name.</returns>
    private static bool IsNamed(Type type, string name)
    {
        return type.FullName == name || type.Name == name;
    }

    /// <summary>
    /// A type by full or short name, from the newest loaded copy of each assembly (a reload that could not unload the
    /// old game assemblies leaves older copies loaded).
    /// </summary>
    /// <param name="name">The full or short type name.</param>
    /// <returns>The one type with that name.</returns>
    /// <exception cref="BridgeRequestException">
    /// No type has the name and some types failed to load (code <c>type_load_failed</c>), no type has the name (code
    /// <c>unknown_type</c>), or several types have it (code <c>ambiguous_type</c>).
    /// </exception>
    private static Type ResolveType(string name)
    {
        var loaderErrors = new List<string>();
        Type[] matches = NewestAssemblies()
            .SelectMany(assembly => LoadableTypes(assembly, loaderErrors))
            .Where(type => IsNamed(type, name))
            .ToArray();
        if (matches.Length == 0 && loaderErrors.Count > 0)
        {
            throw TypeLoadFailed(name, loaderErrors);
        }

        if (matches.Length == 0)
        {
            throw new BridgeRequestException("unknown_type", $"No loaded type is named '{name}'.");
        }

        if (matches.Length > 1)
        {
            string names = string.Join(", ", matches.Select(type => type.FullName));
            throw new BridgeRequestException("ambiguous_type", $"'{name}' names {matches.Length} types: {names}; use the full name.");
        }

        return matches[0];
    }

    /// <summary>
    /// Lists the types of an assembly; when some types cannot load, lists the ones that did and records the loader
    /// errors, so that a lookup that finds nothing can tell that the type may be one that failed to load.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    /// <param name="loaderErrors">Receives one line per loader error: the assembly name and the error message.</param>
    /// <returns>The loadable types.</returns>
    private static Type[] LoadableTypes(Assembly assembly, List<string> loaderErrors)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A game assembly may reference a type that is gone; its other types stay usable.
            string? assemblyName = assembly.GetName().Name;
            int countBefore = loaderErrors.Count;
            foreach (Exception? loaderException in ex.LoaderExceptions)
            {
                if (loaderException is not null)
                {
                    loaderErrors.Add($"{assemblyName}: {loaderException.Message}");
                }
            }

            if (loaderErrors.Count == countBefore)
            {
                loaderErrors.Add($"{assemblyName}: {ex.Message}");
            }

            return ex.Types.OfType<Type>().ToArray();
        }
    }

    /// <summary>Builds the error for a type lookup that found nothing while some types failed to load.</summary>
    /// <param name="name">The type name that was looked up.</param>
    /// <param name="loaderErrors">The loader errors, one line each.</param>
    /// <returns>The error (code <c>type_load_failed</c>); its message names the first distinct loader errors.</returns>
    private static BridgeRequestException TypeLoadFailed(string name, List<string> loaderErrors)
    {
        string[] distinctErrors = loaderErrors.Distinct(StringComparer.Ordinal).ToArray();
        IEnumerable<string> shownErrors = distinctErrors.Take(MaxReportedLoaderErrors);
        string message =
            $"No loaded type is named '{name}', and types of the loaded assemblies failed to load, so it may be one of " +
            $"them. Loader errors: {string.Join("; ", shownErrors)}";
        int notShown = distinctErrors.Length - MaxReportedLoaderErrors;
        if (notShown > 0)
        {
            message += $"; {notShown} more";
        }

        return new BridgeRequestException("type_load_failed", message + ".");
    }

    /// <summary>Resolves a dotted member path to the members it names.</summary>
    /// <param name="target">The object that owns the first member.</param>
    /// <param name="path">Member names separated by dots; each is a property or field of the previous member's type.</param>
    /// <returns>The members, in path order.</returns>
    /// <exception cref="BridgeRequestException">A name is not a field or property (code <c>unknown_member</c>).</exception>
    private static MemberInfo[] MemberPath(object target, string path)
    {
        var members = new List<MemberInfo>();
        Type type = target.GetType();
        foreach (string name in path.Split('.'))
        {
            MemberInfo? member = (MemberInfo?)type.GetProperty(name, InstanceMembers) ?? type.GetField(name, InstanceMembers);
            if (member is null)
            {
                string listing = MemberListing(type);
                throw new BridgeRequestException("unknown_member", $"{type.FullName} has no field or property '{name}'. Members: {listing}.");
            }

            members.Add(member);
            type = TypeOf(member);
        }

        return members.ToArray();
    }

    /// <summary>Lists the names of the properties and fields of a type, for an error message.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The sorted names, separated by commas; compiler-made backing fields are left out.</returns>
    private static string MemberListing(Type type)
    {
        IEnumerable<string> propertyNames = type.GetProperties(InstanceMembers).Select(info => info.Name);
        IEnumerable<string> fieldNames = type.GetFields(InstanceMembers)
            .Where(info => !info.Name.Contains('<'))
            .Select(info => info.Name);
        IEnumerable<string> sorted = propertyNames.Concat(fieldNames).Distinct().OrderBy(text => text);
        return string.Join(", ", sorted);
    }

    /// <summary>Gives the type of a property or field.</summary>
    /// <param name="member">A property or a field.</param>
    /// <returns>The property type or the field type.</returns>
    private static Type TypeOf(MemberInfo member)
    {
        if (member is PropertyInfo property)
        {
            return property.PropertyType;
        }

        return ((FieldInfo)member).FieldType;
    }

    /// <summary>Reads a property or field.</summary>
    /// <param name="member">A property or a field.</param>
    /// <param name="owner">The object that owns the member.</param>
    /// <returns>The value of the member.</returns>
    private static object? ValueOf(MemberInfo member, object owner)
    {
        if (member is PropertyInfo property)
        {
            return property.GetValue(owner);
        }

        return ((FieldInfo)member).GetValue(owner);
    }

    /// <summary>Sets the last member of a path; a struct on the way is written back into its owner.</summary>
    /// <param name="owner">The object that owns the first member.</param>
    /// <param name="path">The members from the owner down to the one to write.</param>
    /// <param name="index">Where in the path to start; 0 for the whole path.</param>
    /// <param name="value">The new value of the last member.</param>
    /// <returns>The owner, after the write.</returns>
    /// <exception cref="BridgeRequestException">A member on the way is null (<c>null_member</c>) or a member cannot be written (<c>read_only_member</c>).</exception>
    private static object SetPath(object owner, MemberInfo[] path, int index, object? value)
    {
        int last = path.Length - 1;
        var owners = new object[path.Length];
        owners[index] = owner;
        for (int step = index; step < last; step++)
        {
            MemberInfo onTheWay = path[step];
            object? inner = ValueOf(onTheWay, owners[step]);
            if (inner is null)
            {
                throw new BridgeRequestException("null_member", $"{onTheWay.Name} is null.");
            }

            owners[step + 1] = inner;
        }

        // Write from the last member back to the first, so a struct is copied back into the object that holds it.
        object? newValue = value;
        for (int step = last; step >= index; step--)
        {
            WriteMemberValue(path[step], owners[step], newValue, step == last);
            newValue = owners[step];
        }

        return owner;
    }

    /// <summary>Writes one member of the path.</summary>
    /// <param name="member">The property or field.</param>
    /// <param name="owner">The object that owns the member.</param>
    /// <param name="newValue">The value to write.</param>
    /// <param name="isLast">Whether this is the last member of the path.</param>
    /// <exception cref="BridgeRequestException">The member cannot be written and the write is needed (code <c>read_only_member</c>).</exception>
    private static void WriteMemberValue(MemberInfo member, object owner, object? newValue, bool isLast)
    {
        if (member is PropertyInfo property && property.CanWrite)
        {
            property.SetValue(owner, newValue);
            return;
        }

        if (member is FieldInfo field && !field.IsInitOnly)
        {
            field.SetValue(owner, newValue);
            return;
        }

        // A read-only member on the way is fine when it holds a reference object: the inner write changed that object.
        if (isLast || TypeOf(member).IsValueType)
        {
            throw new BridgeRequestException("read_only_member", $"{member.Name} cannot be written.");
        }
    }

    /// <summary>Converts a JSON value to a type.</summary>
    /// <param name="value">The JSON value.</param>
    /// <param name="type">The target type.</param>
    /// <param name="what">What is converted (a member or parameter name), for the error.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="BridgeRequestException">The value does not fit the type (code <c>invalid_value</c>).</exception>
    private static object? ConvertJson(JsonElement value, Type type, string what)
    {
        try
        {
            return value.Deserialize(type, MemberValueOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new BridgeRequestException("invalid_value", $"{what}: {value.GetRawText()} is not a {type.FullName} ({ex.Message}).");
        }
    }
}
