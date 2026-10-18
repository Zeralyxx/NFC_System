# CHAPTER 5

# SUMMARY OF FINDINGS, PRESENTATION, INTERPRETATION, DISCUSSION OF RESULTS AND RECOMMENDATIONS

## Introduction

This chapter synthesizes the implementation, functionality testing, system observations, manual paper-attendance baseline, and respondent evaluations presented in Chapter 4. It summarizes the findings of the Risk-Based Two-Factor Student Verification and Event Attendance Management System and relates them to the study's research questions. Conclusions and recommendations are limited to the documented prototype, supplied observation records, and participating respondent groups.

The proposed-system survey involved 80 respondents: 70 students, five security personnel, and five faculty/event-organizer/administrator respondents. A separate group of 70 students evaluated the manual process. The automated observation involved 70 participants testing each of the three verification modes, producing 210 successful entry trials rather than 210 different participants. The manual observation contained 70 attendance-writing durations. The survey comparison and observation measurements are interpreted separately because they address different aspects of the processes.

## Summary of Findings

### Prototype Implementation and Functionality

The prototype integrated an ESP32 microcontroller and PN532 NFC reader with a C# WinUI 3 application. The serial-connected reader supplied NFC credential information, while the application handled PIN entry, camera-based QR validation, configured access decisions, transaction records, and monitoring interfaces. Local MySQL storage and Firebase Firestore synchronization supported the record-management workflow.

Fast mode used NFC validation, Standard mode added PIN authentication, and High-Security mode added QR credential validation. Mode selection was configured by an authorized operator rather than automatically determined from an individual risk score. QR matching functioned as an additional credential-integrity check, not an independently established third authentication factor.

Appendix H documented 16 functionality cases, all marked PASS. These cases covered valid mode workflows, wrong-PIN handling and lockout, QR mismatch, unknown credentials, configured inactive and expelled student status, restricted-event registration, entry/exit state rules, and selected offline, synchronization, and application-closure conditions. The outcomes demonstrated the expected responses in the listed cases; they did not constitute an exhaustive security or reliability evaluation.

### System Observation and Performance

The exports contained 438 records: 210 successful entry trials, 210 granted reset exits performed by the research team, and 18 denied records. Each mode contained 70 successful entry trials. Twenty preconfigured NFC credential profiles were reused, and participants encountered Fast, Standard, and High-Security modes in that fixed order.

Mean internal system latency was 11.49 ms for Fast, 57.37 ms for Standard, and 54.06 ms for High-Security. Standard recorded a mean PIN interaction duration of 2.505 seconds. High-Security recorded a mean PIN duration of 2.162 seconds and a mean QR duration of 3.637 seconds, giving a combined mean of 5.799 seconds. Fast bypassed PIN and QR, so its PIN/QR interaction duration was not applicable. These measurements did not cover the complete physical verification task.

The supplementary recorded-entry rates were 21.98, 7.46, and 4.95 intervals per minute for Fast, Standard, and High-Security, respectively. Each rate used 66 within-round between-entry intervals from groups of 20, 20, 20, and 10 entries. The rates described observed spacing between recorded entries; they were not measurements of peak campus capacity or independently timed complete-round throughput.

### Credential Decisions and Security Conditions

The researcher-labelled applicable credential-decision sample contained 210 expected-valid granted entries and 12 expected-invalid credential denials. All 222 applicable recorded decisions agreed with the expected outcomes, giving observed credential-decision accuracy of 100.00%. Correct rejections were counted as correct decisions rather than authentication failures. Reset exits, five consecutive-entry violations, and one irregular-exit condition were excluded from this credential-accuracy denominator.

The 18 denied records comprised eight PIN failures, five consecutive-entry violations, two deliberately triggered PIN lockouts, two QR mismatches, and one irregular-exit condition. The recorded denial proportion was 4.11% (18/438). Two triggered lockouts among 152 recorded PIN submissions yielded a lockout rate of 1.32%. These rates reflected the controlled mixture of participant transactions, team resets, and intentionally invalid conditions; they did not estimate ordinary students' failure or lockout risk.

### Manual Paper-Attendance Baseline

The manual simulation required participants to write their name, course and section, student ID, and signature. Timing began with the first written character and ended when the required information was complete. The 70 writing observations had a mean duration of 21.803 seconds and a sample standard deviation of 4.436 seconds. Personnel did not perform ID or enrollment-status checks during the timed procedure.

The derived service-time-equivalent rate was 2.75 writing completions per minute. This rate was calculated from mean active writing duration and did not include waiting, transitions between participants, or subsequent encoding. It was not directly comparable to the automated recorded-entry interval rates. Consequently, the supplied measurements did not establish a percentage reduction in complete-task duration or a matched manual-versus-system throughput advantage.

### Respondent Evaluation

The three proposed-system respondent groups gave overall equal-category means of 3.74 for students, 3.63 for security personnel, and 3.81 for faculty/event-organizer/administrator respondents. Each overall mean was interpreted as Strongly Agree / Highly Satisfactory under the study's four-point scale. The manual student group obtained an overall mean of 2.28, interpreted as Disagree / Unsatisfactory.

The following category findings were reported separately by respondent group:

- **Functional Suitability.** Means were 3.76 for students, 3.84 for security personnel, and 3.80 for faculty/event-organizer/administrator respondents. These ratings indicated favorable perceptions of the prototype's verification and attendance-management functions.
- **Performance Efficiency.** Means were 3.74, 3.45, and 3.68, respectively. Security personnel gave comparatively lower ratings to the monitoring workflow, identifying an area for further usability and performance investigation. The ratings did not independently establish a cause of slower or less satisfactory monitoring.
- **Compatibility.** The faculty/event-organizer/administrator group gave a mean of 3.75. This reflected its assessment of compatibility-related indicators, not independently demonstrated interoperability with every university system.
- **Usability.** Means were 3.74 for students, 3.60 for security personnel, and 3.88 for the faculty/event-organizer/administrator group. These results indicated favorable perceived ease of use within the evaluation setting.
- **Reliability.** Means were 3.71, 3.48, and 3.76, respectively. The documented offline tests provided separate evidence of the tested local-verification and synchronization functions, but did not establish uninterrupted operation under every network condition.
- **Security.** Means were 3.75, 3.80, and 3.84, respectively. These perceptions complemented the recorded rule-handling results but did not demonstrate complete resistance to impersonation, credential copying, or physical tailgating.
- **Maintainability.** The faculty/event-organizer/administrator group gave a mean of 3.88. This was a respondent assessment of the relevant indicators; long-term maintenance effort and technical code quality were not independently quantified by these ratings.
- **Portability.** The same group gave a mean of 3.92, its highest category rating. This reflected perceived portability within respondents' experience and did not establish operation across untested operating systems or hardware configurations.

No single pooled ISO-based overall score was calculated across the three proposed-system groups because their role-specific instruments covered different indicators and category sets. The findings represented respondent evaluations rather than formal ISO certification.

### Comparison of Manual and Proposed-System Survey Ratings

The independent student survey groups each contained 70 respondents. The manual group obtained an overall mean of 2.284 with a sample SD of 0.753, while the proposed-system group obtained 3.742 with a sample SD of 0.290. A two-tailed Welch independent-samples t-test showed a statistically significant difference, t(89.08) = 15.12, p < .001. The mean difference favoring the proposed system was 1.458 points, with a 95% confidence interval of [1.267, 1.650]. The null hypothesis of equal population mean overall survey ratings was rejected at the .05 significance level.

The result demonstrated a difference in reported evaluations, not a causal within-person improvement. The groups differed in demographic composition, and their questionnaires used process-specific wording. The t-test concerned overall survey scores, not processing times or five separately tested category hypotheses.

## Presentation of Data

Table 44 summarizes the respondent-group means already reported in Chapter 4. A dash indicates a category not included in that group's instrument, not a zero rating.

**Table 44. Summary of Category Means by Respondent Group**

| Category | System Students (n = 70) | Security Personnel (n = 5) | Faculty/Event Organizer/Administrator (n = 5) | Manual Students (n = 70) |
| --- | ---: | ---: | ---: | ---: |
| Functional Suitability | 3.76 | 3.84 | 3.80 | 2.48 |
| Performance Efficiency | 3.74 | 3.45 | 3.68 | 1.93 |
| Compatibility | - | - | 3.75 | - |
| Usability | 3.74 | 3.60 | 3.88 | 2.64 |
| Reliability | 3.71 | 3.48 | 3.76 | 2.18 |
| Security | 3.75 | 3.80 | 3.84 | 2.18 |
| Maintainability | - | - | 3.88 | - |
| Portability | - | - | 3.92 | - |
| **Overall, equal category weights** | **3.74** | **3.63** | **3.81** | **2.28** |

**Note.** Each overall score gives equal weight to the categories in the corresponding instrument. The proposed-system groups were interpreted separately and were not pooled into one system-wide category score. The three proposed-system overall means were Highly Satisfactory; the manual overall mean was Unsatisfactory. Individual-item and respondent-level sample SDs are presented in Chapter 4. Composite SDs were unavailable for the two five-person groups because only item-frequency totals were supplied.

Table 45 summarizes the observation findings while preserving their measurement boundaries.

**Table 45. Summary of Observation and Inferential Findings**

| Finding | Result | Interpretation Boundary |
| --- | --- | --- |
| Successful entry trials | 70 per mode; 210 across modes | The same 70 participants tested the three modes |
| Mean internal latency: Fast / Standard / High-Security | 11.49 / 57.37 / 54.06 ms | Instrumented internal processing, not complete task duration |
| Mean recorded interaction: Standard / High-Security | 2.505 / 5.799 s | PIN and combined PIN/QR stages, respectively |
| Recorded-entry rate: Fast / Standard / High-Security | 21.98 / 7.46 / 4.95 intervals/min | Supplementary within-round timestamp-spacing measure |
| Observed applicable credential-decision accuracy | 100.00% (222/222) | Researcher-labelled recorded credential decisions |
| Recorded denial proportion | 4.11% (18/438) | Includes reset exits and deliberate invalid-condition records |
| Recorded PIN lockout rate | 1.32% (2/152) | Deliberately triggered lockouts per captured PIN submission |
| Mean manual writing duration | 21.803 s; sample SD 4.436 s | Active attendance writing; 70 observations |
| Manual service-time-equivalent rate | 2.75 writing completions/min | Derived from mean writing time, not observed continuous throughput |
| Overall survey difference, system minus manual | 1.458 points; 95% CI [1.267, 1.650] | Different student groups and process-specific survey wording |
| Welch overall-score test | t = 15.124; df = 89.076; p < .001 | Overall respondent-level survey scores; not processing times |

## Interpretation of Results

The role-specific evaluations indicated favorable perceived quality of the proposed prototype. Students gave their highest category rating to Functional Suitability at 3.76. Security personnel also rated Functional Suitability highest at 3.84, while their lowest category rating was Performance Efficiency at 3.45. The faculty/event-organizer/administrator group rated Portability highest at 3.92 and Performance Efficiency lowest at 3.68. These within-group patterns help identify strengths and priorities without creating an unsupported pooled ranking across different instruments.

The manual group reported less favorable perceptions in four of the five categories. Usability, at 2.64, remained Satisfactory, while Functional Suitability, Performance Efficiency, Reliability, and Security were Unsatisfactory. The overall-score t-test supported a statistically significant difference between the manual and proposed-system groups' reported evaluations. However, group differences and method-specific item wording limited attribution of that difference solely to the attendance method.

The observation data showed internal processing on a millisecond scale and recorded PIN/QR interaction on a seconds scale. Fast had the shortest internal latency and within-round entry spacing. Standard's mean internal latency exceeded High-Security's, so the internal data did not show a simple increase with each additional credential stage. The fixed mode order also limited causal interpretation of the lower mean PIN duration recorded in High-Security mode.

The security findings demonstrated expected handling of the recorded credential and state conditions. The accuracy, denial, and lockout measures used different denominators and must not be treated as interchangeable measures of security or usability. The credential-decision accuracy concerned the known recorded tests, while the denial and lockout proportions included deliberate demonstrations. None established universal attack resistance or routine operational failure rates.

The manual and automated timing data described different portions of different procedures. Their available values could be reported descriptively, but did not support a matched complete-task processing-time t-test, throughput multiplier, or percentage speed improvement. This measurement limitation did not negate the demonstrated rule handling or favorable survey evaluation; it defined the scope of the study's conclusions.

## Discussion

### Multi-Layer Verification Architecture: SOP 1

The prototype combined NFC credential identification with PIN authentication and QR credential validation according to the configured mode. Valid Standard and High-Security workflows were documented, while wrong PINs and QR mismatches produced the expected rejection or lockout responses. QR validation checked consistency with the NFC-linked credential record rather than independently proving the physical identity of the person presenting it. The findings supported operation of the implemented verification stages under the tested conditions, not complete prevention of credential copying or misuse.

### Configurable Verification Modes: SOP 2

Fast, Standard, and High-Security modes applied different credential requirements selected by the operator. The observation documented all three configurations and their internal and interaction-stage measurements. These results supported configurable verification enforcement, but did not demonstrate automatic individual risk scoring or adaptive mode selection for each student. The relationship with risk-adaptive approaches reviewed in Chapter 2 was therefore conceptual rather than evidence that the prototype implemented an equivalent automated risk model. Differences in study procedures also prevented direct performance ranking against the reviewed systems.

### Automated Decisions and Personnel Roles: SOP 3

The application applied configured access rules and produced visible decisions, transaction records, and alerts. Appendix H documented responses to unknown credentials, inactive or expelled records, and restricted-event registration conditions. These capabilities supported an alert-monitoring role for personnel rather than requiring manual approval of every valid transaction. Nevertheless, the study did not independently measure reductions in staff workload or establish live university enrollment verification for every participant using the reused test profiles.

### Entry/Exit Rules and Data Integrity: SOP 4

The recorded consecutive-entry and irregular-exit conditions demonstrated entry/exit state-rule handling. Appendix H also documented normal state changes, local verification during disconnection, synchronization after reconnection, and the administrator-PIN requirement for the tested application-closure condition. These findings supported the operation of the listed controls. Logical consecutive-entry rejection should not be equated with proof of physical tailgating prevention, and the selected offline cases did not establish reliable recovery under every outage, conflict, or power-failure condition.

### ISO-Based Respondent Evaluation: SOP 5

Students, security personnel, and faculty/event-organizer/administrator respondents gave overall means of 3.74, 3.63, and 3.81, respectively, interpreted as Highly Satisfactory. All category means in the three proposed-system instruments were also Highly Satisfactory, although several individual security-personnel items were only Satisfactory. The findings indicated favorable perceived quality within these respondent groups. Compatibility, Maintainability, and Portability were evaluated by the faculty/event-organizer/administrator group, not by every respondent. The study did not pool the different instruments into one system-wide score or treat agreement ratings as formal technical certification.

### Comparison with the Manual Process: SOP 6

For SOP 6A, the manual simulation produced a mean active writing duration of 21.803 seconds. The automated exports supplied internal latency and recorded PIN/QR stages, but not an independently timed complete task with a matching boundary. The observations therefore did not establish a directly comparable complete-task time difference.

For SOP 6B, the supplementary recorded-entry rates were 21.98, 7.46, and 4.95 intervals per minute, while the manual service-time-equivalent rate was 2.75 writing completions per minute. These rates used different denominators and excluded or included different workflow components. They did not establish an overall-throughput advantage under equivalent procedures.

For SOP 6C, the proposed-system group reported higher means across the five corresponding survey categories. Its overall score was significantly higher than the manual group's score, with t(89.08) = 15.12, p < .001, a difference of 1.458 points, and a 95% confidence interval of [1.267, 1.650]. This supported a difference in student-reported evaluations, subject to the limitations of independent groups and process-specific questionnaire wording.

The manual simulation contained no ID or enrollment check, while the prototype implemented configured validation controls. These differences described the procedures and tested capabilities; they did not quantify reductions in transcription errors, proxy attendance, or fraudulent entry. The study consequently supported the survey comparison and prototype functionality without claiming an objective speed or fraud-reduction result that the supplied measurements did not establish.

## Recommendations

Based on the findings and limitations, the following directions are recommended:

### a. Measure Equivalent Complete-Task Durations and Sustained Throughput

Conduct a comparison using the same attendance or verification outcome for both methods and explicitly defined, comparable start and finish points. Record active service time separately from queue waiting, participant transitions, retries, and other interruptions. For throughput, independently record the elapsed observation period and number of completed tasks instead of substituting between-entry intervals for full-round duration. Where the same participants perform both methods, account for the paired design and counterbalance method order; where groups differ, document the independent design. Select the statistical analysis only after the task boundaries and study design are established.

### b. Improve Personnel Monitoring and Investigate User Interaction Delays

Use the relatively lower security-personnel Performance Efficiency and Reliability ratings to guide targeted interface evaluation. Study alert prioritization, filtering, grouping, readability, and response during rapid successive events. For the student terminal, investigate the longer PIN/QR interactions through observation or video rather than assuming a hardware or user cause from exported durations alone. Evaluate terminal placement and feedback while preserving the required verification controls.

### c. Conduct Larger-Scale, Counterbalanced, and Multi-Terminal Testing

Evaluate larger participant groups, multiple synchronized terminals, longer operating periods, and varied gate and event conditions. Assign individual credentials where identity-linked performance is being studied. Counterbalance mode order to reduce practice effects and document environmental and device conditions. Separate successful trials, challenge attempts, cancellations, and hardware failures in the test records so that sustained performance and error estimates have clear denominators.

### d. Strengthen Credential Integrity and Evaluate Security Controls Directly

Conduct documented challenge testing for copied credentials, mismatched QR payloads, credential sharing, repeated PIN attempts, and recovery-channel misuse. Investigate stronger credential-integrity mechanisms without presenting QR matching as an independent authentication factor. Distinguish logical state-rule tests from observation of physical gate bypass or tailgating. Report expected and actual outcomes for each applicable attempt rather than inferring security effectiveness from survey agreement alone.

### e. Expand Offline, Synchronization, and Recovery Testing

Test prolonged disconnections, stale student-status data, simultaneous offline terminals, conflicting updates, interrupted synchronization, and recovery after power failure. Verify both successful recovery and safe handling of conditions in which credential status or required record storage cannot be confirmed. Measure queued-record completeness and reconciliation outcomes instead of assuming that the listed successful cases cover every network condition.

### f. Evaluate PIN Usability and Credential Recovery Separately from Demonstrations

Retain tests of lockout enforcement, but distinguish intentionally triggered lockouts from spontaneous user errors during routine verification. Evaluate recovery usability, attempt limits, access controls, and audit records through dedicated test cases. Use clearly defined counts of participants, submissions, and workflows when reporting failure or recovery rates.

### g. Broaden Respondent Groups and Retain Individual Evaluation Records

Include students from additional colleges and year levels and larger security and administrative samples. Preserve anonymized respondent-level records for all groups so that category and overall composite SDs can be calculated correctly. For future manual/system survey comparisons, improve comparability of corresponding items and document recruitment, group differences, and the experiences participants actually evaluated. Use technical assessments in addition to perception surveys when examining code quality, maintainability, interoperability, and portability.

### h. Measure Operational Workload and Record Quality

Evaluate personnel workload, alert response, report preparation, and record reconciliation directly before claiming administrative labor savings. Independently audit attendance records or use documented challenge procedures when measuring transcription errors, duplicate records, proxy attendance, or invalid enrollment acceptance. Keep these outcomes separate from credential-decision correctness and participant satisfaction.

### i. Assess Deployment Readiness Before Institutional Expansion

Examine terminal placement, network requirements, device durability, credential issuance, staff training, maintenance, access governance, and data handling before university-wide deployment. Test the complete operational workflow, not only the verification screen. Establish procedures for outages, invalid records, locked credentials, and exceptional access requests while preserving traceability and role-based controls.

### j. Consider Additional Modules as Separately Evaluated Future Work

Guest or visitor credentialing and optional alternative authentication technologies may be investigated as future extensions. Evaluate their operational need, cost, security, usability, and data-handling implications before adding them. Do not describe these extensions as implemented or evaluated features of the present prototype.

## Summary

The study produced a functional prototype integrating NFC credential capture, PIN authentication, QR credential validation, configured access rules, transaction records, and monitoring interfaces. The documented functionality cases and observation records demonstrated the expected responses under the tested conditions. The three proposed-system respondent groups gave favorable overall evaluations, while the independent student survey comparison showed significantly higher reported ratings for the proposed system than for the manual process.

The recorded component timings, security-condition outcomes, and manual writing baseline provided useful descriptive findings. However, their different timing boundaries did not establish a matched complete-task speed or throughput advantage. The controlled tests and survey ratings also did not establish universal security effectiveness, automatic individual risk scoring, or readiness for immediate institution-wide deployment.

The findings therefore supported the prototype's demonstrated functionality and perceived acceptability within the evaluation setting. Further matched performance measurement, broader respondent evaluation, direct security and recovery testing, and operational assessment are needed before making stronger claims about full-scale institutional effectiveness.
