"""Validate portable repository instructions using only Python's standard library.

Run from any directory; --root selects a temporary Git fixture or another checkout.
Checks static Markdown links (inline/reference), GitHub-style heading anchors, folder
coverage, instruction ancestry and skill registration. Network links are not fetched.
"""
import argparse
from collections import Counter
import html
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import unicodedata
from urllib.parse import unquote, urlsplit

PACKAGE = PurePosixPath("Assets/CullingSystemDOTS")
STANDALONE = {PurePosixPath("."), PACKAGE, PACKAGE / "Samples~/Basic"}
CODE_SUFFIXES = {".cs", ".py", ".asmdef", ".asmref", ".compute", ".hlsl", ".shader",
                 ".ps1", ".sh", ".yml", ".yaml", ".js", ".ts", ".tsx", ".jsx"}
EXCLUDED_PARTS = {".git", "library", "temp", "obj", "logs", "usersettings", "build",
                  "builds", "node_modules", "__pycache__", ".venv", "venv", ".vs",
                  ".idea", ".gradle", ".superpowers", "migration-work", "packagecache"}
CLIENT_PARTS = {".claude", ".gemini", ".codex", ".cursor", ".windsurf"}
CLIENT_FILES = {"claude.md", "gemini.md", ".cursorrules", ".windsurfrules",
                ".aider.conf.yml", "copilot-instructions.md"}
CHAIN_LIMIT = 32768


def maintained(path):
    lower = tuple(part.lower() for part in path.parts)
    return (not any(part in EXCLUDED_PARTS for part in lower)
            and path.as_posix() != ".agents/local-context.md"
            and lower[:2] != ("assets", "packages")
            and lower[:2] != ("assets", "scenedependencycache")
            and not (len(lower) > 3 and lower[:3] == ("assets", "plugins", "editor")
                     and lower[3].startswith("jetbrains")))


def strip_examples(text, inline=True):
    """Keep line offsets intact so every diagnostic still locates the source."""
    result = []
    fence = None
    for line in text.splitlines(keepends=True):
        marker = re.match(r"^ {0,3}(`{3,}|~{3,})", line)
        if fence:
            if marker and marker[1][0] == fence[0] and len(marker[1]) >= len(fence):
                fence = None
            result.append("\n" if line.endswith("\n") else "")
        elif marker:
            fence = marker[1]
            result.append("\n" if line.endswith("\n") else "")
        else:
            result.append(re.sub(r"(`+).*?\1", "", line) if inline else line)
    return re.sub(r"<!--.*?-->", lambda match: "\n" * match[0].count("\n"),
                  "".join(result), flags=re.S)


def links(text):
    """Yield (destination, line), resolving full/collapsed reference links."""
    definitions = {}
    for match in re.finditer(r"(?m)^ {0,3}\[([^\]\n]+)\]:\s*(<[^>\n]*>|\S+)", text):
        definitions[match[1].casefold()] = match[2].strip("<>")
        yield match[2].strip("<>"), text.count("\n", 0, match.start()) + 1
    pattern = r"!?\[[^\]\n]*\]\(\s*(<[^>\n]*>|[^\s)]+)(?:\s+[^)\n]*)?\)"
    for match in re.finditer(pattern, text):
        yield match[1].strip("<>"), text.count("\n", 0, match.start()) + 1
    for match in re.finditer(r"!?\[([^\]\n]+)\]\[([^\]\n]*)\]", text):
        destination = definitions.get((match[2] or match[1]).casefold())
        if destination:
            yield destination, text.count("\n", 0, match.start()) + 1


def anchors(text):
    # GitHub heading slugs preserve letters/numbers/underscores/hyphens and spaces.
    found = set(re.findall(r'<(?:a|h[1-6])\b[^>]*(?:id|name)=["\']([^"\']+)', text))
    counts = Counter()
    lines = text.splitlines()
    for index, line in enumerate(lines):
        heading = re.match(r"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$", line)
        setext = index + 1 < len(lines) and re.match(r"^ {0,3}(?:=+|-+)\s*$", lines[index + 1])
        if not heading and not (line.strip() and setext):
            continue
        title = heading[1] if heading else line.strip()
        title = re.sub(r"!?\[([^]]*)\]\([^)]*\)", r"\1", title)
        title = re.sub(r"<[^>]*>", "", html.unescape(title)).lower()
        slug = "".join(char for char in title if char.isalnum() or char in "_- ")
        slug = slug.replace(" ", "-")
        number = counts[slug]
        counts[slug] += 1
        found.add(slug if number == 0 else f"{slug}-{number}")
    return found


def relative_target(source, destination):
    decoded = unquote(destination)
    # urlsplit treats a drive letter as a URI scheme and //host as a netloc.
    # Reject these local-machine paths before the external-URI fast path.
    if re.match(r"^[A-Za-z]:", decoded) or decoded.startswith(("\\\\", "//")):
        raise ValueError("Windows drive/UNC paths are not portable; use a repository-relative path")
    parsed = urlsplit(destination)
    if parsed.scheme or parsed.netloc:
        return None
    raw = unquote(parsed.path)
    # Backslashes and root-relative paths are not portable Markdown references.
    if "\\" in raw or raw.startswith("/"):
        raise ValueError("use a relative path with forward slashes")
    parts = list(source.parent.parts) if raw else list(source.parts)
    for part in PurePosixPath(raw).parts:
        if part == "..":
            if not parts:
                raise ValueError("relative link escapes the repository")
            parts.pop()
        elif part != ".":
            parts.append(part)
    return PurePosixPath(*parts), unquote(parsed.fragment)


def yaml_string(value):
    """Read the supported single-line YAML string subset, or return None.

    Support plain strings, single quotes with doubled apostrophes and JSON-style
    double quotes. Whitespace before # starts a comment only outside quotes;
    plain colon followed by whitespace/end is invalid. Ambiguous YAML 1.1/1.2
    numeric, boolean and null scalars must be quoted to express string metadata.
    Other YAML escapes, tags, anchors and flow collections are not supported.
    """
    if value.startswith("'"):
        match = re.fullmatch(r"'((?:[^']|'')*)'(?:[ \t]+#.*)?", value)
        return match[1].replace("''", "'") if match else None
    if value.startswith('"'):
        match = re.fullmatch(r'("(?:[^"\\]|\\.)*")(?:[ \t]+#.*)?', value)
        if match:
            try:
                return json.loads(match[1])
            except ValueError:
                pass
        return None
    value = re.split(r"(?:^|[ \t]+)#", value, maxsplit=1)[0].rstrip()
    if (not value or value[0] in "[]{},&*!|>%@`" or
            re.match(r"^[?:-](?:\s|$)", value) or re.search(r":(?:\s|$)", value)):
        return None
    if value.lower() in {"null", "~", "false", "true", "yes", "no", "on", "off", "y", "n"}:
        return None
    number = (r"[+-]?(?:0x[0-9a-f_]+|0o[0-7_]+|0b[01_]+|"
              r"(?:[0-9][0-9_]*(?:\.[0-9_]*)?|\.[0-9][0-9_]*)"
              r"(?:e[+-]?[0-9_]+)?|\.(?:inf|nan)|"
              r"[0-9][0-9_]*(?::[0-5]?[0-9])+(?:\.[0-9_]*)?)")
    if re.fullmatch(number, value, re.I):
        return None
    return value


def skill_fields(text):
    """Read the required YAML string scalars, including indented block descriptions.

    This is deliberately not a general YAML loader; repository skills do not need
    objects, tags or executable/custom constructors in these two metadata fields.
    See yaml_string for the restricted inline grammar. Block scalars support |/>
    with optional +/- chomping and a header comment, without indentation indicators.
    """
    frontmatter = re.match(r"\A---\r?\n(.*?)\r?\n---(?:\r?\n|$)", text, re.S)
    if not frontmatter:
        return {}
    lines = frontmatter[1].splitlines()
    fields = {}
    for index, line in enumerate(lines):
        match = re.match(r"^(name|description):[ \t]*(.*)$", line)
        if not match:
            continue
        if match[1] in fields:
            return {}
        value = match[2].strip()
        if re.fullmatch(r"[|>][+-]?(?:[ \t]+#.*)?", value):
            body = []
            for following in lines[index + 1:]:
                if following.strip() and not following.startswith((" ", "\t")):
                    break
                body.append(following.strip())
            value = " ".join(body).strip()
        else:
            value = yaml_string(value)
            if value is None:
                return {}
        fields[match[1]] = value
    return fields


class Validator:
    def __init__(self, root):
        self.root = root
        self.errors = []
        self.markdown = {}
        self.targets = {}
        self.maximum_chain = 0

    def error(self, path, rule, message, line=1):
        self.errors.append(f"{path}:{line}: {rule}: {message}")

    def collect(self):
        command = ["git", "-C", str(self.root), "ls-files", "--cached", "--others",
                   "--exclude-standard", "-z"]
        result = subprocess.run(command, check=True, capture_output=True)
        paths = {PurePosixPath(name.decode("utf-8")) for name in result.stdout.split(b"\0") if name}
        self.files = {path for path in paths if maintained(path) and (self.root / path).is_file()}
        for path in sorted(self.files):
            if path.suffix.lower() == ".md":
                try:
                    self.markdown[path] = (self.root / path).read_text(encoding="utf-8-sig")
                except (UnicodeError, OSError) as error:
                    self.error(path, "markdown-read", str(error))

    def check_coverage(self):
        if PurePosixPath("AGENTS.md") not in self.files:
            self.error("AGENTS.md", "coverage", "repository guide is missing")
        code_dirs = {path.parent for path in self.files if path.suffix.lower() in CODE_SUFFIXES}
        for directory in sorted(code_dirs):
            if directory / "AGENTS.md" not in self.files:
                self.error(directory / "AGENTS.md", "coverage", "maintained code directory needs local guidance")
        for path in sorted(self.files):
            if (path.name.lower() in CLIENT_FILES or
                    any(part.lower() in CLIENT_PARTS for part in path.parts) or
                    path.as_posix().lower() == ".vscode/mcp.json" or
                    path.as_posix().lower().startswith(".github/instructions/")):
                self.error(path, "client-adapter", "use AGENTS.md and portable .agents skills")

    def exact_path(self, path):
        current = self.root
        for part in path.parts:
            try:
                names = {item.name for item in current.iterdir()}
            except OSError:
                return "link-target"
            if part not in names:
                return "link-case" if any(name.casefold() == part.casefold() for name in names) else "link-target"
            current /= part
        return None if current.exists() else "link-target"

    def check_links(self):
        for path, text in sorted(self.markdown.items()):
            cleaned = strip_examples(text)
            self.targets[path] = []
            for destination, line in links(cleaned):
                try:
                    target = relative_target(path, destination)
                except ValueError as error:
                    self.error(path, "link-target", f"{destination}: {error}", line)
                    continue
                if target is None:
                    continue
                resolved, fragment = target
                self.targets[path].append((resolved, line))
                failure = self.exact_path(resolved)
                if failure:
                    self.error(path, failure, f"{destination} -> {resolved}; check target existence and exact casing", line)
                elif fragment and resolved.suffix.lower() == ".md":
                    target_text = self.markdown.get(resolved)
                    if target_text is None:
                        target_text = (self.root / resolved).read_text(encoding="utf-8-sig")
                    if fragment not in anchors(strip_examples(target_text, inline=False)):
                        self.error(path, "link-anchor", f"{destination}: heading/HTML anchor does not exist", line)
                if (path.is_relative_to(PACKAGE) and not resolved.is_relative_to(PACKAGE)):
                    # The paragraph must explicitly classify an escaped link as optional.
                    lines = cleaned.splitlines()
                    start, end = line - 1, line
                    while start > 0 and lines[start - 1].strip():
                        start -= 1
                    while end < len(lines) and lines[end].strip():
                        end += 1
                    paragraph = "\n".join(lines[start:end])
                    if not re.search(r"\boptional\b", paragraph, re.I):
                        self.error(path, "package-link", f"mandatory reference leaves installable package: {destination}", line)

    def check_parents(self):
        guides = {path for path in self.markdown if path.name == "AGENTS.md"}
        edges = {}
        for guide in sorted(guides):
            if guide.parent in STANDALONE:
                continue
            ancestors = [parent / "AGENTS.md" for parent in guide.parent.parents]
            nearest = next((parent for parent in ancestors if parent in guides), None)
            # Only real early links count; a later navigation table is not inheritance.
            early = [target for target, line in self.targets.get(guide, [])
                     if line <= 12 and target in guides]
            if nearest is None or nearest not in early:
                self.error(guide, "parent-link", f"link to nearest parent {nearest or 'AGENTS.md'} in the opening 12 lines")
            # Retain a wrong first link to diagnose accidental cycles as well as ancestry.
            edges[guide] = nearest if nearest in early else (early[0] if early else None)
        for guide in sorted(guides):
            chain = []
            current = guide
            while current is not None:
                if current in chain:
                    self.error(guide, "parent-cycle", " -> ".join(str(path) for path in chain + [current]))
                    break
                chain.append(current)
                current = edges.get(current)
            # Filesystem inheritance still applies in the source checkout, even when
            # an independently installed package/sample has its own navigation root.
            inherited = [guide] + [parent / "AGENTS.md" for parent in guide.parent.parents
                                    if parent / "AGENTS.md" in guides]
            size = sum(len((self.root / path).read_bytes()) for path in inherited)
            self.maximum_chain = max(self.maximum_chain, size)
            if size > CHAIN_LIMIT:
                self.error(guide, "chain-budget", f"inherited instructions total {size} bytes; limit is {CHAIN_LIMIT}")

    def check_skills_and_language(self):
        catalog = PurePosixPath(".agents/README.md")
        registrations = {target for target, _ in self.targets.get(catalog, [])}
        for path, text in sorted(self.markdown.items()):
            skill = path.name == "SKILL.md" and path.is_relative_to(PurePosixPath(".agents/skills"))
            if skill:
                fields = skill_fields(text)
                if (fields.get("name", "") != path.parent.name or
                        not fields.get("description", "").strip()):
                    self.error(path, "skill-frontmatter", "require name matching folder and a nonempty string description; "
                               "quote YAML punctuation and numeric/bool/null text")
                if path not in registrations:
                    self.error(path, "skill-catalog", "register the skill with a relative link in .agents/README.md")
            agent_prose = (path.name == "AGENTS.md" or skill or path == catalog or
                           path.as_posix() in {"Docs/AGENT-WORKFLOW.md", "Docs/TESTING.md"} or
                           path.is_relative_to(PurePosixPath("Docs/AgentRules")))
            if agent_prose:
                for number, line in enumerate(strip_examples(text).splitlines(), 1):
                    if any(char.isalpha() and "LATIN" not in unicodedata.name(char, "") for char in line):
                        self.error(path, "agent-language", "maintained agent prose must use English (non-Latin letters found)", number)

    def run(self):
        self.collect()
        self.check_coverage()
        self.check_links()
        self.check_parents()
        self.check_skills_and_language()
        return self.errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1],
                        help="Git checkout root (default: this script's repository)")
    args = parser.parse_args()
    validator = Validator(args.root.resolve())
    try:
        errors = validator.run()
    except (OSError, UnicodeError, subprocess.CalledProcessError) as error:
        print(f"agent-docs: cannot inspect checkout: {error}", file=sys.stderr)
        return 2
    for error in errors:
        print(error)
    if errors:
        print(f"Agent documentation validation failed: {len(errors)} error(s).")
        return 1
    print(f"Agent documentation valid: {len(validator.markdown)} Markdown files; "
          f"largest instruction chain {validator.maximum_chain}/{CHAIN_LIMIT} bytes.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
