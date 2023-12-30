using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;

public class Profile
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Command { get; set; }

    public Profile( int id, string name, string command)
    {
        Id = id;
        Name = name;
        Command = command;
    }
}

public class ProfileManager
{
    private string _path;
    private string _file;
    
    public ProfileManager(String path)
    {
        _path = path;
        
        if (!isDatabaseCreated())
        {
            File.WriteAllText(_path,"{}");
        }
        _file = File.ReadAllText(_path);
    }

    public bool isDatabaseCreated()
    {
        return File.Exists(_path);
    }

    public List<Profile> getProfiles()
    {
        List<Profile> profiles = new List<Profile>();
        JObject jsonObject = JObject.Parse(_file);

        foreach (var item in jsonObject)
        {
            string key = item.Key;
            JObject data = item.Value as JObject;

            string id = data["Id"].ToString();
            string profile = data["Name"].ToString();
            string command = data["Command"].ToString();
            profiles.Add(new Profile(int.Parse(key),profile,command ));
        }

        return profiles;
    }
    public void addProfile(string name, string command)
    {
        JObject jsonObject = JObject.Parse(_file);

        int lastId = jsonObject.Properties().Any() ? int.Parse(jsonObject.Properties().Last().Name) : 0;

        int newId = lastId + 1;

        Profile newProfile = new Profile(newId, name, command);

        jsonObject[newId.ToString()] = JObject.FromObject(newProfile);

        _file = jsonObject.ToString();
        File.WriteAllText(_path, _file);
    }
    
    public void removeProfile(int id)
    {
        JObject jsonObject = JObject.Parse(_file);

        if (jsonObject.ContainsKey(id.ToString()))
        {
            jsonObject.Remove(id.ToString());
            _file = jsonObject.ToString();
            File.WriteAllText(_path, _file);
        }
    }
}