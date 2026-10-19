"""Real REST + RabbitMQ + webhook + SSE regression, with disposable data/vhost.

Build first: dotnet build SistemasDistribuidos2.slnx --artifacts-path artifacts/payment-tests -m:1
Run: python tests/payment_e2e.py
Uses RabbitMQ management on localhost:15672 (guest/guest by default).
"""
import base64
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import threading
import time
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen
import uuid

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "artifacts" / "payment-tests" / "bin"
MANAGEMENT = os.environ.get("RABBITMQ_MANAGEMENT_URL", "http://localhost:15672")
USER = os.environ.get("RABBITMQ_TEST_USER", "guest")
PASSWORD = os.environ.get("RABBITMQ_TEST_PASSWORD", "guest")
AUTH = base64.b64encode(f"{USER}:{PASSWORD}".encode()).decode()


def request(url, method="GET", data=None, admin=False):
    headers = {"Content-Type": "application/json"}
    if admin:
        headers["Authorization"] = "Basic " + AUTH
    body = json.dumps(data).encode() if data is not None else None
    try:
        with urlopen(Request(url, body, headers, method=method), timeout=10) as response:
            return response.status, response.read().decode()
    except HTTPError as error:
        return error.code, error.read().decode()


def eventually(check, description, timeout=25):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(0.1)
    raise AssertionError("Timeout: " + description)


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


class Stream:
    def __init__(self, url):
        self.events = []
        self.ready = threading.Event()
        self.error = None
        self.url = url
        threading.Thread(target=self.read, daemon=True).start()
        assert self.ready.wait(10), "SSE connection did not open"
        assert not self.error, self.error

    def read(self):
        try:
            with urlopen(self.url, timeout=60) as response:
                self.ready.set()
                for raw in response:
                    line = raw.decode().strip()
                    if line.startswith("data:"):
                        self.events.append(json.loads(line[5:]))
        except Exception as error:
            self.error = str(error)
            self.ready.set()

    def for_order(self, order_id):
        return [e for e in self.events if e.get("pedidoId") == order_id]

    def wait(self, order_id, status):
        return eventually(lambda: next((e for e in self.for_order(order_id)
                                       if e["status"] == status), None), f"{order_id}: {status}")


def main():
    services = ["MS.Estoque", "Mock.Pagamento", "MS.Pagamento", "MS.Entrega", "MS.Principal"]
    for name in services:
        assert (BUILD / name / "debug" / (name + ".dll")).exists(), f"Build {name} first"

    run_dir = ROOT / "artifacts" / "payment-tests" / "runs"
    assert run_dir.resolve().is_relative_to(ROOT)
    run_dir.mkdir(parents=True, exist_ok=True)
    vhost = "payment-test-" + uuid.uuid4().hex
    vhost_path = quote(vhost, safe="")
    code, body = request(f"{MANAGEMENT}/api/vhosts/{vhost_path}", "PUT", {}, True)
    assert code in (201, 204), (code, body)
    processes, logs = [], []
    try:
        code, body = request(f"{MANAGEMENT}/api/permissions/{vhost_path}/{quote(USER, safe='')}",
                             "PUT", {"configure": ".*", "write": ".*", "read": ".*"}, True)
        assert code in (201, 204), (code, body)
        with tempfile.TemporaryDirectory(prefix="payment-", dir=run_dir) as temporary:
            isolated = Path(temporary)
            urls = {name: f"http://127.0.0.1:{free_port()}" for name in services if name != "MS.Entrega"}
            env = dict(os.environ)
            env.update({
                "RabbitMq__Uri": f"amqp://{quote(USER, safe='')}:{quote(PASSWORD, safe='')}@localhost:5672/{vhost_path}",
                "SOLUTION_ROOT_PATH": str(isolated),
                "Estoque__Url": urls["MS.Estoque"],
                "Pagamento__MockUrl": urls["Mock.Pagamento"],
                "Pagamento__WebhookUrl": urls["MS.Pagamento"] + "/api/webhook",
                "ASPNETCORE_ENVIRONMENT": "Development",
            })
            try:
                for name in services:
                    content = isolated / name
                    content.mkdir(parents=True, exist_ok=True)
                    if name == "MS.Estoque":
                        (content / "Estoque.ini").write_text("[Estoque]\nA=10;25.00\nB=7;10.00\n")
                    log = open(isolated / (name + ".log"), "w", encoding="utf-8")
                    logs.append(log)
                    command = ["dotnet", str(BUILD / name / "debug" / (name + ".dll"))]
                    if name != "MS.Entrega":
                        command += ["--contentRoot", str(content), "--urls", urls[name]]
                    process = subprocess.Popen(command, cwd=content, env=env, stdin=subprocess.PIPE,
                                               stdout=log, stderr=subprocess.STDOUT)
                    processes.append(process)
                    if name != "MS.Entrega":
                        def listening():
                            assert process.poll() is None, f"{name} exited"
                            try:
                                return request(urls[name] + "/ready-probe")[0] == 404
                            except OSError:
                                return False
                        eventually(listening, name + " startup")

                def queues_ready():
                    code, data = request(f"{MANAGEMENT}/api/queues/{vhost_path}", admin=True)
                    queues = {q["name"]: q.get("consumers", 0) for q in json.loads(data)} if code == 200 else {}
                    return all(queues.get(q, 0) >= 1 for q in
                               ["fila_principal", "fila_estoque", "fila_pagamento", "fila_entrega"])
                eventually(queues_ready, "all RabbitMQ consumers ready")

                gateway, mock, payment = urls["MS.Principal"], urls["Mock.Pagamento"], urls["MS.Pagamento"]
                stream = Stream(gateway + "/api/pedidos/eventos?clienteId=teste%40example.com")
                other = Stream(gateway + "/api/pedidos/eventos?clienteId=outro%40example.com")

                def inventory(product):
                    code, data = request(urls["MS.Estoque"] + "/api/produtos/" + product)
                    assert code == 200
                    return json.loads(data)["quantidadeDisponivel"]

                def order(product, quantity):
                    code, data = request(gateway + "/api/pedidos", "POST", {
                        "clienteId": "teste@example.com", "itens": [{"id": product, "quantidade": quantity}]})
                    assert code == 202, (code, data)
                    return json.loads(data)["pedidoId"]

                def webhook(order_id, status):
                    return request(payment + "/api/webhook", "POST", {"pedidoId": order_id, "status": status})[0]

                approved = order("A", 2)
                pending = stream.wait(approved, "pagamento_pendente")
                stream.wait(approved, "estoque_confirmado")
                assert inventory("A") == 8
                code, html = request(pending["checkoutUrl"])
                assert code == 200 and "Pagamento Aprovado" in html and "Pagamento Recusado" in html
                assert webhook(approved, "INVALIDO") == 400
                code, _ = request(mock + f"/checkout/{approved}/aprovar", "POST")
                assert code == 200
                stream.wait(approved, "pagamento_aprovado")
                stream.wait(approved, "pedido_enviado")
                with ThreadPoolExecutor(max_workers=8) as pool:
                    assert all(code == 200 for code in pool.map(lambda _: webhook(approved, "APROVADO"), range(8)))
                assert webhook(approved, "RECUSADO") == 409
                assert inventory("A") == 8
                print("PASS: approved checkout -> webhook -> SSE -> delivery; concurrent duplicate callbacks", flush=True)

                refused = order("B", 3)
                stream.wait(refused, "pagamento_pendente")
                assert inventory("B") == 4
                assert request(mock + f"/checkout/{refused}/recusar", "POST")[0] == 200
                stream.wait(refused, "pagamento_recusado")
                eventually(lambda: inventory("B") == 7, "release reserved inventory")
                assert webhook(refused, "RECUSADO") == 200
                # A duplicated checkout creation must preserve the original decision.
                code, _ = request(mock + "/api/cobranca", "POST", {"pedidoId": refused, "valorTotal": 30,
                    "webhookUrl": payment + "/api/webhook"})
                assert code == 201
                assert request(mock + f"/checkout/{refused}/aprovar", "POST")[0] == 200
                print("PASS: refused checkout -> SSE -> exact inventory restoration; repeated checkout preserves decision", flush=True)

                unavailable = order("A", 100)
                stream.wait(unavailable, "estoque_indisponivel")
                assert inventory("A") == 8
                assert request(mock + f"/checkout/{unavailable}")[0] == 404
                assert webhook("unknown", "APROVADO") == 404
                assert webhook(approved, None) == 400
                time.sleep(1)
                assert not other.events, "SSE must only deliver the client's orders"
                assert not any(e["status"] == "pagamento_pendente" for e in stream.for_order(unavailable))
                assert not any(e["status"] == "pedido_enviado" for e in stream.for_order(refused))
                assert sum(e["status"] == "pagamento_aprovado" for e in stream.for_order(approved)) == 1
                assert sum(e["status"] == "pedido_enviado" for e in stream.for_order(approved)) == 1
                assert sum(e["status"] == "pagamento_recusado" for e in stream.for_order(refused)) == 1
                assert inventory("B") == 7
                replay = Stream(gateway + "/api/pedidos/eventos?clienteId=teste%40example.com")
                replay.wait(approved, "pedido_enviado")
                replay.wait(refused, "pagamento_recusado")
                print("PASS: insufficient inventory creates no charge; invalid callbacks; SSE isolation; no duplicate events", flush=True)
            except Exception:
                for log in logs:
                    log.flush()
                    print(f"\n--- {Path(log.name).name} ---\n{Path(log.name).read_text(encoding='utf-8', errors='replace')[-12000:]}")
                raise
            finally:
                for process in reversed(processes):
                    if process.poll() is None:
                        process.terminate()
                for process in processes:
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
                    if process.stdin:
                        process.stdin.close()
                for log in logs:
                    log.close()
    finally:
        code, body = request(f"{MANAGEMENT}/api/vhosts/{vhost_path}", "DELETE", admin=True)
        assert code in (204, 404), f"Failed to clean disposable vhost {vhost}: {code} {body}"


if __name__ == "__main__":
    main()
