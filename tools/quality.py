"""Quality gate for the code this fork owns: analyzer warnings, complexity and coverage.

"Our files" are the files added since the fork point (git merge-base HEAD upstream/main). Upstream's files are not
gated, so upstream code can be merged as it is. The gate builds once with the recommended .NET analyzers and code
style, runs lizard on our C# files, and runs the tests with coverage. It prints one line per gate and exits 1 if a
gate fails. Coverage is reported, not gated.

    python tools/quality.py            # needs: .NET 10 SDK, lizard on PATH, the git remote "upstream"
"""
import dataclasses
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ElementTree

REPOSITORY = pathlib.Path(__file__).resolve().parent.parent
LIZARD_ARGUMENTS = ["-l", "csharp", "-C", "9", "-L", "60", "-a", "6", "-w"]
ANALYZER_WARNING = re.compile(r"(?P<path>(?:[A-Za-z]:)?[^:(\s][^:(]*\.cs)\((?P<line>\d+),\d+\): warning (?P<code>[A-Z]+\d+): (?P<text>[^\[]*)")


@dataclasses.dataclass(frozen=True)
class AnalyzerFinding:
    """One analyzer warning in one of our files."""
    path: str
    line: int
    code: str
    text: str


@dataclasses.dataclass
class CoverageTally:
    """Covered and total lines and branches."""
    lines: int = 0
    lines_hit: int = 0
    branches: int = 0
    branches_hit: int = 0

    def add(self, element: ElementTree.Element) -> None:
        """Counts one Cobertura <line> element."""
        self.lines += 1
        hits = int(element.get("hits", "0"))
        if hits > 0:
            self.lines_hit += 1
        condition = element.get("condition-coverage")
        if element.get("branch") == "True" and condition is not None:
            fraction = condition.split("(")[1].rstrip(")")
            covered_text, total_text = fraction.split("/")
            self.branches += int(total_text)
            self.branches_hit += int(covered_text)

    def text(self) -> str:
        """Line and branch percentages with their counts."""
        line_percent = 100.0 * self.lines_hit / self.lines if self.lines > 0 else 0.0
        branch_percent = 100.0 * self.branches_hit / self.branches if self.branches > 0 else 0.0
        return (f"line {line_percent:.1f}% ({self.lines_hit}/{self.lines}), "
                f"branch {branch_percent:.1f}% ({self.branches_hit}/{self.branches})")


@dataclasses.dataclass
class GateResult:
    """The outcome of one gate."""
    name: str
    passed: bool
    summary: str
    details: list[str]


class QualityGate:
    """Runs the gates on the files this fork added since the fork point."""

    def __init__(self, repository: pathlib.Path) -> None:
        """@param repository The repository root."""
        self.repository = repository
        self.our_files = self.find_our_files()

    def run_command(self, arguments: list[str]) -> subprocess.CompletedProcess:
        """Runs a command in the repository and returns its result with text output."""
        return subprocess.run(arguments, cwd=self.repository, capture_output=True, text=True, encoding="utf-8",
                              errors="replace", check=False)

    def find_our_files(self) -> list[str]:
        """Returns the C# files added since the fork point, as repository-relative paths with forward slashes."""
        merge_base = self.run_command(["git", "merge-base", "HEAD", "upstream/main"])
        if merge_base.returncode != 0:
            raise RuntimeError("quality: cannot find the fork point (git merge-base HEAD upstream/main): "
                               + merge_base.stderr.strip())
        fork_point = merge_base.stdout.strip()
        added = self.run_command(["git", "diff", "--name-only", "--diff-filter=A", fork_point, "HEAD", "--", "*.cs"])
        if added.returncode != 0:
            raise RuntimeError("quality: git diff failed: " + added.stderr.strip())
        paths = [line.strip() for line in added.stdout.splitlines() if line.strip()]
        return sorted(paths)

    def relative_path(self, path_text: str) -> str:
        """Turns a compiler path into a repository-relative path with forward slashes."""
        normalized = path_text.replace("\\", "/")
        root = str(self.repository).replace("\\", "/") + "/"
        lowered_root = root.lower()
        if normalized.lower().startswith(lowered_root):
            return normalized[len(root):]
        return normalized

    def analyzers(self) -> GateResult:
        """Builds with the recommended analyzers and code style; fails on warnings in our files."""
        build = self.run_command([
            "dotnet", "build", "FlaxMcp.slnx", "-c", "Release", "--no-incremental", "--nologo",
            "-p:TreatWarningsAsErrors=false", "-p:AnalysisLevel=latest-recommended",
            "-p:EnforceCodeStyleInBuild=true"])
        if build.returncode != 0:
            return GateResult("Analyzers", False, "build failed", build.stdout.splitlines()[-20:])
        our_set = set(self.our_files)
        findings: set[AnalyzerFinding] = set()
        for text in build.stdout.splitlines():
            match = ANALYZER_WARNING.search(text)
            if match is None:
                continue
            path = self.relative_path(match["path"])
            if path in our_set:
                findings.add(AnalyzerFinding(path, int(match["line"]), match["code"], match["text"].strip()))
        ordered = sorted(findings, key=lambda finding: (finding.path, finding.line))
        details = [f"{f.path}:{f.line}: {f.code} {f.text}" for f in ordered]
        return GateResult("Analyzers", len(ordered) == 0, f"{len(ordered)} findings in our files", details)

    def complexity(self) -> GateResult:
        """Runs lizard on our C# files."""
        lizard = shutil.which("lizard")
        if lizard is None:
            return GateResult("Complexity", False, "NOT VERIFIED (lizard is not on PATH)", [])
        result = self.run_command([lizard, *LIZARD_ARGUMENTS, *self.our_files])
        warnings = [line for line in result.stdout.splitlines() if ": warning:" in line]
        return GateResult("Complexity", len(warnings) == 0, f"{len(warnings)} functions over CCN 9 / 60 lines / 6 parameters",
                          warnings)

    def coverage(self) -> GateResult:
        """Runs the tests with coverage and reports line and branch coverage of our src files."""
        with tempfile.TemporaryDirectory() as results_folder:
            test = self.run_command([
                "dotnet", "test", "tests/FlaxMcp.Tests", "-c", "Release", "--no-build",
                "--collect", "Code Coverage;Format=cobertura", "--results-directory", results_folder])
            if test.returncode != 0:
                return GateResult("Tests", False, "tests failed", test.stdout.splitlines()[-20:])
            reports = list(pathlib.Path(results_folder).rglob("*.cobertura.xml"))
            if len(reports) != 1:
                return GateResult("Coverage", False, f"NOT VERIFIED ({len(reports)} coverage reports found)", [])
            root = ElementTree.parse(reports[0]).getroot()
        tally = CoverageTally()
        source_files = [path for path in self.our_files if path.startswith("src/")]
        for class_element in root.iter("class"):
            filename = class_element.get("filename", "").replace("\\", "/")
            is_ours = any(filename.endswith(path) for path in source_files)
            if is_ours:
                for line in class_element.iter("line"):
                    tally.add(line)
        return GateResult("Coverage", True, "our src files: " + tally.text(), [])

    def run(self) -> int:
        """Runs every gate, prints the results, and returns the exit code."""
        print(f"quality: {len(self.our_files)} C# files added since the fork point")
        results = [self.analyzers(), self.complexity(), self.coverage()]
        failed = False
        for result in results:
            status = "PASS" if result.passed else "FAIL"
            print(f"{result.name + ':':12} {status}  {result.summary}")
            for detail in result.details[:40]:
                print("    " + detail)
            if not result.passed:
                failed = True
        return 1 if failed else 0


def main() -> None:
    """Entry point."""
    gate = QualityGate(REPOSITORY)
    sys.exit(gate.run())


main()
