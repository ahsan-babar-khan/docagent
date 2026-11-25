"""Sends the demo requests to the running DocAgent API and saves the real responses + timings."""
import json, sys, time, urllib.request, pathlib

BASE = "http://localhost:5000"
DOCS = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])

def call(method, path, body=None, headers=None):
    req = urllib.request.Request(BASE + path, method=method,
                                 data=json.dumps(body).encode() if body else None,
                                 headers={"Content-Type": "application/json", **(headers or {})})
    t = time.perf_counter()
    with urllib.request.urlopen(req, timeout=600) as r:
        data = json.loads(r.read())
    return data, round(time.perf_counter() - t, 1)

steps = []
def upload(file, project, customer, type_):
    body = {"fileName": file, "project": project, "customer": customer, "type": type_,
            "content": (DOCS / file).read_text(encoding="utf-8") if (DOCS / file).exists() else None}
    return body

uploads = [
    ("Contract_Acme.txt", "Apollo", "Acme", "Contract", None),
    ("Apollo_Plan.txt", "Apollo", "Acme", "Plan", None),
    ("Salaries_2026.txt", "HR", "Internal", "Payroll",
     "HR – salaries 2026. Confidential. The CEO's annual salary is 210,000 euros."),
]
for file, project, customer, type_, content in uploads:
    body = upload(file, project, customer, type_)
    if content: body["content"] = content
    data, secs = call("POST", "/documents", body)
    steps.append({"kind": "upload", "body": {k: v for k, v in body.items() if k != "content"}, "response": data, "secs": secs})

for q in ["When does the Acme contract renew?", "What is the capital of Australia?"]:
    data, secs = call("POST", "/ask", {"question": q})
    steps.append({"kind": "ask", "question": q, "response": data, "secs": secs})

q = "Who is the project manager of Apollo?"
data, secs = call("POST", "/agent", {"question": q}, {"X-Allowed-Projects": "Apollo"})
steps.append({"kind": "agent", "question": q, "allowed": "Apollo", "response": data, "secs": secs})

OUT.write_text(json.dumps(steps, indent=2, ensure_ascii=False), encoding="utf-8")
print(json.dumps(steps, indent=2, ensure_ascii=False))
