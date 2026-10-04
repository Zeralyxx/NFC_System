# CHAPTER 4 RESULTS AND DISCUSSION

This chapter presents the implementation, documented functionality tests, controlled system observation, manual paper-attendance baseline, and user evaluation of the Risk-Based Two-Factor Student Verification and Event Attendance Management System. The analysis distinguishes software processing, recorded credential interaction, paper-writing duration, log-derived entry spacing, and respondent perceptions. Expected security-rule rejections are interpreted according to the conditions under which they occurred.

## 4.1 Implementation Details

### 4.1.1 System Architecture and Development Environment

The prototype uses an ESP32 microcontroller and PN532 NFC reader to acquire credential identifiers. NFC data is transferred through a USB serial connection to a Windows computer running a C# and WinUI 3 application. PIN entry and camera-based QR capture are handled by the application. The processing unit performs student-record lookup, configured eligibility checks, credential validation, transaction-state checks, logging, and administrative monitoring.

The application supports Fast mode using NFC validation, Standard mode using NFC and PIN validation, and High-Security mode using NFC, PIN, and QR credential validation. Operators select the mode according to the configured security requirements. The evaluation does not establish that the system independently inferred risk levels or automatically selected a mode for each participant.

Local MySQL storage supports the prototype's operational records, while Firebase Firestore is used for cloud synchronization. Local state and pending-record handling support operation during temporary connectivity interruptions. These implementation capabilities are distinguished from proof of uninterrupted or failure-free operation under all network conditions.

### 4.1.2 Technical Hurdles Resolved

State synchronization prioritizes confirmed local state changes while remote synchronization is pending. This approach reduces stale-state conflicts affecting entry, exit, and PIN-security state. The documented offline-cache and synchronization tests provide evidence for the tested conditions, not a guarantee of universal state continuity.

Hardware lifecycle handling allows the active window to acquire the NFC serial connection while other windows release it. Dialog sequencing and camera lifecycle handling reduce overlapping operations and invalid-state exceptions. Timestamp serialization and clock-offset handling improve consistency of exported and synchronized records. Formatting timestamps to millisecond precision does not, by itself, prove atomic database integrity.

## 4.2 Results Presentation

### 4.2.1 System Interfaces

The following figures present the core application interfaces, including staff authentication, role-specific dashboards, gate monitoring, and the student-facing kiosk.

![Figure 4.1. System Authentication Interface](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-1.png)

The staff authentication interface requests an authorized credential before displaying the appropriate operational view. Its presence illustrates the intended access workflow, while access-control correctness is evaluated through the documented tests.

![Figure 4.2. Administrator Dashboard](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-2.png)

The administrator dashboard provides navigation to credential registration, attendance processing, gate verification, student records, security monitoring, and reports. Administrative operations remain distinct from the participant-facing verification workflow.

![Figure 4.3. Security Personnel Dashboard](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-3.png)

The security view emphasizes verification and monitoring tasks. The role-specific presentation limits the operational modules displayed to the assigned personnel.

![Figure 4.4. Event Organizer Dashboard](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-4.png)

The event-organizer view presents attendance and reporting functions relevant to event administration. The timing exports analysed below identify entry and exit transactions; they are not separately identified event-attendance transactions.

![Figure 4.5. Gate Monitoring Console](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-5.png)

The operator console presents the selected verification mode, student information, verification outcomes, and security alerts. Logged decisions from this workflow form the automated observation dataset.

![Figure 4.6. Student Facing Kiosk](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/full-chapter-assets/Interface-6.png)

The kiosk presents access outcomes and the required credential prompts. A visible access decision is distinguished from a measured duration: complete NFC-tap-to-finish timing was not independently recorded in the supplied exports.

### 4.2.2 Verification Mode Routing

After record and eligibility checks, the verification engine routes the active session to the credential stages required by the selected mode. Exit handling is a separate state-reset pathway. The following excerpt illustrates the mode-dependent routing; it does not contain all eligibility, state, or persistence checks.

```csharp
if (mode == VerificationMode.Fast ||
    transactionType == TransactionType.Exit)
{
    return await GrantAsync(session,
        "NFC validation passed.");
}

// Standard and High-Security require PIN.
// After an accepted PIN, High-Security requires QR.
if (session.Mode == VerificationMode.HighSecurity &&
    !session.IsQrFallback)
{
    session.NextStep = VerificationStep.RequiresQr;
}
```
Figure 4.7. Verification Mode Routing Excerpt

## 4.3 Testing and Performance Evaluation

### 4.3.1 Functionality Testing Results

The functionality-test record documents the expected and actual results of sixteen selected scenarios. The reported PASS statuses refer to those documented scenarios, not to every observed transaction, every attack type, or all operating conditions. QR mismatch testing is not presented as proof that an identical copy of a valid QR credential can be detected.

**Table 4.1. Documented Functionality Test Cases**

| Case | Test condition | Expected result | Reported actual result | Status |
|---|---|---|---|---|
| T-01 | Valid NFC in Fast mode | Grant; update entry state | Granted; state updated | PASS |
| T-02 | Valid NFC and correct PIN | Grant Standard entry | Granted; PIN validated | PASS |
| T-03 | First or second wrong PIN | Reject PIN; allow retry | Invalid-PIN prompt displayed | PASS |
| T-04 | Third consecutive wrong PIN | Lock credential; generate alert | Lockout and alert reported | PASS |
| T-05 | Valid NFC, PIN and matching QR | Grant High-Security entry | Granted after credential checks | PASS |
| T-06 | Mismatched QR credential | Deny; generate mismatch alert | QR mismatch denial reported | PASS |
| T-07 | Unregistered NFC credential | Deny unknown credential | Unauthorized-credential denial | PASS |
| T-08 | Inactive configured student record | Deny before PIN/QR | Inactive-status denial | PASS |
| T-09 | Expelled configured student record | Deny before PIN/QR | Expelled-status denial | PASS |
| T-10 | Registered restricted-event attendee | Record attendance | Attendance recorded | PASS |
| T-11 | Unregistered restricted-event attendee | Deny event access | Unregistered-event denial | PASS |
| T-12 | Consecutive entry without exit | Deny state-rule violation | Anti-tailgating denial | PASS |
| T-13 | Entry followed by exit | Update inside/outside state | Both transitions reported | PASS |
| T-14 | Network disconnected | Use local verification data | Local verification reported | PASS |
| T-15 | Network restored | Synchronize pending records | Synchronization reported | PASS |
| T-16 | Application close with pending records | Require authorized close action | Admin-PIN interception reported | PASS |

The test-case results demonstrate the documented behavior for the tested inputs. They are not combined with the observation records to create an inflated authentication-accuracy denominator, because the source does not map each test case to every exported attempt.

### 4.3.2 System Metrics and Performance

#### Observation Procedure and Record Selection

System observation was conducted on August 12 and 13, 2026, in batches of 40 and 30 participants. According to the researcher's protocol, the same 70 participants tested Fast, Standard, and High-Security modes in that fixed order. The exports contain 70 successful entry trials per mode, or 210 successful entry trials across modes. This is not a sample of 210 distinct people.

Twenty NFC cards, preconfigured with PIN and QR credentials, were reused. For each mode, the first batch completed two rounds of 20 entries, while the second completed rounds of 20 and 10 entries. The research team performed exit transactions to reset the profiles between rounds. Explanations were provided before switching to the next mode. Reset exits were retained in the record audit but excluded from participant entry timing and authentication-accuracy calculations.

**Table 4.2. Classification of Exported System Records**

| Mode | Successful entries | Granted reset exits | Denied records | Total records |
|---|---|---|---|---|
| Fast | 70 | 70 | 2 | 142 |
| Standard | 70 | 70 | 12 | 152 |
| High-Security | 70 | 70 | 4 | 144 |
| Total | 210 | 210 | 18 | 438 |

All 210 records marked Entry and GRANTED were retained for timing analysis, including slow observations. No exact duplicate source rows were found. Repeated profile names were not removed because test cards were reused. The participant count is supported by the observation protocol, while the exported names identify test profiles rather than 70 uniquely assigned identities.

The researcher confirmed that wrong-PIN, PIN-lockout, QR-mismatch, and irregular-exit records resulted from deliberate demonstrations. Anti-tailgating records resulted from the researcher and students intentionally trying consecutive entry without exiting. These are expected invalid-condition rejections, not ordinary participants unexpectedly failing authentication. The expected-outcome classification comes from the researcher's account rather than from the verdict codes alone.

#### Average Processing Time

Chapter 3 defines average processing time from NFC tap to completed verification. The exports provide internal and interaction-stage timing fields, but not independent start and finish timestamps covering that complete boundary. Accordingly, full automated processing time under the original definition is not determined here. The available component measurements are reported separately and are not combined with an invented physical-interaction baseline.

**Table 4.3. Internal Timing for Successful Entry Trials**

| Mode | n | Mean system (ms) | Median (ms) | Sample SD (ms) | Range (ms) | Mean DB (ms) |
|---|---|---|---|---|---|---|
| Fast | 70 | 11.49 | 11.30 | 2.20 | 8.26-26.36 | 2.88 |
| Standard | 70 | 57.37 | 50.66 | 17.46 | 44.55-137.47 | 47.95 |
| High-Security | 70 | 54.06 | 52.64 | 6.34 | 49.44-86.36 | 44.90 |

Fast mode recorded mean internal latency of 11.49 ms, compared with 57.37 ms for Standard and 54.06 ms for High-Security. The observations do not show a strictly increasing internal latency with each added credential stage. Standard also showed greater dispersion, with sample SD of 17.46 ms and a maximum of 137.47 ms. Database-query duration overlaps the component processing measurements and must not be added again to Total System Latency.

**Table 4.4. Recorded PIN and QR Interaction Durations**

| Mode | n | Mean PIN (s) | Mean QR (s) | Mean total (s) | Median total (s) | Sample SD (s) | Maximum (s) |
|---|---|---|---|---|---|---|---|
| Fast | 70 | N/A | N/A | N/A | N/A | N/A | N/A |
| Standard | 70 | 2.505 | N/A | 2.505 | 2.035 | 1.152 | 6.875 |
| High-Security | 70 | 2.162 | 3.637 | 5.799 | 4.348 | 3.878 | 22.604 |

Standard recorded mean PIN interaction of 2.505 seconds. High-Security recorded mean PIN and QR interactions of 2.162 and 3.637 seconds, yielding mean combined interaction of 5.799 seconds. Its median was 4.348 seconds and maximum was 22.604 seconds, so longer observations increased the mean. Slow successful observations were retained. Fast bypassed PIN and QR; N/A does not mean zero-duration physical entry.

The lower mean High-Security PIN duration cannot be attributed to the mode itself. Mode order was fixed and respondents had already practised PIN entry in Standard mode. The results are descriptive; no causal or paired participant-level inference is made.

![Figure 4.8. Successful Entry Timing by Verification Mode](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/Figure-System-Timing.png)

Panel A shows mean internal latency with sample-SD error bars. Panel B shows the distribution of recorded interaction duration. Neither panel measures complete NFC-tap-to-finish processing time.

#### System Throughput Rate

The original Chapter 3 throughput formula requires a successful-verification count and a defined elapsed observation period. Independent complete-round start and finish times were not supplied. Therefore, the following log-derived recorded-entry rate is presented as a supplementary measure, not as measured full-round throughput or uninterrupted campus capacity.

Each mode contained four entry rounds with 20, 20, 20, and 10 entries. A round with n entries contains n - 1 recorded between-entry intervals, giving 19 + 19 + 19 + 9 = 66 intervals per mode. Mean spacing equals summed within-round first-to-last spans divided by 66. Recorded-entry rate equals 60 multiplied by 66 divided by the summed spans in seconds.

**Table 4.5. Supplementary Recorded Entry Spacing and Rate**

| Mode | Rounds | Intervals | Summed spans (s) | Mean spacing (s) | Rate (/min) | Within-span denials |
|---|---|---|---|---|---|---|
| Fast | 4 | 66 | 180.200 | 2.730 | 21.98 | 2 |
| Standard | 4 | 66 | 531.167 | 8.048 | 7.46 | 8 |
| High-Security | 4 | 66 | 800.508 | 12.129 | 4.95 | 3 |

Fast recorded mean spacing of 2.730 seconds and a rate of 21.98 intervals per minute. Standard and High-Security recorded mean spacing of 8.048 and 12.129 seconds and rates of 7.46 and 4.95. Between-round reset intervals and explanations were excluded, while any within-round denials, demonstrations, or pauses remained included. Thirteen denied records fell within the selected spans. Deleting their rows would not delete the associated elapsed time.

![Figure 4.9. Supplementary Recorded Successful Entry Rate](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/Figure-Recorded-Entry-Rate.png)

The displayed rates use 66 observed intervals per mode, not 70 complete individually timed transactions. Within-round invalid attempts and pauses remain included.

### 4.3.3 Authentication and Security Metrics

#### Authentication Accuracy Rate

Authentication accuracy was operationalized as agreement between the researcher-confirmed expected credential decision and the recorded actual outcome. The researcher confirmed that granted participant entries used expected-valid test credentials and that no invalid attempts were granted. The incorrect PIN, triggered lockout, and QR-mismatch attempts were expected to be denied. Correct rejection is therefore counted as a correct decision rather than an authentication failure.

The applicable authentication-decision set contains 210 valid granted entries and 12 credential-related expected denials: eight PIN failures, two newly triggered PIN lockouts, and two QR mismatches. Five anti-tailgating records were excluded because they were pre-authentication entry-state violations, and one irregular exit was excluded because it was an exit-state test. The 210 granted reset exits were also excluded. These rules yield 222 recorded applicable authentication decisions, not 222 distinct students or NFC sessions.

**Table 4.6. Observed Credential Decision Accuracy**

| Mode | Expected-valid grants | Expected-invalid denials | Applicable decisions | Correct decisions | Accuracy |
|---|---|---|---|---|---|
| Fast | 70 | 0 | 70 | 70 | 100.00% |
| Standard | 70 | 9 | 79 | 79 | 100.00% |
| High-Security | 70 | 3 | 73 | 73 | 100.00% |
| Total | 210 | 12 | 222 | 222 | 100.00% |

Observed credential-decision accuracy = (222 correct decisions / 222 applicable recorded decisions) x 100 = 100.00%. This is a descriptive result for known valid test credentials and deliberately invalid challenge conditions. It is not proof of universal accuracy, resistance to every attack, or verification of each participant's real identity. Twenty preconfigured cards were reused by 70 people, so the observation primarily tested configured credential validity and rule enforcement, not exclusive ownership of real university credentials. Expected outcomes were classified from the researcher's observation account, not through a blinded independent assessor.

#### Transaction Denial Rate

**Table 4.7. Logged Denial Conditions**

| Condition | Records | Researcher confirmed condition |
|---|---|---|
| PIN_FAILURE | 8 | Deliberately incorrect PIN submissions |
| PIN_LOCKED | 2 | Deliberately reached third consecutive wrong PIN |
| QR_REPLACED_OR_MISMATCH | 2 | Deliberately mismatched QR credential |
| ANTI_TAILGATING_VIOLATION | 5 | Consecutive entry without an intervening exit |
| IRREGULAR_EXIT_SEQUENCE | 1 | Deliberately invalid exit-state sequence |
| Total | 18 | Expected invalid-condition rejections |

**Table 4.8. Recorded Transaction Denial Rate**

| Mode | Granted records | Denied records | Total records | Denial rate |
|---|---|---|---|---|
| Fast | 140 | 2 | 142 | 1.41% |
| Standard | 140 | 12 | 152 | 7.89% |
| High-Security | 140 | 4 | 144 | 2.78% |
| Total | 420 | 18 | 438 | 4.11% |

Recorded transaction denial rate = (18 / 438) x 100 = 4.11%. The denominator includes participant entry records, operator-assisted reset exits, and deliberate invalid-condition records. It is not a participant-only failure rate. Standard's higher denial proportion reflects the tested mix, including deliberate wrong-PIN and lockout demonstrations, and is not evidence that the mode is less reliable.

No hardware-read or database-timeout code appears among these 18 denial conditions. This observation is narrower than a claim of zero hardware or database failures; missed scans, cancellations, and errors not represented by such codes were not independently audited in this dataset.

#### PIN Lockout Rate

The researcher confirmed that both PIN_LOCKED records resulted from deliberately reaching the third consecutive incorrect PIN submission, rather than repeat scans of an already locked credential. The researcher also confirmed that every individual PIN submission was captured in the exports. The recorded denominator was reconstructed from PIN-bearing entry outcomes, including retries and correct PIN stages followed by QR denial.

**Table 4.9. Recorded PIN Submissions and Lockout Rate**

| Mode | PIN stages in granted entries | PIN_FAILURE | Triggered PIN_LOCKED | PIN passed then QR denied | Total PIN submissions | Lockout rate |
|---|---|---|---|---|---|---|
| Standard | 70 | 7 | 2 | 0 | 79 | 2.53% |
| High-Security | 70 | 1 | 0 | 2 | 73 | 0.00% |
| Total | 140 | 8 | 2 | 2 | 152 | 1.32% |

Recorded PIN lockout rate = (2 newly triggered lockouts / 152 recorded PIN submissions) x 100 = 1.32%. The 152 submissions comprise 140 PIN stages in successful entries, eight PIN-failure records, two triggering lockout submissions, and two correct PIN stages followed by QR mismatch. Fast mode and reset exits did not require PIN and were excluded. The denominator counts individual submissions, not participants or successful workflows.

The export hides the PIN component fields for PIN_LOCKED records even though their aggregate timings retain PIN-related time. These two submissions were retained based on the triggered-lockout condition, nonzero aggregate timing, and researcher confirmation; counting only populated PIN component fields would omit them. The percentage describes intentional test-induced lockouts, not spontaneous usability failures among students.

### 4.3.4 Manual Paper Attendance Baseline

The manual baseline contains 70 reported individual durations from a simulated paper-based event-attendance process. Participants wrote their name, course and section, student ID, and signature. The researcher clarified that timing began with the first written character and ended when all required information was completed. Personnel did not check an ID or enrollment status during this simulation.

The measurement therefore represents active paper-attendance writing duration. It excludes any unrecorded time before the first character, queue waiting, acquisition or handover of the pen, and subsequent validation or encoding. It should not be labelled complete manual identity verification time.

**Table 4.10. Manual Paper Attendance Writing Duration**

| Measure | Result |
|---|---|
| Number of reported observations | 70 |
| Sum of individual writing durations | 1,526.24 s |
| Mean writing duration | 21.803 s |
| Median writing duration | 21.135 s |
| Sample standard deviation | 4.436 s |
| Minimum writing duration | 13.21 s |
| Maximum writing duration | 37.63 s |
| Service-time-equivalent rate | 2.75 writing completions/min |

The recalculated mean of 21.803 seconds agrees with the sheet's rounded mean of 21.80 seconds. The median was 21.135 seconds, with sample SD of 4.436 seconds and a range of 13.21-37.63 seconds. The 1,526.24-second sum is accumulated writing time, not an independently measured continuous queue duration.

The service-time-equivalent rate was calculated as 60 / 21.803 = 2.75 writing completions per minute. It describes hypothetical consecutive use of the measured writing time without additional gaps. It is not directly observed queue throughput because transition and waiting intervals were not supplied.

![Figure 4.10. Manual Paper Attendance Writing Duration](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/Figure-Manual-Writing-Duration.png)

The distribution presents all 70 supplied writing durations. It does not include queue waiting or identity and enrollment validation.

### 4.3.5 Descriptive Comparison Addressing SOP 6

#### Average Processing Time per Student

The manual observation establishes mean active writing duration of 21.803 seconds. The automated exports establish internal system latency and recorded PIN/QR interaction duration, but not the complete NFC-tap-to-completed-verification duration defined in Chapter 3. Moreover, the manual test records event-attendance writing, whereas the analysed automated exports identify entry/exit verification without a specific event identifier. The datasets therefore differ in both task content and timing boundaries.

**Table 4.11. Timing Measurements and Their Boundaries**

| Method or stage | Available mean | Measurement boundary |
|---|---|---|
| Manual paper attendance | 21.803 s | First character to completion of required fields |
| Fast internal processing | 11.49 ms | Instrumented system components only |
| Standard PIN interaction | 2.505 s | Recorded PIN interaction stage |
| High-Security PIN plus QR interaction | 5.799 s | Recorded PIN and QR stages |
| Full automated processing time | Not established | NFC tap to completed verification |

These measurements describe the observed stages of each process. A percentage reduction in complete processing time cannot be established from their different boundaries. A matched comparison requires automated timing for the same attendance or verification task and an explicitly comparable start and finish boundary.

#### Students Processed per Minute

The manual service-time-equivalent rate is 2.75 completions per minute. The automated recorded-entry rates are 21.98, 7.46, and 4.95 intervals per minute for Fast, Standard, and High-Security. These rates arise from different denominators: summed manual writing times versus automated first-to-last timestamp intervals that include within-round pauses and invalid attempts. They are supplementary descriptive rates, not matched measures of overall throughput. No manual-versus-system throughput multiplier is claimed.

#### Human Error Proxy Attendance and Unverified Enrollment

Because the manual procedure contained no ID or enrollment-status check, all 70 recorded manual attendance-writing observations proceeded without that validation step. This corresponds to 70/70, or 100%, unchecked enrollment observations under the stated simulation procedure. It does not mean that all respondents were unenrolled or that 100% of entries were false. Student IDs written on paper were self-reported and were not independently validated in the timed procedure.

The automated prototype checks configured credential and student-record conditions and demonstrated rejection of deliberately invalid credentials and entry-state violations. However, its 20 reusable test profiles do not establish live university enrollment verification or unique identity validation for all 70 physical participants. The separate documented inactive/expelled tests show configured rule behavior, not the prevalence of enrollment violations in the observation sample.

The manual pre-evaluation provides additional evidence about respondents' perceptions of reliability and security, as presented in Section 4.5. It does not measure the incidence of transcription errors, proxy attendance, or false enrollment acceptance. Those rates require an independent record audit or documented challenge attempts. SOP 6 is addressed through the observed validation procedure, demonstrated configured controls, and the descriptive perception comparison, with empirical error and proxy-attendance rate comparisons remaining incomplete.

### 4.3.6 Summary of Measured Results

**Table 4.12. Performance and Security Summary**

| Metric | Result | Scope |
|---|---|---|
| Automated average full processing time | Not established | Original NFC-tap-to-finish timing boundary not supplied |
| Internal latency Fast / Standard / High-Security | 11.49 / 57.37 / 54.06 ms | 70 successful entry trials per mode |
| Recorded Standard / High-Security interaction | 2.505 / 5.799 s | PIN/QR interaction components |
| Observed credential-decision accuracy | 100.00% (222/222) | Researcher-labelled applicable recorded decisions |
| Recorded transaction denial rate | 4.11% (18/438) | Includes reset exits and invalid-condition tests |
| Recorded PIN lockout rate | 1.32% (2/152) | Intentional triggers; individual captured PIN submissions |
| Mean manual writing duration | 21.803 s | 70 reported writing observations |
| Matched complete-task time reduction | Not established | Different tasks and timing boundaries |
| Directly observed complete-round throughput | Not established | No complete-round observation start and finish |
| Manual enrollment check omission | 100% (70/70) | No enrollment check in the stated manual procedure |
| Real-enrollment false acceptance / proxy attendance rate | Not established | No independent enrollment or proxy-attempt audit |

## 4.4 Respondent Profile

The system evaluation contained responses from 70 students, five security personnel, and five faculty, event-organizer, or administrative respondents. These role groups account for 80 system-evaluation respondents. Each student and security-personnel instrument contained 24 items across five quality categories, while the administrative instrument contained 39 items across eight categories.

**Table 4.13. System Evaluation Respondents by Role**

| Respondent group | Frequency | Percentage |
|---|---|---|
| Students | 70 | 87.50% |
| Security personnel | 5 | 6.25% |
| Faculty / Event Organizer / Admin | 5 | 6.25% |
| Total | 80 | 100.00% |

A separate manual pre-evaluation frequency table contained 70 responses for each of its 24 items. This count is reported separately from the 80 system-evaluation respondents because the supplied frequency tables do not identify whether the manual and system respondents overlap. Age, sex, year level, and other demographic distributions were not included in the supplied survey data and are therefore not inferred.

## 4.5 User Evaluation

### 4.5.1 Scoring and Interpretation

The evaluation used the ISO/IEC 25010-based categories specified in the study instruments. Responses were scored as 4 for Strongly Agree, 3 for Agree, 2 for Disagree, and 1 for Strongly Disagree. For each item, the weighted mean was calculated as (4f4 + 3f3 + 2f2 + f1) / N. All 111 item-frequency totals and weighted scores were reconciled with the supplied survey worksheet.

Category means were calculated from unrounded item means. The overall result for each respondent group was calculated as the arithmetic mean of its category means, giving equal weight to each category. This convention matters because the number of items differs across categories. Final values were rounded to two decimal places only after calculation. The group results were not pooled into a single overall system score because the role-specific instruments cover different indicators and category sets.

The Chapter 3 interpretation scale was applied as follows: 3.26-4.00, Strongly Agree / Highly Satisfactory; 2.51-3.25, Agree / Satisfactory; 1.76-2.50, Disagree / Unsatisfactory; and 1.00-1.75, Strongly Disagree / Highly Unsatisfactory. These labels describe respondent ratings and do not constitute formal ISO certification.

Item SD was calculated as the square root of the sum of f(rating - item mean) squared divided by N, following the denominator specified in Chapter 3. The frequency tables support this item-level calculation. They do not preserve the relationships among each person's answers, so respondent-level category or overall-score SD cannot be reconstructed. Accordingly, category summaries show the range of their item SDs, and the individual-item tables report each item's SD directly.

### 4.5.2 Student Evaluation

**Table 4.14. Students Category Results**

| Category | Items | Mean | Item SD range | Interpretation |
|---|---|---|---|---|
| Functional Suitability | 5 | 3.77 | 0.33-0.54 | Highly Satisfactory |
| Performance Efficiency | 4 | 3.76 | 0.41-0.48 | Highly Satisfactory |
| Usability | 5 | 3.75 | 0.43-0.53 | Highly Satisfactory |
| Reliability | 5 | 3.73 | 0.41-0.47 | Highly Satisfactory |
| Security | 5 | 3.79 | 0.30-0.53 | Highly Satisfactory |
| Overall (equal category weights) | 24 | 3.76 | See note | Highly Satisfactory |

The SD range gives the minimum and maximum individual-item SD within each category, using N in the denominator. It is not a category-composite SD. Overall means give equal weight to categories; respondent-level composite SD cannot be recovered from item-frequency totals alone.

Students rated the system Highly Satisfactory in all five categories, with an overall equal-category mean of 3.76. Security received the highest category mean of 3.79, followed by Functional Suitability at 3.77. Performance Efficiency and Usability recorded means of 3.76 and 3.75, while Reliability recorded 3.73. These results indicate favorable perceptions of the verification experience across the evaluated categories.

At item level, the identity-confirmation statement in Security received a mean of 3.90. The lowest student item mean was 3.66 for processing without unexpected failures or freezing. All student item means remained within the Strongly Agree range. The ratings support perceived acceptability within the evaluation setting; confidence in identity confirmation and protection against credential copying does not independently establish resistance to impersonation or cloning.

**Table 4.15. Students Item Results (n = 70)**

| Survey indicator | Mean | SD (N) | Response interpretation |
|---|---|---|---|
| FS1. The system effectively meets the functional requirement of verifying my student identity using NFC, PIN, and QR codes. | 3.79 | 0.44 | Strongly Agree |
| FS2. The system performs the attendance and entry verification tasks accurately and completely. | 3.87 | 0.33 | Strongly Agree |
| FS3. The features of the system are highly relevant to the problem of monitoring student attendance and campus entry. | 3.76 | 0.49 | Strongly Agree |
| FS4. The system provides appropriate outputs (e.g., visual or audio confirmation) when I input my credentials. | 3.73 | 0.44 | Strongly Agree |
| FS5. The overall functionality successfully aligns with the objective of providing a secure attendance system. | 3.71 | 0.54 | Strongly Agree |
| PE1. The system responds quickly when I tap my NFC card, enter my PIN, or scan my QR code. | 3.79 | 0.41 | Strongly Agree |
| PE2. The system performs well and verifies my entry smoothly even when there is a long line of students. | 3.73 | 0.44 | Strongly Agree |
| PE3. The system maintains consistent verification speed and performance throughout the entire entry process. | 3.76 | 0.43 | Strongly Agree |
| PE4. The overall speed and performance of the system meet my expectations for an attendance process. | 3.77 | 0.48 | Strongly Agree |
| US1. The physical interface of the verification terminal (keypad, scanner, screen) is intuitive and easy to navigate. | 3.76 | 0.43 | Strongly Agree |
| US2. The system provides clear instructions and feedback on what step to do next (e.g., when to tap or scan). | 3.74 | 0.44 | Strongly Agree |
| US3. The design and layout of the verification terminal enhance my overall user experience. | 3.76 | 0.43 | Strongly Agree |
| US4. I was able to learn how to operate the system (tap, PIN, scan) with minimal to no training. | 3.73 | 0.53 | Strongly Agree |
| US5. The system supports accessibility and is straightforward enough to be used by all types of students. | 3.79 | 0.50 | Strongly Agree |
| RE1. The system processes my verification consistently without unexpected failures or freezing. | 3.66 | 0.47 | Strongly Agree |
| RE2. If a minor error occurs (e.g., wrong PIN entered), the system allows me to recover easily and try again. | 3.76 | 0.43 | Strongly Agree |
| RE3. I trust that the system accurately records and maintains the integrity of my attendance data. | 3.67 | 0.47 | Strongly Agree |
| RE4. The system successfully reads my inputs under different physical conditions (e.g., fast tapping, minor screen glare). | 3.79 | 0.47 | Strongly Agree |
| RE5. The system demonstrates stable performance from the start to the end of my verification process. | 3.79 | 0.41 | Strongly Agree |
| SE1. The system effectively protects my personal attendance data from unauthorized viewing by others in line. | 3.73 | 0.44 | Strongly Agree |
| SE2. The two-factor authentication (PIN and QR) successfully confirms my true identity. | 3.90 | 0.30 | Strongly Agree |
| SE3. I feel confident that my sensitive credentials (NFC, PIN, QR) are handled securely during verification. | 3.76 | 0.43 | Strongly Agree |
| SE4. The system's design includes measures that prevent common threats, such as others copying or stealing my credentials. | 3.80 | 0.40 | Strongly Agree |
| SE5. The multi-factor process successfully maintains the confidentiality and security of my campus entry. | 3.74 | 0.53 | Strongly Agree |

Indicator codes: FS = Functional Suitability; PE = Performance Efficiency; CO = Compatibility; US = Usability; RE = Reliability; SE = Security; MA = Maintainability; PO = Portability. Original item wording is retained. SD (N) follows the Chapter 3 formula.

### 4.5.3 Security Personnel Evaluation

**Table 4.16. Security Personnel Category Results**

| Category | Items | Mean | Item SD range | Interpretation |
|---|---|---|---|---|
| Functional Suitability | 5 | 3.84 | 0.00-0.49 | Highly Satisfactory |
| Performance Efficiency | 4 | 3.45 | 0.00-0.63 | Highly Satisfactory |
| Usability | 5 | 3.60 | 0.40-0.75 | Highly Satisfactory |
| Reliability | 5 | 3.48 | 0.40-0.49 | Highly Satisfactory |
| Security | 5 | 3.80 | 0.00-0.49 | Highly Satisfactory |
| Overall (equal category weights) | 24 | 3.63 | See note | Highly Satisfactory |

The SD range gives the minimum and maximum individual-item SD within each category, using N in the denominator. It is not a category-composite SD. Overall means give equal weight to categories; respondent-level composite SD cannot be recovered from item-frequency totals alone.

Security personnel gave an overall equal-category mean of 3.63, interpreted as Highly Satisfactory. Functional Suitability was highest at 3.84, followed by Security at 3.80. Usability, Reliability, and Performance Efficiency received 3.60, 3.48, and 3.45, respectively. These lower relative ratings identify monitoring speed and stability as areas for continued operational attention.

The item on processing speed meeting gate-monitoring expectations received 3.00, while rapid successive entries, minimal-training interpretation of alerts, and freedom from dashboard freezing each received 3.20. These items were interpreted as Agree / Satisfactory despite the favorable overall category ratings. With five respondents, each person accounts for one-fifth of a role-specific item tally, so the findings describe this small evaluation group.

**Table 4.17. Security Personnel Item Results (n = 5)**

| Survey indicator | Mean | SD (N) | Response interpretation |
|---|---|---|---|
| FS1. The system provides me with the necessary on-screen information (e.g., student name, ID number, entry status) to accurately verify the identities of individuals entering the campus. | 4.00 | 0.00 | Strongly Agree |
| FS2. The security dashboard correctly logs and displays real-time entry records as students pass through the gate. | 4.00 | 0.00 | Strongly Agree |
| FS3. The monitoring features provided by the system are highly relevant to my daily tasks of controlling campus entry and identifying unverified individuals. | 3.60 | 0.49 | Strongly Agree |
| FS4. The system provides clear visual or audio alerts on the monitoring screen when access is either successfully granted or denied. | 3.80 | 0.40 | Strongly Agree |
| FS5. The overall functionality successfully supports security personnel in enforcing gate protocols more effectively than traditional methods. | 3.80 | 0.40 | Strongly Agree |
| PE1. The monitoring screen updates immediately without lag when a student taps their card or enters their PIN at the terminal. | 4.00 | 0.00 | Strongly Agree |
| PE2. The dashboard handles rapid, successive entries smoothly, allowing me to continuously monitor a fast-moving queue without system slowdowns. | 3.20 | 0.40 | Agree |
| PE3. The system maintains a consistent response time and screen performance throughout my shift or the entire evaluation period. | 3.60 | 0.49 | Strongly Agree |
| PE4. The speed at which the system processes entries and alerts me to potential issues meets my expectations for an efficient gate monitoring process. | 3.00 | 0.63 | Agree |
| US1. The layout of the security monitoring screen is organized, intuitive, and easy to read at a quick glance. | 3.80 | 0.40 | Strongly Agree |
| US2. The system effectively highlights critical information (e.g., using clear color codes like green for "Pass" and red for "Denied") so I know exactly when to intervene. | 3.80 | 0.40 | Strongly Agree |
| US3. I was able to learn how to monitor the dashboard and interpret system alerts with minimal to no training. | 3.20 | 0.75 | Agree |
| US4. The student information (e.g., Name, ID Number, Status) displayed on the monitoring screen is clear and easy to read from a comfortable standing distance. | 3.80 | 0.40 | Strongly Agree |
| US5. Navigating the security interface is straightforward enough to be used efficiently by all assigned security personnel, regardless of technical background. | 3.40 | 0.49 | Strongly Agree |
| RE1. The security monitoring dashboard runs consistently without unexpected freezing, crashing, or losing connection. | 3.20 | 0.40 | Agree |
| RE2. If a minor sync error occurs between the terminal and the monitor, the interface recovers quickly without disrupting the monitoring of the gate. | 3.40 | 0.49 | Strongly Agree |
| RE3. I trust that the alerts and statuses displayed on my screen accurately reflect what is happening at the student terminal in real-time. | 3.60 | 0.49 | Strongly Agree |
| RE4. The system reliably and consistently flags suspicious activities (e.g., repeated failed PIN attempts) every time they occur during testing. | 3.60 | 0.49 | Strongly Agree |
| RE5. The system demonstrates stable and dependable monitoring capabilities from the start to the end of the evaluation process. | 3.60 | 0.49 | Strongly Agree |
| SE1. The monitoring screen displays only the necessary student information to me, preventing the unnecessary exposure of their private data to bystanders. | 3.60 | 0.49 | Strongly Agree |
| SE2. The system provides me with enough reliable data to confidently confirm if the person at the gate is the true owner of the credentials. | 4.00 | 0.00 | Strongly Agree |
| SE3. I feel confident relying on the system's alerts to prevent unauthorized individuals or outsiders from bypassing the gate. | 3.60 | 0.49 | Strongly Agree |
| SE4. The system effectively detects and alerts me to common security threats, such as students passing back IDs for others to use. | 4.00 | 0.00 | Strongly Agree |
| SE5. The implementation of this system enhances the overall physical security of the campus compared to traditional visual- check methods. | 3.80 | 0.40 | Strongly Agree |

Indicator codes: FS = Functional Suitability; PE = Performance Efficiency; CO = Compatibility; US = Usability; RE = Reliability; SE = Security; MA = Maintainability; PO = Portability. Original item wording is retained. SD (N) follows the Chapter 3 formula.

### 4.5.4 Faculty Event Organizer and Administrative Evaluation

**Table 4.18. Faculty / Event Organizer / Admin Category Results**

| Category | Items | Mean | Item SD range | Interpretation |
|---|---|---|---|---|
| Functional Suitability | 5 | 3.80 | 0.00-0.49 | Highly Satisfactory |
| Performance Efficiency | 5 | 3.68 | 0.00-0.80 | Highly Satisfactory |
| Compatibility | 4 | 3.75 | 0.00-0.49 | Highly Satisfactory |
| Usability | 5 | 3.88 | 0.00-0.40 | Highly Satisfactory |
| Reliability | 5 | 3.76 | 0.00-0.49 | Highly Satisfactory |
| Security | 5 | 3.84 | 0.00-0.49 | Highly Satisfactory |
| Maintainability | 5 | 3.88 | 0.00-0.49 | Highly Satisfactory |
| Portability | 5 | 3.92 | 0.00-0.40 | Highly Satisfactory |
| Overall (equal category weights) | 39 | 3.81 | See note | Highly Satisfactory |

The SD range gives the minimum and maximum individual-item SD within each category, using N in the denominator. It is not a category-composite SD. Overall means give equal weight to categories; respondent-level composite SD cannot be recovered from item-frequency totals alone.

The faculty, event-organizer, and administrative group gave an overall equal-category mean of 3.81, interpreted as Highly Satisfactory. Portability received the highest category mean at 3.92. Usability and Maintainability each received 3.88, followed by Security at 3.84, Functional Suitability at 3.80, Reliability at 3.76, Compatibility at 3.75, and Performance Efficiency at 3.68.

The lowest item mean, 3.40, concerned maintaining dashboard performance while processing or storing large amounts of attendance data. All administrative item means remained in the Strongly Agree range. The favorable maintainability and compatibility ratings are reported as respondent assessments; the supplied tallies do not establish the extent of source-code review, technical expertise, or cross-platform testing undertaken by each respondent.

**Table 4.19. Faculty / Event Organizer / Admin Item Results (n = 5)**

| Survey indicator | Mean | SD (N) | Response interpretation |
|---|---|---|---|
| FS1. The dashboard provides all necessary features to successfully create, configure, and manage university events. | 3.60 | 0.49 | Strongly Agree |
| FS2. The system effectively categorizes and generates accurate attendance records by College, Section, Year Level, and specific Event. | 4.00 | 0.00 | Strongly Agree |
| FS3. The system allows me to easily configure and switch between different verification modes as required for specific events or gate protocols. | 3.60 | 0.49 | Strongly Agree |
| FS4. The centralized monitoring and alert functions provide relevant and accurate information for administrative oversight. | 3.80 | 0.40 | Strongly Agree |
| FS5. Overall, the system's administrative features completely align with the needs of event organizers and faculty members. | 4.00 | 0.00 | Strongly Agree |
| PE1. The centralized dashboard loads quickly and reflects real-time gate and event attendance data without noticeable delay. | 3.80 | 0.40 | Strongly Agree |
| PE2. Generating complex attendance reports (e.g., filtering by multiple categories) is processed and completed in a reasonable amount of time. | 4.00 | 0.00 | Strongly Agree |
| PE3. The system handles multiple administrative actions smoothly, even when receiving rapid data inputs from the verification terminals. | 3.60 | 0.49 | Strongly Agree |
| PE4. The dashboard maintains consistent performance and does not slow down when processing or storing large amounts of attendance data. | 3.40 | 0.80 | Strongly Agree |
| PE5. The overall speed of the backend system meets my expectations for efficient event and attendance management. | 3.60 | 0.49 | Strongly Agree |
| CO1. The exported attendance reports (e.g., CSV , Excel, PDF) are fully compatible with standard office software used by the university. | 4.00 | 0.00 | Strongly Agree |
| CO2. The system operates smoothly without negatively interfering with other existing administrative software or network tools. | 3.60 | 0.49 | Strongly Agree |
| CO3. The generated data is structured in a way that could easily be integrated with existing university databases in the future. | 3.80 | 0.40 | Strongly Agree |
| CO4. The system performs consistently regardless of the type of computer or operating system I use to access the admin dashboard. | 3.60 | 0.49 | Strongly Agree |
| US1. The layout and navigation menus of the centralized dashboard are logical, well-organized, and easy to understand. | 3.80 | 0.40 | Strongly Agree |
| US2. The process of generating reports and filtering attendance lists is straightforward and does not require complex steps. | 4.00 | 0.00 | Strongly Agree |
| US3. I am able to create a new event, assign verification modes, and launch the monitoring screen with minimal effort. | 3.80 | 0.40 | Strongly Agree |
| US4. The system provides clear visual feedback or error messages if I input incomplete or incorrect settings during event creation. | 3.80 | 0.40 | Strongly Agree |
| US5. I would require very minimal training to confidently use all the administrative functions of this system. | 4.00 | 0.00 | Strongly Agree |
| RE1. The centralized dashboard operates consistently without unexpected crashes, freezing, or downtime during active monitoring. | 3.80 | 0.40 | Strongly Agree |
| RE2. All configured event settings and recorded attendance data are saved securely without unexpected loss of records. | 3.60 | 0.49 | Strongly Agree |
| RE3. In the event of a minor disruption (e.g., a brief internet drop), the dashboard recovers quickly and resumes synchronization properly. | 3.60 | 0.49 | Strongly Agree |
| RE4. I can trust the system to accurately reflect exactly what is happening at the verification terminals without data discrepancies. | 4.00 | 0.00 | Strongly Agree |
| RE5. The administrative system demonstrates a high level of stability from setup to the conclusion of the event evaluation. | 3.80 | 0.40 | Strongly Agree |
| SE1. The system requires proper authentication (e.g., secure admin login) before granting access to the centralized dashboard. | 4.00 | 0.00 | Strongly Agree |
| SE2. Administrative privileges are strictly enforced, ensuring unauthorized users or students cannot alter event settings or attendance records. | 4.00 | 0.00 | Strongly Agree |
| SE3. Sensitive student data within the attendance database is adequately protected from unauthorized viewing or extraction. | 3.80 | 0.40 | Strongly Agree |
| SE4. The system architecture prevents common data manipulations, ensuring that generated attendance reports are legitimate and untampered. | 3.60 | 0.49 | Strongly Agree |
| SE5. I feel confident that the system provides a secure environment for managing university-level event data. | 3.80 | 0.40 | Strongly Agree |
| MA1. The source code is clean, well-structured, and easy to read, following standard coding guidelines and best practices. | 3.60 | 0.49 | Strongly Agree |
| MA2. The codebase is thoroughly documented with clear comments and technical documentation, making it easy for other developers to understand and navigate. | 4.00 | 0.00 | Strongly Agree |
| MA3. The system architecture uses modular design principles (e.g., separated components, reusable functions) that make it easy to isolate and update specific features. | 4.00 | 0.00 | Strongly Agree |
| MA4. The code allows for seamless debugging, error logging, and troubleshooting when identifying and fixing software bugs or system issues. | 4.00 | 0.00 | Strongly Agree |
| MA5. Future updates, modifications, or scaling of the system can be implemented with minimal effort and without breaking existing core functionalities. | 3.80 | 0.40 | Strongly Agree |
| PO1. The dashboard can be easily accessed from different devices (e.g., desktop PC or laptop) without losing essential functionality. | 4.00 | 0.00 | Strongly Agree |
| PO2. The system interface adapts well to different monitor screen sizes and resolutions in various university offices. | 4.00 | 0.00 | Strongly Agree |
| PO3. Accessing the administrative system on a new computer requires no complex installations or difficult setup procedures. | 4.00 | 0.00 | Strongly Agree |
| PO4. The system is flexible enough to be deployed and managed for events located in different buildings or areas across the campus. | 3.80 | 0.40 | Strongly Agree |
| PO5. Transitioning the management of the system from one administrative user environment to another is a seamless process. | 3.80 | 0.40 | Strongly Agree |

Indicator codes: FS = Functional Suitability; PE = Performance Efficiency; CO = Compatibility; US = Usability; RE = Reliability; SE = Security; MA = Maintainability; PO = Portability. Original item wording is retained. SD (N) follows the Chapter 3 formula.

### 4.5.5 Manual Process Pre-Evaluation

**Table 4.20. Manual Pre-Evaluation Category Results**

| Category | Items | Mean | Item SD range | Interpretation |
|---|---|---|---|---|
| Functional Suitability | 5 | 2.61 | 1.04-1.13 | Satisfactory |
| Performance Efficiency | 4 | 1.96 | 0.91-1.00 | Unsatisfactory |
| Usability | 5 | 2.66 | 0.92-1.06 | Satisfactory |
| Reliability | 5 | 2.29 | 0.93-1.06 | Unsatisfactory |
| Security | 5 | 2.33 | 1.06-1.13 | Unsatisfactory |
| Overall (equal category weights) | 24 | 2.37 | See note | Unsatisfactory |

The SD range gives the minimum and maximum individual-item SD within each category, using N in the denominator. It is not a category-composite SD. Overall means give equal weight to categories; respondent-level composite SD cannot be recovered from item-frequency totals alone.

The manual pre-evaluation produced an overall equal-category mean of 2.37, interpreted as Disagree / Unsatisfactory. Usability and Functional Suitability received 2.66 and 2.61, respectively, both interpreted as Agree / Satisfactory. Security, Reliability, and Performance Efficiency received 2.33, 2.29, and 1.96, all interpreted as Disagree / Unsatisfactory. The pattern suggests that respondents regarded paper sign-in as understandable while expressing less favorable views of speed, consistency, and protection of information.

The lowest manual item mean was 1.89 for the statement that the overall speed met expectations for event attendance. The highest was 2.76 for the statement that the process was straightforward for students with different levels of familiarity. These survey responses provide perceived-process evidence alongside the independently recorded writing durations.

Several manual questionnaire statements refer to identity checking, although the timed paper-attendance simulation did not include an ID or enrollment check. Agreement with those statements does not establish that a check occurred during the timed procedure. The survey and observation are therefore interpreted according to their respective measurement content.

**Table 4.21. Manual Pre-Evaluation Item Results (n = 70)**

| Survey indicator | Mean | SD (N) | Response interpretation |
|---|---|---|---|
| FS1. The manual process provides a clear way to verify my identity when attending a university event. | 2.57 | 1.13 | Agree |
| FS2. The manual process accurately and completely records my entry information. | 2.63 | 1.10 | Agree |
| FS3. The paper-based attendance procedures are relevant to monitoring student attendance. | 2.67 | 1.04 | Agree |
| FS4. The manual process provides clear confirmation that my attendance has been recorded (e.g., staff confirmation or completed sign-in). | 2.49 | 1.07 | Disagree |
| FS5. Overall, the manual process meets the basic requirements for recording event attendance. | 2.71 | 1.06 | Agree |
| PE1. The manual process is completed quickly when I sign the attendance sheet. | 2.13 | 0.94 | Disagree |
| PE2. The manual process remains manageable even when there is a long line of students. | 1.91 | 0.91 | Disagree |
| PE3. The manual process maintains a consistent processing speed throughout the attendance procedure. | 1.91 | 1.00 | Disagree |
| PE4. The overall speed of the manual process meets my expectations for event attendance. | 1.89 | 0.98 | Disagree |
| US1. The steps for paper sign-in are easy to understand and follow. | 2.74 | 0.97 | Agree |
| US2. The instructions provided by the event staff clearly explain what I need to do. | 2.64 | 0.99 | Agree |
| US3. The arrangement of the sign-in sheet, identification-checking procedure, and related materials makes the process easy to use. | 2.46 | 0.92 | Disagree |
| US4. I can complete the paper sign-in process with minimal or no assistance. | 2.70 | 1.06 | Agree |
| US5. The manual process is straightforward enough to be used by students with different levels of familiarity with the procedure. | 2.76 | 1.03 | Agree |
| RE1. My attendance is recorded consistently without being missed, duplicated, or improperly recorded. | 2.31 | 1.02 | Disagree |
| RE2. If an error occurs in the written information or identity check, it can be corrected before the record is finalized. | 2.39 | 0.96 | Disagree |
| RE3. I trust that the paper attendance sheet or manual record accurately reflects my attendance. | 2.37 | 0.93 | Disagree |
| RE4. The manual process can still be completed under different physical conditions, such as crowded queues or limited writing space. | 2.16 | 1.06 | Disagree |
| RE5. The manual verification and attendance process remains stable from the beginning to the end of the procedure. | 2.23 | 1.06 | Disagree |
| SE1. The manual process limits unnecessary exposure of my personal information on attendance sheets. | 2.31 | 1.06 | Disagree |
| SE2. Manual identity checking provides enough information to verify that the person presenting the identification is the rightful student. | 2.33 | 1.13 | Disagree |
| SE3. I feel confident that my student and attendance information is handled securely during the manual process. | 2.33 | 1.08 | Disagree |
| SE4. The manual process includes measures that help prevent common problems such as proxy attendance, or unauthorized signing. | 2.30 | 1.13 | Disagree |
| SE5. The attendance process adequately protects the confidentiality and security of event attendance information. | 2.39 | 1.09 | Disagree |

Indicator codes: FS = Functional Suitability; PE = Performance Efficiency; CO = Compatibility; US = Usability; RE = Reliability; SE = Security; MA = Maintainability; PO = Portability. Original item wording is retained. SD (N) follows the Chapter 3 formula.

### 4.5.6 Descriptive Manual and System Rating Comparison

**Table 4.22. Descriptive Comparison of Manual and Student System Ratings**

| Category | Manual mean | System mean | Difference in scale points |
|---|---|---|---|
| Functional Suitability | 2.61 | 3.77 | 1.16 |
| Performance Efficiency | 1.96 | 3.76 | 1.80 |
| Usability | 2.66 | 3.75 | 1.09 |
| Reliability | 2.29 | 3.73 | 1.44 |
| Security | 2.33 | 3.79 | 1.45 |
| Overall equal-category mean | 2.37 | 3.76 | 1.39 |

![Figure 4.11. Manual and System Survey Category Means](C:/Users/justine mendoza/source/repos/NFC_System/output/chapter4/Figure-Manual-and-System-Survey.png)

The figure compares category means from 70 manual pre-evaluation responses per item and 70 student system-evaluation responses per item. It illustrates descriptive differences in reported perceptions.

The system received higher category means in all five shared categories. The largest difference was 1.80 scale points in Performance Efficiency, while the overall equal-category difference was 1.39 points. This pattern is consistent with more favorable perceptions of the system process in the supplied evaluation data.

The item wording is adapted to each process, and the supplied tallies do not link individual manual and system responses. No paired significance test or causal effect is therefore claimed. The differences represent descriptive rating contrasts rather than measured percentage improvements in speed, security, or reliability. Survey ratings also do not supply the missing complete-task timing boundaries required for a direct performance comparison.

## 4.6 Discussion

### 4.6.1 Interpretation of System Performance

The observations show that internal processing operated on a millisecond scale, while PIN and QR interaction occurred on a seconds scale. Fast had the shortest internal latency and recorded-entry spacing. The High-Security interaction distribution had a longer upper tail, illustrating the variation introduced by the observed QR stage. The exports do not establish the reasons for each longer interaction.

Standard's mean internal latency was slightly greater than High-Security's, so the data do not support a simple monotonic relationship between the number of credential stages and internal latency. The fixed mode order also prevents attributing differences in PIN speed solely to the mode. No claim of causal improvement or general campus capacity is made.

### 4.6.2 Interpretation of Manual and Automated Measurements

The manual baseline contains 70 writing durations and provides a reproducible estimate for the stated paper-attendance writing task. Its 21.803-second mean does not include verification, waiting, or later encoding, and its service-equivalent rate does not represent a measured continuous queue.

The automated log-derived rates and component timers address different portions of the workflow. They show the observed behavior of the prototype, but cannot establish a complete-task speed multiplier relative to paper attendance. A fair complete-task benchmark must match the task outcome and the timing boundary. The manual survey's lower Performance Efficiency mean provides complementary evidence of perceived speed, while retaining its separate interpretation as a respondent rating.

### 4.6.3 Interpretation of Security Outcomes

The labelled applicable credential decisions agreed with the expected outcomes in the supplied recorded sample. Correctly rejected wrong PINs, triggered lockouts, and mismatched QR credentials contribute to observed decision correctness. Anti-tailgating and irregular-exit conditions provide separate state-rule evidence rather than ordinary authentication failures.

The 1.32% recorded PIN lockout rate reflects deliberate demonstrations, not an estimate that 1.32% of students would become locked out in ordinary use. Similarly, the 4.11% record-based denial proportion reflects the mixture of demonstrations, participant entries, and reset exits. The controlled sample does not establish resistance to all credential copying, identity impersonation, or real-enrollment risks.

### 4.6.4 Practical Advantages and Their Limits

The prototype provides configurable credential requirements, structured transaction logs, and visible rule-based decisions. These features reduce reliance on a paper-only record for the tested verification workflow and provide traceable outcomes for the observed invalid conditions. The manual simulation lacked an identity or enrollment check, highlighting a procedural omission rather than a measured prevalence of fraudulent attendance.

The software's monitoring, export, and local synchronization capabilities are practical implementation features. Students, security personnel, and administrative respondents gave overall means of 3.76, 3.63, and 3.81, all interpreted as Highly Satisfactory. These findings support favorable perceived quality within the evaluation groups. The manual pre-evaluation mean of 2.37 indicates less favorable perceptions of the paper-based process.

The lower security-personnel ratings for processing speed and dashboard reliability identify practical areas for improvement. Survey endorsement of identity confirmation, credential protection, compatibility, or maintainability should be interpreted within respondents' exposure to those features. Technical certification, reduced fraud incidence, and large-scale operational reliability require direct evidence beyond agreement ratings.

### 4.6.5 Methodological Alignment and Remaining Requirements

Authentication-decision accuracy, record-based transaction denial, and recorded PIN lockout rate are now reported with explicit inclusion rules and researcher-confirmed expected conditions. Timing and throughput measures remain limited by the available observation boundaries. Listing all five Chapter 3 metric headings does not mean every original planned measurement has been fulfilled.

To fully satisfy the original automated processing-time and throughput definitions, the study needs end-to-end NFC-tap-to-outcome durations and a complete observation-period timer for a specified task. To fully answer SOP 6 comparatively, that task and its timing boundary must correspond to the manual task. The required Chapter 3 clarifications should describe the actual measurements and supplementary analyses transparently; they must not imply that missing timing data were collected.

The manual enrollment-check omission can be reported from the stated procedure, but empirical transcription-error, proxy-attendance, and real-enrollment acceptance rates require an independent audit or relevant test outcomes. The manual and system survey results address perceptions of those processes and cannot be substituted for observed incident rates.

Other limitations include a controlled prototype setting, one recorded device, 20 reusable test profiles, fixed mode order, non-blinded researcher classification of expected outcomes, and interspersed invalid-condition tests. The survey data are aggregated by item and contain no respondent identifiers linking manual and system answers. Their sequence and respondent overlap cannot be established from the tallies. The role-specific personnel groups each contain five respondents, and technical review exposure is not documented. The comparison therefore remains descriptive.

## 4.7 Summary

The system observation contains 438 records, including 210 successful entry trials, 210 granted reset exits, and 18 expected invalid-condition denials. Internal latency averaged 11.49, 57.37, and 54.06 ms for Fast, Standard, and High-Security. Recorded interaction averaged 2.505 seconds in Standard and 5.799 seconds in High-Security.

Researcher-labelled applicable credential decisions yielded observed accuracy of 100.00% (222/222), while the overall recorded denial proportion was 4.11% (18/438). Two deliberately triggered lockouts among 152 captured PIN submissions yielded a recorded lockout rate of 1.32%. These are controlled recorded-sample findings, not universal accuracy or ordinary-user failure estimates.

The manual paper-attendance baseline contained 70 writing observations with mean duration of 21.803 seconds and sample SD of 4.436 seconds. The timed manual procedure contained no ID or enrollment validation. System-evaluation means were 3.76 for students, 3.63 for security personnel, and 3.81 for faculty, event-organizer, and administrative respondents, all interpreted as Highly Satisfactory. The manual pre-evaluation mean was 2.37, interpreted as Unsatisfactory.

Together, the observations and surveys document the prototype's measured component performance, tested rule enforcement, and favorable perceived quality. A matched end-to-end performance comparison and independent error or fraud audit remain necessary to establish complete-task speed improvement and comparative incident rates.
