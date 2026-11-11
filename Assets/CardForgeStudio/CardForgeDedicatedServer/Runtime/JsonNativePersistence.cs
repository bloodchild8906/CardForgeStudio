using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardForge.Simulation;
using Newtonsoft.Json;

namespace CardForge.DedicatedServer
{
    /// <summary>
    /// User-scoped JSON persistence for local/demo servers. Production continues
    /// to require MongoDB. Writes are atomic so a crash cannot leave half a file.
    /// </summary>
    public sealed class JsonNativePersistence : INativePersistence
    {
        private readonly string directory;
        private readonly object gate = new object();

        public JsonNativePersistence(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("A persistence directory is required.", nameof(directory));
            this.directory = directory;
            Directory.CreateDirectory(directory);
        }

        public NativeState LoadAccounts()
        {
            lock (gate)
                return Read("players.json", new NativeState());
        }

        public void SaveAccounts(NativeState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            lock (gate)
                Write("players.json", state);
        }

        public List<SimMatch> LoadMatches()
        {
            lock (gate)
                return Read("matches.json", new List<SimMatch>());
        }

        public void SaveMatches(IEnumerable<SimMatch> matches)
        {
            if (matches == null) throw new ArgumentNullException(nameof(matches));
            lock (gate)
                Write("matches.json", matches.ToList());
        }

        public bool CheckHealth()
        {
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, ".health");
                File.WriteAllText(probe, DateTimeOffset.UtcNow.ToString("o"));
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private T Read<T>(string name, T fallback)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) return fallback;
            try
            {
                var value = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
                return value == null ? fallback : value;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Corrupt CardForge demo state: " + path, exception);
            }
        }

        private void Write<T>(string name, T value)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            var temporary = path + ".tmp";
            var json = JsonConvert.SerializeObject(value, Formatting.Indented);
            File.WriteAllText(temporary, json);
            if (File.Exists(path))
            {
                var backup = path + ".previous";
                try
                {
                    File.Replace(temporary, path, backup, true);
                    if (File.Exists(backup)) File.Delete(backup);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(path);
                    File.Move(temporary, path);
                }
            }
            else File.Move(temporary, path);
        }

        public void Dispose() { }
    }
}
