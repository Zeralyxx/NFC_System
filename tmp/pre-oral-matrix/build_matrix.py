from copy import deepcopy
from hashlib import sha256
from pathlib import Path
from zipfile import ZipFile

from lxml import etree as E


ROOT = Path(__file__).resolve().parents[2]
REFERENCE = Path(r"C:\Users\justine mendoza\Documents\MATRIX OF REVISIONS Aug 29 (1).docx")
OUTPUT = ROOT / "output/pre-oral/MATRIX-OF-REVISIONS-Pre-Oral-September-2-2026.docx"
W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
NS = {"w": W}


ROWS = [
    (
        "01\nRevision checklist",
        "The panel requested a complete record of its pre-oral recommendations.",
        "Provide a complete checklist of all panelist recommendations.",
        "Addressed. This matrix lists all 17 supplied recommendations separately. Item 18 adds the title clarification visible in the annotated pre-oral photograph. Remaining requirements are identified below rather than recorded as completed.",
    ),
    (
        "02\nChapter 4 presentation",
        "The panel requested clearer presentation of results, comparisons, and survey answers.",
        "Present Chapter 4 data, visual comparisons, and survey-question answer data.",
        "Addressed in the manuscript. Chapter 4 presents performance metrics, manual-writing results, item-level survey tables, and a manual/system survey comparison. Figures 19-22 provide visual summaries. See pp. 115-128 and 135-163. Complete-task speed superiority is not claimed from differently bounded timings.",
    ),
    (
        "03\nRaw data",
        "The panel requested underlying survey answers and manual/proposed comparison records.",
        "Provide raw survey-question answers and the raw data for the manual and proposed-system comparison.",
        "Partially addressed. The student response workbook and system exports are available electronically. Appendix I, pp. 232-237, presents manual sheets and survey-tally screenshots, not the complete respondent-level answers or all transaction rows. Attach anonymized raw records and identify the files used for Chapter 4.",
    ),
    (
        "04\nTest and survey evidence",
        "The panel requested evidence supporting all reported system tests and surveys.",
        "Provide evidence for all system tests and surveys.",
        "Partially addressed. Appendix H lists 16 functionality cases, pp. 230-231; Appendices I-K contain tally images, testing photographs, and hardware documentation, pp. 232-242. These do not independently evidence every trial or current feature. Later additions require separately dated test results; August observations must not be used to validate them.",
    ),
    (
        "05\nSystem interfaces",
        "The panel requested that system interfaces be documented in the appendices.",
        "Place system interfaces in the appendices.",
        "Partially addressed. Appendix D, pp. 205-212, contains selected login and role-dashboard interfaces. Add current screenshots of verification, PIN/QR stages, entry/exit timestamps, retained student results, and administrative maintenance. Check that each screenshot matches the permissions of the displayed role.",
    ),
    (
        "06\nManual questionnaire",
        "The panel requested a survey instrument for the traditional/manual process.",
        "Provide a survey questionnaire for manual processes.",
        "Addressed in the manuscript. The manual-process student questionnaire is included as Appendix G.2, p. 226. Its results and comparison with the proposed-system student ratings are reported in Chapter 4, pp. 154-163.",
    ),
    (
        "07\nManual baseline",
        "The panel noted that manual-process data were absent and the paper focused on the proposed system.",
        "Include manual-process data and compare the manual and proposed system.",
        "Addressed with stated measurement limits. Chapter 4 reports 70 manual writing observations, mean 21.803 seconds and sample SD 4.436 seconds, pp. 123-125, plus manual survey results. Automated component timings are reported separately; they are not equivalent to a complete manual task. See pp. 125-128 and 158-163.",
    ),
        (
        "08\nParticipant counts",
        "The panel requested an explicit number of student participants.",
        "State the amount of student participants in the paper.",
        "Addressed in the text. There were 70 manual-survey students and 70 different proposed-system survey students; five security personnel and five administrative respondents bring the combined survey count to 150. Observation involved 70 participants and 210 successful entries across three modes, not 210 distinct people. See pp. 50-53 and 113-114. Correct Table 1's caption to describe both survey groups.",
    ),
    (
        "09\nSame participants",
        "The panel requested the same students in both evaluations to reduce participant-group bias.",
        "Use the same manual and proposed-system participants to avoid bias in favor of the proposed system.",
        "Not fulfilled as specified. The two student survey groups each contain 70 respondents, but they are different people. Equal group sizes do not establish matching or eliminate selection bias. Chapter 4 uses an independent-groups comparison and states this limitation. A matched study requires new records from the same identified participants.",
    ),
    (
        "10\nQuestionnaire sequence",
        "The panel requested a manual evaluation followed by a proposed-system evaluation with the same participants.",
        "Give participants the manual questionnaire first and then the proposed-system questionnaire.",
        "Not fulfilled as specified. Both questionnaires are included in Appendix G, pp. 225-226, but the supplied student records belong to different groups and cannot be matched. Their availability does not demonstrate the requested within-participant sequence. This remains a study-design limitation, not a completed paired evaluation.",
    ),
    (
        "11\nPersonnel login time",
        "The panel requested a visible timestamp for personnel login.",
        "Display the personnel-login timestamp. The photographed comment also requests visible time-in/time-out information.",
        "Partially addressed in the current code. Successful staff/admin logins create timestamped audit events, and the security audit lists display event times. A dedicated personnel-login-time display was not verified. Add or identify that visible field and provide a role-appropriate screenshot; a general clock is not the login timestamp.",
    ),
    (
        "12\nStudent timestamps",
        "The panel requested a timestamp on the student verification display.",
        "Display the student's system timestamp, including applicable time-in and time-out values.",
        "Implemented in current code; interface evidence pending. KioskModeWindow displays the scan timestamp and, for campus visits, IN and OUT values; an unrecorded exit is shown accordingly. Add a screenshot of an actual entry and exit to Appendix D and a dated test record. Do not attribute a later implementation to the August observations.",
    ),
    (
        "13\nStudent result retention",
        "The panel requested that the student display remain visible for a few seconds; the photo says to keep the last log on screen.",
        "Maintain the student's result and details briefly rather than immediately clearing the display.",
        "Implemented for a timed result in current code. The kiosk is configured to retain the granted/denied result for 10 seconds before returning to idle. This is not permanent retention of the last log after idle. Document the timed display with a dated UI test; confirm whether the panel additionally requires a persistent last-result display.",
    ),
    (
        "14\nAdministrative purge",
        "The panel requested an administrative ability to remove old records.",
        "Allow the administrator to purge records, either actively or passively.",
        "Implemented as an active local cleanup in current code. Maintenance provides a retention cutoff, confirmation, authorized execution, and an audit record. Log cleanup removes eligible local records already confirmed in cloud backup; cloud history remains. Add before/after counts and interface evidence. No destructive purge was run during this review.",
    ),
    (
        "15\nData destruction",
        "The panel separately requested data destruction, beyond a general purge control.",
        "Include data destruction in the system.",
        "Partially addressed. Local log cleanup and inactive-student deletion exist. The cleanup explicitly retains cloud history; destruction of cloud copies, exports, caches, and backups was not established. Define the approved retention/disposal scope and verify deletion across applicable stores before describing this requirement as fully completed.",
    ),
    (
        "16\nUse case diagram",
        "The panel requested a use case diagram documenting actors and system functions.",
        "Include a use case diagram.",
        "Diagram provided; alignment correction pending. Figure 13 appears on p. 88, with discussion on pp. 89-92. An unconditional PIN include does not match NFC-only Fast mode. Make PIN conditional on Standard/High-Security or use separate mode-specific cases before marking the final diagram fully aligned with the implementation.",
    ),
    (
        "17\nT test comparison",
        "The panel requested a t-test in the statistical treatment to compare manual and proposed-system average or weighted means.",
        "Use t-test statistics to compare the manual and proposed-system means; update the statistical treatment accordingly.",
        "Addressed for overall student survey scores. Chapter 3 gives the Welch independent-samples method and hypotheses, pp. 64-66; Chapter 4 gives the computation, pp. 160-163. Result: t = 15.124, df = 89.076, p < .001; reject H0. Complete-task processing-time means were not tested because equivalent durations were unavailable. The separate SD formula on p. 67 still needs n - 1.",
    ),
    (
        "18\nSpelled out NFC",
        "The photographed title annotation reads NFC spelled out.",
        "Spell out Near Field Communication in the thesis title rather than using NFC alone.",
        "Addressed in the latest manuscript. Page 1 spells out Near Field Communication, Personal Identification Number, and Quick Response, followed by their abbreviations. This additional row records the unique title comment in the pre-oral photograph; the other photographed comments are covered in items 02, 07, and 11-17.",
    ),
]

EXCEPTIONS = [
    (
        "Items 09 and 10\nSame participants and questionnaire sequence",
        "A matched manual-then-system evaluation with the same students.",
        "Not fulfilled. Different groups were used. Welch's independent-samples test is appropriate to these records but does not fulfill a same-participant requirement or eliminate group-selection bias. Seek panel acceptance of the stated limitation or collect a matched evaluation.",
    ),
    (
        "Items 03 and 04\nComplete supporting evidence",
        "Complete anonymized survey answers, transaction exports, and dated test evidence for all reported features.",
        "Partially addressed. Existing appendices contain selected sheets, tallies, cases, and photographs. Provide a traceable evidence set and separately document features added after the August observation. Do not invent respondent-level records for the two five-person groups.",
    ),
    (
        "Items 05 and 11-13\nInterface documentation",
        "Full current interface coverage, dedicated personnel-login time, and confirmation of last-result behavior.",
        "Partially addressed. Add current role-specific screenshots and UI tests. Confirm whether timed retention satisfies the request to keep the last log visible. Audit timestamps alone do not establish a dedicated login-time field.",
    ),
    (
        "Item 15\nDestruction scope",
        "Verified disposal of all in-scope copies, not only cloud-backed local logs.",
        "Partially addressed. Active local cleanup retains cloud history. Document retention authority, applicable stores, disposal behavior, and verification evidence. Do not equate routine local cleanup with complete data destruction.",
    ),
    (
        "Item 16\nUse case alignment",
        "Conditional PIN requirement for the configured mode.",
        "Pending diagram correction. Fast mode uses NFC only. Update the unconditional PIN include or separate the mode-specific use cases, while retaining the existing actors and relevant functions.",
    ),
    (
        "Item 17\nTiming comparison and SD consistency",
        "Equivalent complete-task timing for a processing-time t-test; consistent sample-SD notation.",
        "The overall survey t-test is reported. Complete-task timing was unavailable, so no valid speed t-test was performed. If required, collect equivalent manual and automated timings prospectively. Replace the conflicting p. 67 SD denominator with n - 1.",
    ),
]


def q(name):
    return f"{{{W}}}{name}"


def set_child(parent, name, attrs):
    child = parent.find(q(name))
    if child is None:
        child = E.SubElement(parent, q(name))
    for key, value in attrs.items():
        child.set(q(key), str(value))
    return child


def paragraph(template, text, bold=False, centered=False, title=False):
    p = E.Element(q("p"))
    old = template.find(q("pPr"))
    pr = deepcopy(old) if old is not None else E.Element(q("pPr"))
    p.append(pr)
    for node in pr.findall(q("pBdr")):
        pr.remove(node)
    set_child(pr, "spacing", {"before": 0, "after": 0, "line": 240, "lineRule": "auto"})
    set_child(pr, "jc", {"val": "center" if centered else "left"})
    set_child(pr, "keepNext", {"val": 0})
    set_child(pr, "keepLines", {"val": 1})
    if title:
        set_child(pr, "pStyle", {"val": "Title"})
    elif pr.find(q("pStyle")) is not None:
        pr.remove(pr.find(q("pStyle")))
    r = E.SubElement(p, q("r"))
    rp = E.SubElement(r, q("rPr"))
    set_child(rp, "rFonts", {"ascii": "Arial", "hAnsi": "Arial", "cs": "Arial"})
    set_child(rp, "sz", {"val": 28 if title else 22})
    set_child(rp, "szCs", {"val": 28 if title else 22})
    set_child(rp, "b", {"val": 1 if bold else 0})
    set_child(rp, "bCs", {"val": 1 if bold else 0})
    set_child(rp, "color", {"val": "000000"})
    for i, line in enumerate(text.split("\n")):
        if i:
            E.SubElement(r, q("br"))
        t = E.SubElement(r, q("t"))
        t.text = line
        t.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
    return p


def fill_row(template, values, header=False):
    row = deepcopy(template)
    pr = row.find(q("trPr"))
    if pr is None:
        pr = E.Element(q("trPr"))
        row.insert(0, pr)
    for old in pr.findall(q("trHeight")):
        pr.remove(old)
    set_child(pr, "cantSplit", {"val": 1})
    set_child(pr, "tblHeader", {"val": 1 if header else 0})
    for cell, value in zip(row.findall(q("tc")), values):
        proto = cell.find(q("p"))
        for node in list(cell):
            if node.tag != q("tcPr"):
                cell.remove(node)
        cell.append(paragraph(proto, value, bold=header, centered=header))
    return row


def build_table(reference, header, rows):
    table = deepcopy(reference)
    old_rows = table.findall(q("tr"))
    head_proto, row_proto = old_rows[0], old_rows[1]
    for row in old_rows:
        table.remove(row)
    table.append(fill_row(head_proto, header, True))
    for values in rows:
        table.append(fill_row(row_proto, values))
    return table


def main():
    original_hash = sha256(REFERENCE.read_bytes()).hexdigest()
    with ZipFile(REFERENCE) as src:
        parts = {name: src.read(name) for name in src.namelist()}
        info = src.infolist()
    doc = E.fromstring(parts["word/document.xml"])
    body = doc.find(q("body"))
    paras = body.findall(q("p"))
    tables = body.findall(q("tbl"))
    sect = deepcopy(body.find(q("sectPr")))
    template_p = paras[1]
    for node in list(body):
        body.remove(node)

    title = paragraph(paras[0], "MATRIX OF REVISIONS FOR PRE ORAL DEFENSE", True, True, True)
    body.append(title)
    body.append(paragraph(template_p, ""))
    body.append(paragraph(template_p, "A Risk-Based Two-Factor Student Verification and Event Attendance Management System Using Near Field Communication (NFC), Personal Identification Number (PIN) Authentication, and Quick Response (QR) Credential Validation", centered=True))
    body.append(paragraph(template_p, ""))
    body.append(paragraph(template_p, "Researchers:  Justine G. Mendoza\nMichael Daniel M. Milallos\nSeanne Stiffel T. Sintay", centered=True))
    body.append(paragraph(template_p, ""))
    body.append(paragraph(template_p, "Pre-oral defense date: September 2, 2026"))
    body.append(paragraph(template_p, ""))
    body.append(paragraph(template_p, "This matrix records the action taken on each supplied pre-oral recommendation and identifies remaining requirements. Paper locations refer to the latest 244-page manuscript. Current-code implementation is distinguished from documented test evidence. Items 01-17 follow the supplied list; item 18 records the additional photographed title comment."))
    body.append(paragraph(template_p, ""))
    body.append(build_table(tables[0], ("Section", "Pre-oral Work or Identified Gap", "Panel Recommendations or Concerns", "Action Taken and Status"), ROWS))
    body.append(paragraph(template_p, ""))
    heading = paragraph(paras[8], "Recommendations Requiring Further Action", True, True)
    set_child(heading.find(q("pPr")), "keepNext", {"val": 1})
    body.append(heading)
    body.append(build_table(tables[1], ("Panel Recommendations or Concerns", "Unresolved Component", "Status and Required Follow Through"), EXCEPTIONS))
    body.append(paragraph(template_p, ""))
    body.append(paragraph(template_p, "Review basis: the latest thesis manuscript, supplied pre-oral list, annotated photographs, available survey/observation records, and current system code. A status of implemented in code is not a claim of independently reproduced hardware testing. No unverified requirement is recorded as completed."))
    body.append(sect)

    # The source uses decimal twips; integer normalization keeps the same dimensions.
    for element in doc.iter():
        for attr, value in list(element.attrib.items()):
            if attr.startswith("{" + W + "}") and "." in value:
                try:
                    number = float(value)
                except ValueError:
                    continue
                if abs(number - round(number)) < 0.001:
                    element.set(attr, str(round(number)))
        for attr in list(element.attrib):
            if E.QName(attr).localname in ("paraId", "textId"):
                del element.attrib[attr]
    parts["word/document.xml"] = E.tostring(doc, xml_declaration=True, encoding="UTF-8", standalone=True)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(OUTPUT, "w") as dest:
        for item in info:
            dest.writestr(item, parts[item.filename])
    with ZipFile(REFERENCE) as src, ZipFile(OUTPUT) as dest:
        assert src.namelist() == dest.namelist()
        changed = [name for name in src.namelist() if src.read(name) != dest.read(name)]
        assert changed == ["word/document.xml"], changed
    assert original_hash == sha256(REFERENCE.read_bytes()).hexdigest()
    assert len(ROWS) == 18
    print(OUTPUT)
    print("Verified: 18 recommendations; only document.xml changed; reference unchanged.")


if __name__ == "__main__":
    main()
