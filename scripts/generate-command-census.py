#!/usr/bin/env python3
"""Capture the current rt CLI/MCP discovery contracts without copying help text.

Build the CLI and MCP projects first. The census records advertised contracts, not
runtime proof of effects, completeness, or result quality. This local capture
requires the checkout to have already been approved in rt's trust store; it does
not grant repository trust. Use --check before revising the pre-freeze disposition.
"""

import argparse
import json
import queue
import re
import subprocess
import sys
import threading
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CHANGE = ROOT / "openspec/changes/shape-pre-release-product-spine"
DEFAULT_OUTPUT = CHANGE / "command-census.jsonl"
EXECUTABLE_SUFFIX = ".exe" if sys.platform == "win32" else ""
CLI = ROOT / f"src/RoslynTools.Cli/bin/Debug/net10.0/rt{EXECUTABLE_SUFFIX}"
MCP = ROOT / f"src/RoslynTools.Mcp/bin/Debug/net10.0/RoslynTools.Mcp{EXECUTABLE_SUFFIX}"
ANCILLARY = (
    ("prime", "prime"),
    ("suggest", "suggest"),
    ("dump-help", "dump-help"),
    ("feedback_add", "maintainer feedback add"),
    ("feedback_list", "maintainer feedback list"),
    ("feedback_process", "maintainer feedback process"),
    ("feedback_report", "maintainer feedback report"),
    ("daemon_start", "maintainer daemon start"),
    ("daemon_stop", "maintainer daemon stop"),
    ("daemon_status", "maintainer daemon status"),
    ("daemon_logs", "maintainer daemon logs"),
)


def cli_output(*args):
    return subprocess.check_output([str(CLI), *args], cwd=ROOT, text=True, timeout=60)


def verify_existing_trust():
    status = json.loads(cli_output("trust", "status", str(ROOT), "--json"))
    if status.get("state") != "Trusted" or status.get("worktreeRoot") != str(ROOT):
        raise RuntimeError("This checkout must already have rt trust approval; "
                           "review trust outside the census generator")


def mcp_tools():
    process = subprocess.Popen(
        [str(MCP), "--trust-repository", str(ROOT)],
        cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL, text=True, bufsize=1,
    )
    assert process.stdin and process.stdout
    responses = queue.Queue()
    threading.Thread(target=lambda: [responses.put(line) for line in process.stdout],
                     daemon=True).start()

    def request(number, method, params):
        process.stdin.write(json.dumps({
            "jsonrpc": "2.0", "id": number, "method": method, "params": params,
        }, separators=(",", ":")) + "\n")
        process.stdin.flush()
        try:
            line = responses.get(timeout=30)
        except queue.Empty as exc:
            raise TimeoutError(f"MCP {method} did not respond within 30 seconds") from exc
        reply = json.loads(line)
        if reply.get("id") != number or "error" in reply:
            raise RuntimeError(f"MCP {method} failed: {reply}")
        return reply["result"]

    try:
        initialized = request(1, "initialize", {
            "protocolVersion": "2025-06-18", "capabilities": {},
            "clientInfo": {"name": "rt-command-census", "version": "1"},
        })
        process.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
        process.stdin.flush()
        tools, cursor, page = [], None, 0
        while True:
            result = request(2 + page, "tools/list", {"cursor": cursor} if cursor else {})
            tools.extend(result["tools"])
            cursor = result.get("nextCursor")
            page += 1
            if not cursor:
                break
            if page > 20:
                raise RuntimeError("MCP pagination did not terminate")
        return initialized["protocolVersion"], tools, page
    finally:
        process.terminate()
        try:
            process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=3)


def dispositions():
    content = (CHANGE / "surface-disposition.md").read_text()
    result = {}
    for line in content.splitlines():
        if not (line.startswith("| `") or line.startswith("| daemon lifecycle |")):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) < 3:
            continue
        name = cells[0].strip("`")
        if name in result:
            raise RuntimeError(f"Duplicate disposition: {name}")
        result[name] = {"destination": cells[1]}
        if len(cells) >= 4:
            result[name]["targetMaturity"] = cells[2]
            result[name]["decision"] = cells[3]
        else:
            result[name]["decision"] = cells[2]
    return result


def help_children(*path):
    output = cli_output(*path, "--help")
    if "Commands:\n" not in output:
        raise RuntimeError(f"No child commands listed by rt {' '.join(path)} --help")
    return set(re.findall(r"^  ([a-z][a-z-]*)\s{2,}",
                          output.split("Commands:\n", 1)[1], re.MULTILINE))


def verify_ancillary_inventory():
    if help_children("maintainer") != {"feedback", "daemon"}:
        raise RuntimeError("Maintainer command inventory changed")
    for parent, expected in (("feedback", {"add", "list", "process", "report"}),
                             ("daemon", {"start", "stop", "status", "logs"})):
        if help_children("maintainer", parent) != expected:
            raise RuntimeError(f"Maintainer {parent} command inventory changed")


def cli_inputs(command):
    options = command["options"]
    arguments = command["arguments"]
    return {
        "options": {item["name"]: item["valueType"] for item in options},
        "arguments": {item["name"]: item["valueType"] for item in arguments},
        "requiredOptions": [item["name"] for item in options if item["required"]],
        "requiredArguments": [item["name"] for item in arguments if item["required"]],
        "jsonAdvertised": command["jsonOutput"],
    }


def mcp_contract(tool, annotations, cli_product=None):
    schema = tool["inputSchema"]
    properties = schema.get("properties", {})
    metadata = tool.get("_meta", {})
    annotation_json = json.dumps({key: value for key, value in tool["annotations"].items()
                                  if key != "title"}, sort_keys=True)
    if annotation_json not in annotations:
        annotations[annotation_json] = f"a{len(annotations) + 1:02d}"
    projected = {key.removeprefix("rt/"): value for key, value in metadata.items()
                 if key != "rt/cliExamples"}
    comparable = ("maturity", "effects", "audience", "workspaceRequirement",
                  "defaultTimeoutMilliseconds", "maximumItems", "maximumOutputBytes")
    parity = cli_product is not None and all(
        projected.get(key) == cli_product.get(key) for key in comparable)
    return {
        "name": tool["name"],
        "required": schema.get("required", []),
        "inputs": {name: value.get("type", "unspecified") for name, value in properties.items()},
        "describedInputs": [name for name, value in properties.items() if value.get("description")],
        "enums": {name: value["enum"] for name, value in properties.items() if "enum" in value},
        "outputShape": "object-or-array" if tool.get("outputSchema") == {
            "oneOf": [{"type": "object"}, {"type": "array"}]
        } else "other",
        "annotationProfile": annotations[annotation_json],
        "title": tool["annotations"].get("title"),
        **({"sevenProjectedFieldsMatchCliProfile": True} if parity else {"metadata": projected}),
    }


def ancillary_cli(path):
    output = cli_output(*path.split(), "--help")
    usage = re.search(r"^  (rt .*?)$", output, re.MULTILINE)
    if usage is None:
        return {"usage": None, "required": [], "helpGap": "no usage in --help"}
    required = re.findall(r"^  (--[\w-]+) [^\n]*?\(REQUIRED\)", output, re.MULTILINE)
    return {"usage": usage.group(1), "required": required,
            "jsonAdvertised": "--json" in output}


def capture():
    verify_existing_trust()
    verify_ancillary_inventory()
    catalogue = json.loads(cli_output("dump-help", "--json"))
    protocol, tools, pages = mcp_tools()
    by_tool = {tool["name"]: tool for tool in tools}
    if len(by_tool) != len(tools):
        raise RuntimeError("MCP contains duplicate tool names")
    decision = dispositions()
    profile_lookup, profiles, annotations, operations = {}, {}, {}, []

    for command in catalogue["commands"]:
        name = command["name"]
        if name not in decision:
            raise RuntimeError(f"No destination for {name}")
        profile_json = json.dumps(command["product"], sort_keys=True)
        if profile_json not in profile_lookup:
            profile = f"p{len(profile_lookup) + 1:02d}"
            profile_lookup[profile_json] = profile
            profiles[profile] = command["product"]
        tool_name = name.replace("-", "_")
        tool = by_tool.pop(tool_name, None)
        operations.append({
            "name": name, "category": command["category"],
            "hosts": ["cli"] + (["mcp"] if tool else []),
            "profile": profile_lookup[profile_json],
            "disposition": decision[name],
            "cli": cli_inputs(command),
            **({"mcp": mcp_contract(tool, annotations, command["product"])} if tool else {}),
        })

    for name, path in ANCILLARY:
        tool_name = "suggest_command" if name == "suggest" else name
        tool = by_tool.pop(tool_name, None)
        key = tool_name if name == "suggest" else name
        disposition = decision.get(key)
        if name.startswith("daemon_"):
            disposition = decision.get("daemon lifecycle")
        if disposition is None:
            raise RuntimeError(f"No ancillary destination for {name}")
        operations.append({
            "name": name, "category": "ancillary",
            "hosts": ["cli"] + (["mcp"] if tool else []),
            "disposition": disposition,
            "cli": ancillary_cli(path),
            **({"mcp": mcp_contract(tool, annotations)} if tool else {}),
            "cliDumpHelpProductProfile": None,
        })
    if by_tool:
        raise RuntimeError(f"Unmapped MCP tools: {sorted(by_tool)}")

    mcp_ops = [operation for operation in operations if "mcp" in operation]
    missing_required = {}
    mcp_extra_required = {}
    for operation in mcp_ops:
        if operation["category"] == "ancillary":
            continue
        cli_required = (operation["cli"]["requiredOptions"]
                        + operation["cli"]["requiredArguments"])
        aliases = {"with": "replacement",
                   "operationepochnotafter": "operationepochnotafterutc",
                   "dispatchleaseexpiresat": "dispatchleaseexpiresatutc",
                   "intakeattemptexpiresat": "intakeattemptexpiresatutc"}
        if operation["name"] == "add-dynamic-dependency":
            aliases["type"] = "types"
        normalize = lambda name: name.replace("-", "").replace("_", "").lower()
        mcp_names = {normalize(name) for name in operation["mcp"]["inputs"]}
        mapped_cli_required = {aliases.get(normalize(name), normalize(name))
                               for name in cli_required}
        absent = [name for name in cli_required
                  if aliases.get(normalize(name), normalize(name)) not in mcp_names]
        if absent:
            missing_required[operation["name"]] = absent
        extra = [name for name in operation["mcp"]["required"]
                 if normalize(name) not in mapped_cli_required]
        if extra:
            mcp_extra_required[operation["name"]] = extra

    operations.sort(key=lambda item: item["name"])
    return {
        "schema": "rt.command-census.v1",
        "source": {
            "cli": "source-built rt dump-help --json and ancillary --help",
            "mcp": "source-built MCP initialize + paged tools/list",
            "mcpProtocol": protocol,
            "mcpProfile": "already-approved checkout, explicit --trust-repository host argument",
            "runtimeProof": False,
            "ancillaryScope": "Known root primer/suggestion/help and complete maintainer child help lists; other hidden root commands need source review",
            "mcpCliProfileComparisonFields": ["maturity", "effects", "audience",
                                               "workspaceRequirement", "defaultTimeoutMilliseconds",
                                               "maximumItems", "maximumOutputBytes"],
            "cliGlobalFormats": ["text"] + [option["name"] for option in catalogue["globalOptions"]
                                           if option["name"] in ("json", "llm", "markdown", "yaml")],
        },
        "counts": {
            "cliCatalogue": catalogue["commandCount"],
            "mcpTools": len(tools), "mcpPages": pages,
            "sharedCatalogueTools": sum("mcp" in item["hosts"] for item in operations
                                        if item["category"] != "ancillary"),
            "knownAncillaryOperations": len(ANCILLARY),
            "unmatchedRequiredCliInputNamesInMcp": missing_required,
            "mcpCliRequirednessProjectionDifferences": mcp_extra_required,
            "mcpGenericOutputSchemas": sum(item["mcp"]["outputShape"] == "object-or-array"
                                           for item in mcp_ops),
            "ancillaryAbsentFromCliDumpHelpProductCatalogue": [name for name, _ in ANCILLARY],
        },
        "profiles": profiles,
        "annotationProfiles": {value: json.loads(key) for key, value in annotations.items()},
        "operations": operations,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail if the committed snapshot differs")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    census = capture()
    expected = "\n".join(
        json.dumps({"section": key, "value": census[key]}, ensure_ascii=False,
                   separators=(",", ":"))
        for key in ("schema", "source", "counts", "profiles", "annotationProfiles")
    ) + "\n" + "\n".join(
        json.dumps({"section": "operation", **operation}, ensure_ascii=False,
                   separators=(",", ":")) for operation in census["operations"]
    ) + "\n"
    if args.check:
        if not args.output.exists() or args.output.read_text() != expected:
            print(f"Census drift: regenerate {args.output}", file=sys.stderr)
            return 1
        print(f"Census current: {args.output}")
        return 0
    args.output.write_text(expected)
    print(f"Wrote {args.output} ({len(expected.encode())} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
