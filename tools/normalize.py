"""Applies the encoding and line ending rules of the repository's .editorconfig to an explicit list of files.

Usage (from anywhere):
	python -I tools/normalize.py [--check] <file> [<file> ...]

For every listed file it applies charset (utf-8 or utf-8-bom), end_of_line and insert_final_newline as the sections of
the .editorconfig in the repository root define them. It only touches the listed files, never anything outside the
repository, binary files, or files that aren't utf-8. With --check it writes nothing and exits with 1 if a file would
change, e.g. to check every file: python -I tools/normalize.py --check $(git ls-files)
"""
import pathlib
import re
import sys

BOM = b"\xef\xbb\xbf"
ROOT = pathlib.Path(__file__).resolve().parent.parent
NEWLINES = {"crlf": b"\r\n", "lf": b"\n", "cr": b"\r"}


def glob_to_regex(pattern):
	"""Translates an .editorconfig section glob into a regular expression for paths relative to the root."""
	# A glob without a slash matches files of that name in any folder
	pattern = pattern.lstrip("/") if "/" in pattern else "**/" + pattern
	regex = ""
	i = 0
	while i < len(pattern):
		if pattern.startswith("**/", i):
			regex += "(?:.*/)?"
			i += 3
		elif pattern.startswith("**", i):
			regex += ".*"
			i += 2
		elif pattern[i] == "*":
			regex += "[^/]*"
			i += 1
		elif pattern[i] == "?":
			regex += "[^/]"
			i += 1
		elif pattern[i] == "{" and "}" in pattern[i:]:
			end = pattern.index("}", i)
			regex += "(?:" + "|".join(re.escape(option) for option in pattern[i + 1:end].split(",")) + ")"
			i = end + 1
		else:
			regex += re.escape(pattern[i])
			i += 1
	return re.compile(regex + r"\Z")


def read_sections(editorconfig):
	"""Reads the sections of an .editorconfig as (glob regex, properties) in file order."""
	sections = []
	properties = None
	for line in editorconfig.read_text(encoding="utf-8-sig").splitlines():
		line = line.strip()
		if not line or line[0] in "#;":
			continue
		if line.startswith("[") and line.endswith("]"):
			properties = {}
			sections.append((glob_to_regex(line[1:-1]), properties))
		elif properties is not None and "=" in line:
			key, value = (part.strip() for part in line.split("=", 1))
			properties[key.lower()] = value.lower()
	return sections


def rules_for(relative, sections):
	"""The properties for a file: later sections override earlier ones, as in EditorConfig."""
	rules = {}
	for regex, properties in sections:
		if regex.match(relative):
			rules.update(properties)
	return rules


def normalize(data, rules):
	"""Returns the file content with the rules applied, or None if the rules don't apply to it."""
	charset = rules.get("charset")
	if charset not in (None, "utf-8", "utf-8-bom"):
		return None
	had_bom = data.startswith(BOM)
	text = data[len(BOM):] if had_bom else data
	try:
		text.decode("utf-8")
	except UnicodeDecodeError:
		return None

	newline = NEWLINES.get(rules.get("end_of_line"))
	if newline is not None:
		text = re.sub(rb"\r\n|\r|\n", lambda _: newline, text)
	if rules.get("insert_final_newline") == "true" and text and not text.endswith(newline or b"\n"):
		text += newline or b"\n"
	if charset == "utf-8-bom" or (charset is None and had_bom):
		text = BOM + text
	return text


def main(args):
	check = "--check" in args
	files = [arg for arg in args if arg != "--check"]
	if not files:
		print(__doc__)
		return 2

	sections = read_sections(ROOT / ".editorconfig")
	would_change = False
	for name in files:
		path = pathlib.Path(name).resolve()
		if ROOT not in path.parents:
			raise SystemExit(f"refusing to touch {path}: outside {ROOT}")
		relative = path.relative_to(ROOT).as_posix()
		data = path.read_bytes()
		if b"\0" in data:
			print(f"skipped    {relative} (binary)")
			continue

		text = normalize(data, rules_for(relative, sections))
		if text is None:
			print(f"skipped    {relative} (not utf-8)")
		elif text == data:
			print(f"unchanged  {relative}")
		elif check:
			would_change = True
			print(f"would fix  {relative}")
		else:
			path.write_bytes(text)
			print(f"normalized {relative}")
	return 1 if would_change else 0


if __name__ == "__main__":
	sys.exit(main(sys.argv[1:]))
