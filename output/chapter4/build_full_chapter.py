import html
import json
from pathlib import Path
from statistics import mean, stdev

import openpyxl

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_JUSTIFY, TA_LEFT
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.platypus import Image, KeepTogether, Paragraph, Preformatted, SimpleDocTemplate, Spacer, Table, TableStyle
from PIL import Image as PILImage, ImageDraw, ImageFont

ROOT = Path(__file__).parent
data = json.loads((ROOT / 'analysis.json').read_text())
manual = json.loads((ROOT / 'manual.json').read_text())
survey = json.loads((ROOT / 'survey-analysis.json').read_text())
completed = json.loads((ROOT / 'completed-survey-analysis.json').read_text())
entry_book = openpyxl.load_workbook(ROOT / 'Survey-Response-Entry-Template.xlsx', data_only=False)
def rating_label(value):
    if value >= 3.26: return 'Strongly Agree / Highly Satisfactory'
    if value >= 2.51: return 'Agree / Satisfactory'
    if value >= 1.76: return 'Disagree / Unsatisfactory'
    return 'Strongly Disagree / Highly Unsatisfactory'

for group_index, sheet_name in [(0, 'System'), (3, 'Manual')]:
    group = survey['groups'][group_index]
    responses = [[entry_book[sheet_name].cell(r, c).value for c in range(2, 26)] for r in range(2, 72)]
    assert all(type(v) in (int, float) and v in (1, 2, 3, 4) for row in responses for v in row)
    for i, item in enumerate(group['items']):
        values = [row[i] for row in responses]
        item.update(frequencies=[values.count(rating) for rating in (4, 3, 2, 1)], n=70,
                    score=sum(values), mean=mean(values), sample_sd=stdev(values),
                    population_sd=stdev(values)*(69/70)**0.5, interpretation=rating_label(mean(values)))
    group['respondent_descriptive'] = completed['groups'][sheet_name]['descriptive']

for group in survey['groups']:
    for category in group['categories']:
        items = [item for item in group['items'] if item['category'] == category['name']]
        category.update(mean=mean(item['mean'] for item in items),
                        item_sd_min=min(item['sample_sd'] for item in items),
                        item_sd_max=max(item['sample_sd'] for item in items))
        category['interpretation'] = rating_label(category['mean'])
    group['overall_category_mean'] = mean(category['mean'] for category in group['categories'])
    group['interpretation'] = rating_label(group['overall_category_mean'])
survey['individual_response_source'] = completed['source']
survey['individual_response_sha256'] = completed['sha256']
(ROOT / 'chapter-survey-analysis.json').write_text(json.dumps(survey, indent=2), encoding='utf-8')
entry_book.close()
styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name='ChapterTitle', fontName='Times-Bold', fontSize=17, leading=22, alignment=TA_CENTER, spaceAfter=18))
styles.add(ParagraphStyle(name='ChapterBody', fontName='Times-Roman', fontSize=11.5, leading=17, alignment=TA_JUSTIFY, spaceAfter=9, allowWidows=0, allowOrphans=0))
styles.add(ParagraphStyle(name='Section', fontName='Times-Bold', fontSize=14, leading=18, spaceBefore=14, spaceAfter=9, keepWithNext=True))
styles.add(ParagraphStyle(name='Subsection', fontName='Times-Bold', fontSize=12, leading=16, spaceBefore=11, spaceAfter=7, keepWithNext=True))
styles.add(ParagraphStyle(name='Subhead', fontName='Times-Bold', fontSize=11.5, leading=15, spaceBefore=9, spaceAfter=5, keepWithNext=True))
styles.add(ParagraphStyle(name='Cell', fontName='Times-Roman', fontSize=9.2, leading=11.5, alignment=TA_LEFT))
styles.add(ParagraphStyle(name='CellNumber', parent=styles['Cell'], alignment=TA_CENTER))
styles.add(ParagraphStyle(name='CellHeader', parent=styles['Cell'], fontName='Times-Bold'))
styles.add(ParagraphStyle(name='Caption', fontName='Times-Bold', fontSize=10.5, leading=14, spaceBefore=6, spaceAfter=8, keepWithNext=True))
styles.add(ParagraphStyle(name='FigureCaption', parent=styles['Caption'], keepWithNext=False))
styles.add(ParagraphStyle(name='LongTableCaption', parent=styles['Caption'], keepWithNext=False))
styles.add(ParagraphStyle(name='ReviewNote', fontName='Times-Italic', fontSize=10.5, leading=14, spaceAfter=10))
styles.add(ParagraphStyle(name='CodeBlock', fontName='Courier', fontSize=8.5, leading=11, spaceAfter=8))

story, markdown = [], []
def H(level, text):
    story.append(Paragraph(html.escape(text), styles[{1:'ChapterTitle',2:'Section',3:'Subsection',4:'Subhead'}[level]]))
    markdown.append('#' * level + ' ' + text + '\n')

def P(text, note=False):
    story.append(Paragraph(html.escape(text), styles['ReviewNote' if note else 'ChapterBody']))
    markdown.append(text + '\n')

def T(caption, heads, rows, widths=None):
    cells = [[Paragraph(html.escape(str(h)), styles['CellHeader']) for h in heads]]
    for row in rows:
        cells.append([Paragraph(html.escape(str(v)), styles['Cell'] if i == 0 else styles['CellNumber']) for i, v in enumerate(row)])
    widths = widths or [468 / len(heads)] * len(heads)
    table = Table(cells, colWidths=widths, repeatRows=1, hAlign='LEFT')
    table.setStyle(TableStyle([
        ('BACKGROUND',(0,0),(-1,0),colors.HexColor('#E9ECEE')),
        ('GRID',(0,0),(-1,-1),0.45,colors.HexColor('#B9BEC1')),
        ('VALIGN',(0,0),(-1,-1),'MIDDLE'),
        ('LEFTPADDING',(0,0),(-1,-1),6),('RIGHTPADDING',(0,0),(-1,-1),6),
        ('TOPPADDING',(0,0),(-1,-1),6),('BOTTOMPADDING',(0,0),(-1,-1),6),
        ('ROWBACKGROUNDS',(0,1),(-1,-1),[colors.white,colors.HexColor('#F8F9FA')]),
    ]))
    caption_style = styles['LongTableCaption'] if len(rows) > 12 else styles['Caption']
    story.extend([Paragraph(html.escape(caption),caption_style),table,Spacer(1,9)])
    markdown.extend(['**' + caption + '**\n','| ' + ' | '.join(heads) + ' |','|' + '|'.join(['---']*len(heads)) + '|'])
    markdown.extend('| ' + ' | '.join(str(v).replace('|','/') for v in row) + ' |' for row in rows)
    markdown.append('')

def FIG(path, caption, explanation):
    with PILImage.open(path) as im: w,h=im.size
    width=468; height=width*h/w
    if height>300: width*=300/height; height=300
    story.append(KeepTogether([Image(str(path),width=width,height=height),Paragraph(html.escape(caption),styles['FigureCaption'])]))
    markdown.append(f'![{caption}]({path.as_posix()})\n')
    P(explanation)

def CODE(text, caption):
    story.append(KeepTogether([Preformatted(text,styles['CodeBlock']),Paragraph(caption,styles['FigureCaption'])]))
    markdown.extend(['```csharp',text,'```',caption+'\n'])

def make_manual_figure():
    values=[r['seconds'] for r in manual['observations']]
    image=PILImage.new('RGB',(1600,850),'white'); d=ImageDraw.Draw(image)
    f=lambda n,b=False:ImageFont.truetype('C:/Windows/Fonts/'+('arialbd.ttf' if b else 'arial.ttf'),n)
    d.text((75,45),'Manual paper-attendance writing duration',font=f(42,True),fill='#20282A')
    d.text((75,105),'70 observations | first written character to completed required fields',font=f(26),fill='#58656A')
    x0,y0,x1,y1=130,225,1490,650
    bins=list(range(12,40,2)); counts=[sum(lo<=v<lo+2 for v in values) for lo in bins[:-1]]
    maximum=max(counts)+2
    for tick in range(0,maximum+1,2):
        y=y1-tick/maximum*(y1-y0)
        d.line((x0,y,x1,y),fill='#E1E5E7',width=2)
        d.text((x0-20,y),str(tick),font=f(24),fill='#58656A',anchor='rm')
    d.line((x0,y0,x0,y1,x1,y1),fill='#20282A',width=3)
    for i,count in enumerate(counts):
        x=x0+i*(x1-x0)/len(counts)
        w=(x1-x0)/len(counts)
        d.rectangle((x+3,y1-count/maximum*(y1-y0),x+w-3,y1),fill='#178072')
    for tick in range(12,39,4):
        x=x0+(tick-12)/26*(x1-x0)
        d.text((x,y1+30),str(tick),font=f(26),fill='#20282A',anchor='mt')
    d.text((x0,y0-42),'Number of observations',font=f(25),fill='#58656A')
    d.text(((x0+x1)/2,720),'Writing duration in seconds',font=f(27),fill='#20282A',anchor='mt')
    d.text((75,788),'Mean 21.803 s | Median 21.135 s | Sample SD 4.436 s | Range 13.21-37.63 s',font=f(27),fill='#58656A')
    path=ROOT/'Figure-Manual-Writing-Duration.png'; image.save(path); return path

def make_survey_figure():
    image=PILImage.new('RGB',(1500,920),'white'); d=ImageDraw.Draw(image)
    f=lambda n,b=False:ImageFont.truetype('C:/Windows/Fonts/'+('arialbd.ttf' if b else 'arial.ttf'),n)
    d.text((55,35),'Student Ratings of Manual and System Processes',font=f(39,True),fill='#20282A')
    d.ellipse((60,112,80,132),fill='#69757C'); d.text((96,108),'Manual pre-evaluation (n = 70)',font=f(28),fill='#20282A')
    d.ellipse((750,112,770,132),fill='#178072'); d.text((786,108),'System evaluation (n = 70)',font=f(28),fill='#20282A')
    x0,x1,y0,y1=470,1380,235,740
    for tick in [1,1.5,2,2.5,3,3.5,4]:
        x=x0+(tick-1)/3*(x1-x0)
        d.line((x,y0-35,x,y1+20),fill='#DDE2E4',width=2)
        d.text((x,y1+42),f'{tick:g}',font=f(28),fill='#47565C',anchor='mt')
    for i,(system,baseline) in enumerate(zip(survey['groups'][0]['categories'],survey['groups'][3]['categories'])):
        y=y0+i*(y1-y0)/4
        d.text((55,y),system['name'],font=f(29),fill='#20282A',anchor='lm')
        xm=x0+(baseline['mean']-1)/3*(x1-x0); xs=x0+(system['mean']-1)/3*(x1-x0)
        d.line((xm,y,xs,y),fill='#BDC7CC',width=5)
        for x,value,color in [(xm,baseline['mean'],'#69757C'),(xs,system['mean'],'#178072')]:
            d.ellipse((x-11,y-11,x+11,y+11),fill=color)
            d.text((x,y-26),f'{value:.2f}',font=f(29,True),fill=color,anchor='mb')
    d.text(((x0+x1)/2,825),'Weighted mean on the 1-4 response scale',font=f(29),fill='#20282A',anchor='mt')
    d.text((55,883),'Separate respondent groups; corresponding categories use method-specific item wording.',font=f(25),fill='#58656A')
    path=ROOT/'Figure-Manual-and-System-Survey.png'; image.save(path); return path

def survey_summary(group, table_number):
    rows=[[c['name'],c['items'],f"{c['mean']:.2f}",f"{c['item_sd_min']:.2f}-{c['item_sd_max']:.2f}",c['interpretation'].split(' / ')[1]] for c in group['categories']]
    rows.append(['Overall (equal category weights)',sum(c['items'] for c in group['categories']),f"{group['overall_category_mean']:.2f}",'See note',group['interpretation'].split(' / ')[1]])
    T(f"Table 4.{table_number}. {group['name']} Category Results",['Category','Items','Mean','Item Sample SD Range','Interpretation'],rows,[156,36,47,84,145])
    note = 'The SD range gives the minimum and maximum individual-item sample SD within each category, using N - 1 in the denominator. It is not a category-composite SD. Overall means give equal weight to the five categories.'
    if 'respondent_descriptive' in group:
        note += ' Respondent-level category and overall sample SDs, calculated from individual responses, are presented in Table 4.22.'
    else:
        note = note.replace('the five categories', 'categories')
        note += ' Respondent-level composite SD cannot be recovered from item-frequency totals alone.'
    P(note,True)

def survey_items(group, table_number):
    abbreviations={'Functional Suitability':'FS','Performance Efficiency':'PE','Compatibility':'CO','Usability':'US','Reliability':'RE','Security':'SE','Maintainability':'MA','Portability':'PO'}
    rows=[[f"{abbreviations[item['category']]}{item['number']}. {item['question']}",f"{item['mean']:.2f}",f"{item['sample_sd']:.2f}",item['interpretation'].split(' / ')[0]] for item in group['items']]
    T(f"Table 4.{table_number}. {group['name']} Item Results (n = {group['respondents']})",['Survey indicator','Mean','Sample SD','Response interpretation'],rows,[309,43,43,73])
    P('Indicator codes: FS = Functional Suitability; PE = Performance Efficiency; CO = Compatibility; US = Usability; RE = Reliability; SE = Security; MA = Maintainability; PO = Portability. Original item wording is retained. Sample SD uses N - 1.',True)

H(1,'CHAPTER 4 RESULTS AND DISCUSSION')
P('This chapter presents the implementation, documented functionality tests, controlled system observation, manual paper-attendance baseline, and user evaluation of the Risk-Based Two-Factor Student Verification and Event Attendance Management System. The analysis distinguishes software processing, recorded credential interaction, paper-writing duration, log-derived entry spacing, and respondent perceptions. Expected security-rule rejections are interpreted according to the conditions under which they occurred.')

H(2,'4.1 Implementation Details')
H(3,'4.1.1 System Architecture and Development Environment')
P('The prototype uses an ESP32 microcontroller and PN532 NFC reader to acquire credential identifiers. NFC data is transferred through a USB serial connection to a Windows computer running a C# and WinUI 3 application. PIN entry and camera-based QR capture are handled by the application. The processing unit performs student-record lookup, configured eligibility checks, credential validation, transaction-state checks, logging, and administrative monitoring.')
P('The application supports Fast mode using NFC validation, Standard mode using NFC and PIN validation, and High-Security mode using NFC, PIN, and QR credential validation. Operators select the mode according to the configured security requirements. The evaluation does not establish that the system independently inferred risk levels or automatically selected a mode for each participant.')
P('Local MySQL storage supports the prototype\'s operational records, while Firebase Firestore is used for cloud synchronization. Local state and pending-record handling support operation during temporary connectivity interruptions. These implementation capabilities are distinguished from proof of uninterrupted or failure-free operation under all network conditions.')
H(3,'4.1.2 Technical Hurdles Resolved')
P('State synchronization prioritizes confirmed local state changes while remote synchronization is pending. This approach reduces stale-state conflicts affecting entry, exit, and PIN-security state. The documented offline-cache and synchronization tests provide evidence for the tested conditions, not a guarantee of universal state continuity.')
P('Hardware lifecycle handling allows the active window to acquire the NFC serial connection while other windows release it. Dialog sequencing and camera lifecycle handling reduce overlapping operations and invalid-state exceptions. Timestamp serialization and clock-offset handling improve consistency of exported and synchronized records. Formatting timestamps to millisecond precision does not, by itself, prove atomic database integrity.')

H(2,'4.2 Results Presentation')
H(3,'4.2.1 System Interfaces')
P('The following figures present the core application interfaces, including staff authentication, role-specific dashboards, gate monitoring, and the student-facing kiosk.')
interface_info=[
 ('System Authentication Interface','The staff authentication interface requests an authorized credential before displaying the appropriate operational view. Its presence illustrates the intended access workflow, while access-control correctness is evaluated through the documented tests.'),
 ('Administrator Dashboard','The administrator dashboard provides navigation to credential registration, attendance processing, gate verification, student records, security monitoring, and reports. Administrative operations remain distinct from the participant-facing verification workflow.'),
 ('Security Personnel Dashboard','The security view emphasizes verification and monitoring tasks. The role-specific presentation limits the operational modules displayed to the assigned personnel.'),
 ('Event Organizer Dashboard','The event-organizer view presents attendance and reporting functions relevant to event administration. The timing exports analysed below identify entry and exit transactions; they are not separately identified event-attendance transactions.'),
 ('Gate Monitoring Console','The operator console presents the selected verification mode, student information, verification outcomes, and security alerts. Logged decisions from this workflow form the automated observation dataset.'),
 ('Student Facing Kiosk','The kiosk presents access outcomes and the required credential prompts. A visible access decision is distinguished from a measured duration: complete NFC-tap-to-finish timing was not independently recorded in the supplied exports.'),
]
for i,(title,explanation) in enumerate(interface_info,1):
    FIG(ROOT/'full-chapter-assets'/f'Interface-{i}.png',f'Figure 4.{i}. {title}',explanation)
H(3,'4.2.2 Verification Mode Routing')
P('After record and eligibility checks, the verification engine routes the active session to the credential stages required by the selected mode. Exit handling is a separate state-reset pathway. The following excerpt illustrates the mode-dependent routing; it does not contain all eligibility, state, or persistence checks.')
CODE('if (mode == VerificationMode.Fast ||\n    transactionType == TransactionType.Exit)\n{\n    return await GrantAsync(session,\n        "NFC validation passed.");\n}\n\n// Standard and High-Security require PIN.\n// After an accepted PIN, High-Security requires QR.\nif (session.Mode == VerificationMode.HighSecurity &&\n    !session.IsQrFallback)\n{\n    session.NextStep = VerificationStep.RequiresQr;\n}', 'Figure 4.7. Verification Mode Routing Excerpt')

H(2,'4.3 Testing and Performance Evaluation')
H(3,'4.3.1 Functionality Testing Results')
P('The functionality-test record documents the expected and actual results of sixteen selected scenarios. The reported PASS statuses refer to those documented scenarios, not to every observed transaction, every attack type, or all operating conditions. QR mismatch testing is not presented as proof that an identical copy of a valid QR credential can be detected.')
cases=[
 ('T-01','Valid NFC in Fast mode','Grant; update entry state','Granted; state updated','PASS'),
 ('T-02','Valid NFC and correct PIN','Grant Standard entry','Granted; PIN validated','PASS'),
 ('T-03','First or second wrong PIN','Reject PIN; allow retry','Invalid-PIN prompt displayed','PASS'),
 ('T-04','Third consecutive wrong PIN','Lock credential; generate alert','Lockout and alert reported','PASS'),
 ('T-05','Valid NFC, PIN and matching QR','Grant High-Security entry','Granted after credential checks','PASS'),
 ('T-06','Mismatched QR credential','Deny; generate mismatch alert','QR mismatch denial reported','PASS'),
 ('T-07','Unregistered NFC credential','Deny unknown credential','Unauthorized-credential denial','PASS'),
 ('T-08','Inactive configured student record','Deny before PIN/QR','Inactive-status denial','PASS'),
 ('T-09','Expelled configured student record','Deny before PIN/QR','Expelled-status denial','PASS'),
 ('T-10','Registered restricted-event attendee','Record attendance','Attendance recorded','PASS'),
 ('T-11','Unregistered restricted-event attendee','Deny event access','Unregistered-event denial','PASS'),
 ('T-12','Consecutive entry without exit','Deny state-rule violation','Anti-tailgating denial','PASS'),
 ('T-13','Entry followed by exit','Update inside/outside state','Both transitions reported','PASS'),
 ('T-14','Network disconnected','Use local verification data','Local verification reported','PASS'),
 ('T-15','Network restored','Synchronize pending records','Synchronization reported','PASS'),
 ('T-16','Application close with pending records','Require authorized close action','Admin-PIN interception reported','PASS'),
]
T('Table 4.1. Documented Functionality Test Cases',['Case','Test condition','Expected result','Reported actual result','Status'],cases,[38,120,120,150,40])
P('The test-case results demonstrate the documented behavior for the tested inputs. They are not combined with the observation records to create an inflated authentication-accuracy denominator, because the source does not map each test case to every exported attempt.')

H(3,'4.3.2 System Metrics and Performance')
H(4,'Observation Procedure and Record Selection')
P('System observation was conducted on August 12 and 13, 2026, in batches of 40 and 30 participants. According to the researcher\'s protocol, the same 70 participants tested Fast, Standard, and High-Security modes in that fixed order. The exports contain 70 successful entry trials per mode, or 210 successful entry trials across modes. This is not a sample of 210 distinct people.')
P('Twenty NFC cards, preconfigured with PIN and QR credentials, were reused. For each mode, the first batch completed two rounds of 20 entries, while the second completed rounds of 20 and 10 entries. The research team performed exit transactions to reset the profiles between rounds. Explanations were provided before switching to the next mode. Reset exits were retained in the record audit but excluded from participant entry timing and authentication-accuracy calculations.')
counts=[[r['mode'] if r['mode']!='HighSecurity' else 'High-Security',r['entry'],r['exits'],r['denied'],r['total']] for r in []]
counts=[]
for mode in ['Fast','Standard','HighSecurity']:
    records=[r for r in data['reconciliation'] if r['mode']==mode]
    counts.append([mode.replace('HighSecurity','High-Security'),sum(r['entry'] for r in records),sum(r['exits'] for r in records),sum(r['denied'] for r in records),sum(r['total'] for r in records)])
counts.append(['Total',210,210,18,438])
T('Table 4.2. Classification of Exported System Records',['Mode','Successful entries','Granted reset exits','Denied records','Total records'],counts,[100,92,92,92,92])
P('All 210 records marked Entry and GRANTED were retained for timing analysis, including slow observations. No exact duplicate source rows were found. Repeated profile names were not removed because test cards were reused. The participant count is supported by the observation protocol, while the exported names identify test profiles rather than 70 uniquely assigned identities.')
P('The researcher confirmed that wrong-PIN, PIN-lockout, QR-mismatch, and irregular-exit records resulted from deliberate demonstrations. Anti-tailgating records resulted from the researcher and students intentionally trying consecutive entry without exiting. These are expected invalid-condition rejections, not ordinary participants unexpectedly failing authentication. The expected-outcome classification comes from the researcher\'s account rather than from the verdict codes alone.')
H(4,'Average Processing Time')
P('Chapter 3 defines average processing time from NFC tap to completed verification. The exports provide internal and interaction-stage timing fields, but not independent start and finish timestamps covering that complete boundary. Accordingly, full automated processing time under the original definition is not determined here. The available component measurements are reported separately and are not combined with an invented physical-interaction baseline.')
latency=[]
for perf in data['performance']:
    a=perf['metrics']['total_system_ms']; db=perf['metrics']['db_ms']
    latency.append([perf['mode'].replace('HighSecurity','High-Security'),70,f"{a['mean']:.2f}",f"{a['median']:.2f}",f"{a['sd']:.2f}",f"{a['min']:.2f}-{a['max']:.2f}",f"{db['mean']:.2f}"])
T('Table 4.3. Internal Timing for Successful Entry Trials',['Mode','n','Mean system (ms)','Median (ms)','Sample SD (ms)','Range (ms)','Mean DB (ms)'],latency,[85,25,67,62,62,100,67])
P('Fast mode recorded mean internal latency of 11.49 ms, compared with 57.37 ms for Standard and 54.06 ms for High-Security. The observations do not show a strictly increasing internal latency with each added credential stage. Standard also showed greater dispersion, with sample SD of 17.46 ms and a maximum of 137.47 ms. Database-query duration overlaps the component processing measurements and must not be added again to Total System Latency.')
T('Table 4.4. Recorded PIN and QR Interaction Durations',['Mode','n','Mean PIN (s)','Mean QR (s)','Mean total (s)','Median total (s)','Sample SD (s)','Maximum (s)'],[
 ['Fast',70,'N/A','N/A','N/A','N/A','N/A','N/A'],
 ['Standard',70,'2.505','N/A','2.505','2.035','1.152','6.875'],
 ['High-Security',70,'2.162','3.637','5.799','4.348','3.878','22.604'],
],[83,25,59,59,63,63,58,58])
P('Standard recorded mean PIN interaction of 2.505 seconds. High-Security recorded mean PIN and QR interactions of 2.162 and 3.637 seconds, yielding mean combined interaction of 5.799 seconds. Its median was 4.348 seconds and maximum was 22.604 seconds, so longer observations increased the mean. Slow successful observations were retained. Fast bypassed PIN and QR; N/A does not mean zero-duration physical entry.')
P('The lower mean High-Security PIN duration cannot be attributed to the mode itself. Mode order was fixed and respondents had already practised PIN entry in Standard mode. The results are descriptive; no causal or paired participant-level inference is made.')
FIG(ROOT/'Figure-System-Timing.png','Figure 4.8. Successful Entry Timing by Verification Mode','Panel A shows mean internal latency with sample-SD error bars. Panel B shows the distribution of recorded interaction duration. Neither panel measures complete NFC-tap-to-finish processing time.')
H(4,'System Throughput Rate')
P('The original Chapter 3 throughput formula requires a successful-verification count and a defined elapsed observation period. Independent complete-round start and finish times were not supplied. Therefore, the following log-derived recorded-entry rate is presented as a supplementary measure, not as measured full-round throughput or uninterrupted campus capacity.')
P('Each mode contained four entry rounds with 20, 20, 20, and 10 entries. A round with n entries contains n - 1 recorded between-entry intervals, giving 19 + 19 + 19 + 9 = 66 intervals per mode. Mean spacing equals summed within-round first-to-last spans divided by 66. Recorded-entry rate equals 60 multiplied by 66 divided by the summed spans in seconds.')
spacing=[[r['mode'].replace('HighSecurity','High-Security'),4,66,f"{r['span_s']:.3f}",f"{r['spacing_s']:.3f}",f"{r['rate_per_min']:.2f}",r['denied_within']] for r in data['pooled_spacing']]
T('Table 4.5. Supplementary Recorded Entry Spacing and Rate',['Mode','Rounds','Intervals','Summed spans (s)','Mean spacing (s)','Rate (/min)','Within-span denials'],spacing,[83,40,48,82,75,62,78])
P('Fast recorded mean spacing of 2.730 seconds and a rate of 21.98 intervals per minute. Standard and High-Security recorded mean spacing of 8.048 and 12.129 seconds and rates of 7.46 and 4.95. Between-round reset intervals and explanations were excluded, while any within-round denials, demonstrations, or pauses remained included. Thirteen denied records fell within the selected spans. Deleting their rows would not delete the associated elapsed time.')
FIG(ROOT/'Figure-Recorded-Entry-Rate.png','Figure 4.9. Supplementary Recorded Successful Entry Rate','The displayed rates use 66 observed intervals per mode, not 70 complete individually timed transactions. Within-round invalid attempts and pauses remain included.')

H(3,'4.3.3 Authentication and Security Metrics')
H(4,'Authentication Accuracy Rate')
P('Authentication accuracy was operationalized as agreement between the researcher-confirmed expected credential decision and the recorded actual outcome. The researcher confirmed that granted participant entries used expected-valid test credentials and that no invalid attempts were granted. The incorrect PIN, triggered lockout, and QR-mismatch attempts were expected to be denied. Correct rejection is therefore counted as a correct decision rather than an authentication failure.')
P('The applicable authentication-decision set contains 210 valid granted entries and 12 credential-related expected denials: eight PIN failures, two newly triggered PIN lockouts, and two QR mismatches. Five anti-tailgating records were excluded because they were pre-authentication entry-state violations, and one irregular exit was excluded because it was an exit-state test. The 210 granted reset exits were also excluded. These rules yield 222 recorded applicable authentication decisions, not 222 distinct students or NFC sessions.')
T('Table 4.6. Observed Credential Decision Accuracy',['Mode','Expected-valid grants','Expected-invalid denials','Applicable decisions','Correct decisions','Accuracy'],[
 ['Fast',70,0,70,70,'100.00%'],['Standard',70,9,79,79,'100.00%'],['High-Security',70,3,73,73,'100.00%'],['Total',210,12,222,222,'100.00%'],
],[87,79,82,80,78,62])
P('Observed credential-decision accuracy = (222 correct decisions / 222 applicable recorded decisions) x 100 = 100.00%. This is a descriptive result for known valid test credentials and deliberately invalid challenge conditions. It is not proof of universal accuracy, resistance to every attack, or verification of each participant\'s real identity. Twenty preconfigured cards were reused by 70 people, so the observation primarily tested configured credential validity and rule enforcement, not exclusive ownership of real university credentials. Expected outcomes were classified from the researcher\'s observation account, not through a blinded independent assessor.')
H(4,'Transaction Denial Rate')
T('Table 4.7. Logged Denial Conditions',['Condition','Records','Researcher confirmed condition'],[
 ['PIN_FAILURE',8,'Deliberately incorrect PIN submissions'],['PIN_LOCKED',2,'Deliberately reached third consecutive wrong PIN'],['QR_REPLACED_OR_MISMATCH',2,'Deliberately mismatched QR credential'],['ANTI_TAILGATING_VIOLATION',5,'Consecutive entry without an intervening exit'],['IRREGULAR_EXIT_SEQUENCE',1,'Deliberately invalid exit-state sequence'],['Total',18,'Expected invalid-condition rejections'],
],[175,50,243])
T('Table 4.8. Recorded Transaction Denial Rate',['Mode','Granted records','Denied records','Total records','Denial rate'],[
 ['Fast',140,2,142,'1.41%'],['Standard',140,12,152,'7.89%'],['High-Security',140,4,144,'2.78%'],['Total',420,18,438,'4.11%'],
],[105,90,90,90,93])
P('Recorded transaction denial rate = (18 / 438) x 100 = 4.11%. The denominator includes participant entry records, operator-assisted reset exits, and deliberate invalid-condition records. It is not a participant-only failure rate. Standard\'s higher denial proportion reflects the tested mix, including deliberate wrong-PIN and lockout demonstrations, and is not evidence that the mode is less reliable.')
P('No hardware-read or database-timeout code appears among these 18 denial conditions. This observation is narrower than a claim of zero hardware or database failures; missed scans, cancellations, and errors not represented by such codes were not independently audited in this dataset.')
H(4,'PIN Lockout Rate')
P('The researcher confirmed that both PIN_LOCKED records resulted from deliberately reaching the third consecutive incorrect PIN submission, rather than repeat scans of an already locked credential. The researcher also confirmed that every individual PIN submission was captured in the exports. The recorded denominator was reconstructed from PIN-bearing entry outcomes, including retries and correct PIN stages followed by QR denial.')
T('Table 4.9. Recorded PIN Submissions and Lockout Rate',['Mode','PIN stages in granted entries','PIN_FAILURE','Triggered PIN_LOCKED','PIN passed then QR denied','Total PIN submissions','Lockout rate'],[
 ['Standard',70,7,2,0,79,'2.53%'],['High-Security',70,1,0,2,73,'0.00%'],['Total',140,8,2,2,152,'1.32%'],
],[84,77,56,67,76,64,44])
P('Recorded PIN lockout rate = (2 newly triggered lockouts / 152 recorded PIN submissions) x 100 = 1.32%. The 152 submissions comprise 140 PIN stages in successful entries, eight PIN-failure records, two triggering lockout submissions, and two correct PIN stages followed by QR mismatch. Fast mode and reset exits did not require PIN and were excluded. The denominator counts individual submissions, not participants or successful workflows.')
P('The export hides the PIN component fields for PIN_LOCKED records even though their aggregate timings retain PIN-related time. These two submissions were retained based on the triggered-lockout condition, nonzero aggregate timing, and researcher confirmation; counting only populated PIN component fields would omit them. The percentage describes intentional test-induced lockouts, not spontaneous usability failures among students.')

H(3,'4.3.4 Manual Paper Attendance Baseline')
P('The manual baseline contains 70 reported individual durations from a simulated paper-based event-attendance process. Participants wrote their name, course and section, student ID, and signature. The researcher clarified that timing began with the first written character and ended when all required information was completed. Personnel did not check an ID or enrollment status during this simulation.')
P('The measurement therefore represents active paper-attendance writing duration. It excludes any unrecorded time before the first character, queue waiting, acquisition or handover of the pen, and subsequent validation or encoding. It should not be labelled complete manual identity verification time.')
T('Table 4.10. Manual Paper Attendance Writing Duration',['Measure','Result'],[
 ['Number of reported observations',70],['Sum of individual writing durations','1,526.24 s'],['Mean writing duration','21.803 s'],['Median writing duration','21.135 s'],['Sample standard deviation','4.436 s'],['Minimum writing duration','13.21 s'],['Maximum writing duration','37.63 s'],['Service-time-equivalent rate','2.75 writing completions/min'],
],[290,178])
P('The recalculated mean of 21.803 seconds agrees with the sheet\'s rounded mean of 21.80 seconds. The median was 21.135 seconds, with sample SD of 4.436 seconds and a range of 13.21-37.63 seconds. The 1,526.24-second sum is accumulated writing time, not an independently measured continuous queue duration.')
P('The service-time-equivalent rate was calculated as 60 / 21.803 = 2.75 writing completions per minute. It describes hypothetical consecutive use of the measured writing time without additional gaps. It is not directly observed queue throughput because transition and waiting intervals were not supplied.')
FIG(make_manual_figure(),'Figure 4.10. Manual Paper Attendance Writing Duration','The distribution presents all 70 supplied writing durations. It does not include queue waiting or identity and enrollment validation.')

H(3,'4.3.5 Descriptive Comparison Addressing SOP 6')
H(4,'Average Processing Time per Student')
P('The manual observation establishes mean active writing duration of 21.803 seconds. The automated exports establish internal system latency and recorded PIN/QR interaction duration, but not the complete NFC-tap-to-completed-verification duration defined in Chapter 3. Moreover, the manual test records event-attendance writing, whereas the analysed automated exports identify entry/exit verification without a specific event identifier. The datasets therefore differ in both task content and timing boundaries.')
T('Table 4.11. Timing Measurements and Their Boundaries',['Method or stage','Available mean','Measurement boundary'],[
 ['Manual paper attendance','21.803 s','First character to completion of required fields'],['Fast internal processing','11.49 ms','Instrumented system components only'],['Standard PIN interaction','2.505 s','Recorded PIN interaction stage'],['High-Security PIN plus QR interaction','5.799 s','Recorded PIN and QR stages'],['Full automated processing time','Not established','NFC tap to completed verification'],
],[155,83,230])
P('These measurements describe the observed stages of each process. A percentage reduction in complete processing time cannot be established from their different boundaries. A matched comparison requires automated timing for the same attendance or verification task and an explicitly comparable start and finish boundary.')
H(4,'Students Processed per Minute')
P('The manual service-time-equivalent rate is 2.75 completions per minute. The automated recorded-entry rates are 21.98, 7.46, and 4.95 intervals per minute for Fast, Standard, and High-Security. These rates arise from different denominators: summed manual writing times versus automated first-to-last timestamp intervals that include within-round pauses and invalid attempts. They are supplementary descriptive rates, not matched measures of overall throughput. No manual-versus-system throughput multiplier is claimed.')
H(4,'Human Error Proxy Attendance and Unverified Enrollment')
P('Because the manual procedure contained no ID or enrollment-status check, all 70 recorded manual attendance-writing observations proceeded without that validation step. This corresponds to 70/70, or 100%, unchecked enrollment observations under the stated simulation procedure. It does not mean that all respondents were unenrolled or that 100% of entries were false. Student IDs written on paper were self-reported and were not independently validated in the timed procedure.')
P('The automated prototype checks configured credential and student-record conditions and demonstrated rejection of deliberately invalid credentials and entry-state violations. However, its 20 reusable test profiles do not establish live university enrollment verification or unique identity validation for all 70 physical participants. The separate documented inactive/expelled tests show configured rule behavior, not the prevalence of enrollment violations in the observation sample.')
P('The manual pre-evaluation provides additional evidence about respondents\' perceptions of reliability and security, as presented in Section 4.5. It does not measure the incidence of transcription errors, proxy attendance, or false enrollment acceptance. Those rates require an independent record audit or documented challenge attempts. SOP 6 is addressed through the observed validation procedure, demonstrated configured controls, and the descriptive perception comparison, with empirical error and proxy-attendance rate comparisons remaining incomplete.')

H(3,'4.3.6 Summary of Measured Results')
T('Table 4.12. Performance and Security Summary',['Metric','Result','Scope'],[
 ['Automated average full processing time','Not established','Original NFC-tap-to-finish timing boundary not supplied'],
 ['Internal latency Fast / Standard / High-Security','11.49 / 57.37 / 54.06 ms','70 successful entry trials per mode'],
 ['Recorded Standard / High-Security interaction','2.505 / 5.799 s','PIN/QR interaction components'],
 ['Observed credential-decision accuracy','100.00% (222/222)','Researcher-labelled applicable recorded decisions'],
 ['Recorded transaction denial rate','4.11% (18/438)','Includes reset exits and invalid-condition tests'],
 ['Recorded PIN lockout rate','1.32% (2/152)','Intentional triggers; individual captured PIN submissions'],
 ['Mean manual writing duration','21.803 s','70 reported writing observations'],
 ['Matched complete-task time reduction','Not established','Different tasks and timing boundaries'],
 ['Directly observed complete-round throughput','Not established','No complete-round observation start and finish'],
 ['Manual enrollment check omission','100% (70/70)','No enrollment check in the stated manual procedure'],
 ['Real-enrollment false acceptance / proxy attendance rate','Not established','No independent enrollment or proxy-attempt audit'],
],[175,110,183])

H(2,'4.4 Respondent Profile')
P('The system evaluation contained responses from 70 students, five security personnel, and five faculty, event-organizer, or administrative respondents. These role groups account for 80 system-evaluation respondents. Each student and security-personnel instrument contained 24 items across five quality categories, while the administrative instrument contained 39 items across eight categories.')
T('Table 4.13. System Evaluation Respondents by Role',['Respondent group','Frequency','Percentage'],[
 ['Students',70,'87.50%'],['Security personnel',5,'6.25%'],['Faculty / Event Organizer / Admin',5,'6.25%'],['Total',80,'100.00%'],
],[300,84,84])
P('The manual pre-evaluation contained 70 student respondents with 24 answers each. These students were a different group from the 70 students who evaluated the proposed system. The manual group is reported separately from the 80 system-evaluation respondents. Age, sex, year level, and other demographic distributions were not included in the supplied survey data and are therefore not inferred.')
H(2,'4.5 User Evaluation')
H(3,'4.5.1 Scoring and Interpretation')
P('The evaluation used the ISO/IEC 25010-based categories specified in the study instruments. Responses were scored as 4 for Strongly Agree, 3 for Agree, 2 for Disagree, and 1 for Strongly Disagree. For each item, the weighted mean was calculated as (4f4 + 3f3 + 2f2 + f1) / N. Student-system and manual-process tallies were calculated from the 70 individual completed forms in each group. Security-personnel and administrative results were calculated from their role-specific frequency tables. The chapter reports 111 survey items across these instruments.')
P('Category means were calculated from unrounded item means. The overall result for each respondent group was calculated as the arithmetic mean of its category means, giving equal weight to each category. This convention matters because the number of items differs across categories. Final values were rounded to two decimal places only after calculation. The group results were not pooled into a single overall system score because the role-specific instruments cover different indicators and category sets.')
P('The Chapter 3 interpretation scale was applied as follows: 3.26-4.00, Strongly Agree / Highly Satisfactory; 2.51-3.25, Agree / Satisfactory; 1.76-2.50, Disagree / Unsatisfactory; and 1.00-1.75, Strongly Disagree / Highly Unsatisfactory. These labels describe respondent ratings and do not constitute formal ISO certification.')
P('Individual-item sample SD was calculated as the square root of the sum of f(rating - item mean) squared divided by N - 1. Category summaries show the range of their individual-item sample SDs. For the student-system and manual groups, individual answers additionally supported each respondent\'s category and overall scores. Sample SDs of those respondent scores were used in the comparison and t-test; they are not averages of item SDs. Individual respondent records were not available for the security-personnel and administrative groups, so no respondent-level composite SD is reported for those groups.')
H(3,'4.5.2 Student Evaluation')
survey_summary(survey['groups'][0],14)
P('Students rated the system Highly Satisfactory in all five categories, with an overall equal-category mean of 3.74. Functional Suitability received the highest category mean of 3.76, followed by Security at 3.75. Performance Efficiency and Usability each recorded 3.74, while Reliability recorded 3.71. These results indicate favorable perceptions of the verification experience across the evaluated categories.')
P('At item level, the identity-confirmation statement in Security received a mean of 3.84. The lowest student item mean was 3.60 for processing without unexpected failures or freezing. All student item means remained within the Strongly Agree range. The ratings support perceived acceptability within the evaluation setting; confidence in identity confirmation and protection against credential copying does not independently establish resistance to impersonation or cloning.')
survey_items(survey['groups'][0],15)
H(3,'4.5.3 Security Personnel Evaluation')
survey_summary(survey['groups'][1],16)
P('Security personnel gave an overall equal-category mean of 3.63, interpreted as Highly Satisfactory. Functional Suitability was highest at 3.84, followed by Security at 3.80. Usability, Reliability, and Performance Efficiency received 3.60, 3.48, and 3.45, respectively. These lower relative ratings identify monitoring speed and stability as areas for continued operational attention.')
P('The item on processing speed meeting gate-monitoring expectations received 3.00, while rapid successive entries, minimal-training interpretation of alerts, and freedom from dashboard freezing each received 3.20. These items were interpreted as Agree / Satisfactory despite the favorable overall category ratings. With five respondents, each person accounts for one-fifth of a role-specific item tally, so the findings describe this small evaluation group.')
survey_items(survey['groups'][1],17)
H(3,'4.5.4 Faculty Event Organizer and Administrative Evaluation')
survey_summary(survey['groups'][2],18)
P('The faculty, event-organizer, and administrative group gave an overall equal-category mean of 3.81, interpreted as Highly Satisfactory. Portability received the highest category mean at 3.92. Usability and Maintainability each received 3.88, followed by Security at 3.84, Functional Suitability at 3.80, Reliability at 3.76, Compatibility at 3.75, and Performance Efficiency at 3.68.')
P('The lowest item mean, 3.40, concerned maintaining dashboard performance while processing or storing large amounts of attendance data. All administrative item means remained in the Strongly Agree range. The favorable maintainability and compatibility ratings are reported as respondent assessments; the supplied tallies do not establish the extent of source-code review, technical expertise, or cross-platform testing undertaken by each respondent.')
survey_items(survey['groups'][2],19)
H(3,'4.5.5 Manual Process Pre-Evaluation')
survey_summary(survey['groups'][3],20)
P('The manual pre-evaluation produced an overall equal-category mean of 2.28, interpreted as Disagree / Unsatisfactory. Usability received 2.64, interpreted as Agree / Satisfactory. Functional Suitability received 2.48, while Reliability and Security each received 2.18 and Performance Efficiency received 1.93; these four categories were interpreted as Disagree / Unsatisfactory. The pattern suggests that respondents regarded paper sign-in as understandable while expressing less favorable views of speed, consistency, and protection of information.')
P('The lowest manual item mean was 1.86, shared by the statements that processing remained manageable during long student queues and that processing speed remained consistent throughout attendance. The highest was 2.79 for the statement that paper sign-in steps were easy to understand and follow. These survey responses provide perceived-process evidence alongside the independently recorded writing durations.')
P('Several manual questionnaire statements refer to identity checking, although the timed paper-attendance simulation did not include an ID or enrollment check. Agreement with those statements does not establish that a check occurred during the timed procedure. The survey and observation are therefore interpreted according to their respective measurement content.')
survey_items(survey['groups'][3],21)
H(3,'4.5.6 Comparison of Manual and Proposed-System Survey Ratings')
H(4,'Descriptive Results')
comparison=[]
for system,baseline in zip(survey['groups'][0]['categories'],survey['groups'][3]['categories']):
    label=system['name']
    baseline_sd=completed['groups']['Manual']['descriptive'][label]['sample_sd']
    system_sd=completed['groups']['System']['descriptive'][label]['sample_sd']
    comparison.append([label,f"{baseline['mean']:.3f}",f"{baseline_sd:.3f}",f"{system['mean']:.3f}",f"{system_sd:.3f}",f"{system['mean']-baseline['mean']:.3f}"])
baseline_overall=completed['groups']['Manual']['descriptive']['Overall']
system_overall=completed['groups']['System']['descriptive']['Overall']
welch=completed['welch_overall']
comparison.append(['Overall equal-category score',f"{baseline_overall['mean']:.3f}",f"{baseline_overall['sample_sd']:.3f}",f"{system_overall['mean']:.3f}",f"{system_overall['sample_sd']:.3f}",f"{welch['difference_system_minus_manual']:.3f}"])
T('Table 4.22. Descriptive Comparison of Manual and Student System Ratings',['Category','Manual Mean','Manual Sample SD','System Mean','System Sample SD','Difference'],comparison,[142,62,68,62,68,66])
P('Each group contains 70 different student respondents. Category scores are the mean of each person\'s answers within a category; overall scores are the equal mean of the five category scores. The sample SDs in this table describe variation among respondent scores, not ranges or averages of individual-item SDs. The difference is system minus manual.',True)
FIG(make_survey_figure(),'Figure 4.11. Manual and System Survey Category Means','The figure compares category means from 70 manual pre-evaluation responses per item and 70 student system-evaluation responses per item. It illustrates descriptive differences in reported perceptions.')
P('The system received higher category means in all five shared categories. The largest difference was 1.811 scale points in Performance Efficiency, while the overall equal-category difference was 1.458 points. This pattern indicates more favorable reported perceptions among the students who evaluated the system than among the different students who evaluated the manual process.')
H(4,'Independent-Samples t-Test')
T('Table 4.23. Welch Independent-Samples t-Test of Overall Survey Ratings',['Comparison','Mean Difference','t','Welch df','Two-Tailed p','95% CI','Decision'],[
 ['Overall survey score',f"{welch['difference_system_minus_manual']:.3f}",f"{welch['t']:.3f}",f"{welch['df']:.3f}",'< .001',f"[{welch['ci95'][0]:.3f}, {welch['ci95'][1]:.3f}]",'Reject H0'],
],[80,62,45,50,64,93,74])
P('A two-tailed Welch independent-samples t-test compared the respondent-level overall scores at alpha = 0.05. The null hypothesis stated that the population mean overall ratings were equal; the alternative stated that they differed. Welch\'s procedure did not assume equal group variances. This was an independent-groups test because the manual and system respondents were different students, not matched participants.')
P(f"The result was statistically significant, t({welch['df']:.2f}) = {welch['t']:.2f}, p < .001. The mean difference favoring the proposed system was {welch['difference_system_minus_manual']:.3f} points, with a 95% confidence interval from {welch['ci95'][0]:.3f} to {welch['ci95'][1]:.3f}. The null hypothesis of equal mean overall survey ratings was rejected. Category results were reported descriptively; five additional category-level hypothesis tests were not conducted.")
P('The item wording was adapted to each process. The test compares overall ratings across corresponding quality categories; it does not establish identical measurement across differently worded questionnaires or a causal within-person improvement. Survey ratings also do not supply the missing complete-task timing boundaries required for a direct processing-time comparison.')

H(2,'4.6 Discussion')
H(3,'4.6.1 Interpretation of System Performance')
P('The observations show that internal processing operated on a millisecond scale, while PIN and QR interaction occurred on a seconds scale. Fast had the shortest internal latency and recorded-entry spacing. The High-Security interaction distribution had a longer upper tail, illustrating the variation introduced by the observed QR stage. The exports do not establish the reasons for each longer interaction.')
P('Standard\'s mean internal latency was slightly greater than High-Security\'s, so the data do not support a simple monotonic relationship between the number of credential stages and internal latency. The fixed mode order also prevents attributing differences in PIN speed solely to the mode. No claim of causal improvement or general campus capacity is made.')
H(3,'4.6.2 Interpretation of Manual and Automated Measurements')
P('The manual baseline contains 70 writing durations and provides a reproducible estimate for the stated paper-attendance writing task. Its 21.803-second mean does not include verification, waiting, or later encoding, and its service-equivalent rate does not represent a measured continuous queue.')
P('The automated log-derived rates and component timers address different portions of the workflow. They show the observed behavior of the prototype, but cannot establish a complete-task speed multiplier relative to paper attendance. A fair complete-task benchmark must match the task outcome and the timing boundary. The manual survey\'s lower Performance Efficiency mean provides complementary evidence of perceived speed, while retaining its separate interpretation as a respondent rating.')
H(3,'4.6.3 Interpretation of Security Outcomes')
P('The labelled applicable credential decisions agreed with the expected outcomes in the supplied recorded sample. Correctly rejected wrong PINs, triggered lockouts, and mismatched QR credentials contribute to observed decision correctness. Anti-tailgating and irregular-exit conditions provide separate state-rule evidence rather than ordinary authentication failures.')
P('The 1.32% recorded PIN lockout rate reflects deliberate demonstrations, not an estimate that 1.32% of students would become locked out in ordinary use. Similarly, the 4.11% record-based denial proportion reflects the mixture of demonstrations, participant entries, and reset exits. The controlled sample does not establish resistance to all credential copying, identity impersonation, or real-enrollment risks.')
H(3,'4.6.4 Practical Advantages and Their Limits')
P('The prototype provides configurable credential requirements, structured transaction logs, and visible rule-based decisions. These features reduce reliance on a paper-only record for the tested verification workflow and provide traceable outcomes for the observed invalid conditions. The manual simulation lacked an identity or enrollment check, highlighting a procedural omission rather than a measured prevalence of fraudulent attendance.')
P('The software\'s monitoring, export, and local synchronization capabilities are practical implementation features. Students, security personnel, and administrative respondents gave overall means of 3.74, 3.63, and 3.81, all interpreted as Highly Satisfactory. These findings support favorable perceived quality within the evaluation groups. The manual pre-evaluation mean of 2.28 indicates less favorable perceptions of the paper-based process. The independent-groups survey comparison was statistically significant, while remaining a comparison of reported ratings rather than objective task performance.')
P('The lower security-personnel ratings for processing speed and dashboard reliability identify practical areas for improvement. Survey endorsement of identity confirmation, credential protection, compatibility, or maintainability should be interpreted within respondents\' exposure to those features. Technical certification, reduced fraud incidence, and large-scale operational reliability require direct evidence beyond agreement ratings.')
H(3,'4.6.5 Methodological Alignment and Remaining Requirements')
P('Authentication-decision accuracy, record-based transaction denial, and recorded PIN lockout rate are now reported with explicit inclusion rules and researcher-confirmed expected conditions. Timing and throughput measures remain limited by the available observation boundaries. Listing all five Chapter 3 metric headings does not mean every original planned measurement has been fulfilled.')
P('To fully satisfy the original automated processing-time and throughput definitions, the study needs end-to-end NFC-tap-to-outcome durations and a complete observation-period timer for a specified task. To fully answer SOP 6 comparatively, that task and its timing boundary must correspond to the manual task. The required Chapter 3 clarifications should describe the actual measurements and supplementary analyses transparently; they must not imply that missing timing data were collected.')
P('The manual enrollment-check omission can be reported from the stated procedure, but empirical transcription-error, proxy-attendance, and real-enrollment acceptance rates require an independent audit or relevant test outcomes. The manual and system survey results address perceptions of those processes and cannot be substituted for observed incident rates.')
P('Other limitations include a controlled prototype setting, one recorded device, 20 reusable test profiles, fixed mode order, non-blinded researcher classification of expected outcomes, and interspersed invalid-condition tests. The manual and student-system survey groups contained different respondents, and their questionnaires used method-specific item wording. The independent-samples test therefore does not establish a causal improvement or identical measurement across the two instruments. The role-specific personnel groups each contained five respondents, with only aggregated item responses available; technical review exposure was not documented.')

H(2,'4.7 Summary')
P('The system observation contains 438 records, including 210 successful entry trials, 210 granted reset exits, and 18 expected invalid-condition denials. Internal latency averaged 11.49, 57.37, and 54.06 ms for Fast, Standard, and High-Security. Recorded interaction averaged 2.505 seconds in Standard and 5.799 seconds in High-Security.')
P('Researcher-labelled applicable credential decisions yielded observed accuracy of 100.00% (222/222), while the overall recorded denial proportion was 4.11% (18/438). Two deliberately triggered lockouts among 152 captured PIN submissions yielded a recorded lockout rate of 1.32%. These are controlled recorded-sample findings, not universal accuracy or ordinary-user failure estimates.')
P('The manual paper-attendance baseline contained 70 writing observations with mean duration of 21.803 seconds and sample SD of 4.436 seconds. The timed manual procedure contained no ID or enrollment validation. System-evaluation means were 3.74 for students, 3.63 for security personnel, and 3.81 for faculty, event-organizer, and administrative respondents, all interpreted as Highly Satisfactory. The manual pre-evaluation mean was 2.28, interpreted as Unsatisfactory. The overall survey comparison was statistically significant, t(89.08) = 15.12, p < .001, with a 1.458-point mean difference favoring the proposed system.')
P('Together, the observations and surveys document the prototype\'s measured component performance, tested rule enforcement, and favorable perceived quality. A matched end-to-end performance comparison and independent error or fraud audit remain necessary to establish complete-task speed improvement and comparative incident rates.')

def page(canvas, doc):
    canvas.saveState(); canvas.setFont('Times-Roman',9)
    canvas.drawString(72,756,'Chapter 4 Results and Discussion')
    canvas.drawRightString(540,39,str(doc.page))
    canvas.restoreState()

pdf=ROOT/'Chapter-4-Results-and-Discussion.pdf'
doc=SimpleDocTemplate(str(pdf),pagesize=letter,rightMargin=72,leftMargin=72,topMargin=62,bottomMargin=60,
    title='Chapter 4 Results and Discussion',author='Research Team')
doc.build(story,onFirstPage=page,onLaterPages=page)
(ROOT/'Chapter-4-Results-and-Discussion.md').write_text('\n'.join(markdown),encoding='utf-8')
print('Created',pdf.name,'and complete editable chapter text.')
