#!/usr/bin/env python3
"""Merge two or more Cobertura reports covering the SAME assembly into one honest
line-coverage figure, by taking the union of covered lines rather than naively
summing lines-covered/lines-valid (which would double-count the shared denominator).

Usage: merge_coverage.py <report1.xml> [report2.xml ...]
"""
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict


def main() -> int:
    paths = sys.argv[1:]
    if not paths:
        print("Usage: merge_coverage.py <report1.xml> [report2.xml ...]", file=sys.stderr)
        return 2

    # key: (class name, filename, line number) -> max hits seen across all reports
    line_hits = {}
    # key: (class name, filename, line number) -> best (numerator, denominator) branch coverage seen
    branch_best = {}
    class_names = defaultdict(set)

    for path in paths:
        root = ET.parse(path).getroot()
        for pkg in root.iter('package'):
            for cls in pkg.iter('class'):
                name = cls.attrib['name']
                filename = cls.attrib['filename']
                lines = cls.find('lines')
                if lines is None:
                    continue
                class_names[filename].add(name)
                for line in lines.findall('line'):
                    key = (name, filename, int(line.attrib['number']))
                    hits = int(line.attrib['hits'])
                    line_hits[key] = max(line_hits.get(key, 0), hits)

                    if line.attrib.get('branch') == 'true' and 'condition-coverage' in line.attrib:
                        cc = line.attrib['condition-coverage']
                        frac = cc.split('(')[1].rstrip(')')
                        num, den = (int(x) for x in frac.split('/'))
                        prev = branch_best.get(key)
                        if prev is None or num > prev[0]:
                            branch_best[key] = (num, den)

    total_lines = len(line_hits)
    covered_lines = sum(1 for hits in line_hits.values() if hits > 0)

    total_branches = sum(den for (_num, den) in branch_best.values())
    covered_branches = sum(num for (num, _den) in branch_best.values())

    print(f"Reports merged:        {len(paths)}")
    print(f"Union line coverage:   {covered_lines}/{total_lines} = {covered_lines/total_lines*100:.2f}%")
    if total_branches:
        print(f"Best-effort branch:    {covered_branches}/{total_branches} = {covered_branches/total_branches*100:.2f}%")
        print("  (branch figure is a best-effort union across reports; Cobertura's")
        print("   condition-coverage is a per-line summary fraction, not a per-outcome")
        print("   bitmap, so this takes the higher fraction seen per line rather than a")
        print("   fully rigorous cross-report branch union.)")

    print()
    print("Still-uncovered lines after merge, by file:")
    by_file = defaultdict(list)
    for (name, filename, number), hits in line_hits.items():
        if hits == 0:
            by_file[filename].append((name, number))
    if not by_file:
        print("  (none)")
    else:
        for filename in sorted(by_file):
            nums = sorted(set(n for _cls, n in by_file[filename]))
            print(f"  {filename}: lines {', '.join(str(n) for n in nums)}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
