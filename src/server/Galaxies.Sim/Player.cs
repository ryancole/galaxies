namespace Galaxies.Sim;

/// <summary>A person who plays the game. Their avatar stays where they left it while they're offline.</summary>
public sealed class Player(int id, string name, Avatar avatar)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public Avatar Avatar { get; } = avatar;

    /// <summary>Whether the player is connected, and so in the world where others can see them.</summary>
    public bool Online { get; internal set; }
}
