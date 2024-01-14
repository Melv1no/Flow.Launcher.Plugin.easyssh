using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flow.Launcher.Plugin.EasySsh;
using Newtonsoft.Json.Linq;

/// <summary>
/// Represents a user profile with a unique identifier, name, and associated command.
/// </summary>
public class Profile
{
    /// <summary>
    /// Gets or sets the unique identifier of the profile.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the name associated with the profile.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the command associated with the profile.
    /// </summary>
    public string Command { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Profile"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the profile.</param>
    /// <param name="name">The name associated with the profile.</param>
    /// <param name="command">The command associated with the profile.</param>
    public Profile(int id, string name, string command)
    {
        Id = id;
        Name = name;
        Command = command;
    }
}

/// <summary>
/// Manages user profiles stored in a JSON file.
/// </summary>
public class ProfileManager
{
    private readonly string _path;
    private string _file;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileManager"/> class.
    /// </summary>
    /// <param name="path">The file path to manage user profiles.</param>
    public ProfileManager(string path)
    {
        _path = path;

        InitializeDatabase();
        _file = File.ReadAllText(_path);
    }

    private void InitializeDatabase()
    {
        if (!IsDatabaseCreated())
        {
            try
            {
                File.WriteAllText(_path, "{}");
            }
            catch (IOException e)
            {
            }
        }
    }

    /// <summary>
    /// Checks if the user profiles database file exists.
    /// </summary>
    /// <returns><c>true</c> if the database file exists; otherwise, <c>false</c>.</returns>
    public bool IsDatabaseCreated()
    {
        return File.Exists(_path);
    }

    /// <summary>
    /// Retrieves all user profiles from the JSON file.
    /// </summary>
    /// <returns>A list of <see cref="Profile"/> objects.</returns>
    public List<Profile> GetProfiles()
    {
        var profiles = new List<Profile>();
        var jsonObject = JObject.Parse(_file);

        foreach (var (key, value) in jsonObject)
        {
            if (value is JObject data)
                profiles.Add(new Profile(int.Parse(key), data["Name"]?.ToString(), data["Command"]?.ToString()));
        }

        return profiles;
    }

    /// <summary>
    /// Adds a new user profile to the JSON file.
    /// </summary>
    /// <param name="name">The name associated with the new profile.</param>
    /// <param name="command">The command associated with the new profile.</param>
    public void AddProfile(string name, string command)
    {
        var jsonObject = JObject.Parse(_file);

        var lastId = jsonObject.Properties().Any() ? int.Parse(jsonObject.Properties().Last().Name) : 0;
        var newId = lastId + 1;

        var newProfile = new Profile(newId, name, command);
        jsonObject[newId.ToString()] = JObject.FromObject(newProfile);

        _file = jsonObject.ToString();
        File.WriteAllText(_path, _file);
    }

    /// <summary>
    /// Removes a user profile by its identifier from the JSON file.
    /// </summary>
    /// <param name="id">The unique identifier of the profile to be removed.</param>
    public void RemoveProfile(int id)
    {
        var jsonObject = JObject.Parse(_file);

        if (jsonObject.ContainsKey(id.ToString()))
        {
            jsonObject.Remove(id.ToString());
            _file = jsonObject.ToString();
            File.WriteAllText(_path, _file);
        }
    }
}
