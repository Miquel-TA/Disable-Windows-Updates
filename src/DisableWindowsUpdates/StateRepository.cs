using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DisableWindowsUpdates
{
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
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(PersistentState));
                    var deserialized = serializer.ReadObject(stream) as PersistentState;
                    state = deserialized ?? new PersistentState();
                    return deserialized != null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load persisted state from disk.", ex);
                state = new PersistentState();
                return false;
            }
        }

        public void Save(PersistentState state)
        {
            Directory.CreateDirectory(_stateDirectory);
            using (var stream = File.Create(GetStateFilePath()))
            {
                var serializer = new DataContractJsonSerializer(typeof(PersistentState));
                serializer.WriteObject(stream, state);
            }
            Logger.Info("Persisted Windows Update state to disk.");
        }

        public void Clear()
        {
            var path = GetStateFilePath();
            if (File.Exists(path))
            {
                File.Delete(path);
                Logger.Info("Cleared persisted Windows Update state from disk.");
            }
        }

        private string GetStateFilePath()
        {
            return Path.Combine(_stateDirectory, StateFileName);
        }
    }

    [DataContract]
    internal sealed class PersistentState
    {
        public PersistentState()
        {
            Services = new Dictionary<string, ServiceSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        [DataMember(Order = 1)]
        public bool UpdatesDisabled { get; set; }

        [DataMember(Order = 2)]
        public Dictionary<string, ServiceSnapshot> Services { get; set; }
    }

    [DataContract]
    internal sealed class ServiceSnapshot
    {
        [DataMember(Order = 1)]
        public uint StartType { get; set; }

        [DataMember(Order = 2)]
        public string SecurityDescriptor { get; set; }
    }
}
