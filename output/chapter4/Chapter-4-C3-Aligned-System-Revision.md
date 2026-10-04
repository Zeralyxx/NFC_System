# Chapter 4: Complete System-Only Revision Aligned with Chapter 3

Reviewed document: Copy of TAP (1).pdf, 15 PDF pages. Page references below refer to this attachment, not the page numbering of the full thesis. Chapter 3 alignment is based on the Performance Metrics Analysis on pages 58-59 of the full thesis. No manual-observation results or comparison section is included.

## 1. Page-by-Page Changes

| Location | Required change |
|---|---|
| Pages 5-6, observation procedure and Table 4 | Retain the 40 + 30 participant protocol, 20 reused cards, fixed mode order, and 438-record classification. Call the 210 eligible observations successful entry trials, not 210 people or the complete set of authentication attempts. |
| Page 7, Table 5 | Correct Fast mean 11.4 to 11.49 ms; Fast maximum 23.66 to 26.36 ms; Standard mean 57.3 to 57.37 ms; Standard median 50.60 to 50.66 ms. Use two decimal places consistently. |
| Pages 7-8, timing subsections | Place them under the explicit Chapter 3 heading Average Processing Time, but state that full NFC-tap-to-completed-verification time cannot be determined from the exports. Retain the internal/component measurements as the available results, not a substitute silently relabelled as full processing time. |
| Page 8, Table 6 | Retain the values. Correct Qr to QR in its caption. Label SD as sample SD. N/A for Fast does not mean a zero-duration physical or authentication transaction. |
| Page 9, Figure 20 | Retain the graphic. Complete the caption as Figure 20. Successful-Entry Timing by Verification Mode. Error bars are sample SD, not confidence intervals. |
| Pages 9-11, recorded-entry section | Use System Throughput Rate as the main metric heading, with Supplementary Recorded-Entry Spacing and Rate below it. Retain Table 7 and Figure 21 only with the timing-boundary limitation and a corresponding supplementary-method paragraph in Chapter 3. |
| Page 11, section 4.3.3 | Rename to Authentication and Security Metrics. Include explicit Authentication Accuracy Rate, Transaction Denial Rate, and PIN Lockout Rate subsections. |
| Page 12, Table 8 | Retain the five denial conditions and counts, but integrate the interpretation into the new metric subsections below. |
| Pages 13-15 | Delete the old 95-record analysis completely, including 84 grants, 11 denials, 88.42%, 50/52, 96.15%, 11.58%, eight anti-tailgating/three irregular-exit records, and the zero-PIN-lockout claim. These belong to a different dataset. |
| Page 15, old Table 6 | Delete it and insert the new Table 10 below. This also corrects the duplicate Table 6 numbering. |

The existing Table 3 remains the functionality-test table. Tables 4-8 keep their current sequence; the new denial-rate table becomes Table 9 and the final summary becomes Table 10. Verify numbering against the complete thesis before final submission.

## 2. Full Replacement for Section 4.3.2

### 4.3.2 System Metrics and Performance

#### Observation Procedure and Record Selection

The system observation was conducted in two batches on August 12 and 13, 2026, involving 40 and 30 participants, respectively. According to the observation protocol, the same 70 participants tested Fast, Standard, and High-Security modes in that fixed order. The exports contained 70 successful entry trials per mode, or 210 successful entry trials across the three modes. These trials should not be interpreted as 210 distinct participants.

Twenty physical NFC cards, preconfigured with PIN and QR credentials, were reused. For each mode, the first batch completed two rounds of 20 entries, while the second batch completed rounds of 20 and 10 entries. The research team performed exit transactions between rounds to reset the card profiles. Team-assisted reset exits were retained for record reconciliation but excluded from the participant entry timing summaries. Explanations of the next mode were provided between mode changes.

**Table 4. Classification of System Records**

| Mode | Batch 1 successful entries | Batch 2 successful entries | Successful entries | Granted reset exits | Denied records | Total records |
|---|---:|---:|---:|---:|---:|---:|
| Fast | 40 | 30 | 70 | 70 | 2 | 142 |
| Standard | 40 | 30 | 70 | 70 | 12 | 152 |
| High-Security | 40 | 30 | 70 | 70 | 4 | 144 |
| Total | 120 | 90 | 210 | 210 | 18 | 438 |

The two exports contained 438 records: 210 successful entries, 210 granted reset exits, and 18 denied records. The primary timing analysis included every record marked Entry and GRANTED; slow successful observations were not removed. Based on the researcher's clarification, the PIN failures, PIN lockouts, QR mismatches, and irregular exit were deliberate demonstrations. The anti-tailgating records resulted from the researcher and students intentionally attempting consecutive entry without an intervening exit. These denied records were therefore treated as expected rejections of invalid conditions, not ordinary participant authentication failures. This classification is supported by the researcher's observation account rather than by verdict codes alone.

Repeated profile names were retained because the same 20 card profiles were reused across participants and modes. Profile names therefore cannot independently establish unique participant identity. No exact duplicate source rows were found. The 70-participant count is based on the observation protocol rather than the number of unique names in the exports.

#### Average Processing Time

Chapter 3 defines average processing time as the duration from NFC card tap to completed verification. The supplied exports contain component timing fields but do not provide independently recorded start and finish timestamps for that entire boundary. Consequently, full NFC-tap-to-completed-verification average processing time cannot be determined from these exports alone. The available internal processing and PIN/QR interaction measurements are reported separately below. They should not be relabelled as complete transaction time or combined with an assumed physical-interaction baseline.

**Internal System Latency**

Internal system latency was analysed using Total System Latency, while database-query time was analysed using Total DB Query Time. These fields describe the instrumented processing components. Database-query time overlaps the internal processing measurements and must not be added again to total system latency.

**Table 5. Internal Latency for Successful Entry Trials**

| Mode | n | Mean system latency (ms) | Median (ms) | Sample SD (ms) | Minimum (ms) | Maximum (ms) | Mean DB time (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|
| Fast | 70 | 11.49 | 11.30 | 2.20 | 8.26 | 26.36 | 2.88 |
| Standard | 70 | 57.37 | 50.66 | 17.46 | 44.55 | 137.47 | 47.95 |
| High-Security | 70 | 54.06 | 52.64 | 6.34 | 49.44 | 86.36 | 44.90 |

Fast mode recorded the lowest mean internal latency at 11.49 ms. Standard and High-Security modes recorded means of 57.37 ms and 54.06 ms, respectively. High-Security mode did not exhibit a higher mean internal latency than Standard mode in these observations. The results therefore do not establish that adding a verification factor necessarily increases mean internal latency. Standard mode also showed greater dispersion, with a sample standard deviation of 17.46 ms and a maximum of 137.47 ms. These findings are descriptive and do not establish causal differences between modes.

**Recorded User Interaction Duration**

The exported user-workflow fields measure the recorded PIN and QR interaction stages. Values were converted from milliseconds to seconds. The fields do not establish the entire NFC-tap-to-completed-verification duration. Fast mode bypasses PIN and QR; its interaction fields are reported as not applicable rather than as zero-second transactions.

**Table 6. Recorded PIN and QR Interaction Durations**

| Mode | Successful trials | Mean PIN duration (s) | Mean QR duration (s) | Mean combined interaction (s) | Median combined interaction (s) | Sample SD (s) | Maximum combined interaction (s) |
|---|---:|---:|---:|---:|---:|---:|---:|
| Fast | 70 | N/A | N/A | N/A | N/A | N/A | N/A |
| Standard | 70 | 2.505 | N/A | 2.505 | 2.035 | 1.152 | 6.875 |
| High-Security | 70 | 2.162 | 3.637 | 5.799 | 4.348 | 3.878 | 22.604 |

Standard mode recorded a mean PIN interaction duration of 2.505 seconds. High-Security mode recorded mean PIN and QR durations of 2.162 and 3.637 seconds, resulting in a mean combined interaction duration of 5.799 seconds. Its median combined duration was 4.348 seconds and its maximum was 22.604 seconds, indicating that longer observations increased the mean. All slow successful observations were retained. The exports alone do not establish why particular QR interactions took longer.

The lower mean PIN duration in High-Security mode should not be interpreted as proof that the mode improves PIN performance. Participants always encountered Standard before High-Security; practice and other order effects were not controlled.

**Figure 20. Successful-Entry Timing by Verification Mode**

Panel A presents mean internal system latency with sample-standard-deviation error bars. Panel B presents recorded PIN/QR interaction-duration distributions for successful entries. Fast-mode interaction duration is not plotted because PIN and QR were bypassed. These measurements do not represent complete NFC-tap-to-completed-verification time.

#### System Throughput Rate

Chapter 3 defines system throughput as successful verifications divided by the elapsed observation period in minutes. The exports do not provide a separately timed start-to-finish period covering all complete transactions in each entry round. Therefore, the planned complete-round throughput cannot be reconstructed directly. A supplementary recorded-entry rate was derived from within-round log timestamps and is reported separately from that planned metric.

**Supplementary Recorded-Entry Spacing and Rate**

For each entry round, the elapsed span was calculated between its first and last successful-entry timestamps. A round containing n recorded entries contains n - 1 observed between-entry intervals. Each mode included four rounds containing 20, 20, 20, and 10 successful entries, yielding 19 + 19 + 19 + 9 = 66 observed intervals. All 70 successful entry trials were retained; 66 applies only to the spacing calculation.

Mean recorded-entry spacing (s) = summed within-round elapsed spans / summed observed intervals.

Recorded-entry rate (intervals/min) = 60 x summed observed intervals / summed within-round elapsed spans in seconds.

**Table 7. Supplementary Recorded-Entry Spacing and Rate**

| Mode | Entry rounds | Observed intervals | Summed spans (s) | Mean spacing (s) | Recorded-entry rate (/min) | Denied records within spans |
|---|---:|---:|---:|---:|---:|---:|
| Fast | 4 | 66 | 180.200 | 2.730 | 21.98 | 2 |
| Standard | 4 | 66 | 531.167 | 8.048 | 7.46 | 8 |
| High-Security | 4 | 66 | 800.508 | 12.129 | 4.95 | 3 |

Fast mode exhibited the shortest mean recorded-entry spacing and the highest recorded-entry rate. Standard and High-Security modes exhibited longer spacing under the observed conditions. Team-assisted reset intervals and gaps between rounds were excluded. However, demonstrations, denied records, and pauses occurring within an entry round remain included in its elapsed span. Thirteen denied records occurred within the selected spans. Removing their rows would not remove the time associated with them.

These supplementary values should not be presented as directly timed complete-transaction throughput or uninterrupted gate capacity. Dividing 70 by the summed first-to-last spans would count an unobserved preceding interval for the first entry in each round. The supplementary rate instead uses the 66 intervals actually observed within those spans. No assumed 2.50-second baseline is used.

**Figure 21. Supplementary Recorded Successful-Entry Rate**

Rates are derived from 66 observed between-entry intervals per mode across four entry rounds. Within-round denials and pauses remain included. The figure does not represent directly timed complete-transaction throughput.

## 3. Full Replacement for Section 4.3.3

### 4.3.3 Authentication and Security Metrics

#### Authentication Accuracy Rate

Chapter 3 specifies authentication accuracy using correct transactions relative to applicable authentication attempts. Establishing whether a transaction is correct requires comparison between the expected authentication decision and the actual system outcome. A valid workflow correctly accepted and an invalid workflow correctly rejected are both correct decisions; a grant alone is not proof of accuracy, and a denial alone is not proof of failure.

The researcher identified the 18 denied records as expected rejections: incorrect PIN submissions, intentionally triggered PIN lockouts, mismatched QR credentials, consecutive entries without an intervening exit, and an irregular exit demonstration. This provides expected-outcome information for the denied conditions. It does not make the grant proportion an accuracy measure. Before calculating overall authentication accuracy, the expected validity of the granted authentication attempts and the completeness of the applicable-attempt set must also be confirmed. The 210 successful entry trials were selected for timing analysis and must not, by selection alone, be used to claim 100% authentication accuracy. Team-assisted reset exits remain separate from the participant entry and security-challenge accuracy analysis. The overall accuracy calculation is therefore pending validation of the remaining attempt labels, rather than being described as having no expected-outcome information at all.

The expected-versus-actual outcomes listed in Table 3 provide separate functionality-test evidence. Those test-case results do not establish an authentication accuracy percentage for the 438 exported records because a documented case is not necessarily one observed authentication attempt and the case table is not mapped to every exported record.

#### Transaction Denial Rate

For the record-based analysis, the transaction denial rate was calculated as the number of denied records divided by the total number of exported records, multiplied by 100. All 438 records were included in this denominator, comprising successful participant entries, granted reset exits, and denied records. This is a recorded-transaction measure, not an ordinary-participant failure rate or a count of unique authentication workflows.

Overall recorded transaction denial rate = (18 / 438) x 100 = 4.11%.

Seventeen denied records were marked Entry and one was marked Exit. Their conditions are presented in Table 8.

**Table 8. Distribution of Logged Denial Conditions**

| Logged condition | Number of records |
|---|---:|
| PIN_FAILURE | 8 |
| ANTI_TAILGATING_VIOLATION | 5 |
| PIN_LOCKED | 2 |
| QR_REPLACED_OR_MISMATCH | 2 |
| IRREGULAR_EXIT_SEQUENCE | 1 |
| Total | 18 |

**Table 9. Recorded Transaction Denial Rate by Mode**

| Mode | Granted records | Denied records | Total records | Recorded denial rate |
|---|---:|---:|---:|---:|
| Fast | 140 | 2 | 142 | 1.41% |
| Standard | 140 | 12 | 152 | 7.89% |
| High-Security | 140 | 4 | 144 | 2.78% |
| Total | 420 | 18 | 438 | 4.11% |

The Standard-mode session had the highest recorded denial proportion. The researcher confirmed that the denied conditions were intentionally produced through demonstrations or student attempts to repeat entry without exiting. The percentages therefore describe the mix of logged outcomes during these sessions; they do not establish that Standard mode is less accurate, less usable, or less reliable than the other modes. The 210 reset exits also affect the denominator and prevent interpretation as a participant-only denial rate.

The condition codes establish which denials were logged, while the researcher's account supplies the expected invalid conditions. This supports interpreting the 18 denials as expected security-rule rejections. No hardware-read or database-timeout denial code appears among these conditions, but this does not establish that no missed scan, cancelled workflow, unlogged error, or hardware/database problem occurred during the observation.

#### PIN Lockout Rate

Chapter 3 defines PIN lockout rate as lockout events divided by total PIN attempts, multiplied by 100. The exports contain two records labelled PIN_LOCKED. The researcher clarified that the lockout conditions were intentionally triggered by reaching three consecutive incorrect PIN submissions, rather than merely rescanning an already locked card. The two records are therefore reported as researcher-confirmed intentional lockout triggers, rather than normal participant usability failures.

The remaining requirement for calculating PIN lockout rate is a verified denominator of individual PIN submissions, including retries. The two PIN_LOCKED records mark their component PIN fields as N/A while retaining nonzero aggregate timing, so counting only populated component PIN fields would omit these submissions. The percentage remains pending validation of the individual PIN-attempt denominator. It is not reported as 0.00%, and the two triggers are not divided by the participant count or the number of successful entry trials.

The lockout test in Table 3 provides separate functionality evidence, if its expected and actual outcomes were documented. It does not supply the denominator required for the observation-period lockout rate. An attempt-level audit or the original database is needed to validate that denominator. The intentional nature of the two triggers must remain explicit when interpreting the eventual percentage.

#### Summary of Chapter 3 Performance Metrics

**Table 10. Summary of System Performance and Security Metrics**

| Metric or observation | Result | Reporting scope |
|---|---|---|
| Average processing time from NFC tap to completed verification | Not determinable from supplied exports | Planned full-boundary metric; component timing is reported separately |
| Mean internal latency, Fast / Standard / High-Security | 11.49 / 57.37 / 54.06 ms | 70 successful entry trials per mode |
| Mean recorded interaction, Standard / High-Security | 2.505 / 5.799 s | Recorded PIN/QR stages; Fast N/A |
| Complete-round system throughput | Not directly measured | No independently timed complete-round start and finish |
| Supplementary recorded-entry rate, Fast / Standard / High-Security | 21.98 / 7.46 / 4.95 per minute | 66 observed intervals per mode; within-round denials and pauses included |
| Authentication accuracy rate | Pending validation of remaining attempt labels | The researcher confirmed expected rejection of the 18 invalid-condition records; granted-attempt labels and completeness still need confirmation |
| Overall recorded transaction denial rate | 4.11% (18/438) | Includes participant entries, reset exits and demonstration records |
| PIN lockout rate | Pending validation of the PIN-attempt denominator | Two intentional lockout triggers confirmed by the researcher |
| Intentional PIN lockout triggers | 2 | Researcher deliberately reached three consecutive incorrect PIN submissions |
| Successful entry trials | 210 | 70 participants tested in three modes according to the protocol |
| Granted reset exits | 210 | Team-assisted resets; excluded from primary participant timing |
| Total exported records | 438 | No exact duplicate source rows found |

#### Data Integrity and Study Limitations

All 438 source records were retained with their source batch, worksheet, and Excel row. The successful-entry component totals reconciled with their aggregate system and user totals. Two denied PIN_LOCKED records had hidden component PIN durations but nonzero aggregate timing. These rows were flagged without altering the original values or treating N/A as zero.

The observations were recorded on one device, Admin Office - 1, and used a fixed Fast-to-Standard-to-High-Security sequence. Practice effects, mode order, card reuse, and interspersed demonstrations limit causal interpretation. The exports do not provide a unique participant identifier independent of the reused card profiles, so paired participant-level comparisons cannot be reconstructed from profile names alone. The results describe the observed sessions and do not establish simultaneous multi-kiosk or campus-wide performance.

Not determinable or pending validation is a limitation disclosure, not a measured zero and not fulfilment of the original measurement plan. The researcher's clarification supplies the expected invalid conditions and two intentional lockout triggers. Finalizing the remaining planned metrics requires confirmation of granted-attempt labels, the individual PIN-attempt denominator, and full-boundary timing evidence. No overall improvement or failure-free-operation conclusion is inferred solely from these exports.

## 4. Minimum Chapter 3 Consistency Changes

Keep all five original metric headings. Do not replace authentication accuracy or PIN lockout rate with recorded-entry rate. Do not rewrite the planned full processing-time definition to imply that component timers captured it completely.

### Add after the existing System Throughput Rate discussion

**Supplementary Log-Derived Analysis**

A supplementary descriptive analysis was performed after reviewing the exported system logs. For each entry round, the elapsed span between the first and last recorded successful entries was divided by the number of observed between-entry intervals, n - 1, to obtain mean recorded-entry spacing. Recorded-entry rate was calculated as 60 multiplied by the summed intervals, divided by the summed elapsed spans in seconds. Reset intervals and gaps between rounds were excluded, while within-round denials and pauses remained included. These supplementary measures were distinguished from the planned complete-transaction processing-time and throughput measurements.

### Clarify the existing metric definitions

- Authentication accuracy: clarify that a correct transaction requires an expected-versus-actual decision comparison. Do not equate the proportion granted with accuracy. If the intended metric is instead successful workflow completion, rename it consistently in both chapters rather than mixing the concepts.
- Transaction denial rate: replace the ambiguous numerator label Failed Transactions with Denied Recorded Transactions, and state that the denominator is Total Recorded Transactions for the mixed-session export analysis. This preserves the record-based calculation while distinguishing it from participant-only or unique-workflow rates.
- PIN lockout rate: state that the numerator counts distinct newly triggered lockout events, while the denominator counts individual PIN submissions, including retries. A subsequent scan of an already locked card is not another newly triggered lockout.
- Statistical treatment: retain the existing treatment of survey ratings; add that the performance timing tables use sample standard deviation with denominator n - 1. Do not apply the sample-SD change retrospectively to unrelated survey tables.
- Add a limitation statement: Where the available records did not establish the timing boundary, applicable-attempt denominator, or expected outcome required by a planned metric, that metric was reported as not determinable rather than replaced with an unsupported estimate.

These are transparent clarifications and a labelled supplementary analysis. They should not imply that uncollected timing or labels were available during the original observation.

## 5. Other System-Only Statements to Correct in This Attachment

These edits prevent claims elsewhere in the chapter from contradicting the qualified metrics.

1. Page 2: replace This ensures 100% state continuity and eliminates false rejections regardless of network fluctuations with: This approach prioritizes local state updates while synchronization is pending and is intended to reduce stale-state conflicts during intermittent connectivity. Its behavior was assessed in the documented test cases. Use the latter sentence only if that testing was actually performed and documented.
2. Pages 2-3: avoid absolute wording such as absolute, immutable source of physical truth, mathematically halting, and ensuring atomic data integrity unless those properties are specifically demonstrated. Time formatting alone does not establish atomic transaction integrity. Describe the implemented mechanism and observed result instead of a universal guarantee.
3. Table 3, T-01: replace Instant Access Granted screen display with Access Granted displayed after successful NFC validation. Instant is not a measured zero-latency claim.
4. Table 3, T-06: rename High-Security Anti-Cloning Check to High-Security QR Credential Mismatch Check. A mismatched/replaced QR test does not prove detection of an identical copy of a valid QR credential. Describe the precise invalid input actually tested.
5. Table 3, T-13: replace the visible literal INSIDE $\\rightarrow$ OUTSIDE notation with INSIDE to OUTSIDE.
6. Table 3: retain PASS statuses only for executed tests with recorded actual results. Do not convert sixteen listed passing test cases into 100% observation authentication accuracy; several cases assess alerts, event eligibility, synchronization, or application behavior rather than independent authentication attempts.
7. Remove the old hardware/database failures = 0 summary row unless an independent complete observation log supports it. The absence of corresponding exported error codes is narrower evidence.
8. Use consistent table-caption punctuation and capitalization. Tables 4-10 and Figures 20-21 should appear in numerical order and use their final numbering in the full thesis.

## 6. Evidence Still Needed for the Uncomputed System Metrics

| Metric | Evidence needed |
|---|---|
| Full average processing time | Start at NFC tap and finish at the completed verification outcome for each applicable trial, using the same boundary across modes |
| Complete-round throughput | Start and finish of an explicitly defined observation period, with successful-verification count and treatment of pauses specified |
| Authentication accuracy | Attempt ID, mode, intended valid/invalid inputs, expected decision, actual decision, and inclusion rules |
| PIN lockout rate | Validate every PIN submission including retries and associate the two researcher-confirmed lockout triggers with the original attempt records |

The current code logs PIN attempts and can use PIN_LOCKED for both a pre-existing lock and a failed submission that triggers locking. The researcher has clarified that the two observed lockouts were deliberately triggered by three wrong submissions. The current source code is not proof of the exact software version used on August 12-13. Validate the original observation database before promoting reconstructed attempt counts into published rates.

### Researcher Validation Note: Candidate PIN-Attempt Denominator

The exports contain 140 successful Standard/High-Security entry records with PIN timing, eight PIN_FAILURE records, two QR-mismatch records with completed PIN stages, and two intentionally triggered PIN_LOCKED records whose aggregate times indicate PIN processing. These identify 152 candidate PIN-submission records. If the original logging behavior confirms one record per individual PIN submission with no omitted or duplicated PIN stages, the recorded PIN lockout rate would be (2 / 152) x 100 = 1.32%. This is a conditional reconstruction, not the finalized published rate. It must not be confused with a participant-level lockout rate or a spontaneous usability-failure rate.
