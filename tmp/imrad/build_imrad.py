from copy import deepcopy
from hashlib import sha256
from pathlib import Path
from zipfile import ZipFile
import json
import re

from lxml import etree


ROOT = Path(r"C:\Users\justine mendoza\source\repos\NFC_System")
REFERENCE = Path(r"C:\Users\justine mendoza\Documents\METRICS first batch\IMRAD-TEMPLATE-CSIS (2).docx")
OUT = ROOT / "output" / "imrad" / "NFC-System-IMRAD.docx"
TMP = ROOT / "tmp" / "imrad"
EXPECTED_SHA = "d2d0142c9d46aa4fa48ed461ad833baacd34cdc217cb454dc2b1876c525941ff"
W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
M = "http://schemas.openxmlformats.org/officeDocument/2006/math"
NS = {"w": W, "m": M}

TITLE = (
    "A Risk-Based Two-Factor Student Verification and Event Attendance Management System "
    "Using Near Field Communication (NFC), Personal Identification Number (PIN) Authentication, "
    "and Quick Response (QR) Credential Validation"
)
AUTHORS = "Mendoza, Justine G., Milallos, Michael Daniel M., and Sintay, Seanne Stiffel T."
ABSTRACT = (
    "This study developed and evaluated a risk-based student verification and event attendance "
    "management prototype for Taguig City University. An ESP32 and PN532 NFC reader were integrated "
    "with a WinUI 3 application, PIN authentication, camera-based QR validation, local MySQL storage, "
    "and Firebase Firestore synchronization. Authorized operators selected Fast, Standard, or "
    "High-Security modes. A quantitative developmental and descriptive-evaluative design used "
    "purposive and convenience sampling. The proposed-system survey involved 70 students, five "
    "security personnel, and five faculty/event-organizer/administrator respondents; 70 different "
    "students evaluated the manual process. Controlled observation produced 210 successful entries "
    "from 70 participants using 20 reusable credential profiles across three modes. Mean internal "
    "latency was 11.49, 57.37, and 54.06 milliseconds for Fast, Standard, and High-Security modes. "
    "Recorded Standard PIN and High-Security PIN/QR interaction averaged 2.505 and 5.799 seconds. "
    "Observed credential-decision accuracy was 100% (222/222) under researcher-labelled controlled "
    "conditions. Overall proposed-system survey means were 3.74, 3.63, and 3.81 for the three "
    "respondent groups, interpreted as Highly Satisfactory. Student overall ratings exceeded "
    "manual-process ratings (3.742 versus 2.284), Welch t(89.08) = 15.12, p < .001. Manual writing "
    "averaged 21.803 seconds, but unequal timing boundaries prevented a matched complete-task "
    "speed comparison. Findings support demonstrated prototype functionality and favorable "
    "perceived quality within the evaluation setting. Broader security, recovery, and matched "
    "performance testing are needed before institutional deployment."
)

INTRO = [
    "Student verification and event attendance require institutions to connect an access decision "
    "with an identifiable, retrievable record. Paper sign-in sheets collect attendance information, "
    "but identity checking, enrollment validation, record consolidation, and monitoring remain "
    "separate tasks. These concerns motivated a prototype for Taguig City University that combined "
    "configured credential validation with attendance records and monitoring interfaces.",
    "NFC and Internet of Things technologies have been investigated for attendance capture and "
    "data analysis. Lim et al. (2023) described an attendance approach integrating NFC and IoT "
    "with analytics. Such work provides a basis for electronic record capture, but the presence "
    "of a readable card alone does not establish that the person presenting it is its rightful "
    "owner. El Gaabouri et al. (2023) reviewed authentication and threat challenges in RFID-based "
    "NFC applications, emphasizing the need to consider security beyond identification.",
    "Additional authentication requirements introduce usability and operational demands. "
    "Mohammed et al. (2023) discussed multifactor authentication approaches and their security "
    "challenges. In the present prototype, NFC supplied the possession-based credential and PIN "
    "entry supplied a knowledge-based check where required. QR matching added a check against "
    "the stored credential record; it was not treated as an independently established third "
    "authentication factor. The software quality evaluation drew on selected characteristics "
    "of the ISO/IEC 25010:2011 model (International Organization for Standardization [ISO], 2011).",
    "The study aimed to develop the prototype, document its responses to configured verification "
    "conditions, describe recorded performance components, and evaluate perceived quality among "
    "its intended users. It also compared overall survey ratings from independent manual-process "
    "and proposed-system student groups. The contribution was an operator-configured combination "
    "of verification modes and administrative workflows evaluated under controlled conditions, "
    "rather than an automatic individual risk-scoring engine or a claim of university-wide readiness.",
]

METHODS = [
    ("Research Design and Participants", [
        "A quantitative developmental and descriptive-evaluative design was used. Development "
        "integrated the hardware reader, application, credential rules, and record-management "
        "functions. The evaluation combined documented functionality cases, controlled system "
        "observation, a manual attendance-writing baseline, and role-specific questionnaires.",
        "Purposive and convenience sampling recruited 80 proposed-system evaluators: 70 students "
        "from the College of Information and Communication Technology, five security personnel, "
        "and five faculty/event-organizer/administrator respondents. A separate group of 70 students "
        "evaluated the manual process. The student survey groups were independent, not matched "
        "pairs. The non-probability sample supported findings about the participating groups and "
        "did not establish representativeness of the entire university.",
    ]),
    ("Prototype and Verification Workflow", [
        "The prototype used an ESP32 microcontroller and PN532 NFC reader connected to a computer "
        "through USB serial communication. A C# WinUI 3 application handled credential lookup, "
        "PIN entry, camera-based QR scanning, access decisions, transaction records, and monitoring. "
        "Local MySQL storage and Firebase Firestore synchronization supported record management.",
        "Fast mode required NFC validation. Standard mode required NFC and PIN, while High-Security "
        "mode additionally required the scanned QR credential to match the stored record. Authorized "
        "operators selected the mode. The workflow checked configured student status and entry/exit "
        "state; event attendance also required registration for the selected event. Successful event "
        "verification recorded attendance, whereas student verification updated entry/exit records. "
        "Denials produced the configured alert and transaction record.",
        "Sixteen functionality cases were documented in the thesis. They included valid mode "
        "workflows, incorrect PIN and lockout handling, QR mismatch, unknown credentials, inactive "
        "and expelled status, restricted-event registration, entry/exit rules, and selected offline, "
        "synchronization, and application-closure conditions. These cases were evaluated separately "
        "from the numerical observation records.",
    ]),
    ("Controlled System Observation", [
        "System observation was conducted on August 12 and 13, 2026, with batches of 40 and 30 "
        "participants. The same 70 participants tested Fast, Standard, and High-Security modes in "
        "that order. Twenty NFC cards with preconfigured PIN and QR credentials were reused. "
        "For each mode, the first batch completed two rounds of 20 entries, and the second completed "
        "rounds of 20 and 10 entries. Research-team exit transactions reset the profiles between "
        "rounds, and explanations preceded mode changes.",
        "Exports contained transaction timestamps, outcomes, internal latency, database-query "
        "timings, and recorded PIN/QR durations. All 210 granted participant entries were retained "
        "for timing analysis, including slow observations. Reused profile names were not treated "
        "as duplicate participants. Team reset exits were excluded from entry timing. Database "
        "time overlapped internal latency and was not added to it. Fast-mode PIN/QR duration was "
        "not applicable because these stages were bypassed.",
        "Supplementary recorded-entry spacing used the within-round span from the first to the "
        "last successful entry. The four rounds contributed 19 + 19 + 19 + 9 = 66 intervals per "
        "mode. Mean spacing was the summed spans divided by 66; the recorded-entry rate was "
        "60 times 66 divided by the summed spans in seconds. Within-round pauses and invalid "
        "attempts remained in those spans. No independent complete-round stopwatch times were available.",
    ]),
    ("Credential and State Outcome Classification", [
        "Expected outcomes were assigned from the researcher's observation account. Valid "
        "participant grants and deliberately invalid credential denials counted as correct "
        "decisions when they agreed with the expected outcome. The accuracy denominator excluded "
        "team reset exits and pre-authentication entry/exit state violations. Denial proportion "
        "used all exported records, including resets and challenge conditions. Newly triggered "
        "PIN lockouts were divided by captured individual PIN submissions, including retries, "
        "triggering attempts, and correct PIN stages followed by QR denial. Fast mode and reset "
        "exits were excluded from PIN submissions.",
    ]),
    ("Manual Baseline and Survey Instruments", [
        "The manual simulation produced 70 observations of participants writing their name, course "
        "and section, student ID, and signature. Timing began at the first written character and "
        "ended when all required information was complete. Personnel did not check an ID or "
        "enrollment status during the timed procedure. This measured active writing rather than "
        "queue waiting, complete identity verification, or later encoding.",
        "Four-point questionnaires assessed perceived quality. The student and security instruments "
        "covered Functional Suitability, Performance Efficiency, Usability, Reliability, and Security. "
        "The administrative instrument also included Compatibility, Maintainability, and Portability. "
        "Means of 3.26-4.00 were interpreted as Highly Satisfactory, 2.51-3.25 as Satisfactory, "
        "1.76-2.50 as Unsatisfactory, and 1.00-1.75 as Highly Unsatisfactory. Role-specific instruments "
        "were analyzed separately rather than pooled into one system-wide score.",
    ]),
    ("Statistical Analysis", [
        "Means and sample standard deviations described applicable observations. Individual survey "
        "category scores were averages of each respondent's answers in that category; overall "
        "scores were the equally weighted average of the five shared categories. Student composite "
        "SDs were calculated from respondent-level scores using n - 1, not by averaging item SDs. "
        "Composite SDs were not reported for the two five-person groups because only item-frequency "
        "totals were supplied.",
        "A two-tailed Welch independent-samples t-test at alpha = .05 compared the overall student "
        "survey scores. The null hypothesis stated that the population mean overall ratings were "
        "equal; the alternative stated that they differed. Welch's test did not require equal "
        "population variances. Its statistic was calculated as follows:",
        "The subscripts S and M denote proposed-system and manual groups; x-bar denotes the group "
        "mean, s the sample SD, and n the respondent count. Welch-Satterthwaite degrees of freedom "
        "and a 95% confidence interval were calculated from unrounded respondent-level scores. "
        "Category comparisons and mode timings were descriptive. No processing-time t-test was "
        "performed because manual writing and automated component timings had different boundaries.",
    ]),
]

RESULTS = [
    ("Prototype Functionality", [
        "All 16 documented functionality cases were marked PASS. The export audit contained 438 "
        "records: 210 successful participant entries, 210 granted research-team reset exits, and "
        "18 denied records. Each mode contributed 70 successful entries and 70 granted resets. "
        "Fast, Standard, and High-Security modes contained 2, 12, and 4 denied records, respectively.",
    ]),
    ("Recorded Performance Components", [
        "Table 1 summarizes internal processing and interaction components. Standard's internal "
        "latency sample SD was 17.46 ms, compared with 2.20 ms for Fast and 6.34 ms for High-Security. "
        "Standard's mean PIN duration was 2.505 seconds. High-Security's mean PIN and QR durations "
        "were 2.162 and 3.637 seconds, totaling 5.799 seconds. Its combined median was 4.348 "
        "seconds and maximum was 22.604 seconds; all successful trials were retained.",
        "The summed within-round spans were 180.200, 531.167, and 800.508 seconds for Fast, Standard, "
        "and High-Security. With 66 intervals per mode, mean recorded-entry spacing was 2.730, "
        "8.048, and 12.129 seconds. Corresponding supplementary rates were 21.98, 7.46, and "
        "4.95 intervals per minute. These were timestamp-spacing measures, not independently "
        "measured complete-task throughput.",
    ]),
    ("Credential Decisions and Denials", [
        "The applicable credential sample comprised 210 expected-valid grants and 12 expected-invalid "
        "credential denials. All 222 decisions agreed with the researcher-confirmed expectations "
        "(Table 2). The 18 denied records comprised eight PIN failures, five consecutive-entry "
        "violations, two newly triggered lockouts, two QR mismatches, and one irregular exit. "
        "The five consecutive-entry violations and irregular exit were state tests outside the "
        "credential-accuracy denominator.",
        "Both lockouts were deliberate demonstrations reaching the third consecutive incorrect "
        "PIN submission. The researcher confirmed that individual PIN submissions were captured. "
        "The 152 submissions consisted of 140 PIN stages in successful entries, eight incorrect "
        "PIN records, two triggering lockout submissions, and two correct PIN stages followed "
        "by QR denial. The challenge records did not represent unexpected ordinary-user failures.",
    ]),
    ("Manual Attendance Baseline", [
        "The 70 active writing durations had a mean of 21.803 seconds, sample SD of 4.436 seconds, "
        "median of 21.135 seconds, and range of 13.21-37.63 seconds. Dividing 60 by the mean "
        "yielded a service-time-equivalent rate of 2.75 writing completions per minute. This "
        "derived value did not include queue waiting, participant transitions, or subsequent "
        "encoding and was not a continuously observed throughput rate.",
    ]),
    ("Respondent Evaluations", [
        "Overall proposed-system means were 3.74 for students, 3.63 for security personnel, and "
        "3.81 for faculty/event-organizer/administrator respondents, all Highly Satisfactory. "
        "The manual student overall mean was 2.28, interpreted as Unsatisfactory. Table 3 gives "
        "the respondent-level student category and overall descriptive statistics.",
        "Security personnel rated Functional Suitability at 3.84, Performance Efficiency at 3.45, "
        "Usability at 3.60, Reliability at 3.48, and Security at 3.80. Administrative respondents "
        "rated the same categories at 3.80, 3.68, 3.88, 3.76, and 3.84, respectively, and rated "
        "Compatibility at 3.75, Maintainability at 3.88, and Portability at 3.92. All category "
        "means in these two proposed-system groups fell within the Highly Satisfactory range.",
    ]),
    ("Independent Student Survey Comparison", [
        "The proposed-system and manual groups each contained 70 respondents. Their overall "
        "means were 3.742 and 2.284, with sample SDs of 0.290 and 0.753. Using the unrounded "
        "scores, the standard error was 0.096411 and the mean difference was 1.458143, giving "
        "t = 1.458143 / 0.096411 = 15.124. Table 4 reports the Welch result. The two-tailed "
        "p-value was below .001, and the null hypothesis was rejected at .05.",
    ]),
]

DISCUSSION = [
    ("Perceived Quality and Practical Use", [
        "The independent student groups reported substantially different evaluations, with higher "
        "overall ratings for the proposed prototype. The largest descriptive category difference "
        "was 1.811 points in Performance Efficiency. Functional Suitability differed by 1.280, "
        "Usability by 1.103, Reliability by 1.529, and Security by 1.569 points. These results "
        "concerned participants' reported perceptions across corresponding categories. They did "
        "not show that every individual would improve by the same amount after changing methods.",
        "The prototype combined credential decisions, timestamped records, and monitoring within "
        "the same workflow. This configuration offers a practical basis for organizing access "
        "and attendance information, consistent with the record-capture direction discussed by "
        "Lim et al. (2023). Nevertheless, this study did not quantify a reduction in transcription "
        "errors, proxy attendance, or administrative labor. Favorable ISO-based ratings represent "
        "user evaluation rather than formal ISO certification (ISO, 2011).",
        "Security personnel gave their lowest category mean to Performance Efficiency, at 3.45, "
        "and administrative respondents also rated that category lowest, at 3.68. These ratings "
        "identify monitoring and administrative workflows for further investigation. They do "
        "not by themselves establish whether a particular screen, hardware component, or "
        "database operation caused the lower ratings.",
    ]),
    ("Interpretation of Timing Measurements", [
        "The internal measurements described processing on a millisecond scale, while the "
        "recorded PIN and QR stages occurred on a seconds scale. Added credential interaction "
        "therefore needs to be assessed separately from software latency. Standard's mean "
        "internal latency exceeded High-Security's, so internal timings did not increase "
        "strictly with the number of required credential stages. Standard also had greater "
        "internal variability in this observation.",
        "The lower High-Security PIN mean cannot establish that the mode improved PIN performance, "
        "because participants always completed Standard before High-Security. Practice, changing "
        "conditions, and the demonstration schedule could have influenced the observations. "
        "The reused cards and research-team resets also differed from ordinary campus operation.",
        "The manual baseline measured writing, whereas automated exports measured internal and "
        "credential-stage components. Comparing 21.803 seconds directly with 2.505 or 5.799 "
        "seconds would mix different task boundaries. Similarly, the manual service-time "
        "equivalent and automated recorded-entry interval rates were different constructs. "
        "The study therefore established neither a matched percentage time reduction nor "
        "a complete-task throughput advantage.",
    ]),
    ("Security Scope and Study Limitations", [
        "The recorded credential accuracy demonstrated expected grants and correct rejections "
        "in the known controlled sample. It did not establish that an NFC UID, PIN, or printed "
        "QR code could never be copied or shared. El Gaabouri et al. (2023) identified broader "
        "RFID/NFC authentication threats, while Mohammed et al. (2023) emphasized challenges "
        "that remain in multifactor systems. The present results support configured rule "
        "handling, not comprehensive resistance to those threat classes.",
        "The application label for consecutive-entry denial described an entry-state rule. "
        "Without physical passage sensing or a controlled barrier, such a rule does not "
        "establish prevention of someone following another person through an entrance. "
        "Similarly, a matching QR credential is an additional integrity check against the "
        "stored record, not independent evidence of the presenter's physical identity.",
        "Generalization was limited by non-probability sampling, the five-person operational "
        "groups, fixed mode order, and 20 reusable credential profiles. Expected outcomes were "
        "researcher-labelled rather than independently blinded. Deliberate demonstrations "
        "affected denial and lockout proportions, and exports did not independently audit "
        "unrecorded cancellations, missed scans, or complete elapsed verification tasks. "
        "The manual and system survey groups differed, and process-specific item wording "
        "limited equivalence of measurement. Individual responses were unavailable for "
        "composite SDs in the two five-person groups.",
        "Selected offline and synchronization cases passed, but these observations were not "
        "a prolonged recovery, load, or network-failure study. The results justify further "
        "pilot testing with defined acceptance criteria before institution-wide deployment.",
    ]),
]

CONCLUSION = [
    "The study produced a prototype combining NFC capture, PIN authentication, QR credential "
    "validation, configured student and event rules, transaction recording, and monitoring. "
    "All 16 documented functionality cases passed, and 222 applicable recorded credential "
    "decisions agreed with researcher-confirmed expectations. The proposed-system student, "
    "security, and administrative groups gave Highly Satisfactory overall evaluations.",
    "The Welch test found higher overall survey ratings in the proposed-system student group "
    "than in the separate manual-process group, t(89.08) = 15.12, p < .001. This supported "
    "a difference in reported evaluations within the study setting. Recorded timings provided "
    "useful component-level performance evidence, but their boundaries did not establish "
    "a matched complete-task speed or throughput advantage. The controlled tests did not "
    "establish universal security effectiveness or readiness for immediate campus-wide use.",
]

RECOMMENDATIONS = [
    "Measure manual and automated workflows using the same explicit start and finish boundaries. "
    "Record waiting, identity checks, participant transitions, failures, and complete-round elapsed "
    "time separately. Use these matched observations for an appropriate processing-time comparison.",
    "Extend security testing to shared or copied credentials, unknown and inactive profiles, "
    "repeated attempts, credential revocation, and recovery from lockout. Use independently "
    "specified expected outcomes and assess physical entry controls separately from software "
    "entry-state rules.",
    "Conduct repeated load, hardware-disconnection, power-interruption, and synchronization "
    "tests. Verify record reconciliation, prevention of duplicate records, recovery behavior, "
    "and backup restoration under defined failure conditions.",
    "Investigate the monitoring workflow and QR interaction experience through direct observation "
    "and broader user feedback. Recruit participants from additional colleges and increase "
    "security and administrative sample sizes. Retain individual response records and document "
    "instrument comparability before extending inferential claims.",
    "Begin operational use with a supervised pilot, staff training, explicit access permissions, "
    "privacy and retention procedures, and documented alternatives during outages or exceptional "
    "access cases. Evaluate campus-wide readiness against predefined performance and security criteria.",
]

REFERENCES = [
    "El Gaabouri, I., Senhadji, M., Belkasmi, M., & El Bhiri, B. (2023). A systematic literature "
    "review on authentication and threat challenges on RFID based NFC applications. Future "
    "Internet, 15(11), Article 354. https://doi.org/10.3390/fi15110354",
    "International Organization for Standardization. (2011). Systems and software engineering "
    "- Systems and software Quality Requirements and Evaluation (SQuaRE) - System and software "
    "quality models (ISO/IEC Standard No. 25010:2011). https://www.iso.org/standard/35733.html",
    "Lim, J.-Y., Tan, J.-H., Liew, Z.-W., & Wang, L.-Y.-K. (2023). NFC integrated IoT based "
    "attendance with data analytics. In 2023 11th International Conference on Information and "
    "Communication Technology (ICoICT) (pp. 6-10). IEEE. https://doi.org/10.1109/ICoICT58202.2023.10262728",
    "Mohammed, A. H. Y., Dziyauddin, R. A., & Latiff, L. A. (2023). Current multi-factor of "
    "authentication: Approaches, requirements, attacks and challenges. International Journal "
    "of Advanced Computer Science and Applications, 14(1), 166-178. https://doi.org/10.14569/IJACSA.2023.0140119",
]

REFERENCE_ITALICS = [
    "Future Internet, 15",
    "Systems and software engineering - Systems and software Quality Requirements and Evaluation "
    "(SQuaRE) - System and software quality models",
    "2023 11th International Conference on Information and Communication Technology (ICoICT)",
    "International Journal of Advanced Computer Science and Applications, 14",
]


def el(name, **attrs):
    e = etree.Element(f"{{{W}}}{name}")
    for key, value in attrs.items():
        e.set(f"{{{W}}}{key}", str(value))
    return e


def child(parent, name, **attrs):
    e = el(name, **attrs)
    parent.append(e)
    return e


def mnode(name, text=None, **attrs):
    e = etree.Element(f"{{{M}}}{name}")
    for key, value in attrs.items():
        e.set(f"{{{M}}}{key}", str(value))
    if text is not None:
        e.text = text
    return e


def mrun(text):
    r = mnode("r")
    r.append(mnode("t", text))
    return r


def sub(base, suffix):
    e = mnode("sSub")
    a, b = mnode("e"), mnode("sub")
    a.append(base)
    b.append(mrun(suffix))
    e.extend([a, b])
    return e


def sup(base, exponent):
    e = mnode("sSup")
    a, b = mnode("e"), mnode("sup")
    a.append(base)
    b.append(mrun(exponent))
    e.extend([a, b])
    return e


def xbar(suffix):
    a = mnode("acc")
    pr = mnode("accPr")
    pr.append(mnode("chr", val="\u0305"))
    inner = mnode("e")
    inner.append(mrun("x"))
    a.extend([pr, inner])
    return sub(a, suffix)


def fraction(num, den):
    f = mnode("f")
    n, d = mnode("num"), mnode("den")
    n.extend(num)
    d.extend(den)
    f.extend([n, d])
    return f


def welch_equation():
    p = el("p")
    pr = child(p, "pPr")
    child(pr, "jc", val="center")
    child(pr, "spacing", before="100", after="100")
    rp = child(pr, "rPr")
    child(rp, "sz", val="22")
    mathpara = mnode("oMathPara")
    math = mnode("oMath")
    math.append(mrun("t = "))
    rad = mnode("rad")
    radpr = mnode("radPr")
    radpr.append(mnode("degHide", val="1"))
    rad.extend([radpr, mnode("deg")])
    radbody = mnode("e")
    for suffix in ["S", "M"]:
        if suffix == "M":
            radbody.append(mrun(" + "))
        radbody.append(fraction([sup(sub(mrun("s"), suffix), "2")], [sub(mrun("n"), suffix)]))
    rad.append(radbody)
    math.append(fraction([xbar("S"), mrun(" - "), xbar("M")], [rad]))
    mathpara.append(math)
    p.append(mathpara)
    return p


def make_paragraph(proto, text, *, bold=False, italic=False, title=False, heading=False,
                   centered=False, caption=False, reference=False, abstract=False):
    p = deepcopy(proto)
    for node in list(p):
        if node.tag != f"{{{W}}}pPr":
            p.remove(node)
    pr = p.find("w:pPr", NS)
    if pr is None:
        pr = el("pPr")
        p.insert(0, pr)
    for tag in ["sectPr", "rPr", "pageBreakBefore", "keepNext", "keepLines", "widowControl", "ind", "jc"]:
        for node in pr.findall(f"w:{tag}", NS):
            pr.remove(node)
    child(pr, "widowControl")
    if title:
        st = pr.find("w:pStyle", NS)
        if st is None:
            st = el("pStyle", val="Title")
            pr.insert(0, st)
        else:
            st.set(f"{{{W}}}val", "Title")
    child(pr, "jc", val="center" if centered or title else "left" if heading or caption or reference else "both")
    if heading or caption or title:
        child(pr, "keepNext")
        child(pr, "keepLines")
        child(pr, "ind", right="220" if not title else "0", firstLine="0")
    elif reference:
        child(pr, "ind", right="220", left="360", hanging="360")
        child(pr, "keepLines")
    elif not abstract and not centered:
        child(pr, "ind", right="220", firstLine="720")
    spacing = pr.find("w:spacing", NS)
    if spacing is None:
        spacing = child(pr, "spacing")
    spacing.set(f"{{{W}}}before", "160" if heading else "187" if not centered and not title else "0")
    spacing.set(f"{{{W}}}after", "40" if heading else "0")
    r = child(p, "r")
    rp = child(r, "rPr")
    child(rp, "rFonts", ascii="Arial", hAnsi="Arial", cs="Arial")
    child(rp, "color", val="000000")
    child(rp, "sz", val="28" if title else "20" if caption else "24")
    child(rp, "szCs", val="28" if title else "20" if caption else "24")
    if bold or heading or title or caption:
        child(rp, "b")
    if italic:
        child(rp, "i")
    t = child(r, "t")
    t.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
    t.text = text
    return p


def label_paragraph(proto, label, text, italic=False):
    p = make_paragraph(proto, label + text, abstract=True, italic=italic)
    r = p.find("w:r", NS)
    r.find("w:t", NS).text = label
    child(r.find("w:rPr", NS), "b")
    rr = deepcopy(r)
    rr.find("w:t", NS).text = text
    rr.find("w:rPr", NS).remove(rr.find("w:rPr/w:b", NS))
    p.append(rr)
    return p


def italicize_span(p, text):
    run = p.find("w:r", NS)
    original = run.find("w:t", NS).text
    start = original.index(text)
    pieces = [(original[:start], False), (text, True), (original[start + len(text):], False)]
    p.remove(run)
    for value, italic in pieces:
        if not value:
            continue
        new = deepcopy(run)
        new.find("w:t", NS).text = value
        if italic:
            child(new.find("w:rPr", NS), "i")
        p.append(new)


def table(headers, rows, widths):
    total = sum(widths)
    t = el("tbl")
    tp = child(t, "tblPr")
    child(tp, "tblW", w=total, type="dxa")
    child(tp, "tblLayout", type="fixed")
    borders = child(tp, "tblBorders")
    for side in ["top", "left", "bottom", "right", "insideH", "insideV"]:
        child(borders, side, val="single", sz="4", color="D9D9D9")
    cm = child(tp, "tblCellMar")
    for side, value in [("top", 80), ("left", 80), ("bottom", 80), ("right", 80)]:
        child(cm, side, w=value, type="dxa")
    grid = child(t, "tblGrid")
    for width in widths:
        child(grid, "gridCol", w=width)
    for i, values in enumerate([headers] + rows):
        row = child(t, "tr")
        trp = child(row, "trPr")
        child(trp, "cantSplit")
        if i == 0:
            child(trp, "tblHeader")
        for j, value in enumerate(values):
            c = child(row, "tc")
            cp = child(c, "tcPr")
            child(cp, "tcW", w=widths[j], type="dxa")
            child(cp, "vAlign", val="center")
            if i == 0:
                child(cp, "shd", fill="EDEDED", val="clear")
            p = child(c, "p")
            pp = child(p, "pPr")
            child(pp, "spacing", before="0", after="0", line="240", lineRule="auto")
            child(pp, "jc", val="left" if j == 0 else "center")
            r = child(p, "r")
            rp = child(r, "rPr")
            child(rp, "rFonts", ascii="Arial", hAnsi="Arial")
            child(rp, "sz", val="19")
            child(rp, "color", val="000000")
            if i == 0:
                child(rp, "b")
            text = child(r, "t")
            text.text = str(value)
    return t


def main():
    assert sha256(REFERENCE.read_bytes()).hexdigest() == EXPECTED_SHA
    assert 150 <= len(ABSTRACT.split()) <= 250, len(ABSTRACT.split())
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(REFERENCE) as z:
        parts = {i.filename: z.read(i.filename) for i in z.infolist()}
        infos = z.infolist()
    inventory = {name: {"size": len(data), "sha256": sha256(data).hexdigest()} for name, data in parts.items()}
    (TMP / "template-package-inventory.json").write_text(json.dumps(inventory, indent=2), encoding="utf-8")
    root = etree.fromstring(parts["word/document.xml"])
    body = root.find("w:body", NS)
    old = list(body)
    bodyproto = old[21]
    headingproto = old[24]
    for x in old:
        body.remove(x)
    body.append(deepcopy(old[0]))
    body.append(deepcopy(old[1]))
    body.append(make_paragraph(old[2], TITLE, title=True))
    body.append(make_paragraph(old[5], AUTHORS, centered=True))
    for i in [6, 7, 8]:
        body.append(deepcopy(old[i]))
    for name in ["Justine G. Mendoza", "Michael Daniel M. Milallos", "Seanne Stiffel T. Sintay"]:
        body.append(make_paragraph(old[10], name + ": [email address]", centered=True))
    body.append(label_paragraph(old[14], "Abstract: ", ABSTRACT))
    body.append(label_paragraph(old[16], "Key Words: ", "NFC; PIN authentication; QR credential validation; student verification; attendance management", italic=True))
    body.append(deepcopy(old[18]))
    body.append(make_paragraph(old[19], "I. INTRODUCTION", heading=True))
    body.append(deepcopy(old[20]))
    for text in INTRO:
        body.append(make_paragraph(bodyproto, text))
    body.append(make_paragraph(headingproto, "II. METHODS", heading=True))
    for heading, paragraphs in METHODS:
        body.append(make_paragraph(bodyproto, heading, heading=True))
        for text in paragraphs:
            body.append(make_paragraph(bodyproto, text))
            if text.endswith("calculated as follows:"):
                body.append(welch_equation())
    body.append(make_paragraph(headingproto, "III. RESULTS", heading=True))
    for heading, paragraphs in RESULTS:
        body.append(make_paragraph(bodyproto, heading, heading=True))
        for j, text in enumerate(paragraphs):
            body.append(make_paragraph(bodyproto, text))
            if heading == "Recorded Performance Components" and j == 0:
                body.append(make_paragraph(bodyproto, "Table 1  Successful entry timing components", caption=True))
                body.append(table(["Mode", "Internal latency (ms)", "Interaction (s)"], [
                    ["Fast", "11.49", "N/A"], ["Standard", "57.37", "2.505"], ["High-Security", "54.06", "5.799"]
                ], [1300, 1225, 1225]))
                body.append(make_paragraph(bodyproto, "Note. n = 70 per mode. Interaction denotes PIN or combined PIN/QR stages, not the complete task. Fast bypassed these stages.", caption=False, abstract=True))
            if heading == "Credential Decisions and Denials" and j == 1:
                body.append(make_paragraph(bodyproto, "Table 2  Recorded security measures", caption=True))
                body.append(table(["Measure", "Count", "Result"], [
                    ["Correct credential decisions", "222/222", "100%"],
                    ["Denied exported records", "18/438", "4.11%"],
                    ["Newly triggered PIN lockouts", "2/152", "1.32%"]
                ], [1800, 1050, 900]))
                body.append(make_paragraph(bodyproto, "Note. Denominators differ. Denials include deliberate challenges and resets in the record base; lockouts were deliberately triggered.", abstract=True))
            if heading == "Respondent Evaluations" and j == 0:
                body.append(make_paragraph(bodyproto, "Table 3  Student survey means and sample SDs", caption=True))
                body.append(table(["Category", "Manual M (SD)", "System M (SD)"], [
                    ["Functional Suitability", "2.483 (0.899)", "3.763 (0.318)"],
                    ["Performance Efficiency", "1.932 (0.919)", "3.743 (0.366)"],
                    ["Usability", "2.637 (0.854)", "3.740 (0.329)"],
                    ["Reliability", "2.183 (0.852)", "3.711 (0.348)"],
                    ["Security", "2.183 (0.883)", "3.751 (0.341)"],
                    ["Overall", "2.284 (0.753)", "3.742 (0.290)"]
                ], [1550, 1100, 1100]))
                body.append(make_paragraph(bodyproto, "Note. Independent groups, n = 70 each. SDs are respondent-level composite sample SDs. Overall scores use equal category weights.", abstract=True))
            if heading == "Independent Student Survey Comparison":
                body.append(make_paragraph(bodyproto, "Table 4  Welch test of overall survey scores", caption=True))
                body.append(table(["Statistic", "Result"], [
                    ["Mean difference (system - manual)", "1.458"],
                    ["t", "15.124"], ["Welch df", "89.076"],
                    ["Two-tailed p", "< .001"],
                    ["95% CI for the difference", "[1.267, 1.650]"],
                    ["Decision at alpha = .05", "Reject H0"]
                ], [2450, 1300]))
    body.append(deepcopy(old[32]))
    body.append(make_paragraph(headingproto, "IV. DISCUSSION", heading=True))
    for heading, paragraphs in DISCUSSION:
        body.append(make_paragraph(bodyproto, heading, heading=True))
        for text in paragraphs:
            body.append(make_paragraph(bodyproto, text))
    body.append(make_paragraph(headingproto, "V. CONCLUSION", heading=True))
    for text in CONCLUSION:
        body.append(make_paragraph(bodyproto, text))
    body.append(make_paragraph(headingproto, "VI. RECOMMENDATION", heading=True))
    for i, text in enumerate(RECOMMENDATIONS, 1):
        body.append(make_paragraph(bodyproto, f"{i}. {text}"))
    body.append(make_paragraph(headingproto, "VII. REFERENCES", heading=True))
    for text, italic_span in zip(REFERENCES, REFERENCE_ITALICS):
        p = make_paragraph(bodyproto, text, reference=True)
        italicize_span(p, italic_span)
        body.append(p)
    # Source terminal section elements are retained verbatim.
    end_refs = deepcopy(old[43])
    for e in list(end_refs):
        if e.tag != f"{{{W}}}pPr":
            end_refs.remove(e)
    body.append(end_refs)
    body.append(deepcopy(old[44]))
    body.append(deepcopy(old[47]))
    changed = {"word/document.xml": etree.tostring(root, encoding="UTF-8", xml_declaration=True, standalone=True)}
    styles = etree.fromstring(parts["word/styles.xml"])
    if not styles.xpath("w:style[@w:styleId='Title']", namespaces=NS):
        st = el("style", type="paragraph", styleId="Title")
        child(st, "name", val="Title")
        child(st, "basedOn", val="Normal")
        child(st, "qFormat")
        pp = child(st, "pPr")
        child(pp, "jc", val="center")
        rp = child(st, "rPr")
        child(rp, "rFonts", ascii="Arial", hAnsi="Arial")
        child(rp, "b")
        child(rp, "sz", val="28")
        child(rp, "color", val="000000")
        styles.append(st)
    changed["word/styles.xml"] = etree.tostring(styles, encoding="UTF-8", xml_declaration=True, standalone=True)
    rels = etree.fromstring(parts["word/_rels/document.xml.rels"])
    for e in list(rels):
        if e.get("Type", "").endswith("/hyperlink") and e.get("Target", "").startswith("mailto:"):
            rels.remove(e)
    changed["word/_rels/document.xml.rels"] = etree.tostring(rels, encoding="UTF-8", xml_declaration=True, standalone=True)
    settings = etree.fromstring(parts["word/settings.xml"])
    upd = settings.find("w:updateFields", NS)
    if upd is None:
        upd = child(settings, "updateFields", val="true")
    else:
        upd.set(f"{{{W}}}val", "true")
    changed["word/settings.xml"] = etree.tostring(settings, encoding="UTF-8", xml_declaration=True, standalone=True)
    core = etree.fromstring(parts["docProps/core.xml"])
    dc = "http://purl.org/dc/elements/1.1/"
    for tag, text in [("title", TITLE), ("creator", AUTHORS)]:
        x = core.find(f"{{{dc}}}{tag}")
        if x is None:
            x = etree.SubElement(core, f"{{{dc}}}{tag}")
        x.text = text
    changed["docProps/core.xml"] = etree.tostring(core, encoding="UTF-8", xml_declaration=True, standalone=True)
    with ZipFile(OUT, "w") as z:
        for info in infos:
            z.writestr(info, changed.get(info.filename, parts[info.filename]))
    with ZipFile(OUT) as z:
        preserved = [name for name in parts if name not in changed]
        assert all(z.read(name) == parts[name] for name in preserved)
        finalroot = etree.fromstring(z.read("word/document.xml"))
    original_sections = etree.fromstring(parts["word/document.xml"]).xpath(".//w:sectPr", namespaces=NS)
    final_sections = finalroot.xpath(".//w:sectPr", namespaces=NS)
    assert len(original_sections) == len(final_sections) == 6
    for a, b in zip(original_sections, final_sections):
        assert etree.tostring(a) == etree.tostring(b)
    alltext = " ".join(finalroot.xpath(".//w:t/text()|.//m:t/text()", namespaces=NS))
    for unwanted in ["Bonifacio", "abonifacio", "APA Forma", "Summarize the study", "List of recommendation"]:
        assert unwanted not in alltext
    assert alltext.count("[email address]") == 3
    assert len(finalroot.xpath(".//m:oMath", namespaces=NS)) == 1
    assert sha256(REFERENCE.read_bytes()).hexdigest() == EXPECTED_SHA
    report = {"output": str(OUT), "abstract_word_count": len(ABSTRACT.split()),
              "total_word_count": len(alltext.split()), "sections": 6, "tables": 4,
              "preserved_package_parts": preserved, "changed_package_parts": list(changed),
              "reference_unchanged": True}
    (TMP / "build-checks.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
