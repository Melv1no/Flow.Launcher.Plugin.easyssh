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
        if (File.Exists(path))
        {
            try
            {
                MigrateToNewFormat(path);
            }
            catch
            {
                
            }
        }
        InitializeDatabase();
        _file = File.ReadAllText(_path);
    }

    private void InitializeDatabase()
    {
        if (!IsDatabaseCreated())
        {
            try
            {
                File.WriteAllText(_path, "{\"version\": \"1.0\", \"customShells\": {}, \"profiles\": {}}");
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
        var profilesJson = jsonObject["profiles"] as JObject;

        foreach (var (key, value) in profilesJson)
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
        var profilesJson = jsonObject["profiles"] as JObject;

        var lastId = profilesJson.Properties().Any() ? int.Parse(profilesJson.Properties().Last().Name) : 0;
        var newId = lastId + 1;

        var newProfile = new Profile(newId, name, command);
        profilesJson[newId.ToString()] = JObject.FromObject(newProfile);

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
        var profilesJson = jsonObject["profiles"] as JObject;

        if (profilesJson.ContainsKey(id.ToString()))
        {
            profilesJson.Remove(id.ToString());
            _file = jsonObject.ToString();
            File.WriteAllText(_path, _file);
        }
    }

    /// <summary>
    /// Retrieves the custom shells stored in the JSON file.
    /// </summary>
    /// <returns>A dictionary of custom shell key-value pairs.</returns>
    public Dictionary<string, string> GetCustomShells()
    {
        var jsonObject = JObject.Parse(_file);
        var customShellsJson = jsonObject["customShells"] as JObject;
        var customShells = new Dictionary<string, string>();

        foreach (var (key, value) in customShellsJson)
        {
            customShells[key] = value.ToString();
        }

        return customShells;
    }

    /// <summary>
    /// Adds a new custom shell to the JSON file.
    /// </summary>
    /// <param name="key">The key for the custom shell.</param>
    /// <param name="command">The command associated with the custom shell.</param>
    public void AddCustomShell(string key, string command)
    {
        var jsonObject = JObject.Parse(_file);
        var customShellsJson = jsonObject["customShells"] as JObject;

        customShellsJson[key] = command;

        _file = jsonObject.ToString();
        File.WriteAllText(_path, _file);
    }

    /// <summary>
    /// Retrieves the version number of the JSON file.
    /// </summary>
    /// <returns>The version number.</returns>
    public string GetVersion()
    {
        var jsonObject = JObject.Parse(_file);
        return jsonObject["version"]?.ToString();
    }

    /// <summary>
    /// Updates the version number in the JSON file.
    /// </summary>
    /// <param name="version">The new version number.</param>
    public void SetVersion(string version)
    {
        var jsonObject = JObject.Parse(_file);
        jsonObject["version"] = version;

        _file = jsonObject.ToString();
        File.WriteAllText(_path, _file);
    }
    public void MigrateToNewFormat(string path)
    {
        var oldFileContent = File.ReadAllText(_path);
        File.WriteAllText(path+".old",oldFileContent);
        var oldJsonObject = JObject.Parse(oldFileContent);
        
        var newJsonObject = new JObject();
    
        newJsonObject["version"] = "1.0";  
    
        var customShells = new JObject();
        newJsonObject["customShells"] = customShells;

        var profiles = new JObject();

        foreach (var (key, value) in oldJsonObject)
        {
            if (value is JObject data)
            {
                var id = int.Parse(key);
                var name = data["Name"]?.ToString();
                var command = data["Command"]?.ToString();

                var profile = new JObject
                {
                    { "Id", id },
                    { "Name", name },
                    { "Command", command }
                };
            
                profiles[key] = profile;
            }
        }

        newJsonObject["profiles"] = profiles;

        _file = newJsonObject.ToString();
        File.WriteAllText(_path, _file);
    }

}
