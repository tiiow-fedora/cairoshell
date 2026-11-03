#!/usr/bin/env python3
"""Generate release notes / CHANGELOG sections from git history.

Commits are grouped by Conventional Commit type (feat, fix, ...). Commits that do not
follow the convention are sorted into a section by their first word ("Add", "Fix", ...),
because this repository's history mostly uses plain imperative subjects.

Usage:
    generate_changelog.py --from v0.4.0 --to HEAD --version 0.5.0 [--output notes.md]
    generate_changelog.py --update-file CHANGELOG.md --from v0.4.0 --version 0.5.0
"""
import argparse
import datetime
import re
import subprocess
import sys

SECTIONS = [
    ("breaking", "Breaking changes"),
    ("feat", "Features"),
    ("fix", "Fixes"),
    ("perf", "Performance"),
    ("docs", "Documentation"),
    ("build", "Build and packaging"),
    ("test", "Tests"),
    ("other", "Other changes"),
]

TYPE_ALIASES = {
    "feature": "feat", "bugfix": "fix", "doc": "docs", "ci": "build", "chore": "other",
    "refactor": "other", "style": "other", "revert": "other",
}

VERB_TYPES = {
    "add": "feat", "adds": "feat", "added": "feat", "implement": "feat", "support": "feat",
    "fix": "fix", "fixes": "fix", "fixed": "fix", "correct": "fix", "resolve": "fix",
    "document": "docs", "docs": "docs",
    "test": "test", "tests": "test",
    "bump": "build", "update": "other", "upgrade": "build", "package": "build",
}

CONVENTIONAL = re.compile(r"^(?P<type>[A-Za-z]+)(?:\((?P<scope>[^)]*)\))?(?P<bang>!)?:\s*(?P<desc>.+)$")
SKIP = re.compile(r"^(Merge (branch|pull request|remote-tracking)|Revert \"Merge)", re.I)
SEP = "\x1f"
REC = "\x1e"


def classify(subject, body=""):
    """Return (section_key, scope, description) for a commit subject."""
    breaking = "BREAKING CHANGE" in body or "BREAKING-CHANGE" in body
    m = CONVENTIONAL.match(subject)
    if m:
        t = m.group("type").lower()
        t = TYPE_ALIASES.get(t, t)
        if t not in dict(SECTIONS):
            t = "other"
        if m.group("bang") or breaking:
            t = "breaking"
        return t, m.group("scope"), m.group("desc").strip()
    first = subject.split(" ", 1)[0].lower().rstrip(":")
    t = VERB_TYPES.get(first, "other")
    return ("breaking" if breaking else t), None, subject.strip()


def parse_log(raw):
    commits = []
    for rec in raw.split(REC):
        rec = rec.strip("\n")
        if not rec:
            continue
        parts = rec.split(SEP)
        if len(parts) < 3:
            continue
        sha, subject, body = parts[0].strip(), parts[1].strip(), parts[2]
        if not subject or SKIP.match(subject):
            continue
        commits.append((sha, subject, body))
    return commits


def render(commits, version, date=None, repo_url=None):
    date = date or datetime.date.today().isoformat()
    groups = {key: [] for key, _ in SECTIONS}
    seen = set()
    for sha, subject, body in commits:
        key, scope, desc = classify(subject, body)
        if (key, desc) in seen:
            continue
        seen.add((key, desc))
        desc = desc[0].upper() + desc[1:] if desc else desc
        line = "- " + (f"**{scope}:** " if scope else "") + desc
        if repo_url:
            line += f" ([{sha[:7]}]({repo_url}/commit/{sha}))"
        else:
            line += f" ({sha[:7]})"
        groups[key].append(line)
    out = [f"## {version} - {date}", ""]
    if not any(groups.values()):
        out += ["No user-facing changes.", ""]
    for key, title in SECTIONS:
        if groups[key]:
            out += [f"### {title}", ""] + groups[key] + [""]
    return "\n".join(out).rstrip() + "\n"


def git_log(rev_from, rev_to):
    rng = f"{rev_from}..{rev_to}" if rev_from else rev_to
    fmt = f"%H{SEP}%s{SEP}%b{REC}"
    return subprocess.run(
        ["git", "log", "--no-merges", f"--format={fmt}", rng],
        check=True, capture_output=True, text=True,
    ).stdout


def latest_tag():
    r = subprocess.run(["git", "describe", "--tags", "--abbrev=0"], capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else None


def prepend_to_file(path, section):
    header = "# Changelog\n\n"
    try:
        with open(path, encoding="utf-8") as f:
            existing = f.read()
    except FileNotFoundError:
        existing = header
    if existing.startswith("# Changelog"):
        head, _, rest = existing.partition("\n")
        existing = head + "\n\n" + section + "\n" + rest.lstrip("\n")
    else:
        existing = header + section + "\n" + existing
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(existing)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--from", dest="rev_from", help="start revision (default: latest tag, else whole history)")
    p.add_argument("--to", dest="rev_to", default="HEAD")
    p.add_argument("--version", required=True)
    p.add_argument("--date")
    p.add_argument("--repo-url", help="e.g. https://github.com/owner/repo, used to link commits")
    p.add_argument("--output", help="write section to this file instead of stdout")
    p.add_argument("--update-file", help="prepend the section to this CHANGELOG file")
    a = p.parse_args(argv)
    rev_from = a.rev_from if a.rev_from is not None else latest_tag()
    section = render(parse_log(git_log(rev_from, a.rev_to)), a.version, a.date, a.repo_url)
    if a.update_file:
        prepend_to_file(a.update_file, section)
    if a.output:
        with open(a.output, "w", encoding="utf-8", newline="\n") as f:
            f.write(section)
    if not a.output and not a.update_file:
        sys.stdout.write(section)
    return 0


if __name__ == "__main__":
    sys.exit(main())
