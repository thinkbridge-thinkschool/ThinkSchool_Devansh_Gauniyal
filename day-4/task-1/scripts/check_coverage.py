#!/usr/bin/env python3
"""Fail (non-zero exit) if aggregate Cobertura line coverage is below the given threshold."""
import glob
import hashlib
import sys
import xml.etree.ElementTree as ET


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: check_coverage.py <results-dir> <threshold-percent>", file=sys.stderr)
        return 2

    results_dir, threshold_arg = sys.argv[1], sys.argv[2]
    threshold = float(threshold_arg)

    all_paths = sorted(glob.glob(f"{results_dir}/**/coverage.cobertura.xml", recursive=True))
    if not all_paths:
        print(f"No coverage.cobertura.xml found under {results_dir}")
        return 1

    # dotnet test's VSTest pipeline can write a byte-identical copy of the same
    # coverlet report into a second "_<machine>_<timestamp>/In/..." folder alongside
    # the canonical <results-dir>/<guid>/coverage.cobertura.xml. Dedupe by content
    # hash so a single test run's coverage is never double-counted.
    seen_hashes = set()
    report_paths = []
    for path in all_paths:
        with open(path, "rb") as f:
            digest = hashlib.sha256(f.read()).hexdigest()
        if digest not in seen_hashes:
            seen_hashes.add(digest)
            report_paths.append(path)

    if len(report_paths) < len(all_paths):
        print(f"Note: ignored {len(all_paths) - len(report_paths)} duplicate coverage report(s).")

    total_covered = 0
    total_valid = 0
    for path in report_paths:
        root = ET.parse(path).getroot()
        total_covered += int(root.attrib["lines-covered"])
        total_valid += int(root.attrib["lines-valid"])

    if total_valid == 0:
        print("No lines were measured for coverage.")
        return 1

    measured_percent = (total_covered / total_valid) * 100

    print(f"Coverage reports:       {len(report_paths)}")
    print(f"Lines covered/valid:    {total_covered}/{total_valid}")
    print(f"Measured line coverage: {measured_percent:.2f}%")
    print(f"Required threshold:     {threshold:.2f}%")

    if measured_percent < threshold:
        print(f"FAIL: coverage {measured_percent:.2f}% is below the {threshold:.2f}% threshold.")
        return 1

    print(f"PASS: coverage {measured_percent:.2f}% meets the {threshold:.2f}% threshold.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
