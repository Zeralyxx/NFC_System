# Thesis Verification and Required Corrections

Reviewed file: `C:\Users\justine mendoza\Documents\NFC Thesis Chapter 1-5 (1).pdf`

This review uses the page numbers printed in this specific 244-page PDF. Do not use page numbers from earlier drafts. The PDF and source workbooks were not modified.

## Verdict

Chapter 4's numerical results are consistent with the available records. All 111 survey-item means and sample SDs in the detailed Chapter 4 tables were extracted and checked against the source responses/tallies, with no numerical discrepancies. The observation summaries and Welch test also recalculate correctly. Chapters 4 and 5 appropriately limit claims about physical identity, fraud prevention, processing speed, and full-scale deployment.

The thesis is not yet completely consistent. The most important remaining issues are the separation of the evaluated configuration from later features, the conflicting Chapter 3 SD formula, older claims of proven benefits, and outdated technical descriptions/diagrams.

## Chapter 4: Keep These Results

| Result | Verified value | Current pages |
|---|---|---|
| Exported records | 438: 210 successful entries, 210 granted reset exits, 18 denials | 113, 128, 173 |
| Successful entry sample | 70 per mode; same 70 participants across modes | 113-114 |
| Mean internal latency, Fast / Standard / High-Security | 11.49 / 57.37 / 54.06 ms | 115 |
| Internal-latency sample SDs | 2.20 / 17.46 / 6.34 ms | 115 |
| Mean database-query time | 2.88 / 47.95 / 44.90 ms | 115 |
| Standard PIN interaction | Mean 2.505 s; sample SD 1.152 s | 116 |
| High-Security combined PIN/QR interaction | Mean 5.799 s; sample SD 3.878 s | 116 |
| High-Security PIN / QR component means | 2.162 / 3.637 s | 116 |
| Between-entry intervals | 66 per mode, from 19 + 19 + 19 + 9 | 117-118 |
| Supplementary interval rates | 21.98 / 7.46 / 4.95 intervals/minute | 118-119 |
| Applicable recorded credential decisions | 222, including 210 valid grants and 12 credential denials | 119-120 |
| Observed credential-decision accuracy | 100% (222/222), conditional on researcher-confirmed expected outcomes | 120 |
| Recorded denial proportion | 4.11% (18/438), not a participant failure rate | 121 |
| Recorded PIN lockout rate | 1.32% (2/152), conditional on confirmation that both were newly triggered | 122-123 |
| Manual writing duration | n = 70; mean 21.803 s; sample SD 4.436 s | 123-124 |
| Manual writing sum / median / range | 1526.24 s / 21.135 s / 13.21-37.63 s | 124 |
| Manual service-time-equivalent rate | 2.75 writing completions/minute, not observed queue throughput | 124 |
| Student system overall score | Mean 3.742; composite sample SD 0.290 | 139, 158, 161 |
| Manual overall score | Mean 2.284; composite sample SD 0.753 | 158, 161 |
| Security-personnel overall mean | 3.63, from unrounded 3.634 | 139, 144 |
| Faculty/event-organizer/administrator overall mean | 3.81, from unrounded 3.81375 | 146, 153 |
| Welch overall-score test | t = 15.124; df = 89.076; p < .001 | 160-162 |
| System-minus-manual difference and 95% CI | 1.458 points; [1.267, 1.650] | 162 |

The full-precision recalculation gives t = 15.1241767182, df = 89.0760178841, and two-sided p approximately 2.46554e-26. Reporting p < .001 is correct. Rejecting the null hypothesis is correct for this overall survey-score comparison. The hypotheses on pages 66 and 160 are consistent with the two-tailed test. The formula and rounded demonstration on pages 161-162 are readable and correct; the rounding explanation should remain.

Student-system composite SDs are 0.318, 0.366, 0.329, 0.348, and 0.341 in category order. Manual composite SDs are 0.899, 0.919, 0.854, 0.852, and 0.883. Do not replace these with averages of item SDs. The dashes in the security-personnel and administrative composite-SD rows are appropriate because respondent-level joint responses were unavailable for those groups.

## Chapter 4: Exact Changes

### 1. Page 112: Separate the Evaluated Configuration from Later Features

The researcher confirmed that signed QR credentials, durable offline queues, event-completion rules, and graduation-clearance features were added after the August 12-13 observation. The current architecture and ERD may describe the final implementation, but the August metrics and earlier surveys cannot validate those additions.

Add this paragraph at the end of **4.1 Prototype Implementation Summary**:

> The system observations reported in this chapter describe the configuration used on August 12-13, 2026. Signed QR credentials, durable offline queues, event-completion rules, and graduation-clearance features were implemented after those observations and were not evaluated by the August performance records. The technical architecture and database schema describe the final implementation; the observation and survey findings apply only to the functionality available during their respective evaluation sessions. Later additions require separately documented testing before evaluation outcomes can be attributed to them.

Do not alter the August measurements or relabel the observation as a new test. If subsequent functionality testing exists, report it separately with dates, the tested version, and actual outcomes. Do not invent PASS results for these additions.

### 2. Page 114: Replace the First Paragraph of 4.2.2

Current opening: "Chapter 3 defines average processing time from NFC tap to completed verification."

This contradicts the updated Chapter 3 on page 67, which already distinguishes available component measurements.

Replace the complete first paragraph under **4.2.2 Average Processing Time** with:

> As specified in Chapter 3, complete automated processing time from NFC presentation to finished verification could not be determined from the supplied exports. The exports provide internal system latency and recorded PIN/QR interaction-stage durations rather than independent start and finish timestamps for the complete task. These available components are therefore reported separately and are not treated as equivalent to manual active writing duration or combined with an assumed physical-interaction time.

### 3. Page 117: Remove the Obsolete "Original" Reference

In **4.2.5 System Throughput Rate**, replace:

> The original Chapter 3 throughput formula requires a successful-verification count and a defined elapsed observation period.

With:

> The conceptual complete-round throughput formula in Chapter 3 requires the number of completed successful tasks and an independently measured elapsed observation period.

Keep the remaining explanation, including the four rounds and 66 intervals. Those calculations are correct.

### 4. Page 130: Correct the Administrator-Group Service Categories

The table still displays overlapping categories "11-15 Years" and "15-20 Years", while its footnote already says those categories were combined. Do not guess which category a 15-year answer should belong to.

For the **faculty/event-organizer/administrator years-of-service block only**, use:

| Years of Service | Frequency | Percentage of Valid Responses |
|---|---:|---:|
| 0-5 years | 0 | 0.00% |
| 6-10 years | 0 | 0.00% |
| 11-20 years, combined original upper categories | 4 | 100.00% |
| Total valid responses | 4 | 100.00% |

Keep the note that the student-organization event organizer did not answer. Do not put that respondent into a service bracket or calculate these percentages using five. The paragraph on page 131 is already consistent with the combined range.

## Other Essential Corrections

### 5. Page 67: Replace the Separate SD Paragraph, Formula, and Definitions

Replace everything from **"Standard Deviation - Was calculated..."** through its formula definitions, stopping before **Performance Metrics Analysis**, with:

> Sample Standard Deviation - Sample standard deviation described dispersion in individual observations or respondent scores and used n - 1 in the denominator. Individual-item SDs were calculated from item responses. Category and overall composite SDs were calculated from each respondent's category and overall scores where individual records were available. An average of item SDs was not treated as a composite SD. Composite SDs for security personnel and faculty/event-organizer/administrator respondents were not reported because only item-frequency totals were available for those groups.

Formula, shown here in plain text to avoid display problems:

`s = square root of [sum((x_i - x_bar)^2) / (n - 1)]`

Where `s` is sample SD, `x_i` is an individual observation or respondent score, `x_bar` is their mean, and `n` is the number of applicable observations or respondents. When typesetting, use the same sample-SD equation already shown on page 66. Do not change the correct page 66 formula or Chapter 4 SDs.

### 6. Pages 3-5, 19, 29-31: Qualify Unmeasured Benefits

These earlier sections still present faster processing, reduced queues, eliminated workload, and prevention of credential misuse as achieved outcomes. Chapters 4 and 5 explicitly state that equivalent complete-task timing, workload savings, physical tailgating, and fraud reductions were not measured.

Make these targeted replacements wherever the quoted wording appears:

| Current wording | Replacement |
|---|---|
| "providing a faster, more secure, and data-driven solution" | "providing configurable credential checks and traceable digital records, with comparative speed and security effectiveness requiring further evaluation" |
| "significantly reduces queuing times" / "significantly reducing queuing times" | "is intended to reduce routine interaction demands; queue-time reductions were not measured in this study" |
| "eliminates the encoding bottleneck" | "reduces reliance on later transcription by recording attendance digitally; administrative time savings were not quantified" |
| "ensuring that only actively enrolled and verified individuals are present on campus" | "checking configured student eligibility and credential requirements; these checks do not independently establish each person's physical identity or prevent all physical bypass" |

Anticipated benefits may remain in the significance section, but label them as intended or potential benefits rather than demonstrated improvements. Do not describe all modes as two-factor: Fast is NFC-only; Standard and High-Security add PIN.

### 7. Pages 42, 46-47, 82-83, 89, 107, 110: Use Configured Mode Selection

The final process on pages 101-105 and Chapter 4 correctly describe operator/event configuration. Some older sections still say the system determines a mode from each transaction's risk or real-time flags.

Use this description instead:

> The operator configures the gate mode, or the selected event supplies its configured mode. The application enforces the corresponding credential requirements and eligibility rules. The evaluated prototype did not calculate individual risk scores or automatically escalate the mode for each transaction.

In the Singh discussion on page 42, distinguish the referenced framework from your own implementation:

> Singh et al.'s framework provides conceptual support for differentiated authentication requirements. The present prototype applies this principle through operator-configured Fast, Standard, and High-Security modes rather than an equivalent automatic risk-scoring engine.

On page 46, replace the absolute claims beginning **"no local study has implemented..."** with:

> Among the local studies reviewed in this thesis, the specific combination of operator-configured NFC verification, PIN authentication, QR credential validation, eligibility checks, and entry/exit state handling was not presented as the same integrated campus-entry and event-attendance workflow. This identifies a gap within the reviewed sources rather than proving that no comparable Philippine implementation exists.

Also revise the "single-technology solutions" generalization: Calo et al., whom the thesis itself cites, combine RFID and image cross-verification.

### 8. Page 53: Correct Table 1's Caption

Current: **Distribution of Proposed-System Survey Respondents (n = 150)**.

Replace with: **Table 1 - Distribution of Manual-Process and Proposed-System Survey Respondents (n = 150)**.

The counts and percentages are correct for the combined sample. Only 80 evaluated the proposed system; the other 70 are the separate manual group.

### 9. Page 72: Correct the ESP32's Role

The ESP32 paragraph incorrectly assigns the verification logic and Firebase synchronization to the microcontroller, contrary to pages 99-101 and Chapter 4.

Replace the paragraph below Figure 4 with:

> The ESP32 is the microcontroller used to interface with the PN532 NFC reader and transmit captured NFC UIDs to the Windows application through USB serial communication. Where a physical keypad is installed, it also forwards keypad input. Student-record validation, PIN and QR verification, transaction timestamping, database operations, and Firebase Firestore synchronization are handled by the connected C# WinUI 3 application rather than by the ESP32.

### 10. Page 77: Remove the Unsupported Sub-15-ms Database Claim

In the MySQL row, replace the justification containing **"ensuring the system functions with sub-15ms database latency"** with:

> Used for local relational storage and retrieval of student, event, security, and attendance records. Database-query time was measured separately and varied across verification modes.

Chapter 4's measured database-query means are 2.88, 47.95, and 44.90 ms, so a blanket sub-15-ms claim is inconsistent with the observations.

### 11. Pages 81, 85-87, 106-111: Align the Older Diagrams with the Final Process

The architecture diagram on page 98 and the process on pages 101-105 are more current than these illustrations/descriptions. Do not rewrite correct Chapter 4 results to fit older diagrams.

- Pages 106-107: replace "over the local network using a USB cable" with **"through a USB serial connection to the Windows application"**.
- Pages 109-110: the ESP32 is shown capturing UID and timestamp. Change this to **UID capture and USB serial transmission**; the application assigns timestamps.
- Page 111: replace **Firebase Realtime Database** with **Firebase Firestore**.
- Pages 81-84: replace automatic risk-determination boxes with **Apply operator-selected gate mode** or **Read selected event's configured mode**. Show that normal NFC exit bypasses PIN/QR after preliminary checks, if the diagram is intended to represent the final implementation.
- Pages 81-84 and 109-110: event scans should update event check-in/checkout evidence, not automatically establish a campus visit. This must match page 104.
- Pages 85-87: the sequence diagram names `SCAN_LOGS` and `VERIFICATION_LOGS`, but the final schema uses `fast_mode_logs`, `standard_mode_logs`, `high_security_mode_logs`, and related attendance/security tables. Rename these lifelines/messages to the implemented stores, or clearly mark them as conceptual log processes rather than actual SQL table names.
- Page 89: an unconditional UML `include` for PIN conflicts with Fast mode. Make PIN conditional on Standard/High-Security, or use separate mode-specific authentication use cases.

These are technical alignment changes, not changes to measured performance values. If a diagram deliberately depicts the earlier evaluated configuration, label its version/context instead of presenting it as the final architecture.

### 12. Page 214: Replace the RAM/100%-Continuity Resolution

The paragraph claiming an "absolute, immutable source of physical truth" and "100% state continuity" is inconsistent with the durable recovery design and is not established by the observation.

Replace only the **Resolution** paragraph under **Offline Shadow Cache State Desynchronization** with:

> Resolution: In the final implementation, pending attendance transactions and PIN operations are preserved through a durable local attendance outbox and PIN journal. Recovery services reconcile state and replay pending operations using transaction identifiers, device sequences, and processing receipts. Cached records support permitted offline decisions, while unavailable security state, storage, or uncertain commit results require an unavailable or pending outcome rather than an automatic grant. These mechanisms were implemented after the August observation and require dedicated recovery testing; they do not establish perfect state continuity under every network or power-failure condition.

This accurately distinguishes later development from the historical observation. Do not state that the August tests validated the new durability mechanisms.

### 13. Pages 211-212: Correct the Security-Personnel Manual

The manual says Security Personnel can manage staff/courses and upload system data. The current `SecurityDashboardWindow.xaml.cs` hides upload, staff-management, course-management, and maintenance controls for the Personnel role.

Replace the Security Dashboard module description with:

> Security Dashboard - Used to review the security alerts and monitoring information permitted for the Security Personnel role. Staff management, course management, upload operations, and administrative maintenance controls are not available to this role.

Replace "manage foundational data" in the procedure with **"review permitted monitoring information and respond to security conditions"**. Check screenshots against the actual role before retaining them as a current user manual.

### 14. Pages 230-231: Keep Functionality Cases within Their Actual Test Scope

The appendix records 16 PASS cases. This is a documented case list, not independently reproduced hardware testing during this review. The chapter's careful wording about the listed cases should remain.

- Rename T-06 from **High-Security Anti-Cloning Check** to **High-Security QR-Mismatch Rejection**. A mismatched QR test does not establish detection of a copied NFC plus matching copied QR.
- Identify the tested configuration/version and test date if recorded. Do not extend these PASS outcomes to later signed-QR, durable-queue, completion, or clearance features without new cases.
- For T-14, distinguish loss of internet connectivity from loss of the MySQL connection. Internet disconnection while local MySQL remains available is not a test of persistent-cache fallback during database failure. Clarify the historical condition from the actual test notes, not by guessing.
- T-15 and T-16 should describe the historical synchronization and closure behavior actually tested, not imply that all current recovery and role-dependent closure paths were verified.

### 15. Pages 234-237: Explain the Old SD Footers in Appendix I

The screenshots preserve the corrected tallies, but their category/overall SD cells still show averages of item SDs. Examples include the manual overall SD of 1.01 on page 237 and the student-system overall SD of 0.47 on page 235. They are not the respondent-composite SDs used in Chapter 4.

Keep the raw counts unchanged. Either replace the images with a correctly labelled calculation view or add this note directly below the relevant figures:

> Note: These worksheet images document item-frequency tallies. The category and overall SD cells shown in these images are averages of item SDs, not sample SDs of respondent composite scores, and were not used as composite SDs in Chapter 4. Chapter 4 reports respondent-level composite SDs for the student manual and system groups. Composite SDs were not calculated for the security-personnel and faculty/event-organizer/administrator groups because only item-frequency totals were available.

This avoids implying that the raw tallies themselves are wrong. Do not make up individual records for the two five-person groups.

### 16. Pages 13-17: Refresh the Contents and Lists

Regenerate the table of contents, list of tables, and list of figures after edits. Their current entries are stale: for example, Table 3 is listed on page 111 but appears on page 113, and Table 42 appears across pages 158-159. This explains why some earlier page references were difficult to locate.

## Methodology Boundary: SOP 6

SOP 6C is addressed by descriptive survey results and the overall Welch test. SOP 6A and 6B are only partially addressed: the available observations do not measure equivalent complete tasks or matched full-round throughput. The thesis already discloses this correctly on pages 125-128, 163-165, and 186-187.

Do not claim that every objective comparison was completed. A valid processing-time t-test would require new, comparable task timings. The survey t-test cannot substitute for that missing evidence. No new statistical computation can reconstruct missing timing boundaries from these exports.

The same 70 participants used all three system modes. The fixed mode order limits causal interpretation, and 210 successful trials are not 210 independent people. The existing qualifications should remain.

## Abstract and Chapter 5

The abstract on pages 11-12 and Chapter 5 on pages 174-192 agree with the verified main results. No numerical replacement is needed. Preserve their explicit limitations and the distinction between operator-configured modes and automatic individual risk scoring.

The abstract does not claim that the later signed-QR/durable-queue/completion/clearance additions were evaluated, so it can remain as written after the configuration note is added to Chapter 4. Do not add those features to the abstract's evaluated-results claims without separate evidence.

## External and Technical Cross-Checks

- The Welch statistic, unequal-variance degrees of freedom, and two-sided hypotheses agree with [NIST's two-sample t-test guidance](https://www.itl.nist.gov/div898/handbook/eda/section3/eda353.htm).
- The cited Ajayi attendance timings are supported by the [original journal abstract](https://laujci.lautech.edu.ng/index.php/laujci/article/view/94). Chapter 4 correctly attributes these to that study, not your own observation.
- The PN532/Raspberry Pi/API description of Sali and Okumus is supported by their [original article](https://www.irjet.net/archives/V11/i6/IRJET-V11I676.pdf).
- The distinction between Singh's automatic risk-score/trust-profile framework and your configured modes is supported by the [original RAD-AA paper record](https://arxiv.org/abs/2208.02592).
- Pantilon's reported 78% accuracy-perception and 85% ease-of-use figures appear in the [original article](https://ijmaberjournal.org/index.php/ijmaber/article/download/3018/1620/9046). Chapter 4 correctly avoids adopting them as your study's results.
- Calo's combined RFID/image mechanism is supported by the [original publisher record](https://indjst.org/articles/in-classroom-faculty-attendance-monitoring-system-based-on-ultra-high-frequency-uhf-radio-frequency-identification-rfid-with-captured-image-cross-verification-mechanism).
- Current source code declares 23 MySQL tables, consistent with the final architecture and ERD. The signed-QR implementation checks ECDSA P-256 signatures and the current student credential. Persistent attendance and PIN queues are present. These source checks establish implementation structure, not empirical security or recovery certification.

This review did not reproduce physical hardware tests, authenticate historical respondent forms, verify every full-text literature source, or certify legal/ISO compliance. It checked the supplied records' arithmetic, the thesis's substantive consistency, selected original cited studies, and selected current implementation descriptions. The raw data and the thesis were left unchanged.

## Audit Record

Reviewed PDF SHA-256: `570128c9baa384f15c1661d99505766d5fb4109f03f20e16e415e31ce1196c5f`.

Calculation record: `tmp/pdfs/oct11-thesis-audit/source-verification.json`.

111-item table check: `tmp/pdfs/oct11-thesis-audit/item-table-check.json` (111 checked, zero numerical discrepancies).

Raw student/manual survey source: `output/chapter4/Survey-Response-Entry-Template.xlsx`. Personnel and administrative frequencies were re-read from `UnStudent Weighted Mean - Students (1).pdf`. System timings were re-read from `METRICS FIRST BATCH.xlsx` and `Second Batch.xlsx`. Manual durations were re-read from `Manual Process Baseline Testing - Sheet1.pdf`. Source hashes are retained in the calculation record.
