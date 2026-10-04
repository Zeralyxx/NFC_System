# Chapter 4 Before and Final Changes

Source reviewed: `C:\Users\justine mendoza\Documents\METRICS first batch\Chapter 4 Thesis Update.pdf`

The source PDF was not overwritten. "Before" excerpts identify the old material to replace. "Final" passages are complete replacement text. The administrator demographic correction remains dependent on checking the original demographic forms.

## Placement in Chapter 4

Comparison of the observed methods, interpretation of performance, and discussion of demonstrated strengths belong in Chapter 4 when this chapter is titled Results and Discussion. Recommendations for deployment, future development, institutional adoption, or additional studies belong in Chapter 5. Do not present an unmeasured operational benefit as an observed result.

Use one discussion sequence, rather than retaining the two competing sequences in the present PDF:

- 4.6.1 Interpretation of System Performance
- 4.6.2 Comparison with Existing Systems
- 4.6.3 Interpretation of Security Outcomes
- 4.6.4 System Strengths and Practical Advantages
- 4.6.5 Methodological Limitations
- 4.6.6 Comparison with Related Studies
- 4.7 Summary

The actual comparator in 4.6.2 is the simulated paper-attendance process. "Comparison with the Manual Attendance Process" would be a more specific alternative title. Other published implementations can be discussed in Comparison with Related Studies.

## A. Comparison with Existing Systems

### Before

Pages 52-54 contain an older subsection beginning:

> When compared with the traditional manual paper sign-in process, the proposed system achieved higher estimated throughput across all operational modes under the conditions of the evaluation. Physical stopwatch testing recorded a total elapsed time of 140.81 seconds for six individuals in the manual process. This resulted in an observed average transaction time of 23.47 seconds per student and an observed throughput of approximately 2.56 students per minute.

The remainder uses automated estimates of 2.50, 8.65, and 14.18 seconds, rates of 24.00, 6.94, and 4.23 students per minute, and throughput multipliers of 9.38, 2.71, and 1.66. Replace the entire old subsection with the following text. Do not replace the old rates while leaving the unsupported superiority claim intact.

### Final: 4.6.2 Comparison with Existing Systems

The study compared the proposed system with a simulated paper-based event-attendance process through observation of the available timing measurements and evaluation of student survey ratings. The manual observation involved 70 writing trials in which participants entered their name, course and section, student ID, and signature. Timing began with the first written character and ended when the required information was complete. The mean writing duration was 21.803 seconds, with a sample standard deviation of 4.436 seconds. Personnel did not check an identification card or enrollment status during the timed simulation.

The automated observation involved the same 70 participants testing Fast, Standard, and High-Security modes, producing 210 successful entry trials across modes. Mean internal system latency was 11.49 ms for Fast, 57.37 ms for Standard, and 54.06 ms for High-Security. Standard recorded a mean PIN interaction duration of 2.505 seconds, while High-Security recorded a mean combined PIN and QR interaction duration of 5.799 seconds. Fast bypassed PIN and QR, and its full physical interaction duration was not independently measured.

These measurements describe different portions of the two workflows. Manual writing duration excludes queue waiting and subsequent validation, while the automated component measurements do not independently cover the complete NFC-tap-to-finished-verification boundary. The observations also differ in task content: paper-based event-attendance writing versus automated entry and exit verification. Therefore, the results do not establish a matched complete-task processing-time difference, percentage speed improvement, or throughput multiplier. The automated recorded-entry rates of 21.98, 7.46, and 4.95 intervals per minute are supplementary log-derived measurements and must not be treated as directly comparable to the manual service-time-equivalent rate of 2.75 writing completions per minute.

The survey comparison provides a separate assessment of reported process quality. The manual group obtained an overall mean score of 2.284 (sample SD = 0.753), while the proposed-system group obtained 3.742 (sample SD = 0.290). Each group contained 70 different student respondents. A two-tailed Welch independent-samples t-test indicated a statistically significant difference, t(89.08) = 15.12, p < .001. The mean difference favoring the proposed system was 1.458 points, with a 95% confidence interval of [1.267, 1.650]. These findings indicate higher reported ratings among the students who evaluated the proposed system. They do not demonstrate a causal improvement attributable solely to the method, because the groups differed in demographic composition and their questionnaires used process-specific wording.

The comparison also identifies differences in the verification procedures. The timed manual simulation collected self-reported information without an identity or enrollment check. The prototype applied configured credential and student-record checks, PIN and QR validation when required, and entry-state rules. These capabilities demonstrate the operation of implemented controls under the tested conditions. The study did not independently quantify reductions in transcription errors, proxy attendance, fraudulent entry, or incorrect enrollment acceptance under comparable manual and automated conditions.

## B. System Strengths and Practical Advantages

### Before

Pages 55-56 contain a later, duplicate strengths subsection. Its numerical comparison begins:

> Furthermore, the system demonstrated a higher observed throughput than the traditional manual procedure under the conditions of the evaluation. The automated system achieved up to 24.00 students per minute in Fast Mode compared with 2.56 students per minute for the observed manual baseline. High-Security Mode achieved an estimated throughput of 4.23 students per minute despite requiring NFC, PIN, and QR verification.

Remove that unsupported comparison. Merge the useful configurable-mode and digital-record discussions with Practical Advantages and Their Limits on pages 50-51. Retain only one strengths subsection, using the following replacement.

### Final: 4.6.4 System Strengths and Practical Advantages

One strength of the prototype is its configurable verification workflow. Fast mode uses NFC validation, Standard adds PIN authentication, and High-Security adds QR credential validation. This configuration allows the operator to select the credential requirements appropriate to a gate or event. QR validation serves as an additional credential-integrity check rather than an independently established third authentication factor. The evaluation demonstrated the selected mode requirements; it did not establish automatic calculation of individual risk scores or automatic risk-based selection of a mode for each student.

The system also provides visible decisions, structured transaction records, and security-condition logging. The observation exports contained 438 records, comprising 210 successful entries, 210 granted reset exits, and 18 expected invalid-condition denials. The researcher-labelled credential-decision sample showed agreement with the expected outcomes for all 222 applicable recorded decisions. Deliberate wrong-PIN, triggered-lockout, and QR-mismatch attempts demonstrated credential-rule handling, while consecutive-entry and irregular-exit attempts demonstrated state-rule handling. These results support the operation of the tested controls, not universal authentication accuracy, complete protection against credential copying, or independent verification of each participant's real identity.

Role-specific monitoring and reporting interfaces provide practical support for security and event-administration tasks. Digital records can be reviewed and exported without relying solely on handwritten attendance sheets. The prototype's local storage and synchronization features also support its intended workflow during temporary connectivity interruptions. These are implementation capabilities; the study did not measure the amount of administrative labor saved or establish uninterrupted performance under all network conditions.

The respondent evaluations indicate favorable perceived quality within the study groups. Students, security personnel, and faculty/event-organizer/administrator respondents obtained overall means of 3.74, 3.63, and 3.81, respectively, all interpreted as Highly Satisfactory. The student-system group also reported significantly higher overall ratings than the separate manual group. These findings support perceived acceptability of the evaluated prototype. The small personnel samples, controlled observation setting, reused credential profiles, fixed mode order, and differences between the manual and system survey groups limit claims about full-scale campus effectiveness.

## C. Other Before and Final Corrections

### 1. Internal latency table, page 5

| Entry | Before | Final |
| --- | --- | --- |
| Fast mean internal latency | 11.4 ms | 11.49 ms |
| Standard mean internal latency | 57.3 ms | 57.37 ms |
| High-Security mean internal latency | 54.06 ms | Keep 54.06 ms |

### 2. Authentication table, page 9

| Entry | Before | Final |
| --- | --- | --- |
| Invalid-outcome heading | Expected-Invalid Grants | Expected-Invalid Denials |
| Total expected-valid grants | 70 | 210 |
| Total applicable/correct decisions | 222 / 222 | Keep 222 / 222 |

### 3. PIN-lockout table, page 11

Before, the 79-submission row is labelled Fast, the 73-submission row Standard, and the 152-submission total row High-Security. Replace the mode labels and add the applicable Fast row if retaining all modes:

| Mode | PIN Stages in Granted Entries | PIN_FAILURE | Triggered PIN_LOCKED | PIN Passed Then QR Denied | Total PIN Submissions | Lockout Rate |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Fast | 0 | 0 | 0 | 0 | 0 | N/A |
| Standard | 70 | 7 | 2 | 0 | 79 | 2.53% |
| High-Security | 70 | 1 | 0 | 2 | 73 | 0.00% |
| Total | 140 | 8 | 2 | 2 | 152 | 1.32% |

### 4. Copied captions, pages 13 and 15

| Page | Before | Final Caption |
| --- | --- | --- |
| 13 | Recorded Pin Submissions and Lockout Rate | Manual Paper Attendance Writing Duration |
| 15 | Recorded Pin Submissions and Lockout Rate | Available Timing Measurements and Measurement Boundaries |

Assign unique table numbers after finalizing the chapter's table sequence.

### 5. Misplaced processing-time paragraph, page 14

Before:

> The researcher confirmed that both PIN_LOCKED records resulted from deliberately reaching the third consecutive incorrect PIN submission...

Final:

> The manual observation measured active attendance-writing duration from the first written character to completion of the required information, with a mean of 21.803 seconds. The automated exports measured internal system latency and recorded PIN/QR interaction stages, but did not provide independently timed complete NFC-tap-to-finished-verification durations. The manual simulation concerned event-attendance writing, whereas the automated records identified entry and exit verification. The task content and timing boundaries therefore differ, so these measurements do not establish a directly comparable complete-task speed reduction.

In the manual observation procedure on page 12, retain or add: **Personnel did not check an identification card or enrollment status during the timed simulation.**

### 6. Administrator demographics, pages 18-19: confirmation required

Before: a five-person administrator-related group, but years-of-service counts of 2 + 2 = 4, reported as 50% + 50% without identifying the denominator.

Final cannot be selected without checking the forms:

- If a fifth answer was omitted from the tally, correct the category counts and percentages from that form.
- If only four answered, report valid n = 4 and one missing response. The two 50% figures then describe valid responses, not all five respondents.
- If percentages use the full n = 5, report 40%, 40%, and 20% missing, only when nonresponse is confirmed.
- Check the overlapping 11-15 and 15-20 response intervals against the original form. Do not reclassify responses without that evidence.

### 7. SD method, pages 20-22 and 27

Before:

> Item SD was calculated ... divided by N ... respondent-level category or overall-score SD cannot be reconstructed.

Final:

> Individual-item sample standard deviations were calculated using N - 1. Category-summary ranges show the minimum and maximum individual-item sample SDs, not a category-composite SD. Individual responses were available for the manual and student-system groups, permitting calculation of respondent-level category and overall scores and their sample SDs. Only item-frequency totals were available for security personnel and administrators, so respondent-level composite SDs were not reported for those groups.

Update Chapter 3's SD treatment to match this sample convention. Change table headings from **Item SD Range** to **Item Sample SD Range** and from **Item** to **Items** when the column contains question counts.

### 8. Student interpretation, page 22

| Entry | Before | Final |
| --- | --- | --- |
| Overall student mean | 3.76 | 3.74 |
| Functional Suitability | 3.77 | 3.76 |
| Performance Efficiency | 3.76 | 3.74 |
| Usability | 3.75 | 3.74 |
| Reliability | 3.73 | 3.71 |
| Security | 3.79 | 3.75 |
| Identity-confirmation item | 3.90 | 3.84 |
| Lowest item mean, unexpected failures/freezing | 3.66 | 3.60 |

Final paragraph:

> Students rated the system Highly Satisfactory in all five categories, with an overall equal-category mean of 3.74. Functional Suitability received the highest category mean of 3.76, followed by Security at 3.75. Performance Efficiency and Usability each recorded 3.74, while Reliability recorded 3.71. At item level, the identity-confirmation statement received 3.84, while the lowest item mean was 3.60 for processing without unexpected failures or freezing. These ratings describe perceived quality and do not independently establish resistance to credential copying or impersonation.

### 9. Administrator summary and manual heading, pages 32-33 and 40

| Entry | Before | Final |
| --- | --- | --- |
| Administrator Performance Efficiency item count | 4 | 5 |
| Administrator Compatibility item count | 5 | 4 |
| Administrator overall mean in category summary | 3.63 | 3.81 |
| Administrator overall mean in detailed all-category row | 3.8 | 3.81, using all eight categories and two-decimal reporting |
| Administrator total items | 39 | Keep 39 |
| Manual group heading | n=7, truncated | n = 70 |
| Manual overall row label | OVERALL SYSTEM WEIGHTED MEAN | Overall Manual-Process Mean |

### 10. Category/overall SD rows, pages 23-46

Before: the SD values in Overall Weighted Mean and SD rows average individual-item SDs. Examples are 0.47 for the overall student score and 1.01 for the overall manual score.

Final: preserve each legitimate individual-item SD. In category/overall rows, use respondent-level sample SDs where available, or omit the SD to avoid repeating the comparison table.

| Category | Student-System Composite Sample SD | Manual Composite Sample SD |
| --- | ---: | ---: |
| Functional Suitability | 0.318 | 0.899 |
| Performance Efficiency | 0.366 | 0.919 |
| Usability | 0.329 | 0.854 |
| Reliability | 0.348 | 0.852 |
| Security | 0.341 | 0.883 |
| Overall equal-category score | 0.290 | 0.753 |

For security personnel and administrators, use a dash with an explanatory note in a composite-SD cell unless individual responses become available. Do not substitute an average of item SDs as the respondent-level SD.

### 11. T-test table, page 48

Before: a t-test table without a unique numbered caption.

Final caption: **Welch Independent-Samples t-Test of Overall Survey Ratings**, with a unique table number in the final sequence. Keep t = 15.124, df = 89.076, p < .001, difference = 1.458, and 95% CI [1.267, 1.650].

### 12. Table and figure numbering

Before: repeated table numbers for unrelated tables and Figure 20 used for both system timing and the survey comparison.

Final: assign each table and figure one unique number in order of appearance and update all in-text references. Do this after inserting or restoring any missing functionality-test results. Do not assume the previous review's example table numbers still apply to this differently formatted document.

### 13. User-evaluation numbering

| Before | Final |
| --- | --- |
| 4.5.1 Scoring and Interpretation | 4.5.1 Scoring and Interpretation |
| 4.5.1 Student Evaluation | 4.5.2 Student Evaluation |
| 4.5.2 Security Personnel Evaluation | 4.5.3 Security Personnel Evaluation |
| 4.5.3 Faculty/Event Organizer/Admin Evaluation | 4.5.4 Faculty/Event Organizer/Admin Evaluation |
| 4.5.4 Manual Process Pre-Evaluation | 4.5.5 Manual Process Pre-Evaluation |
| 4.5.5 Manual/System Comparison | 4.5.6 Manual/System Comparison |

Page 2 also jumps from 4.1 directly to 4.3.2. Restore missing parent sections or renumber the retained structure consecutively. Include the documented functionality-test outcomes or a precise appendix reference if those outcomes were intentionally relocated; do not introduce invented test results to fill numbering gaps.

### 14. Discussion and summary placement

Before: two competing discussion sequences, a second 4.6 heading for system-performance interpretation, and 4.7 Summary followed by additional 4.6 subsections.

Final: use the single discussion sequence specified at the top of this document. Merge the earlier manual-measurement discussion into the new comparison section. Merge the two strengths/practical-advantages discussions. Put the summary after related studies and all other Chapter 4 discussion, not in the middle.

### 15. Methodological limitations: final paragraph

> The survey comparison involved different student groups. The manual group comprised Computer Science students in the second and fourth years, whereas the system group included Computer Science and Information System students in the first, third, and fourth years. These differences, together with process-specific questionnaire wording, limit attribution of the rating difference solely to the attendance method. The observation also used a fixed verification-mode order, twenty reused credential profiles, researcher-labelled expected outcomes, and interspersed demonstrations. Complete automated task boundaries comparable to manual writing were not independently measured. Security-personnel and administrative groups each contained five respondents, and their respondent-level composite SDs could not be calculated from the available item-frequency totals. The findings therefore describe the observed prototype conditions and respondent evaluations rather than full-scale campus effectiveness or a causal processing-time improvement.

### 16. Related-study comparison: final replacement for the numerical overall comparison

> The proposed system shares the use of automated credential capture and digital record management with the attendance and verification approaches discussed in Chapter 2. Its implemented workflow combines NFC capture, PIN authentication, QR credential-integrity validation, configured eligibility checks, and entry/exit state handling. The observed component timings and demonstrated rule outcomes describe this prototype's operation, but the study does not provide complete-task measurements comparable with the timing boundaries used in the cited studies. Consequently, no numerical claim that this prototype replicated or exceeded their processing performance is made. The comparison concerns system scope and verification mechanisms, with each literature-specific finding interpreted according to its own study design and evidence.

Keep original citations beside the specific literature claims they actually support. Check those papers before retaining their reported numerical findings. Do not describe operator-configured verification modes as equivalent to automatically computed adaptive risk scores.

### 17. Layout

Before: split summary rows, some questionnaire rows split across pages, detached captions/headers, a truncated n = 70 label, and visible red spell-check marks in pasted table images.

Final: repeat headers on continued tables, keep individual rows together where feasible, keep captions with the relevant table, remove spell-check marks, and use consistent numeric precision. The final Summary should be the final Chapter 4 section.

## D. Results to Retain

- Manual observation: 70 writing records; total 1,526.24 seconds; mean 21.803 seconds; median 21.135 seconds; sample SD 4.436 seconds; range 13.21-37.63 seconds.
- Corrected manual item/category means and individual-item sample SDs on pages 40-46.
- Corrected respondent-level comparison on page 46 and chart on page 47.
- Correct survey t-test and its limitations on page 48.
- Current 438-record system observation and correctly defined 222 credential decisions and 152 PIN submissions, after correcting the table labels/counts identified above.

This document supplies replacement text, not verification of unprovided demographic forms, universal hardware behavior, or external publication claims. The source PDF itself remains unchanged.
