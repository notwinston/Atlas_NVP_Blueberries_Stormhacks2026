// The Console's one WebSocket to the Hub (lane D contract 3.3). It reconnects by itself: a Hub restart or a
// laptop that slept must not need a page reload in the middle of a session.

const RETRY_MS = 1000;
const PING_MS = 5000;

export class Feed {
  /**
   * @param {string} url        ws:// address of the Hub's /ws
   * @param {(frame: object) => void} onFrame   every JSON frame with a member t
   * @param {(open: boolean) => void} onStatus  the socket opened or closed
   */
  constructor(url, onFrame, onStatus) {
    this.url = url;
    this.onFrame = onFrame;
    this.onStatus = onStatus;
    this.socket = null;
    this.open = false;
    this.lastFrameMs = 0;
    this.retry = 0;
    this.ping = 0;
  }

  start() {
    this.connect();
    // A frame arrives at least once a second while the Hub lives; the ping keeps a quiet socket honest.
    this.ping = setInterval(() => this.send({ t: 'ping' }), PING_MS);
  }

  connect() {
    let socket;
    try {
      socket = new WebSocket(this.url);
    } catch (e) {
      this.later();
      return;
    }
    this.socket = socket;
    socket.onopen = () => {
      this.open = true;
      this.onStatus(true);
    };
    socket.onmessage = (message) => {
      let frame;
      try {
        frame = JSON.parse(message.data);
      } catch (e) {
        return;
      }
      if (!frame || typeof frame.t !== 'string') return;
      this.lastFrameMs = performance.now();
      this.onFrame(frame);
    };
    socket.onclose = () => {
      if (this.socket !== socket) return;
      this.socket = null;
      if (this.open) {
        this.open = false;
        this.onStatus(false);
      }
      this.later();
    };
    socket.onerror = () => socket.close();
  }

  later() {
    clearTimeout(this.retry);
    this.retry = setTimeout(() => this.connect(), RETRY_MS);
  }

  /** Sends one frame. False when the socket is not open: the caller tells the operator. */
  send(frame) {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) return false;
    this.socket.send(JSON.stringify(frame));
    return true;
  }
}
