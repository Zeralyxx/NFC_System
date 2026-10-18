# Chapter 4: Full Replacement Text and Corrections

Source reviewed: `C:\Users\justine mendoza\Documents\METRICS first batch\Chapter 4 Thesis Update (1).pdf`.

This copy contains the full 232-page thesis. All page references below refer to the page numbers printed in this current copy, before these edits. Chapter 4 currently occupies pages 106-166. The source PDF has not been edited.

This document supplies the complete replacement text for the portions that still require changes. Sections explicitly marked KEEP do not need to be rewritten. The observation is described directly; it is not called a revised observation.

## 1. Chapter Opening and Prototype Summary: Page 106

Replace the chapter introduction and the entire existing 4.1 paragraph, including the parenthetical Appendix E statement, with the following:

### CHAPTER 4: RESULTS AND DISCUSSION

This chapter presents the implementation, documented functionality tests, system-observation results, manual paper-attendance baseline, and user evaluations of the Risk-Based Two-Factor Student Verification and Event Attendance Management System. The findings distinguish measured internal processing and interaction durations from complete-task duration, and distinguish respondent perceptions from objectively demonstrated security and performance outcomes. The comparison with the manual process uses the available observation data descriptively and an independent-samples t-test of the overall student survey scores.

### 4.1 Prototype Implementation Summary

The prototype combines NFC credential capture with a C# WinUI 3 application for student verification and event-attendance management. An ESP32 microcontroller and PN532 NFC reader provide NFC capture through a USB serial connection to the computer. PIN entry and camera-based QR scanning are handled by the application. Fast mode requires NFC validation, Standard mode adds PIN authentication, and High-Security mode adds QR credential validation. Verification requirements are selected through the configured operating mode rather than an automatically calculated individual risk score.

The application uses local MySQL storage and Firebase Firestore synchronization to support its record-management workflow. Its interfaces present verification decisions, student information, transaction records, and security alerts. Relevant program code is included in Appendix E, while the documented functionality test cases and results are presented in Appendix H.

Editorial check: This wording distinguishes the serial-connected NFC hardware from application PIN entry and Windows camera capture. Confirm that it matches the physical setup used during the observation; do not imply that the webcam sends its video through the ESP32 serial connection.

## 2. Add the Missing Functionality Section: After 4.1

Appendix H, pages 218-219, already contains 16 test cases and their actual outcomes. Insert this summary before the system-observation section. Do not invent additional tests or present these 16 cases as 16 independent statistical trials.

### 4.2 Functionality Testing Results

Functionality testing examined the prototype's configured verification modes, invalid-credential handling, student eligibility checks, restricted-event registration, entry/exit state rules, and selected offline and synchronization functions. Appendix H presents the input or action, expected result, actual result, and status of each documented test case. Table 3 summarizes these cases by function.

**Table 3. Summary of Documented Functionality Test Results**

| Function Tested | Appendix H Cases | Number of Listed Cases | Documented Result |
| --- | --- | ---: | --- |
| Valid verification in Fast, Standard, and High-Security modes | T-01, T-02, T-05 | 3 | PASS for all listed cases |
| Incorrect-PIN handling and third-attempt lockout | T-03, T-04 | 2 | PASS for all listed cases |
| QR credential mismatch handling | T-06 | 1 | PASS |
| Unknown credentials and inactive or expelled student status | T-07, T-08, T-09 | 3 | PASS for all listed cases |
| Restricted-event registration validation | T-10, T-11 | 2 | PASS for all listed cases |
| Consecutive-entry rejection and normal entry/exit state updates | T-12, T-13 | 2 | PASS for all listed cases |
| Local verification during disconnection and synchronization after reconnection | T-14, T-15 | 2 | PASS for all listed cases |
| Controlled application closure with pending offline records | T-16 | 1 | PASS |
| **Total** | **T-01 to T-16** | **16** | **All listed cases marked PASS** |

The documented outcomes show that the expected responses occurred in the listed test conditions. Incorrect PINs produced the corresponding prompts and lockout response, mismatched QR credentials were denied, and configured eligibility and event-registration rules were enforced. The state-rule tests documented rejection of a repeated entry while the record remained INSIDE and successful state changes during the normal entry/exit sequence. The offline cases documented local verification, synchronization following reconnection, and an administrator-PIN requirement for the tested closure condition.

These results describe the listed functionality tests, not an exhaustive assessment of reliability or resistance to every attack. In particular, a QR-mismatch test demonstrates the implemented matching rule, not detection of every possible credential copy. Consecutive-entry rejection demonstrates a logical entry-state control and should not be treated as independent proof that a person cannot physically follow another person through a gate. The listed case outcomes are separate from the exported observation records analyzed in Section 4.3.

## 3. Final Section Structure

Apply these headings in order. Keep the accurate existing observation text under the renamed headings.

| Final Heading | Action on Current Text |
| --- | --- |
| 4.1 Prototype Implementation Summary | Use replacement in Section 1 of this document |
| 4.2 Functionality Testing Results | Insert replacement in Section 2 |
| 4.3 System Observation and Performance | Add this parent heading before the observation subsections |
| 4.3.1 System Metrics and Performance | Rename current 4.3.2, page 106 |
| 4.3.2 Authentication and Security Metrics | Rename current 4.3.3 |
| 4.3.3 Manual Paper-Attendance Baseline | Rename current 4.3.4 |
| 4.3.4 Descriptive Comparison Addressing SOP 6 | Rename current 4.3.5 |
| 4.3.5 Summary of Measured Results | Rename current 4.3.6 |
| 4.4 Demographic Profile of Respondents | Retain; apply demographic correction below |
| 4.5 User Evaluation Results | Retain parent heading |
| 4.5.1 Scoring and Interpretation | Retain this subsection number |
| 4.5.2 Student Evaluation (n = 70) | Rename the second current 4.5.1, page 125 |
| 4.5.3 Security Personnel Evaluation (n = 5) | Renumber current security section |
| 4.5.4 Faculty/Event Organizer/Administrator Evaluation (n = 5) | Renumber current administrative section |
| 4.5.5 Manual Process Student Pre-Evaluation (n = 70) | Replace truncated heading on page 145 |
| 4.5.6 Comparison of Manual and Proposed-System Survey Ratings | Renumber comparison; keep descriptive results and Welch computation |
| 4.6 Discussion | Retain parent heading |
| 4.6.1 Interpretation of System Performance | Rename duplicate 4.6 heading on page 154 |
| 4.6.2 Comparison with the Manual Attendance Process | Merge both existing manual-comparison subsections; use replacement below |
| 4.6.3 Interpretation of Security Outcomes | Retain current text on pages 155-156 |
| 4.6.4 System Strengths and Practical Advantages | Merge the two practical-advantage subsections; use replacement below |
| 4.6.5 Implications of the Study | Rename current 4.6.4 on pages 160-162; KEEP its text |
| 4.6.6 Comparison with Related Studies | Rename current 4.6.5 on pages 162-165; KEEP its text, including Overall Comparison |
| 4.7 Summary | Retain existing summary at the end, pages 165-166 |

## 4. Demographic Correction: Pages 122-123

The user confirmed that the event organizer was a member of a student organization and did not supply years of service. The role distribution still uses all five respondents. Years-of-service percentages must use the four valid answers, not imply that all five answered.

The current service intervals, 11-15 and 15-20 years, overlap at 15. Without the original questionnaire choices, do not silently relabel the second interval as 16-20 or reassign an answer. For a clean final table, combine the two reported service groups into 11-20 years. This preserves all four known responses without inventing a boundary.

Replace only the Faculty/Event Organizer/Administrator portion of the demographic table with:

| Role/Position (n = 5) | Frequency | Percentage |
| --- | ---: | ---: |
| Faculty | 1 | 20.00% |
| Event Organizer | 1 | 20.00% |
| Administrator | 3 | 60.00% |
| **Total** | **5** | **100.00%** |

| Years of Service (Valid n = 4) | Frequency | Percentage of Valid Responses |
| --- | ---: | ---: |
| 0-5 years | 0 | 0.00% |
| 6-10 years | 0 | 0.00% |
| 11-20 years | 4 | 100.00% |
| **Valid responses** | **4** | **100.00%** |

**Note.** One of the five respondents did not report years of service. This respondent was the event organizer, who participated as a member of a student organization. Years-of-service percentages are based on the four valid responses. The 11-20-year group combines the two upper service categories reported in the original tally.

Replace the corresponding administrative demographic paragraph on page 123 with:

Among the Faculty/Event Organizer/Administrator respondents, three were administrators (60.00%), one was a faculty member (20.00%), and one was an event organizer (20.00%). Four respondents supplied years-of-service information, and all four reported service within the combined 11-20-year range. The event organizer, who belonged to a student organization, did not provide a years-of-service response. Therefore, the years-of-service percentages were calculated using four valid responses rather than the full group of five.

If the original form confirms mutually exclusive upper categories, the separate two-and-two tally may instead be retained with 50.00% each, valid n = 4, and the same missing-response note. Do not use an unverified 16-20 label.

## 5. Student Evaluation: Pages 125-126

Replace the student category summary, its note, and its interpretation with:

### 4.5.2 Student Evaluation (n = 70)

**Table 16. Student-System Evaluation Category Results**

| Category | Items | Mean | Item Sample SD Range | Interpretation |
| --- | ---: | ---: | --- | --- |
| Functional Suitability | 5 | 3.76 | 0.35-0.54 | Strongly Agree / Highly Satisfactory |
| Performance Efficiency | 4 | 3.74 | 0.42-0.49 | Strongly Agree / Highly Satisfactory |
| Usability | 5 | 3.74 | 0.42-0.55 | Strongly Agree / Highly Satisfactory |
| Reliability | 5 | 3.71 | 0.42-0.49 | Strongly Agree / Highly Satisfactory |
| Security | 5 | 3.75 | 0.37-0.58 | Strongly Agree / Highly Satisfactory |
| **Overall (equal category weights)** | **24** | **3.74** | **See note** | **Strongly Agree / Highly Satisfactory** |

**Note.** The SD range gives the minimum and maximum individual-item sample standard deviations within each category, using N - 1 in the denominator. It is not a category-composite SD. Overall scores give equal weight to the five categories. Respondent-level category and overall sample SDs are reported in the manual/system comparison table. Detailed category and overall footer rows report composite sample SDs, not averages of item SDs.

Students rated the system Highly Satisfactory in all five categories, with an overall equal-category mean of 3.74. Functional Suitability received the highest category mean of 3.76, followed by Security at 3.75. Performance Efficiency and Usability each recorded 3.74, while Reliability recorded 3.71. These results indicate favorable perceptions of the verification experience across the evaluated categories.

At item level, the identity-confirmation statement in Security received a mean of 3.84. The lowest student item mean was 3.60 for processing without unexpected failures or freezing. All student item means remained within the Strongly Agree range. The ratings support perceived acceptability within the evaluation setting; confidence in identity confirmation and protection against credential copying does not independently establish resistance to impersonation or cloning.

Remove the old interpretation using overall 3.76, Security 3.79, Functional Suitability 3.77, Performance Efficiency 3.76, Usability 3.75, Reliability 3.73, identity confirmation 3.90, and the lowest item 3.66. Those figures do not match the corrected response data.

## 6. Security Personnel Evaluation: Pages 130-131

The category means and current item SD ranges are correct. Use this complete summary and corrected note; retain the existing accurate interpretation.

### 4.5.3 Security Personnel Evaluation (n = 5)

**Table 22. Security Personnel Evaluation Category Results**

| Category | Items | Mean | Item Sample SD Range | Interpretation |
| --- | ---: | ---: | --- | --- |
| Functional Suitability | 5 | 3.84 | 0.00-0.55 | Strongly Agree / Highly Satisfactory |
| Performance Efficiency | 4 | 3.45 | 0.00-0.71 | Strongly Agree / Highly Satisfactory |
| Usability | 5 | 3.60 | 0.45-0.84 | Strongly Agree / Highly Satisfactory |
| Reliability | 5 | 3.48 | 0.45-0.55 | Strongly Agree / Highly Satisfactory |
| Security | 5 | 3.80 | 0.00-0.55 | Strongly Agree / Highly Satisfactory |
| **Overall (equal category weights)** | **24** | **3.63** | **See note** | **Strongly Agree / Highly Satisfactory** |

**Note.** The SD range gives the minimum and maximum individual-item sample standard deviations within each category, using N - 1 in the denominator. It is not a category-composite SD. Overall scores give equal weight to the five categories. Respondent-level category and overall composite SDs were not calculated because only item-frequency totals were available for this group. A dash in a composite-SD footer means that the statistic is unavailable, not zero.

Keep legitimate item SDs such as 0.55. Do not replace them with population SDs such as 0.49. The sample-SD convention must remain consistent throughout the chapter.

## 7. Faculty/Event Organizer/Administrator Evaluation: Pages 136-137

Replace the category summary with:

### 4.5.4 Faculty/Event Organizer/Administrator Evaluation (n = 5)

**Table 28. Faculty/Event Organizer/Administrator Evaluation Category Results**

| Category | Items | Mean | Item Sample SD Range | Interpretation |
| --- | ---: | ---: | --- | --- |
| Functional Suitability | 5 | 3.80 | 0.00-0.55 | Strongly Agree / Highly Satisfactory |
| Performance Efficiency | 5 | 3.68 | 0.00-0.89 | Strongly Agree / Highly Satisfactory |
| Compatibility | 4 | 3.75 | 0.00-0.55 | Strongly Agree / Highly Satisfactory |
| Usability | 5 | 3.88 | 0.00-0.45 | Strongly Agree / Highly Satisfactory |
| Reliability | 5 | 3.76 | 0.00-0.55 | Strongly Agree / Highly Satisfactory |
| Security | 5 | 3.84 | 0.00-0.55 | Strongly Agree / Highly Satisfactory |
| Maintainability | 5 | 3.88 | 0.00-0.55 | Strongly Agree / Highly Satisfactory |
| Portability | 5 | 3.92 | 0.00-0.45 | Strongly Agree / Highly Satisfactory |
| **Overall (equal category weights)** | **39** | **3.81** | **See note** | **Strongly Agree / Highly Satisfactory** |

**Note.** The SD range gives the minimum and maximum individual-item sample standard deviations within each category, using N - 1 in the denominator. It is not a category-composite SD. Overall scores give equal weight to the eight categories. Respondent-level category and overall composite SDs were not calculated because only item-frequency totals were available for this group. A dash in a composite-SD footer means that the statistic is unavailable, not zero.

The faculty, event-organizer, and administrative group gave an overall equal-category mean of 3.81, interpreted as Highly Satisfactory. Portability received the highest category mean at 3.92. Usability and Maintainability each received 3.88, followed by Security at 3.84, Functional Suitability at 3.80, Reliability at 3.76, Compatibility at 3.75, and Performance Efficiency at 3.68.

The lowest item mean, 3.40, concerned maintaining dashboard performance while processing or storing large amounts of attendance data. All administrative item means remained in the Strongly Agree range. The favorable maintainability and compatibility ratings are reported as respondent assessments; the supplied tallies do not establish the extent of source-code review, technical expertise, or cross-platform testing undertaken by each respondent.

Specific corrections: Performance Efficiency has 5 items, Compatibility has 4, total items remain 39, and the overall mean is 3.81 rather than 3.63. The overall detailed-table footer on page 145 must also display 3.81 rather than 3.8.

## 8. Manual Student Pre-Evaluation: Pages 145-146

Replace the truncated heading and use this complete category summary. Its means do not need recalculation.

### 4.5.5 Manual Process Student Pre-Evaluation (n = 70)

**Table 37. Student Manual Sign-in Evaluation Category Results**

| Category | Items | Mean | Item Sample SD Range | Interpretation |
| --- | ---: | ---: | --- | --- |
| Functional Suitability | 5 | 2.48 | 1.02-1.07 | Disagree / Unsatisfactory |
| Performance Efficiency | 4 | 1.93 | 0.98-1.03 | Disagree / Unsatisfactory |
| Usability | 5 | 2.64 | 0.94-1.07 | Agree / Satisfactory |
| Reliability | 5 | 2.18 | 0.92-1.03 | Disagree / Unsatisfactory |
| Security | 5 | 2.18 | 0.98-1.04 | Disagree / Unsatisfactory |
| **Overall (equal category weights)** | **24** | **2.28** | **See note** | **Disagree / Unsatisfactory** |

**Note.** The SD range gives the minimum and maximum individual-item sample standard deviations within each category, using N - 1 in the denominator. It is not a category-composite SD. Overall scores give equal weight to the five categories. Respondent-level category and overall sample SDs are reported in the manual/system comparison table. Detailed category and overall footer rows report composite sample SDs, not averages of item SDs.

The manual pre-evaluation produced an overall equal-category mean of 2.28, interpreted as Disagree / Unsatisfactory. Usability received 2.64, interpreted as Agree / Satisfactory. Functional Suitability received 2.48, while Reliability and Security each received 2.18 and Performance Efficiency received 1.93; these four categories were interpreted as Disagree / Unsatisfactory. These results describe the manual group's perceptions of the paper-based process and should not be substituted for directly observed error, fraud, or processing-time outcomes.

## 9. Replace Detailed-Table Summary Footers: Pages 127-150

Do not replace any respondent's answer or any correct individual-item frequency, mean, or sample SD. Change only the category/overall footer cells listed below. An average of item SDs is not the SD of respondents' category or overall scores.

Use footer labels **Category Mean and Composite Sample SD** and **Overall Mean and Composite Sample SD (Equal Category Weights)**. Individual items may remain at two decimal places; composite SDs below are shown at three decimal places, consistent with the comparison table.

### Student-System Footer Replacements

| Current Page | Footer Category | Mean | Composite Sample SD | Interpretation |
| --- | --- | ---: | ---: | --- |
| 127 | Functional Suitability | 3.76 | 0.318 | Strongly Agree / Highly Satisfactory |
| 128 | Performance Efficiency | 3.74 | 0.366 | Strongly Agree / Highly Satisfactory |
| 129 | Usability | 3.74 | 0.329 | Strongly Agree / Highly Satisfactory |
| 129 | Reliability | 3.71 | 0.348 | Strongly Agree / Highly Satisfactory |
| 130 | Security | 3.75 | 0.341 | Strongly Agree / Highly Satisfactory |
| 130 | Overall | 3.74 | 0.290 | Strongly Agree / Highly Satisfactory |

The old student overall SD of 0.47 must be replaced by 0.290.

### Security-Personnel Footer Replacements

| Current Page | Footer Category | Mean | Composite Sample SD | Interpretation |
| --- | --- | ---: | --- | --- |
| 132 | Functional Suitability | 3.84 | - | Strongly Agree / Highly Satisfactory |
| 133 | Performance Efficiency | 3.45 | - | Strongly Agree / Highly Satisfactory |
| 134 | Usability | 3.60 | - | Strongly Agree / Highly Satisfactory |
| 135 | Reliability | 3.48 | - | Strongly Agree / Highly Satisfactory |
| 136 | Security | 3.80 | - | Strongly Agree / Highly Satisfactory |
| 136 | Overall | 3.63 | - | Strongly Agree / Highly Satisfactory |

Remove the old overall SD of 0.42. Add this note beneath each table containing a composite footer: **Composite sample SD is not reported because individual respondent-level category scores were unavailable; the item SDs above remain individual-item sample SDs.**

### Faculty/Event Organizer/Administrator Footer Replacements

| Current Page | Footer Category | Mean | Composite Sample SD | Interpretation |
| --- | --- | ---: | --- | --- |
| 138 | Functional Suitability | 3.80 | - | Strongly Agree / Highly Satisfactory |
| 139 | Performance Efficiency | 3.68 | - | Strongly Agree / Highly Satisfactory |
| 140 | Compatibility | 3.75 | - | Strongly Agree / Highly Satisfactory |
| 141 | Usability | 3.88 | - | Strongly Agree / Highly Satisfactory |
| 142 | Reliability | 3.76 | - | Strongly Agree / Highly Satisfactory |
| 143 | Security | 3.84 | - | Strongly Agree / Highly Satisfactory |
| 144 | Maintainability | 3.88 | - | Strongly Agree / Highly Satisfactory |
| 145 | Portability | 3.92 | - | Strongly Agree / Highly Satisfactory |
| 145 | Overall | 3.81 | - | Strongly Agree / Highly Satisfactory |

Remove the old overall SD of 0.32. Use the same composite-SD-unavailable note as for security personnel.

### Manual Student Footer Replacements

| Current Page | Footer Category | Mean | Composite Sample SD | Interpretation |
| --- | --- | ---: | ---: | --- |
| 147 | Functional Suitability | 2.48 | 0.899 | Disagree / Unsatisfactory |
| 147 | Performance Efficiency | 1.93 | 0.919 | Disagree / Unsatisfactory |
| 148 | Usability | 2.64 | 0.854 | Agree / Satisfactory |
| 149 | Reliability | 2.18 | 0.852 | Disagree / Unsatisfactory |
| 150 | Security | 2.18 | 0.883 | Disagree / Unsatisfactory |
| 150 | Overall | 2.28 | 0.753 | Disagree / Unsatisfactory |

The old manual overall SD of 1.01 must be replaced by 0.753.

## 10. Survey Comparison and T-Test: Pages 150-154

KEEP the current respondent-level descriptive comparison and worked Welch independent-samples t-test. The corrected raw respondent scores support the figures already used there. Change its subsection number to 4.5.6 and its captions as listed in Section 13 below.

For clarity, the final descriptive comparison is:

**Table 43. Descriptive Comparison of Manual and Proposed-System Survey Scores**

| Score | Manual Mean | Manual Sample SD | System Mean | System Sample SD |
| --- | ---: | ---: | ---: | ---: |
| Functional Suitability | 2.483 | 0.899 | 3.763 | 0.318 |
| Performance Efficiency | 1.932 | 0.919 | 3.743 | 0.366 |
| Usability | 2.637 | 0.854 | 3.740 | 0.329 |
| Reliability | 2.183 | 0.852 | 3.711 | 0.348 |
| Security | 2.183 | 0.883 | 3.751 | 0.341 |
| Overall equal-category score | 2.284 | 0.753 | 3.742 | 0.290 |

**Note.** Each group contains 70 different student respondents. Scores were calculated for each respondent before group means and sample SDs were calculated. Overall scores give equal weight to the five categories. The inferential test below concerns the overall score; category means are descriptive results, not separately tested hypotheses.

The final inferential table is:

**Table 45. Welch Independent-Samples t-Test of Overall Survey Ratings**

| Comparison | Mean Difference (System - Manual) | t | Welch df | Two-Tailed p | 95% CI of Difference | Decision at alpha = .05 |
| --- | ---: | ---: | ---: | --- | --- | --- |
| Overall survey score | 1.458 | 15.124 | 89.076 | < .001 | [1.267, 1.650] | Reject H0 |

Keep the explanation that the final results use unrounded respondent scores. The worked example uses rounded printed inputs and therefore reproduces t approximately 15.12 rather than every final decimal. Do not label p as 0.000.

Do not restore the old spreadsheet values 3.771/0.316 for Functional Suitability or 3.743/0.291 for the overall system score. They resulted from the previously identified calculated-cell discrepancy, not the corrected raw-response calculations.

## 11. Full Merged Manual-Comparison Discussion: Pages 155 and 157-159

Remove the separate existing subsection **Interpretation of Manual and Automated Measurements** on page 155 and the duplicate-numbered **Comparison with the Manual Attendance Process** on pages 157-159. Replace them with this single subsection, positioned after 4.6.1 and before 4.6.3:

### 4.6.2 Comparison with the Manual Attendance Process

The study compared the proposed system with a simulated paper-based event-attendance process through descriptive examination of the available observation measurements and an independent-groups comparison of student survey scores. The manual observation contained 70 writing trials in which participants supplied their name, course and section, student ID, and signature. Timing began with the first written character and ended when the required information was complete. Mean writing duration was 21.803 seconds, with a sample standard deviation of 4.436 seconds. Personnel did not check an identification card or enrollment status during the timed simulation.

In the automated observation, the same 70 participants tested Fast, Standard, and High-Security modes, producing 210 successful entry trials across the three modes. Mean internal system latency was 11.49 ms for Fast, 57.37 ms for Standard, and 54.06 ms for High-Security. Standard recorded a mean PIN interaction duration of 2.505 seconds, while High-Security recorded a mean combined PIN and QR interaction duration of 5.799 seconds. Fast bypassed PIN and QR, and its complete physical interaction duration was not independently measured. The shared participants across modes should not be interpreted as 210 different individuals.

These measurements cover different portions of the workflows. Manual writing duration excludes queue waiting, identity checking, and subsequent encoding. The automated component timers do not independently measure the complete NFC-tap-to-finished-verification interval. The procedures also differ in task content: handwritten event-attendance information versus automated entry verification. Consequently, the available observations do not establish a matched complete-task processing-time difference, percentage speed improvement, or throughput multiplier. No processing-time t-test was performed on these non-equivalent durations.

The recorded-entry rates of 21.98, 7.46, and 4.95 intervals per minute for Fast, Standard, and High-Security are supplementary log-derived measures of spacing between successful entries within the observation groups. They are not directly comparable to the manual service-time-equivalent rate of 2.75 writing completions per minute, which was derived from mean writing duration rather than a continuously observed queue. A complete-task benchmark would require a common task outcome and comparable timing boundaries for both methods.

The survey comparison provides a separate assessment of reported process quality. The manual group obtained an overall mean score of 2.284 (sample SD = 0.753), while the proposed-system group obtained 3.742 (sample SD = 0.290). Each group contained 70 different student respondents. A two-tailed Welch independent-samples t-test indicated a statistically significant difference, t(89.08) = 15.12, p < .001. The mean difference favoring the proposed system was 1.458 points, with a 95% confidence interval of [1.267, 1.650]. The manual group's lower Performance Efficiency mean of 1.93 and the system group's mean of 3.74 provide descriptive evidence of different perceived efficiency, not an objective complete-task timing result.

The survey findings indicate higher reported ratings among the students who evaluated the proposed system. They do not establish a causal improvement attributable solely to the attendance method, because the groups differed in demographic composition and the questionnaires used process-specific wording. The observed manual simulation collected self-reported information without an identity or enrollment check, whereas the prototype applied configured credential, student-record, and entry-state checks. These procedural differences identify implemented controls but do not quantify reductions in transcription errors, proxy attendance, fraudulent entry, or incorrect enrollment acceptance under matched manual and automated conditions.

## 12. Full Merged Strengths Discussion: Pages 156-157 and 159-160

Remove the separate subsection **Practical Advantages and Their Limits** on pages 156-157 and the duplicate-numbered **System Strengths and Practical Advantages** on pages 159-160. Replace them with the following, positioned after 4.6.3:

### 4.6.4 System Strengths and Practical Advantages

One strength of the prototype is its configurable verification workflow. Fast mode uses NFC validation, Standard adds PIN authentication, and High-Security adds QR credential validation. This configuration allows the operator to select verification requirements for the intended gate or event conditions. QR matching serves as an additional credential-integrity check rather than an independently established third authentication factor. The evaluation demonstrated the selected mode requirements; it did not demonstrate automatic calculation of individual risk scores or automatic selection of a mode for each student.

The system also provides visible verification decisions, structured transaction records, and security-condition logging. The observation exports contained 438 records, comprising 210 successful entries, 210 granted reset exits, and 18 expected invalid-condition denials. The researcher-labelled credential-decision sample agreed with the expected outcomes for all 222 applicable recorded decisions. Deliberate wrong-PIN, triggered-lockout, and QR-mismatch attempts demonstrated credential-rule handling, while consecutive-entry and irregular-exit attempts demonstrated state-rule handling. Appendix H additionally documents the listed functionality tests, including configured student-status and restricted-event registration checks. These findings support the operation of the tested controls, not universal authentication accuracy, complete prevention of credential copying, or independent verification of every participant's physical identity.

Role-specific monitoring and reporting interfaces support security and event-administration tasks by making digital records available for review and export. The local storage and synchronization workflow also supports operation during temporary connectivity interruptions. Appendix H documents local verification during a disconnection test and record synchronization after reconnection. These are demonstrated case outcomes and implementation capabilities; the study did not quantify administrative labor savings or establish uninterrupted performance under all network conditions or prolonged outages.

Respondent evaluations indicate favorable perceived quality within the study groups. Students, security personnel, and faculty/event-organizer/administrator respondents obtained overall means of 3.74, 3.63, and 3.81, respectively, all interpreted as Highly Satisfactory. The separate manual group obtained an overall mean of 2.28, interpreted as Unsatisfactory. The student-system group reported significantly higher overall ratings than the manual group, while the comparison remained a test of reported survey scores rather than objective task performance.

The relatively lower security-personnel ratings for processing speed and dashboard reliability identify areas for improvement despite the favorable overall evaluation. Endorsement of identity confirmation, credential protection, compatibility, and maintainability should be interpreted within respondents' exposure to those features. The small personnel samples, controlled observation setting, reused credential profiles, fixed mode order, and differences between the manual and system survey groups limit claims about full-scale campus effectiveness. Technical certification, reduced fraud incidence, physical tailgating prevention, and large-scale operational reliability require evidence beyond agreement ratings and the listed controlled test cases.

KEEP the current Implications of the Study text on pages 160-162, but renumber it 4.6.5. KEEP the current Comparison with Related Studies text on pages 162-165, but renumber it 4.6.6. KEEP the Summary on pages 165-166 as 4.7. Their current qualifications and principal numerical results are consistent with the corrected findings.

## 13. Complete Table and Figure Numbering Map

The current full thesis has Table 1 and Table 2 before Chapter 4. The new functionality summary fills Table 3. Keep the existing observation Tables 4-14, then continue sequentially as follows. These page references identify current locations and will shift after insertion and consolidation.

| Current Page | Table Content | Final Number |
| --- | --- | ---: |
| After page 106 prototype summary | Documented functionality test summary | 3 |
| 107-121 | Existing observation and manual-baseline Tables 4-14 | 4-14, unchanged |
| 122 | Demographic profile of respondents | 15 |
| 125-126 | Student-system category summary | 16 |
| 127 | Student Functional Suitability | 17 |
| 128 | Student Performance Efficiency | 18 |
| 129 | Student Usability | 19 |
| 129 | Student Reliability | 20 |
| 130 | Student Security | 21 |
| 130-131 | Security-personnel category summary | 22 |
| 132 | Security-personnel Functional Suitability | 23 |
| 133 | Security-personnel Performance Efficiency | 24 |
| 134 | Security-personnel Usability | 25 |
| 135 | Security-personnel Reliability | 26 |
| 136 | Security-personnel Security | 27 |
| 136-137 | Faculty/Event Organizer/Administrator category summary | 28 |
| 138 | Administrative Functional Suitability | 29 |
| 139 | Administrative Performance Efficiency | 30 |
| 140 | Administrative Compatibility | 31 |
| 141 | Administrative Usability | 32 |
| 142 | Administrative Reliability | 33 |
| 143 | Administrative Security | 34 |
| 144 | Administrative Maintainability | 35 |
| 145 | Administrative Portability | 36 |
| 145 | Manual student category summary | 37 |
| 147 | Manual Functional Suitability | 38 |
| 147 | Manual Performance Efficiency | 39 |
| 148 | Manual Usability | 40 |
| 149 | Manual Reliability | 41 |
| 150 | Manual Security | 42 |
| 150 | Descriptive comparison of manual and system survey scores | 43 |
| 153 | Inputs to worked Welch computation, currently without a unique caption | 44 |
| 154 | Welch results, currently captioned Table 999 | 45 |

Use the following exact caption for the computation-input table: **Table 44. Summary Statistics Used in the Worked Welch t-Test Computation**.

The existing Chapter 3 sequence ends with Figure 19. Keep Figure 20 for successful-entry timing, Figure 21 for recorded-entry rate, and Figure 22 for the manual writing distribution. Rename the survey-comparison chart on page 151 to **Figure 23. Mean Manual and Proposed-System Survey Scores by Category**. Its plotted values can stay.

Update every related in-text table/figure reference. In particular, the demographic discussion must refer to Table 15 rather than Table 7. Regenerate the table of contents, list of tables, and list of figures only after all edits and pagination are complete. Do not retain Table 999 or duplicate Figure 20.

## 14. Results to Keep Unchanged

- 438 exported records: 210 successful entries, 210 granted reset exits, and 18 denied records.
- Mode record totals: Fast 142, Standard 152, High-Security 144.
- Seventy successful entry trials per mode; the same 70 participants tested the three modes.
- Internal latency means: Fast 11.49 ms, Standard 57.37 ms, High-Security 54.06 ms.
- Standard mean PIN interaction: 2.505 seconds.
- High-Security mean PIN interaction: 2.162 seconds; QR interaction: 3.637 seconds; combined interaction: 5.799 seconds.
- Within-group entry intervals: 66 per mode, calculated as 19 + 19 + 19 + 9 from group sizes 20, 20, 20, and 10. Do not include intervals across team resets or mode explanations.
- Supplementary recorded-entry rates: 21.98, 7.46, and 4.95 intervals per minute.
- Observed labelled applicable credential-decision accuracy: 222/222 = 100.00%, limited to the recorded expected-valid and expected-invalid credential decisions.
- Record-based denial proportion: 18/438 = 4.11%.
- Recorded PIN lockout rate: 2/152 = 1.32%; Standard-specific rate: 2/79 = 2.53%. These were deliberately triggered demonstrations, not estimates of ordinary-user lockout risk.
- Manual writing baseline: n = 70, total duration 1,526.24 seconds, mean 21.803 seconds, median 21.135 seconds, sample SD 4.436 seconds, range 13.21-37.63 seconds.
- Final Welch survey comparison: t = 15.124, df = 89.076, p < .001, difference = 1.458, 95% CI [1.267, 1.650].

Keep the distinction between observed credential decisions and state-rule denials. Keep the exclusion of team reset exits from the participant-entry timing analysis. Do not interpret demonstration denials as ordinary student authentication mistakes.

## 15. Necessary Chapter 3 Alignment

These are short consistency changes outside Chapter 4, needed because the latest supplied PDF includes the full thesis. They do not make missing measurements available.

### Page 56: Comparative Analysis

Replace the statement promising a t-test of both transaction speeds and mean ratings with:

The manual and proposed-system student survey scores were compared using a two-tailed Welch independent-samples t-test at a significance level of .05. Each respondent's overall score was calculated by giving equal weight to the five evaluation categories. The comparison used separate groups of 70 students. Observation-based processing measurements were summarized descriptively. A processing-time t-test was not applied because the available manual writing durations and automated component timings did not measure equivalent complete-task boundaries.

### Page 61: Standard Deviation

Use the sample-SD convention consistently with Chapter 4. Replace any formula using N in the denominator with:

`s = sqrt[ sum((x_i - x_bar)^2) / (n - 1) ]`

Here, `s` is the sample standard deviation, `x_i` is an individual observed value or respondent score, `x_bar` is its sample mean, and `n` is the number of observations. For frequency-based item ratings, the equivalent numerator is `sum[f_j(r_j - x_bar)^2]`, with denominator `n - 1`. An average of individual-item SDs is not used as a category-composite SD.

### Pages 61-62: Processing Time and Throughput

Do not redefine the original complete-task timing boundary to claim it was measured. Preserve it as the intended measure, but acknowledge the available evidence using:

The intended complete-task processing measure covers the stated verification workflow from NFC presentation to completion. Where the exported records provide only internal latency, PIN/QR interaction durations, and timestamps of successful entries, those components are reported separately rather than treated as a complete-task duration. Recorded-entry spacing and its reciprocal rate are supplementary descriptive measures within observation groups. They are not estimates of peak campus capacity and do not establish a statistically significant throughput difference from the manual writing baseline.

## 16. Final Layout and Submission Checks

- Keep each category-summary row together and repeat table headers when a table continues onto another page.
- Use two decimal places for descriptive item/category means, three for composite SDs and inferential statistics where shown, and consistent units for all timing results.
- Keep individual-item SDs separate from respondent-level composite SDs.
- Make all administrative overall mean references 3.81 and all student-system overall mean references 3.74, or 3.742 when three-decimal precision is appropriate.
- Ensure that service percentages explicitly use four valid answers, while the administrative role distribution uses five respondents.
- Retain the limitations concerning timing boundaries, different student survey groups, fixed mode order, reused credentials, and unmeasured objective error/fraud reductions.
- Do not claim that SOP 6's objective complete-task speed and error/fraud comparisons are fully established. The survey comparison is inferentially supported; unmatched timings and unmeasured error/fraud outcomes remain evidence limitations.
- Update the table of contents and lists of tables/figures after the final pagination changes.

No original observation records, respondent answers, or supplied PDFs were changed in preparing this replacement text.
