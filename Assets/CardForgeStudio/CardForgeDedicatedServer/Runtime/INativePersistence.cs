using System;
using System.Collections.Generic;
using CardForge.Simulation;

namespace CardForge.DedicatedServer
{
    /// <summary>
    /// Persistence boundary. Production always uses MongoNativePersistence.
    /// Tests can inject an isolated in-memory implementation.
    /// </summary>
    public interface INativePersistence : IDisposable
    {
        NativeState LoadAccounts();
        void SaveAccounts(NativeState state);
        List<SimMatch> LoadMatches();
        void SaveMatches(IEnumerable<SimMatch> matches);
        bool CheckHealth();
    }
}
