using System;
using UnityEngine.Networking;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The Forge transport of the running app: UnityWebRequest, started and looked at from the main thread at
    /// step 8b, never waited for (docs/contracts/lane-d-link.md section 4.3).
    /// </summary>
    public sealed class UnityForgeTransport : IForgeTransport, IDisposable
    {
        // An upload, a poll, a download and a delete can be in flight together; the rest is headroom.
        const int Slots = 8;

        readonly UnityWebRequest[] _requests = new UnityWebRequest[Slots];
        readonly int[] _handles = new int[Slots];
        int _lastHandle;

        /// <summary>Why the last request could not be started, or null.</summary>
        public string LastError { get; private set; }

        public int Begin(string method, string url, byte[] body, string contentType, float timeoutSeconds)
        {
            int slot = Array.IndexOf(_handles, 0);
            if (slot < 0)
            {
                // Never expected; the oldest request gives way rather than the caller failing.
                slot = 0;
                Release(slot);
            }
            var request = new UnityWebRequest(url, method);
            if (body != null)
            {
                var upload = new UploadHandlerRaw(body);
                if (!string.IsNullOrEmpty(contentType)) upload.contentType = contentType;
                request.uploadHandler = upload;
            }
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Math.Max(1, (int)Math.Ceiling(timeoutSeconds));
            try
            {
                request.SendWebRequest();
            }
            catch (InvalidOperationException e)
            {
                // Unity refuses plain http when the player setting "Allow downloads over HTTP" is off. The
                // request then counts as unanswered, and the reason is kept for the log.
                LastError = e.Message;
                request.Dispose();
                if (++_lastHandle == 0) _lastHandle = 1;
                return _lastHandle;                         // unknown to TryTake: ended with no answer
            }

            if (++_lastHandle == 0) _lastHandle = 1;
            _requests[slot] = request;
            _handles[slot] = _lastHandle;
            return _lastHandle;
        }

        public bool TryTake(int handle, out int status, out byte[] body)
        {
            status = 0;
            body = null;
            int slot = handle != 0 ? Array.IndexOf(_handles, handle) : -1;
            if (slot < 0) return true;                      // unknown or cancelled: it has ended with no answer
            UnityWebRequest request = _requests[slot];
            if (!request.isDone) return false;
            // A protocol error is still an answer of the Forge; only a missing one is status 0.
            if (request.result == UnityWebRequest.Result.Success || request.result == UnityWebRequest.Result.ProtocolError)
            {
                status = (int)request.responseCode;
                body = request.downloadHandler != null ? request.downloadHandler.data : null;
            }
            Release(slot);
            return true;
        }

        public void Cancel(int handle)
        {
            int slot = handle != 0 ? Array.IndexOf(_handles, handle) : -1;
            if (slot < 0) return;
            _requests[slot].Abort();
            Release(slot);
        }

        public void Dispose()
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                if (_handles[slot] == 0) continue;
                _requests[slot].Abort();
                Release(slot);
            }
        }

        void Release(int slot)
        {
            if (_requests[slot] != null) _requests[slot].Dispose();
            _requests[slot] = null;
            _handles[slot] = 0;
        }
    }
}
