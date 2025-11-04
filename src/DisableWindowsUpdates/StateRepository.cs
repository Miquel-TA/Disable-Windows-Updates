using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DisableWindowsUpdates;

internal sealed class StateRepository
{
    private const string StateFileName = "state.json";
    private readonly string _stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DisableWindowsUpdates");

    public bool TryLoad(out PersistentState state)
    {
        var path = GetStateFilePath();
        if (!File.Exists(path))
        {
            state = new PersistentState();
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var serializer = new DataContractJsonSerializer(typeof(PersistentState));
            state = (PersistentState?)serializer.ReadObject(stream) ?? new PersistentState();
            return true;
        }
        catch
        {
            state = new PersistentState();
            return false;
        }
    }

    public void Save(PersistentState state)
    {
        Directory.CreateDirectory(_stateDirectory);
        using var stream = File.Create(GetStateFilePath());
        var serializer = new DataContractJsonSerializer(typeof(PersistentState));
        serializer.WriteObject(stream, state);
    }

    public void Clear()
    {
        var path = GetStateFilePath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetStateFilePath() => Path.Combine(_stateDirectory, StateFileName);
}

[DataContract]
internal sealed class PersistentState
{
    [DataMember(Order = 1)]
    public bool UpdatesDisabled { get; set; }

    [DataMember(Order = 2)]
    public Dictionary<string, ServiceSnapshot> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[DataContract]
internal sealed class ServiceSnapshot
{
    [DataMember(Order = 1)]
    public uint StartType { get; set; }

    [DataMember(Order = 2)]
    public string? SecurityDescriptor { get; set; }
}
