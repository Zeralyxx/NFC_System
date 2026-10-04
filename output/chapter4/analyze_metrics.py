import collections
import datetime as dt
import hashlib
import json
import statistics
from pathlib import Path

import openpyxl
import numpy as np

OUT = Path(__file__).parent
SOURCES = [
    Path(r"C:\Users\justine mendoza\Documents\METRICS first batch\METRICS FIRST BATCH.xlsx"),
    Path(r"C:\Users\justine mendoza\Documents\METRICS first batch\Second Batch.xlsx"),
]
MODES = ["Fast", "Standard", "HighSecurity"]
HEADERS = ["Date & Time", "Student Name", "Action", "Verification Flow", "Verdict",
           "NFC System Latency (ms)", "PIN User Workflow (ms)", "PIN System Latency (ms)",
           "QR User Workflow (ms)", "QR System Latency (ms)", "Total User Workflow Time (ms)",
           "Total System Latency (ms)", "Total DB Query Time (ms)", "Device ID", "Device Name"]
METRICS = dict(nfc_ms=5, pin_user_ms=6, pin_system_ms=7, qr_user_ms=8,
               qr_system_ms=9, total_user_ms=10, total_system_ms=11, db_ms=12)


def summary(values):
    values = [float(v) for v in values if isinstance(v, (int, float)) and not isinstance(v, bool)]
    if not values:
        return {"n": 0, "mean": None, "median": None, "sd": None, "min": None, "max": None, "p95": None}
    return dict(n=len(values), mean=statistics.mean(values), median=statistics.median(values),
                sd=statistics.stdev(values) if len(values) > 1 else 0,
                min=min(values), max=max(values), p95=float(np.percentile(values, 95)))


records, runs, checks, sources = [], [], [], []
for batch, source in enumerate(SOURCES, 1):
    sources.append(dict(batch=batch, path=str(source), sha256=hashlib.sha256(source.read_bytes()).hexdigest()))
    workbook = openpyxl.load_workbook(source, read_only=True, data_only=True)
    for worksheet in workbook:
        values = list(worksheet.iter_rows(values_only=True))
        assert list(values[0]) == HEADERS, worksheet.title
        sheet_records = []
        for row_number, row in enumerate(values[1:], 2):
            assert len(row) == 15 and isinstance(row[0], dt.datetime)
            mode, action, verdict = row[3], row[2], row[4]
            assert mode in MODES and action in ["Entry", "Exit"]
            category = "Successful entry" if action == "Entry" and verdict == "GRANTED" else (
                "Reset exit" if action == "Exit" and verdict == "GRANTED" else "Denied record")
            record = dict(batch=batch, sheet=worksheet.title, row=row_number,
                          values=[v.isoformat(sep=" ", timespec="milliseconds") if isinstance(v, dt.datetime) else v for v in row],
                          category=category, round=None, epoch=row[0].timestamp())
            records.append(record)
            sheet_records.append(record)
            for key, index in METRICS.items():
                if isinstance(row[index], (int, float)):
                    assert row[index] >= 0, (batch, worksheet.title, row_number, key)
            expected_system = sum(row[i] if isinstance(row[i], (int, float)) else 0 for i in [5, 7, 9])
            expected_user = sum(row[i] if isinstance(row[i], (int, float)) else 0 for i in [6, 8])
            checks.append(dict(batch=batch, sheet=worksheet.title, row=row_number,
                               system_difference=row[11] - expected_system,
                               user_difference=(row[10] - expected_user) if isinstance(row[10], (int, float)) else None))
        ordered = sorted(sheet_records, key=lambda r: r["epoch"])
        current, segments = None, []
        for record in ordered:
            if record["values"][4] != "GRANTED":
                continue
            action = record["values"][2]
            if current is None or current[0]["values"][2] != action:
                current = []
                segments.append(current)
            current.append(record)
        assert [segment[0]["values"][2] for segment in segments] == ["Entry", "Exit", "Entry", "Exit"]
        expected = [20, 20, 20, 20] if batch == 1 else [20, 20, 10, 10]
        assert [len(segment) for segment in segments] == expected
        for round_number, segment in enumerate([s for s in segments if s[0]["values"][2] == "Entry"], 1):
            first, last = segment[0], segment[-1]
            for record in segment:
                record["round"] = round_number
            span = last["epoch"] - first["epoch"]
            denied_within = [r for r in ordered if r["category"] == "Denied record" and first["epoch"] <= r["epoch"] <= last["epoch"]]
            runs.append(dict(batch=batch, mode=first["values"][3], round=round_number, n=len(segment),
                             intervals=len(segment)-1, first=first["values"][0], last=last["values"][0],
                             span_s=span, spacing_s=span/(len(segment)-1), rate_per_min=60*(len(segment)-1)/span,
                             denied_within=len(denied_within), source_rows=[r["row"] for r in segment]))
    workbook.close()

performance, reconciliation, pooled_spacing = [], [], []
for mode in MODES:
    selected = [r for r in records if r["values"][3] == mode and r["category"] == "Successful entry"]
    assert len(selected) == 70
    performance.append(dict(mode=mode, n=len(selected), metrics={key: summary(r["values"][index] for r in selected) for key,index in METRICS.items()}))
    for batch in [1, 2]:
        matching = [r for r in records if r["batch"] == batch and r["values"][3] == mode]
        reconciliation.append(dict(batch=batch, mode=mode, total=len(matching),
                                   entry=sum(r["category"] == "Successful entry" for r in matching),
                                   exits=sum(r["category"] == "Reset exit" for r in matching),
                                   denied=sum(r["category"] == "Denied record" for r in matching)))
    mode_runs = [r for r in runs if r["mode"] == mode]
    intervals = sum(r["intervals"] for r in mode_runs)
    span = sum(r["span_s"] for r in mode_runs)
    assert intervals == 66
    pooled_spacing.append(dict(mode=mode, intervals=intervals, span_s=span, spacing_s=span/intervals,
                               rate_per_min=60*intervals/span, denied_within=sum(r["denied_within"] for r in mode_runs)))

denied = collections.Counter(r["values"][4] for r in records if r["category"] == "Denied record")
assert len(records) == 438
assert sum(r["category"] == "Successful entry" for r in records) == 210
assert sum(r["category"] == "Reset exit" for r in records) == 210
assert sum(denied.values()) == 18
anomalies = [c for c in checks if abs(c["system_difference"]) > 1e-7 or
             (c["user_difference"] is not None and abs(c["user_difference"]) > 1e-7)]
entry_keys = {(r["batch"], r["sheet"], r["row"]) for r in records if r["category"] == "Successful entry"}
assert not any((c["batch"], c["sheet"], c["row"]) in entry_keys for c in anomalies), "Successful-entry component totals disagree"
fingerprints = collections.Counter(json.dumps(r["values"], default=str) for r in records)
assert not any(v > 1 for v in fingerprints.values()), "Exact duplicate source rows"
output = dict(sources=sources, headers=HEADERS, records=records, performance=performance,
              reconciliation=reconciliation, runs=runs, pooled_spacing=pooled_spacing,
              denied=dict(denied), component_anomalies=anomalies, checks=dict(records=len(records), duplicates=0,
              max_system_difference=max(abs(c["system_difference"]) for c in checks),
              max_user_difference=max(abs(c["user_difference"]) for c in checks if c["user_difference"] is not None)))
(OUT / "analysis.json").write_text(json.dumps(output, indent=2, ensure_ascii=True), encoding="utf-8")
print(json.dumps({k: output[k] for k in ["performance", "reconciliation", "pooled_spacing", "denied", "component_anomalies", "checks"]}, indent=2))
