namespace Test.Shared.Integration
{
    using System;

    /// <summary>
    /// A disposable stand-in for a pooled connector session.
    /// </summary>
    public class FakeSession : IDisposable
    {
        /// <summary>
        /// Whether it was disposed.
        /// </summary>
        public bool Disposed { get; private set; } = false;

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
