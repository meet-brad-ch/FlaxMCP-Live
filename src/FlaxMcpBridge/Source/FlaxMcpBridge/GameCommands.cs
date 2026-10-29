#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FlaxMcpBridge;

/// <summary>
/// Named commands a game registers for the FlaxMcp bridge: an MCP client lists them (<c>game_commands</c>) and runs
/// them (<c>game_command</c>) in edit mode and in play mode. A handler runs on the main thread, takes the client's
/// argument string and returns a text result; it must return quickly (the bridge request times out after 5 seconds).
/// </summary>
/// <remarks>
/// The registry is static because game scripts register from their own lifetimes (OnEnable/OnDisable) and the
/// bridge has no scene object to hold it; a script reload starts it empty and the scripts register again. Reads take
/// a snapshot, so listing never blocks and the game makes no garbage while nothing registers.
/// </remarks>
public static class GameCommands
{
    /// <summary>Serializes writers of <see cref="_commands"/>; readers use a snapshot and take no lock.</summary>
    private static readonly object Gate = new();

    /// <summary>The registered commands; replaced as a whole on every change, never edited in place.</summary>
    private static GameCommand[] _commands = Array.Empty<GameCommand>();

    /// <summary>Registers a command until the returned registration is disposed.</summary>
    /// <param name="name">Lowercase letters, digits and dashes, starting with a letter.</param>
    /// <param name="description">What the command does and what its argument means.</param>
    /// <param name="handler">Runs the command on the main thread: argument string in, text result out.</param>
    /// <returns>Disposing it removes the command.</returns>
    /// <exception cref="ArgumentException">The name breaks the naming rule or the description is empty.</exception>
    /// <exception cref="InvalidOperationException">A command with this name is already registered.</exception>
    public static GameCommandRegistration Register(string name, string description, Func<string, string> handler)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException($"Game command name '{name}' must be lowercase letters, digits and dashes, starting with a letter.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException($"Game command '{name}' needs a description.", nameof(description));
        }

        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        string owner = OwnerOf(handler);
        var info = new GameCommandInfo(name, description, owner);
        var command = new GameCommand(info, handler);
        AddCommand(command);
        return new GameCommandRegistration(command);
    }

    /// <summary>Adds a command to the registry under the lock.</summary>
    /// <param name="command">The command to add.</param>
    /// <exception cref="InvalidOperationException">A command with this name is already registered.</exception>
    private static void AddCommand(GameCommand command)
    {
        string name = command.Info.Name;
        lock (Gate)
        {
            GameCommand? existing = _commands.FirstOrDefault(registered => registered.Info.Name == name);
            if (existing is not null)
            {
                throw new InvalidOperationException($"Game command '{name}' is already registered by {existing.Info.Owner}.");
            }

            GameCommand[] extended = _commands.Append(command).ToArray();
            Volatile.Write(ref _commands, extended);
        }
    }

    /// <summary>The registered commands, sorted by name.</summary>
    /// <returns>A snapshot.</returns>
    public static IReadOnlyList<GameCommandInfo> List() =>
        Volatile.Read(ref _commands).Select(command => command.Info).OrderBy(info => info.Name, StringComparer.Ordinal).ToArray();

    /// <summary>Finds a command by name.</summary>
    /// <param name="name">The command name.</param>
    /// <returns>The command, or null when none is registered under the name.</returns>
    public static GameCommand? Find(string name) =>
        Volatile.Read(ref _commands).FirstOrDefault(command => command.Info.Name == name);

    /// <summary>The type that wrote the handler: a lambda's compiler-made closure class names its enclosing type.</summary>
    private static string OwnerOf(Delegate handler)
    {
        var type = handler.Method.DeclaringType;
        while (type is not null && type.Name.Contains('<') && type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type?.FullName ?? "?";
    }

    /// <summary>Checks the naming rule: lowercase letters, digits and dashes, starting with a letter.</summary>
    /// <param name="name">The candidate name.</param>
    /// <returns>True when the name follows the rule.</returns>
    private static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (!IsLowercaseLetter(name[0]))
        {
            return false;
        }

        foreach (char character in name)
        {
            if (!IsNameCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Tells whether a character is a lowercase ASCII letter.</summary>
    /// <param name="character">The character to test.</param>
    /// <returns>True for a to z.</returns>
    private static bool IsLowercaseLetter(char character)
    {
        return character >= 'a' && character <= 'z';
    }

    /// <summary>Tells whether a character may appear in a command name.</summary>
    /// <param name="character">The character to test.</param>
    /// <returns>True for a lowercase letter, a digit or a dash.</returns>
    private static bool IsNameCharacter(char character)
    {
        if (IsLowercaseLetter(character))
        {
            return true;
        }

        bool isDigit = character >= '0' && character <= '9';
        return isDigit || character == '-';
    }

    /// <summary>Removes a command from the registry; a command that is not registered is ignored.</summary>
    /// <param name="command">The command to remove.</param>
    internal static void Unregister(GameCommand command)
    {
        lock (Gate)
        {
            GameCommand[] remaining = _commands.Where(existing => !ReferenceEquals(existing, command)).ToArray();
            Volatile.Write(ref _commands, remaining);
        }
    }
}

/// <summary>What a client sees of a game command.</summary>
public sealed class GameCommandInfo
{
    /// <summary>Creates the description of a command.</summary>
    /// <param name="name">The command name.</param>
    /// <param name="description">What it does and what its argument means.</param>
    /// <param name="owner">The type that declared the handler.</param>
    public GameCommandInfo(string name, string description, string owner)
    {
        Name = name;
        Description = description;
        Owner = owner;
    }

    /// <summary>The command name.</summary>
    public string Name { get; }

    /// <summary>What it does and what its argument means.</summary>
    public string Description { get; }

    /// <summary>The type that declared the handler.</summary>
    public string Owner { get; }
}

/// <summary>A registered command: its description and its handler.</summary>
public sealed class GameCommand
{
    /// <summary>Creates a registered command.</summary>
    /// <param name="info">What a client sees.</param>
    /// <param name="handler">Runs the command on the main thread.</param>
    internal GameCommand(GameCommandInfo info, Func<string, string> handler)
    {
        Info = info;
        Handler = handler;
    }

    /// <summary>What a client sees.</summary>
    public GameCommandInfo Info { get; }

    /// <summary>Runs the command: argument string in, text result out. Main thread only.</summary>
    public Func<string, string> Handler { get; }
}

/// <summary>Keeps a command registered until disposed.</summary>
public sealed class GameCommandRegistration : IDisposable
{
    /// <summary>The registered command; null once disposed.</summary>
    private GameCommand? _command;

    /// <summary>Creates the registration of a command that is already in the registry.</summary>
    /// <param name="command">The command to remove on dispose.</param>
    internal GameCommandRegistration(GameCommand command)
    {
        _command = command;
    }

    /// <summary>Removes the command; disposing twice does nothing more.</summary>
    public void Dispose()
    {
        GameCommand? command = Interlocked.Exchange(ref _command, null);
        if (command is not null)
        {
            GameCommands.Unregister(command);
        }
    }
}
