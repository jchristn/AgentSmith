namespace Test.Shared.Agent.StubLlm
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The stub LLM's script: chat requests are answered in order from a queue. A <see cref="Responder"/>, when set,
    /// sees each request first and may answer it (return non-null) instead of the queue, which suits dynamic or
    /// repeating behavior (for example "always fail" or benchmark loops). When neither answers,
    /// <see cref="DefaultResponse"/> is used. Thread-safe.
    /// </summary>
    public class StubLlmScript
    {
        #region Public-Members

        /// <summary>
        /// Optional responder consulted before the queue. Null for none.
        /// </summary>
        public Func<StubRecordedRequest, StubLlmResponse?>? Responder { get; set; } = null;

        /// <summary>
        /// Response used when the queue is empty and no responder answers. Never null.
        /// </summary>
        public StubLlmResponse DefaultResponse { get; set; } = StubLlmResponse.Message("stub: no scripted response");

        /// <summary>
        /// Responses still queued.
        /// </summary>
        public int Remaining
        {
            get
            {
                lock (_Lock) return _Queue.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly Queue<StubLlmResponse> _Queue = new Queue<StubLlmResponse>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Queue responses.
        /// </summary>
        /// <param name="responses">Responses.</param>
        /// <returns>This script.</returns>
        public StubLlmScript Then(params StubLlmResponse[] responses)
        {
            lock (_Lock)
            {
                foreach (StubLlmResponse r in responses) _Queue.Enqueue(r);
            }
            return this;
        }

        /// <summary>
        /// Pick the response for a request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Response.</returns>
        public StubLlmResponse Next(StubRecordedRequest request)
        {
            Func<StubRecordedRequest, StubLlmResponse?>? responder = Responder;
            if (responder != null)
            {
                StubLlmResponse? answered = responder(request);
                if (answered != null) return answered;
            }
            lock (_Lock)
            {
                if (_Queue.Count > 0) return _Queue.Dequeue();
            }
            return DefaultResponse;
        }

        #endregion
    }
}
