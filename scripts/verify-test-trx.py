#!/usr/bin/env python3
"""Require a fresh, completed, nonempty dotnet test TRX report."""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def one_child(parent: ET.Element, name: str) -> ET.Element:
    matches = [child for child in parent if local_name(child.tag) == name]
    if len(matches) != 1:
        raise ValueError(f"expected one {name} element, found {len(matches)}")
    return matches[0]


def nonnegative_count(counters: ET.Element, name: str) -> int:
    raw = counters.get(name)
    if raw is None or not raw.isdecimal():
        raise ValueError(f"missing or invalid {name} count")
    return int(raw)


def verify(report: Path) -> tuple[int, int]:
    root = ET.parse(report).getroot()
    if local_name(root.tag) != "TestRun":
        raise ValueError("root is not TestRun")
    summary = one_child(root, "ResultSummary")
    if summary.get("outcome") != "Completed":
        raise ValueError(f"test run outcome is {summary.get('outcome')!r}")
    counters = one_child(summary, "Counters")
    total = nonnegative_count(counters, "total")
    executed = nonnegative_count(counters, "executed")
    passed = nonnegative_count(counters, "passed")
    if total == 0 or executed == 0 or passed == 0 or not passed <= executed <= total:
        raise ValueError(f"test run has no valid passing tests (total={total}, executed={executed}, passed={passed})")
    for name in ("failed", "error", "timeout", "aborted", "passedButRunAborted", "disconnected"):
        if nonnegative_count(counters, name) != 0:
            raise ValueError(f"test run has nonzero {name} count")
    return total, passed


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: verify-test-trx.py <report.trx> <project>", file=sys.stderr)
        return 2
    report = Path(sys.argv[1])
    project = sys.argv[2]
    try:
        total, passed = verify(report)
    except (OSError, ET.ParseError, ValueError) as error:
        print(f"check-local: incomplete test report for {project}: {report}: {error}", file=sys.stderr)
        return 1
    print(f"check-local: completed {project}: total={total} passed={passed} report={report}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
