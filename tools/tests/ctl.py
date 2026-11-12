"""Sends one command to a game started with --dwmp-test-control PORT and prints its reply.

    ctl.py PORT WORD [WORD...]     e.g.  ctl.py 27799 args --dwmp-host --dwmp-continue 1

The words go as one line, separated by tabs (so a word may hold spaces). Exits 1 if the reply isn't
ok/ready/done (optionally followed by more words).

As a module, Ctl(port) keeps one connection open for many commands (tools/tests/mapdriver.py):
    c = Ctl(27799); reply = c.cmd("do", "toggle-door"); c.ok(reply)
"""
import socket, sys, time


def good(reply):
    return reply.split(" ", 1)[0] in ("ok", "ready", "done")


class Ctl:
    def __init__(self, port, connect_for=0.0):
        self.port = port
        until = time.time() + connect_for
        while True:
            try:
                self.s = socket.create_connection(("127.0.0.1", port), timeout=5)
                break
            except OSError:
                if time.time() >= until:
                    raise
                time.sleep(1)
        self.buf = b""

    def cmd(self, *words, timeout=200):
        """One command; its one-line reply (a reset may wait for a world to finish loading)."""
        self.s.settimeout(timeout)
        self.s.sendall(("\t".join(str(w) for w in words) + "\n").encode())
        while b"\n" not in self.buf:
            d = self.s.recv(4096)
            if not d:
                raise ConnectionError(f"the game on {self.port} closed the control connection")
            self.buf += d
        line, self.buf = self.buf.split(b"\n", 1)
        return line.decode().strip()

    def close(self):
        try:
            self.s.close()
        except OSError:
            pass


if __name__ == "__main__":
    port, words = int(sys.argv[1]), sys.argv[2:]
    c = Ctl(port)
    try:
        reply = c.cmd(*words)
    except (OSError, ConnectionError) as e:
        reply = f"no reply ({e})"
    if not good(reply):
        print(f"control: {' '.join(words)} -> {reply or 'no reply'}")
        sys.exit(1)
