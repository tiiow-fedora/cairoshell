import os
import subprocess
import tempfile
import unittest

import generate_changelog as g

S, R = g.SEP, g.REC


def rec(sha, subject, body=""):
    return f"{sha}{S}{subject}{S}{body}{R}\n"


class ClassifyTests(unittest.TestCase):
    def test_conventional(self):
        self.assertEqual(g.classify("feat(menu): add clock"), ("feat", "menu", "add clock"))
        self.assertEqual(g.classify("fix: crash on start"), ("fix", None, "crash on start"))

    def test_aliases_and_unknown_types(self):
        self.assertEqual(g.classify("ci: tweak")[0], "build")
        self.assertEqual(g.classify("wibble: thing")[0], "other")

    def test_breaking_bang_and_footer(self):
        self.assertEqual(g.classify("feat!: drop net480")[0], "breaking")
        self.assertEqual(g.classify("Rename config", "BREAKING CHANGE: key renamed")[0], "breaking")

    def test_plain_imperative(self):
        self.assertEqual(g.classify("Add Nord theme pack")[0], "feat")
        self.assertEqual(g.classify("Fix taskbar flicker")[0], "fix")
        self.assertEqual(g.classify("Tidy things")[0], "other")

    def test_colon_in_plain_subject_is_not_a_type(self):
        # "Note: ..." has a colon but is not a known type; falls to other, text kept.
        t, scope, desc = g.classify("Hook light/dark mode into the service (not compiled)")
        self.assertEqual((scope, desc), (None, "Hook light/dark mode into the service (not compiled)"))


class ParseRenderTests(unittest.TestCase):
    def test_merges_skipped_and_dupes_removed(self):
        raw = rec("a" * 40, "Merge branch 'x'") + rec("b" * 40, "Fix a bug") + rec("c" * 40, "Fix a bug")
        commits = g.parse_log(raw)
        self.assertEqual(len(commits), 2)
        out = g.render(commits, "1.0.0", "2026-01-02")
        self.assertEqual(out.count("Fix a bug"), 1)
        self.assertTrue(out.startswith("## 1.0.0 - 2026-01-02"))
        self.assertIn("### Fixes", out)

    def test_section_order_and_links(self):
        raw = rec("1" * 40, "docs: guide") + rec("2" * 40, "feat(x)!: new") + rec("3" * 40, "feat: other")
        out = g.render(g.parse_log(raw), "2.0.0", "2026-01-02", "https://example/r")
        self.assertLess(out.index("Breaking changes"), out.index("Features"))
        self.assertLess(out.index("Features"), out.index("Documentation"))
        self.assertIn("[1111111](https://example/r/commit/" + "1" * 40 + ")", out)
        self.assertIn("**x:** New", out)

    def test_empty(self):
        self.assertIn("No user-facing changes.", g.render([], "1.0.0", "2026-01-02"))


class FileTests(unittest.TestCase):
    def test_prepend_creates_and_stacks(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "CHANGELOG.md")
            g.prepend_to_file(path, "## 1.0.0 - x\n\n- a\n")
            g.prepend_to_file(path, "## 1.1.0 - y\n\n- b\n")
            text = open(path, encoding="utf-8").read()
            self.assertTrue(text.startswith("# Changelog"))
            self.assertLess(text.index("1.1.0"), text.index("1.0.0"))


class GitIntegrationTests(unittest.TestCase):
    def test_real_repo(self):
        with tempfile.TemporaryDirectory() as d:
            def git(*a):
                subprocess.run(["git", "-C", d, "-c", "user.name=t", "-c", "user.email=t@t", *a],
                               check=True, capture_output=True)
            git("init", "-q")
            for i, msg in enumerate(["Add one", "tag-me"]):
                open(os.path.join(d, f"f{i}"), "w").close()
                git("add", "."); git("commit", "-q", "-m", msg)
            git("tag", "v0.1.0")
            open(os.path.join(d, "g"), "w").close()
            git("add", "."); git("commit", "-q", "-m", "fix: later", "-m", "BREAKING CHANGE: yes")
            cwd = os.getcwd()
            os.chdir(d)
            try:
                self.assertEqual(g.latest_tag(), "v0.1.0")
                commits = g.parse_log(g.git_log("v0.1.0", "HEAD"))
            finally:
                os.chdir(cwd)
            self.assertEqual(len(commits), 1)
            self.assertEqual(g.classify(commits[0][1], commits[0][2])[0], "breaking")


if __name__ == "__main__":
    unittest.main()
