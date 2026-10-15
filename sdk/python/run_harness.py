#!/usr/bin/env python3
"""Hugo Python SDK test harness.

Runs the pytest suite in tests/ against a live Hugo server and prints a summary. The harness provisions its own tenant
with the system administrator and deletes it afterwards, so it can run against any server.

Usage:
    python run_harness.py [server] [extra pytest arguments]

Environment (see tests/conftest.py): HUGO_SDK_SERVER (default http://127.0.0.1:8800), HUGO_SDK_ADMIN_EMAIL,
HUGO_SDK_ADMIN_PASSWORD, HUGO_SDK_ADMIN_TENANT.

Exit code 0 when every test passed or was skipped, 1 otherwise.
"""

import os
import sys
import time
from typing import List, Tuple

import pytest

HERE = os.path.dirname(os.path.abspath(__file__))


class _Collector:
    def __init__(self) -> None:
        self.results: List[Tuple[str, str, float, str]] = []

    def pytest_runtest_logreport(self, report):  # noqa: D401 - pytest hook
        if report.when == "call" or (report.when == "setup" and report.outcome != "passed"):
            detail = ""
            if report.skipped and isinstance(report.longrepr, tuple):
                detail = str(report.longrepr[2])
            elif report.failed:
                detail = str(report.longreprtext).strip().splitlines()[-1] if report.longreprtext else ""
            self.results.append((report.nodeid, report.outcome, report.duration, detail))


def main(argv: List[str]) -> int:
    args = list(argv)
    if args and not args[0].startswith("-"):
        os.environ["HUGO_SDK_SERVER"] = args.pop(0)
    server = os.environ.get("HUGO_SDK_SERVER", "http://127.0.0.1:8800")
    print(f"Hugo Python SDK harness against {server}")
    print("-" * 79)
    collector = _Collector()
    started = time.monotonic()
    code = pytest.main(["-q", "-p", "no:cacheprovider", os.path.join(HERE, "tests")] + args, plugins=[collector])
    elapsed = time.monotonic() - started

    print()
    print("Summary")
    print("-" * 79)
    counts = {"passed": 0, "failed": 0, "skipped": 0}
    for nodeid, outcome, duration, detail in collector.results:
        counts[outcome] = counts.get(outcome, 0) + 1
        label = {"passed": "PASS", "failed": "FAIL", "skipped": "SKIP"}.get(outcome, outcome.upper())
        line = f"{label:5} {nodeid} ({int(duration * 1000)}ms)"
        if detail and outcome != "passed":
            line += f"  {detail}"
        print(line)
    print("-" * 79)
    print(f"{counts['passed']} passed, {counts['skipped']} skipped, {counts['failed']} failed in {elapsed:.1f}s")
    ok = code in (0, 5) and counts["failed"] == 0
    print("Harness succeeded" if ok else "Harness failed")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
