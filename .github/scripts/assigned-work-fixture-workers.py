"""Exchange tokens for two distinct scoped worker identities."""

import base64
import hashlib
import hmac
import json
from pathlib import Path
import ssl
import sys
import time
from urllib.parse import urlencode
from urllib.request import Request, urlopen


def segment(value):
    return base64.urlsafe_b64encode(value).rstrip(b"=")


def main(directory, username, token_name):
    base = "https://127.0.0.1:5197"
    now = int(time.time())
    header = segment(b'{"alg":"HS256","typ":"JWT"}')
    claims = segment(json.dumps({
        "iss": "https://fixture.invalid", "aud": "honua-fixture", "sub": username,
        "roles": ["editor"], "nbf": now - 10, "exp": now + 1800,
    }).encode())
    signature = segment(hmac.digest((directory / "signing").read_bytes().strip(),
                                    header + b"." + claims, hashlib.sha256))
    jwt = b".".join((header, claims, signature)).decode()
    context = ssl.create_default_context(cafile=str(directory / "tls.crt"))

    def post(route, data, headers):
        with urlopen(Request(base + route, data, headers), context=context, timeout=20) as response:
            return json.load(response)

    token = post("/sharing/rest/generateToken", urlencode({
        "username": username, "password": jwt, "client": "requestip", "f": "json",
    }).encode(), {"Content-Type": "application/x-www-form-urlencoded"})["token"]
    (directory / token_name).write_text(token)
main(Path(sys.argv[1]), "combined-crew-a", "a.token")
main(Path(sys.argv[1]), "combined-crew-b", "b.token")
