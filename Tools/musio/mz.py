"""Musio Studio の MCP（127.0.0.1:47820/mcp）を呼ぶ小さな道具。
使い方: py -3 mz.py list                      … 道具の一覧
        py -3 mz.py call NAME '{"a": 1}'      … 道具を呼ぶ（引数は JSON。@file.json でファイルから）
トークンは %APPDATA%\\Musio Studio\\agent-token から読む（表示しない）。"""
import json, os, sys, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
SESSION_FILE = os.path.join(HERE, "mz_session.txt")
APPDATA = os.environ["APPDATA"]


def settings_port():
    try:
        text = open(os.path.join(APPDATA, "Musio Studio", "Musio Studio.settings"), encoding="utf-8").read()
        import re
        m = re.search(r'name="agentPort" val="(\d+)"', text)
        if m:
            return int(m.group(1))
    except OSError:
        pass
    return 47820


URL = f"http://127.0.0.1:{settings_port()}/mcp"
TOKEN = open(os.path.join(APPDATA, "Musio Studio", "agent-token"), encoding="utf-8").read().strip()


def post(payload, session=None):
    data = json.dumps(payload).encode("utf-8")
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
        "Authorization": "Bearer " + TOKEN,
    }
    if session:
        headers["Mcp-Session-Id"] = session
    req = urllib.request.Request(URL, data=data, headers=headers, method="POST")
    with urllib.request.urlopen(req, timeout=600) as res:
        sid = res.headers.get("Mcp-Session-Id")
        body = res.read().decode("utf-8")
    if body.startswith("event:") or body.startswith("data:"):
        msgs = [json.loads(line[5:]) for line in body.splitlines() if line.startswith("data:")]
        body_obj = msgs[-1] if msgs else None
    else:
        body_obj = json.loads(body) if body.strip() else None
    return sid, body_obj


def session():
    if os.path.exists(SESSION_FILE):
        return open(SESSION_FILE).read().strip()
    sid, res = post({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
        "protocolVersion": "2025-03-26", "capabilities": {},
        "clientInfo": {"name": "claude-code-saikoro", "version": "1.0"}}})
    post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid)
    open(SESSION_FILE, "w").write(sid or "")
    return sid


def rpc(method, params):
    sid = session()
    try:
        _, res = post({"jsonrpc": "2.0", "id": 2, "method": method, "params": params}, sid)
    except urllib.error.HTTPError as e:
        if e.code in (400, 404) and os.path.exists(SESSION_FILE):
            os.remove(SESSION_FILE)  # 古いセッション。作り直す
            _, res = post({"jsonrpc": "2.0", "id": 2, "method": method, "params": params}, session())
        else:
            raise
    return res


def call(name, args=None):
    """道具を呼んで、文字の結果を返す（JSON なら dict）。"""
    res = rpc("tools/call", {"name": name, "arguments": args or {}})
    if "error" in res:
        raise RuntimeError(json.dumps(res["error"], ensure_ascii=False))
    text = "\n".join(c.get("text", "") for c in res["result"].get("content", []) if c.get("type") == "text")
    if res["result"].get("isError"):
        raise RuntimeError(text)
    try:
        return json.loads(text)
    except ValueError:
        return text


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    if sys.argv[1] == "list":
        res = rpc("tools/list", {})
        for t in res["result"]["tools"]:
            print("##", t["name"])
            print(t.get("description", ""))
            print(json.dumps(t.get("inputSchema", {}), ensure_ascii=False))
            print()
        return
    if sys.argv[1] == "call":
        name = sys.argv[2]
        arg = sys.argv[3] if len(sys.argv) > 3 else "{}"
        if arg.startswith("@"):
            arg = open(arg[1:], encoding="utf-8").read()
        res = rpc("tools/call", {"name": name, "arguments": json.loads(arg)})
        if "error" in res:
            print("ERROR", json.dumps(res["error"], ensure_ascii=False))
            return
        for c in res["result"].get("content", []):
            if c.get("type") == "text":
                print(c["text"])
            else:
                print("[" + c.get("type", "?") + "]")
        if res["result"].get("isError"):
            print("(isError)")


if __name__ == "__main__":
    main()
