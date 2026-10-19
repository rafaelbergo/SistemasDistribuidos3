"""REST -> signed RabbitMQ events -> persistent MS.Promocoes subscriptions.

Build using the artifacts path documented in tests/README.md, then run this file.
Only standard Python modules are required; all data and queues are disposable.
"""
import copy
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
from urllib.parse import quote
import uuid
from types import SimpleNamespace

from payment_e2e import ROOT, BUILD, MANAGEMENT, USER, PASSWORD, request, eventually, free_port


def main(email_checks=None, email_env=None):
    for name in ("MS.Principal", "MS.Promocoes"):
        assert (BUILD / name / "debug" / (name + ".dll")).exists(), f"Build {name} first"
    run_dir = ROOT / "artifacts" / "payment-tests" / "runs"
    assert run_dir.resolve().is_relative_to(ROOT)
    run_dir.mkdir(parents=True, exist_ok=True)
    vhost = "interesses-test-" + uuid.uuid4().hex
    host_path = quote(vhost, safe="")
    api = MANAGEMENT + "/api"

    def admin(path, method="GET", data=None):
        code, body = request(api + path, method, data, admin=True)
        assert 200 <= code < 300, (path, code, body)
        return json.loads(body) if body else None

    admin(f"/vhosts/{host_path}", "PUT", {})
    processes, logs = [], []
    try:
        admin(f"/permissions/{host_path}/{quote(USER, safe='')}", "PUT",
              {"configure": ".*", "write": ".*", "read": ".*"})
        with tempfile.TemporaryDirectory(prefix="interesses-", dir=run_dir) as temporary:
            isolated = Path(temporary)
            gateway = f"http://127.0.0.1:{free_port()}"
            env = dict(os.environ)
            env.update({"RabbitMq__Uri": f"amqp://{quote(USER, safe='')}:{quote(PASSWORD, safe='')}@localhost:5672/{host_path}",
                        "SOLUTION_ROOT_PATH": str(isolated), "ASPNETCORE_ENVIRONMENT": "Development",
                        "Email__ApiKey": "", "Email__From": ""})
            env.update(email_env or {})
            store_path = isolated / "MS.Promocoes" / "Dados" / "interesses.json"

            def start(name, interactive=False):
                content = isolated / name
                content.mkdir(exist_ok=True)
                log = open(isolated / f"{name}-{len(logs)}.log", "w", encoding="utf-8")
                logs.append(log)
                command = ["dotnet", str(BUILD / name / "debug" / (name + ".dll"))]
                command += ([] if interactive else ["--consumer-only"]) if name == "MS.Promocoes" else ["--contentRoot", str(content), "--urls", gateway]
                process = subprocess.Popen(command, cwd=content, env=env, stdin=subprocess.PIPE,
                                           stdout=log, stderr=subprocess.STDOUT)
                processes.append(process)
                return process

            def stop(process):
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()

            def ready():
                return any(q["name"] == "fila_interesses_promocoes" and q.get("consumers", 0) == 1
                           for q in admin(f"/queues/{host_path}"))

            def state():
                # Windows may briefly deny reads while the writer atomically replaces the file.
                for attempt in range(100):
                    try:
                        return json.loads(store_path.read_text(encoding="utf-8"))["Interesses"] if store_path.exists() else {}
                    except PermissionError:
                        if attempt == 99:
                            raise
                        time.sleep(0.01)

            def categories(email, expected):
                return eventually(lambda: state().get(email, {}).get("Categorias") == expected,
                                  email + " categories " + str(expected))

            def register(email, cats):
                code, body = request(gateway + "/api/interesse", "POST", {"email": email, "categorias": cats})
                assert code == 202, (code, body)
                return json.loads(body)

            def publish(payload, routing="interesse.promocao"):
                result = admin(f"/exchanges/{host_path}/eCommerce/publish", "POST", {
                    "properties": {"delivery_mode": 2}, "routing_key": routing,
                    "payload": payload if isinstance(payload, str) else json.dumps(payload), "payload_encoding": "string"})
                assert result["routed"]

            try:
                promo = start("MS.Promocoes")
                principal = start("MS.Principal")

                def listening():
                    assert principal.poll() is None and promo.poll() is None, "Service exited"
                    try:
                        return request(gateway + "/ready-probe")[0] == 404
                    except OSError:
                        return False
                eventually(listening, "gateway startup")
                eventually(ready, "promotion consumer startup")
                admin(f"/queues/{host_path}/audit", "PUT", {"durable": True, "auto_delete": False, "arguments": {}})
                admin(f"/bindings/{host_path}/e/eCommerce/q/audit", "POST", {"routing_key": "interesse.promocao", "arguments": {}})

                for invalid in [
                    {"email": "invalido", "categorias": ["A"]},
                    {"email": "user@example.com", "categorias": []},
                    {"email": "user@example.com", "categorias": ["Z"]},
                    {"email": "user@example.com", "categorias": [None]},
                    {"email": "user@example.com", "categorias": None},
                    {"categorias": ["A"]},
                ]:
                    assert request(gateway + "/api/interesse", "POST", invalid)[0] == 400
                assert request(gateway + "/api/interesse/invalido", "DELETE")[0] == 400
                assert not store_path.exists(), "Invalid requests must not modify subscriptions"

                response = register("  CLIENTE@EXAMPLE.COM  ", ["b", " A ", "a"])
                assert response["email"] == "cliente@example.com" and response["categorias"] == ["A", "B"]
                categories("cliente@example.com", ["A", "B"])
                captured = admin(f"/queues/{host_path}/audit/get", "POST",
                                 {"count": 1, "ackmode": "ack_requeue_false", "encoding": "auto", "truncate": 50000})
                original = json.loads(captured[0]["payload"])
                assert original["Producer"] == "MS.Principal" and original["Signature"]
                admin(f"/queues/{host_path}/audit", "DELETE")
                register("cliente@example.com", ["C"])
                categories("cliente@example.com", ["C"])
                assert len(state()) == 1
                register("outro@example.com", ["A"])
                categories("outro@example.com", ["A"])
                print("PASS: validation, signed REST events, normalization and category replacement without duplicates", flush=True)

                for _ in range(2):
                    assert request(gateway + "/api/interesse/" + quote("CLIENTE@example.com", safe=""), "DELETE")[0] == 202
                eventually(lambda: "cliente@example.com" not in state(), "cancellation")
                # Queue new work while offline, then consume it with the same durable file.
                stop(promo)
                register("fila@example.com", ["C"])
                assert "fila@example.com" not in state()
                promo = start("MS.Promocoes")
                eventually(ready, "restarted consumer")
                categories("fila@example.com", ["C"])
                publish(original)
                register("outro@example.com", ["B"])
                categories("outro@example.com", ["B"])
                assert "cliente@example.com" not in state(), "Redelivered old event reactivated cancelled email"
                print("PASS: durable cancellation, queued work while offline, restart and replay protection", flush=True)

                forged = copy.deepcopy(original)
                forged["MessageId"] = uuid.uuid4().hex
                forged["Content"]["Email"] = "forjado@example.com"
                publish(forged)
                publish(original, "interesse.cancelado")
                publish("invalid json")
                register("outro@example.com", ["C"])
                categories("outro@example.com", ["C"])
                assert "forjado@example.com" not in state()
                register("cliente@example.com", ["B"])
                categories("cliente@example.com", ["B"])
                print("PASS: forged signature, wrong routing and malformed JSON rejected; fresh resubscription succeeds", flush=True)

                if email_checks:
                    promo = email_checks(SimpleNamespace(gateway=gateway, admin=admin, host_path=host_path,
                        start=start, stop=stop, promo=promo, state=state, register=register,
                        categories=categories, env=env, isolated=isolated))

                stop(promo)
                admin(f"/queues/{host_path}/fila_interesses_promocoes", "DELETE")
                assert request(gateway + "/api/interesse", "POST", {
                    "email": "falha@example.com", "categorias": ["A"]})[0] == 503
                assert "falha@example.com" not in state()
                print("PASS: unroutable registration returns 503 instead of reporting success", flush=True)
            except Exception:
                for log in logs:
                    log.flush()
                    print(f"\n--- {Path(log.name).name} ---\n{Path(log.name).read_text(encoding='utf-8', errors='replace')[-10000:]}")
                raise
            finally:
                for process in reversed(processes):
                    stop(process)
                    process.stdin.close()
                for log in logs:
                    log.close()
    finally:
        admin(f"/vhosts/{host_path}", "DELETE")


if __name__ == "__main__":
    main()
