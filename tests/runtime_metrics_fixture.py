"""Loopback-only fake metrics server for Windows CLI integration checks."""
import argparse
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path


class Metrics(BaseHTTPRequestHandler):
    reads = 0

    def do_GET(self):
        if self.path != "/metrics":
            self.send_error(404)
            return
        Metrics.reads += 1
        count = Metrics.reads * 10
        body = (
            f"llamacpp:tokens_predicted_total {count}\n"
            f"llamacpp:prompt_tokens_total {count * 2}\n"
            "llamacpp:requests_processing 1\nllamacpp:requests_deferred 2\n"
            f'vllm:generation_tokens_total{{engine="0"}} {count}\n'
            f'vllm:prompt_tokens_total{{engine="0"}} {count * 2}\n'
            'vllm:num_requests_running{engine="0"} 1\n'
            'vllm:num_requests_waiting{engine="0"} 2\n'
        ).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; version=0.0.4")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_args):
        pass


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port-file", type=Path, required=True)
    args = parser.parse_args()
    with HTTPServer(("127.0.0.1", 0), Metrics) as server:
        temporary = args.port_file.with_suffix(".tmp")
        temporary.write_text(str(server.server_port), encoding="ascii")
        temporary.replace(args.port_file)
        server.serve_forever()
