#!/usr/bin/env python3
"""
ASTRA UAV - C# structural validator
====================================

WHY THIS EXISTS
---------------
The development environment for this project has no C# compiler and no Unity
Editor available, so the normal "does it build?" feedback loop is not present.
A full C# parser is out of scope, but a surprisingly large fraction of the
errors that actually stop a Unity project from compiling are *structural* and
can be caught by a lexer that understands C# comments, strings and character
literals well enough to ignore delimiters that live inside them.

WHAT IT CHECKS (and what it deliberately does NOT)
--------------------------------------------------
It CHECKS, per file, after classifying comments / strings / char-literals:
  * balanced  {}  ()  []   (with the line/column of the first offender)
  * every file that opens a namespace closes it
  * `using` directives appear before the first type/namespace body
  * no unterminated string, verbatim string, interpolated string or char literal
  * a few C# 9+ constructs the project deliberately avoids (advisory), because
    the target is a conservative C# 7/8 subset.

It does NOT check types, method resolution, overloads or anything semantic.
A clean run means "the delimiters line up and nothing lexical is broken",
NOT "this compiles". That distinction is stated so the report is never oversold.

EXIT CODE:  0 = no ERRORs   1 = at least one ERROR   2 = IO/invocation problem
"""

import sys
import os
import argparse

ERROR = "ERROR"
WARN = "WARN"
INFO = "INFO"


class Finding:
    def __init__(self, level, path, line, col, message):
        self.level = level
        self.path = path
        self.line = line
        self.col = col
        self.message = message

    def format(self, root):
        rel = os.path.relpath(self.path, root)
        where = "{}:{}:{}".format(rel, self.line, self.col)
        return "  [{:5}] {:<48} {}".format(self.level, where, self.message)


# ---------------------------------------------------------------------------
# Single-pass lexer.
#
# It emits a "code mask": a list, one entry per character of the source, that
# is True when the character is real code and False when it is inside a comment,
# string, verbatim string, interpolated string or char literal. Delimiter
# counting then only looks at code-True characters, which is what makes the
# brace check trustworthy in the presence of things like  "}"  or  // )
#
# Interpolated strings ($"...") are subtle: the braces inside them ARE code
# again (they hold expressions), and "{{" / "}}" are escaped literal braces.
# The lexer tracks interpolation depth so that `$"x={obj.M()}"` contributes the
# () of M() to the code mask but not the surrounding string text.
# ---------------------------------------------------------------------------

def build_code_mask(src):
    """Return (mask, findings) where mask[i] is True for real-code chars."""
    n = len(src)
    mask = [False] * n
    findings = []
    i = 0
    line = 1
    col = 1

    def pos(idx):
        # compute (line, col) for an index by counting; only used on error paths
        ln = 1
        cl = 1
        for k in range(idx):
            if src[k] == "\n":
                ln += 1
                cl = 1
            else:
                cl += 1
        return ln, cl

    while i < n:
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ""

        # line comment
        if c == "/" and nxt == "/":
            while i < n and src[i] != "\n":
                i += 1
            continue

        # block comment
        if c == "/" and nxt == "*":
            start = i
            i += 2
            closed = False
            while i < n - 1:
                if src[i] == "*" and src[i + 1] == "/":
                    i += 2
                    closed = True
                    break
                i += 1
            if not closed:
                ln, cl = pos(start)
                findings.append(Finding(ERROR, None, ln, cl,
                                        "unterminated block comment /* ... */"))
                i = n
            continue

        # verbatim string  @"..."  ("" is an escaped quote; braces are literal)
        if c == "@" and nxt == '"':
            start = i
            i += 2
            closed = False
            while i < n:
                if src[i] == '"':
                    if i + 1 < n and src[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    closed = True
                    break
                i += 1
            if not closed:
                ln, cl = pos(start)
                findings.append(Finding(ERROR, None, ln, cl,
                                        "unterminated verbatim string @\"...\""))
            continue

        # interpolated string $"..." (may itself be $@"..." or @$"...")
        if c == "$" and (nxt == '"' or (nxt == "@" and i + 2 < n and src[i + 2] == '"')):
            verbatim = (nxt == "@")
            start = i
            i += 2 if not verbatim else 3
            closed = False
            while i < n:
                ch = src[i]
                if ch == "\\" and not verbatim:
                    i += 2
                    continue
                if ch == '"':
                    if verbatim and i + 1 < n and src[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    closed = True
                    break
                if ch == "{":
                    if i + 1 < n and src[i + 1] == "{":
                        i += 2
                        continue
                    # enter interpolation expression: mark braces + inner as code
                    depth = 1
                    mask[i] = True
                    i += 1
                    while i < n and depth > 0:
                        if src[i] == "{":
                            depth += 1
                        elif src[i] == "}":
                            depth -= 1
                        mask[i] = True  # expression chars count as code
                        i += 1
                    continue
                i += 1
            if not closed:
                ln, cl = pos(start)
                findings.append(Finding(ERROR, None, ln, cl,
                                        "unterminated interpolated string $\"...\""))
            continue

        # normal string "..."
        if c == '"':
            start = i
            i += 1
            closed = False
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == '"':
                    i += 1
                    closed = True
                    break
                if src[i] == "\n":
                    break  # normal strings cannot span lines
                i += 1
            if not closed:
                ln, cl = pos(start)
                findings.append(Finding(ERROR, None, ln, cl,
                                        "unterminated string literal"))
            continue

        # char literal '.'
        if c == "'":
            start = i
            i += 1
            closed = False
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == "'":
                    i += 1
                    closed = True
                    break
                if src[i] == "\n":
                    break
                i += 1
            if not closed:
                ln, cl = pos(start)
                findings.append(Finding(ERROR, None, ln, cl,
                                        "unterminated char literal"))
            continue

        # ordinary code character
        mask[i] = True
        i += 1

    return mask, findings


# ---------------------------------------------------------------------------
# Delimiter balance check over the code mask.
# ---------------------------------------------------------------------------

PAIRS = {")": "(", "]": "[", "}": "{"}
OPENERS = set("([{")


def line_col_at(src, idx):
    ln = 1
    cl = 1
    for k in range(idx):
        if src[k] == "\n":
            ln += 1
            cl = 1
        else:
            cl += 1
    return ln, cl


def check_delimiters(src, mask):
    findings = []
    stack = []  # (char, index)
    for i, ch in enumerate(src):
        if not mask[i]:
            continue
        if ch in OPENERS:
            stack.append((ch, i))
        elif ch in PAIRS:
            want = PAIRS[ch]
            if not stack:
                ln, cl = line_col_at(src, i)
                findings.append(Finding(ERROR, None, ln, cl,
                                        "closing '{}' with nothing open".format(ch)))
            elif stack[-1][0] != want:
                ln, cl = line_col_at(src, i)
                op, oi = stack[-1]
                oln, ocl = line_col_at(src, oi)
                findings.append(Finding(
                    ERROR, None, ln, cl,
                    "closing '{}' but innermost open is '{}' (opened at {}:{})".format(
                        ch, op, oln, ocl)))
                stack.pop()
            else:
                stack.pop()
    for op, oi in stack:
        oln, ocl = line_col_at(src, oi)
        findings.append(Finding(ERROR, None, oln, ocl,
                                "unclosed '{}'".format(op)))
    return findings


def code_only_text(src, mask):
    """Blank out non-code chars (keep newlines) so line scans are safe."""
    out = []
    for i, ch in enumerate(src):
        if ch == "\n":
            out.append("\n")
        elif mask[i]:
            out.append(ch)
        else:
            out.append(" ")
    return "".join(out)


def check_structure(src, mask):
    findings = []
    code = code_only_text(src, mask)
    lines = code.split("\n")

    first_type_line = None
    for idx, ln in enumerate(lines, start=1):
        stripped = ln.strip()
        if not stripped:
            continue
        if stripped.startswith("namespace ") and stripped.endswith(";"):
            findings.append(Finding(
                WARN, None, idx, 1,
                "file-scoped namespace (C# 10) - project targets C# 7/8 block form"))
        for kw in ("class ", "struct ", "interface ", "enum "):
            if (" " + kw) in (" " + stripped) and first_type_line is None \
                    and not stripped.startswith("using"):
                first_type_line = idx

    if first_type_line is not None:
        for idx, ln in enumerate(lines, start=1):
            s = ln.strip()
            if idx > first_type_line and s.startswith("using ") and s.endswith(";") \
                    and "(" not in s and " = " not in s:
                findings.append(Finding(
                    WARN, None, idx, 1,
                    "using directive appears after first type declaration"))

    for idx, ln in enumerate(lines, start=1):
        s = ln.strip()
        if s.startswith("record ") or s.startswith("public record ") \
                or s.startswith("internal record ") or s.startswith("sealed record "):
            findings.append(Finding(INFO, None, idx, 1,
                                    "'record' type (C# 9) - advisory only"))
    return findings


def validate_file(path, root):
    try:
        with open(path, "r", encoding="utf-8-sig") as fh:
            src = fh.read()
    except Exception as exc:  # noqa
        return [Finding(ERROR, path, 0, 0, "cannot read: {}".format(exc))]

    mask, lex_findings = build_code_mask(src)
    all_findings = []
    all_findings.extend(lex_findings)
    if not any(f.level == ERROR for f in lex_findings):
        all_findings.extend(check_delimiters(src, mask))
        all_findings.extend(check_structure(src, mask))
    for f in all_findings:
        f.path = path
    return all_findings


def main():
    ap = argparse.ArgumentParser(description="ASTRA C# structural validator")
    ap.add_argument("root", nargs="?", default=".",
                    help="directory (recursed) or single .cs file")
    ap.add_argument("--quiet", action="store_true",
                    help="only print files that have findings")
    args = ap.parse_args()

    target = os.path.abspath(args.root)
    if os.path.isfile(target):
        files = [target]
        root = os.path.dirname(target)
    else:
        root = target
        files = []
        for dirpath, _dirs, names in os.walk(target):
            for nm in names:
                if nm.endswith(".cs"):
                    files.append(os.path.join(dirpath, nm))
    files.sort()

    if not files:
        print("No .cs files found under {}".format(target))
        return 2

    total_err = total_warn = total_info = 0
    files_with_errors = []

    for path in files:
        findings = validate_file(path, root)
        errs = [f for f in findings if f.level == ERROR]
        total_err += len(errs)
        total_warn += len([f for f in findings if f.level == WARN])
        total_info += len([f for f in findings if f.level == INFO])
        if errs:
            files_with_errors.append(os.path.relpath(path, root))
        if findings:
            if args.quiet and not errs:
                continue
            print(os.path.relpath(path, root))
            for f in sorted(findings, key=lambda x: (x.line, x.col)):
                print(f.format(root))
            print("")

    print("=" * 70)
    print("Scanned {} file(s).".format(len(files)))
    print("ERRORS: {}   WARNINGS: {}   ADVISORIES: {}".format(
        total_err, total_warn, total_info))
    if files_with_errors:
        print("Files with structural ERRORS:")
        for rel in files_with_errors:
            print("  - {}".format(rel))
    print("")
    print("NOTE: a clean run means delimiters balance and nothing lexical is")
    print("broken. It does NOT mean the project compiles - types, overloads and")
    print("references are not checked. Open Unity for the authoritative build.")
    return 1 if total_err else 0


if __name__ == "__main__":
    sys.exit(main())
