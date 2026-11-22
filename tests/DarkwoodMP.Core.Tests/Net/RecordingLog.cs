using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// An ILog that keeps what was written, for the tests to read.
    public sealed class RecordingLog : ILog
    {
        public List<string> Lines { get; } = new List<string>();

        public void LogInfo(string text) => Lines.Add("info: " + text);

        public void LogWarning(string text) => Lines.Add("warning: " + text);

        public void LogError(string text) => Lines.Add("error: " + text);
    }
}
