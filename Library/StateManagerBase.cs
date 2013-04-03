using System;

namespace Library
{
    public enum ManagerState
    {
        Start,
        Stop,
    }

    public abstract class StateManagerBase : ManagerBase
    {
        public abstract void Start();
        public abstract void Stop();

        public void Restart()
        {
            this.Stop();
            this.Start();
        }

        public abstract ManagerState State { get; }
    }

    [Serializable]
    public class StateManagerException : ManagerException
    {
        public StateManagerException() : base() { }
        public StateManagerException(string message) : base(message) { }
        public StateManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
