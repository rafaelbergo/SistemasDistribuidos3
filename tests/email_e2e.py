"""Real signed promotions + subscriptions + RabbitMQ -> local mock Resend API.

No real email is sent. Reuses the isolated subscriptions fixture and its cleanup.
"""
import copy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import threading
from urllib.parse import quote
import uuid

from payment_e2e import request, eventually, free_port
from interesses_e2e import main as interesses_main


class MockResend:
    def __init__(self):
        self.calls = []
        self.fail = {"a-falha@example.com": [422] * 10}
        mock = self

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
                    chunks = []
                    while True:
                        size = int(self.rfile.readline().split(b";", 1)[0], 16)
                        if not size:
                            while self.rfile.readline().strip():
                                pass
                            break
                        chunks.append(self.rfile.read(size))
                        assert self.rfile.read(2) == b"\r\n"
                    raw = b"".join(chunks)
                else:
                    raw = self.rfile.read(int(self.headers["Content-Length"]))
                body = json.loads(raw)
                email = body["to"][0]
                statuses = mock.fail.get(email, [])
                code = statuses.pop(0) if statuses else 200
                mock.calls.append({"body": body, "status": code,
                                   "key": self.headers.get("Idempotency-Key"),
                                   "auth": self.headers.get("Authorization"), "path": self.path})
                self.send_response(code)
                self.send_header("Content-Type", "application/json")
                self.send_header("Retry-After", "0")
                self.end_headers()
                self.wfile.write(json.dumps({"id": "mock-" + str(len(mock.calls))} if code == 200 else {"message": "simulated failure"}).encode())

            def log_message(self, *_):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", free_port()), Handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def close(self):
        self.server.shutdown()
        self.server.server_close()


def checks(f, mock):
    # Remove subscriptions from the shared fixture before testing recipient selection.
    for email in f.state():
        assert request(f.gateway + "/api/interesse/" + quote(email, safe=""), "DELETE")[0] == 202
    eventually(lambda: not f.state(), "clear fixture subscriptions")
    for email, cats in [("a-falha@example.com", ["A"]), ("z-sucesso@example.com", ["A"]),
                        ("b-apenas@example.com", ["B"]), ("cancelado@example.com", ["A"])]:
        f.register(email, cats)
        f.categories(email, cats)
    assert request(f.gateway + "/api/interesse/cancelado%40example.com", "DELETE")[0] == 202
    eventually(lambda: "cancelado@example.com" not in f.state(), "cancel recipient")
    f.stop(f.promo)
    promo = f.start("MS.Promocoes", interactive=True)
    exchange = quote("Promoções", safe="")
    f.admin(f"/queues/{f.host_path}/email_audit", "PUT", {"durable": True, "auto_delete": False, "arguments": {}})
    f.admin(f"/bindings/{f.host_path}/e/{exchange}/q/email_audit", "POST", {"routing_key": "promocao.categoria.*", "arguments": {}})

    def get_message(queue, wait=True):
        def get():
            messages = f.admin(f"/queues/{f.host_path}/{queue}/get", "POST", {
                "count": 1, "ackmode": "ack_requeue_false", "encoding": "auto", "truncate": 50000})
            return json.loads(messages[0]["payload"]) if messages else None
        return eventually(get, queue + " message") if wait else get()

    def generate(category):
        promo.stdin.write(f"4\n{category}\n".encode())
        promo.stdin.flush()
        return get_message("email_audit")

    def publish(message):
        assert f.admin(f"/exchanges/{f.host_path}/{exchange}/publish", "POST", {
            "properties": {"delivery_mode": 2}, "routing_key": "promocao.categoria." + message["Content"]["Categoria"],
            "payload": json.dumps(message), "payload_encoding": "string"})["routed"]
        observed = get_message("email_audit")
        assert observed["Content"]["Id"] == message["Content"]["Id"]

    def successes():
        return [c for c in mock.calls if c["status"] == 200]

    first = generate("A")
    eventually(lambda: len(mock.calls) >= 2, "partial dispatch")
    failed = get_message("fila_email_falhas")
    assert failed["Content"]["Id"] == first["Content"]["Id"]
    assert [c["body"]["to"][0] for c in mock.calls] == ["a-falha@example.com", "z-sucesso@example.com"]
    good = successes()[0]
    assert good["auth"] == "Bearer mock-key" and good["path"] == "/emails"
    assert good["body"]["from"] == "Loja <loja@example.com>" and good["key"]
    assert first["Content"]["Produto"] in good["body"]["text"] and "Categoria: A" in good["body"]["text"]
    print("PASS: signed promotion -> category-only email; cancelled recipient excluded; recipient failure does not block others", flush=True)

    mock.fail.clear()
    publish(failed)
    eventually(lambda: len(successes()) == 2, "reprocess failed recipient")
    assert successes()[-1]["body"]["to"] == ["a-falha@example.com"]
    f.stop(promo)
    promo = f.start("MS.Promocoes", interactive=True)
    publish(first)
    generate("B")  # FIFO barrier: replay must finish before the new promotion.
    eventually(lambda: len(successes()) == 3, "category B barrier")
    assert successes()[-1]["body"]["to"] == ["b-apenas@example.com"]
    assert sum(c["body"]["to"] == ["z-sucesso@example.com"] for c in mock.calls) == 1
    print("PASS: partial retry and replay after restart do not duplicate accepted emails", flush=True)

    assert request(f.gateway + "/api/interesse/z-sucesso%40example.com", "DELETE")[0] == 202
    eventually(lambda: "z-sucesso@example.com" not in f.state(), "cancel after previous promotion")
    mock.fail["a-falha@example.com"] = [429, 503]
    before = len(mock.calls)
    generate("A")
    eventually(lambda: len(successes()) == 4, "temporary errors recover")
    attempts = mock.calls[before:]
    assert [c["status"] for c in attempts] == [429, 503, 200]
    assert len({c["key"] for c in attempts}) == 1
    assert all(c["body"]["to"] == ["a-falha@example.com"] for c in attempts)
    print("PASS: cancellation respected on later promotions; 429/503 retries reuse the same idempotency key", flush=True)

    # A forged payload must never reach the external email API.
    tampered = copy.deepcopy(first)
    tampered["MessageId"] = uuid.uuid4().hex
    tampered["Content"]["Produto"] = "FORGED"
    publish(tampered)
    get_message("fila_email_falhas")
    assert all("FORGED" not in c["body"]["subject"] for c in mock.calls)

    # Missing credentials leave durable work queued until configuration is restored.
    f.stop(promo)
    f.env["Email__ApiKey"] = ""
    before = len(mock.calls)
    promo = f.start("MS.Promocoes", interactive=True)
    generate("A")
    queue = eventually(lambda: next((q for q in f.admin(f"/queues/{f.host_path}")
        if q["name"] == "fila_email_promocoes" and q.get("messages_ready", 0) >= 1), None), "mail queued without credentials")
    assert queue.get("consumers", 0) == 0 and len(mock.calls) == before
    f.stop(promo)
    f.env["Email__ApiKey"] = "mock-key"
    promo = f.start("MS.Promocoes", interactive=True)
    eventually(lambda: len(successes()) == 5, "resume pending mail")
    print("PASS: forged signature blocked; missing credentials preserve queued promotions until restart with configuration", flush=True)
    return promo


if __name__ == "__main__":
    mock = MockResend()
    try:
        interesses_main(lambda fixture: checks(fixture, mock), {
            "Email__ApiKey": "mock-key", "Email__From": "Loja <loja@example.com>",
            "Email__Endpoint": f"http://127.0.0.1:{mock.server.server_port}/emails"})
    finally:
        mock.close()
