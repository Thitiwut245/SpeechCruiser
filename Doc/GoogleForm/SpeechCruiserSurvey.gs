/**
 * SpeechCruiser study: builds the two Google Forms from Appendix ก (ภาคผนวก ก)
 * and scores the answers.
 *
 * How to use
 *   1. Open https://script.google.com and create a new project.
 *   2. Replace the contents of Code.gs with this file and save.
 *   3. Run createForms() once and allow the permissions it asks for.
 *      The log (View > Logs) prints the links of both forms and the response sheet.
 *   4. After the study, run scoreResponses(). It writes the sheet "คะแนน" with each
 *      participant's pre/post scores, Cronbach's alpha and the paired t-test.
 *
 * The two forms
 *   ก่อนเล่น  consent, participant code, Part 1 (general), Part 2 (anxiety scale)
 *   หลังเล่น  participant code, Part 2 again, Part 3 (game experience), Part 4 (comments)
 * Both write to one spreadsheet. The participant code links them to each other
 * and to the game's CSV logs (enter the same code in the game's Settings > Study).
 * Keep the item wording identical to the thesis appendix: scoring matches answers by title.
 */

const TITLE = 'แบบวัดความวิตกกังวลในการพูดภาษาอังกฤษและประสบการณ์การเล่นเกม SpeechCruiser';

// Part 2: speaking anxiety, written in Thai from the FLCAS constructs (Horwitz, Horwitz & Cope, 1986).
// Same order as the thesis appendix. The form never shows which items are reversed.
const ANXIETY = [
  'ฉันรู้สึกประหม่าเมื่อต้องพูดภาษาอังกฤษโดยไม่มีเวลาเตรียมตัว',
  'ฉันรู้สึกมั่นใจเมื่อต้องพูดภาษาอังกฤษให้ผู้อื่นทำตาม',
  'ฉันรู้สึกกดดันเมื่อต้องพูดภาษาอังกฤษภายใต้เวลาที่จำกัด',
  'ฉันกังวลว่าผู้ฟังจะไม่เข้าใจสิ่งที่ฉันพูดเป็นภาษาอังกฤษ',
  'ฉันสบายใจที่จะพูดภาษาอังกฤษ แม้จะไม่แน่ใจว่าใช้คำถูกต้อง',
  'ฉันรู้สึกอายเมื่อผู้ฟังขอให้ฉันพูดซ้ำ',
  'ฉันกลัวว่าจะพูดผิดเมื่อต้องสื่อสารเป็นภาษาอังกฤษ',
  'ฉันกังวลว่าผู้อื่นจะคิดว่าภาษาอังกฤษของฉันไม่ดี',
  'ฉันนึกคำศัพท์ที่รู้ไม่ออกเมื่อรู้สึกกดดันขณะพูดภาษาอังกฤษ',
  'ฉันกล้าพูดภาษาอังกฤษอีกครั้งเมื่อผู้ฟังไม่เข้าใจในครั้งแรก',
  'ฉันพูดภาษาอังกฤษได้อย่างสงบแม้อยู่ในสถานการณ์เร่งด่วน',
  'ฉันอยากหลีกเลี่ยงสถานการณ์ที่ต้องพูดภาษาอังกฤษ',
];
const ANXIETY_REVERSED = [2, 5, 10, 11];        // positive statements: score = 6 - answer
const COMMUNICATION_APPREHENSION = [1, 2, 3, 9, 11, 12];
const FEAR_OF_NEGATIVE_EVALUATION = [4, 5, 6, 7, 8, 10];

// Part 3: game experience (after playing only).
const EXPERIENCE = [
  'การสั่งการเรือด้วยเสียงภาษาอังกฤษเข้าใจง่าย',
  'การเคลื่อนที่ของเรือในเกมมีความสมจริง',
  'การตอบกลับของลูกเรือปัญญาประดิษฐ์มีความสมจริง',
  'เมื่อลูกเรือขอให้พูดซ้ำ ฉันรู้ว่าต้องปรับการพูดอย่างไร',
  'คำแนะนำประโยค (Try Saying) ช่วยให้ฉันรู้ว่าควรพูดอะไร',
  'การที่ลูกเรือทวนคำสั่งด้วยรูปประโยคมาตรฐานช่วยให้ฉันใช้ภาษาได้ถูกต้องขึ้น',
  'ฉันไม่กังวลเมื่อพูดผิดกับลูกเรือปัญญาประดิษฐ์',
  'ระหว่างเล่น ฉันกล้าออกคำสั่งเป็นภาษาอังกฤษ',
  'ฉันรู้สึกสนุกขณะเล่นเกมนี้',
  'ฉันรู้สึกหงุดหงิดเมื่อระบบไม่เข้าใจคำสั่งของฉัน',
];
const EXPERIENCE_REVERSED = [10];               // negative statement: score = 6 - answer

const SCALE_LOW = 'ไม่เห็นด้วยอย่างยิ่ง';
const SCALE_HIGH = 'เห็นด้วยอย่างยิ่ง';
const CODE_TITLE = 'รหัสผู้เข้าร่วม';
const CODE_PATTERN = '[A-Za-z0-9-]{1,24}';
const CONSENT_TITLE = 'ความยินยอมในการเข้าร่วม';
const CONSENT_YES = 'ยินยอมเข้าร่วม';
const CONSENT_NO = 'ไม่ยินยอม';

const anxietyTitle = (i) => (i + 1) + '. ' + ANXIETY[i];
const experienceTitle = (i) => (i + 1) + '. ' + EXPERIENCE[i];

function createForms() {
  const sheet = SpreadsheetApp.create('ผลแบบสอบถาม SpeechCruiser');
  const pre = buildPreForm_();
  const post = buildPostForm_();
  pre.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());
  post.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());

  PropertiesService.getScriptProperties().setProperties({
    PRE_FORM_ID: pre.getId(),
    POST_FORM_ID: post.getId(),
    SHEET_ID: sheet.getId(),
  });

  Logger.log('แบบฟอร์มก่อนเล่น (ส่งให้ผู้เข้าร่วม): ' + pre.getPublishedUrl());
  Logger.log('แบบฟอร์มหลังเล่น (ส่งให้ผู้เข้าร่วม): ' + post.getPublishedUrl());
  Logger.log('แก้ไขแบบฟอร์มก่อนเล่น: ' + pre.getEditUrl());
  Logger.log('แก้ไขแบบฟอร์มหลังเล่น: ' + post.getEditUrl());
  Logger.log('ชีตผลคำตอบ: ' + sheet.getUrl());
}

function buildPreForm_() {
  const form = FormApp.create('[ก่อนเล่น] ' + TITLE);
  setUp_(form,
    'แบบวัดนี้เป็นส่วนหนึ่งของโครงงาน "การออกแบบและพัฒนาเกมเดินเรือด้วยระบบสั่งการด้วยเสียงเพื่อส่งเสริมทักษะการสื่อสาร" ' +
    'ท่านจะตอบแบบวัดชุดนี้ก่อนเล่นเกม แล้วเล่นเกมประมาณ 30 นาที และตอบแบบวัดชุดที่ 2 หลังเล่น\n\n' +
    'การเข้าร่วมเป็นไปโดยสมัครใจ ท่านยุติการเข้าร่วมได้ทุกเมื่อโดยไม่มีผลกระทบใด ๆ ' +
    'ข้อมูลทั้งหมดอ้างอิงด้วยรหัสผู้เข้าร่วมเท่านั้น ไม่มีการเก็บชื่อหรืออีเมล และนำไปใช้เพื่อการศึกษาเท่านั้น ' +
    'เกมไม่บันทึกเสียงของผู้เข้าร่วม แต่ข้อความที่ระบบถอดจากเสียงพูดอาจส่งไปประมวลผลผ่าน OpenAI API');

  const consent = form.addMultipleChoiceItem()
    .setTitle(CONSENT_TITLE)
    .setHelpText('ข้าพเจ้าได้อ่านคำชี้แจงแล้ว และยินยอมให้นำคำตอบและข้อมูลการเล่นเกมไปใช้เพื่อการศึกษา')
    .setRequired(true);
  consent.setChoices([
    consent.createChoice(CONSENT_YES, FormApp.PageNavigationType.CONTINUE),
    consent.createChoice(CONSENT_NO, FormApp.PageNavigationType.SUBMIT),
  ]);

  form.addPageBreakItem().setTitle('ตอนที่ 1 ข้อมูลทั่วไป');
  addCode_(form);
  form.addMultipleChoiceItem().setTitle('1. เพศ')
    .setChoiceValues(['ชาย', 'หญิง', 'ไม่ระบุ']).setRequired(true);
  form.addTextItem().setTitle('2. อายุ (ปี)').setRequired(true)
    .setValidation(FormApp.createTextValidation()
      .requireNumberBetween(15, 80).setHelpText('กรอกตัวเลข 15–80').build());
  form.addMultipleChoiceItem().setTitle('3. ชั้นปีที่ศึกษาในระดับปริญญาตรี')
    .setChoiceValues(['ชั้นปีที่ 1', 'ชั้นปีที่ 2', 'ชั้นปีที่ 3', 'ชั้นปีที่ 4 ขึ้นไป']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('4. ความสามารถในการพูดภาษาอังกฤษของตนเอง')
    .setChoiceValues(['อ่อนมาก', 'อ่อน', 'ปานกลาง', 'ดี', 'ดีมาก']).setRequired(true);
  form.addMultipleChoiceItem().setTitle('5. ความถี่ในการเล่นวิดีโอเกม')
    .setChoiceValues(['ไม่เล่น', 'น้อยกว่าสัปดาห์ละครั้ง', '1–3 วันต่อสัปดาห์', '4 วันขึ้นไปต่อสัปดาห์'])
    .setRequired(true);
  form.addMultipleChoiceItem().setTitle('6. เคยเรียนหรือใช้ภาษาอังกฤษทางทะเลมาก่อนหรือไม่')
    .setChoiceValues(['เคย', 'ไม่เคย']).setRequired(true);

  addAnxiety_(form);
  form.setConfirmationMessage('ขอบคุณครับ/ค่ะ กรุณาแจ้งผู้ดำเนินการทดลองเพื่อเริ่มเล่นเกม');
  return form;
}

function buildPostForm_() {
  const form = FormApp.create('[หลังเล่น] ' + TITLE);
  setUp_(form,
    'กรุณาตอบแบบวัดชุดนี้หลังจากเล่นเกมเสร็จแล้ว โดยใช้รหัสผู้เข้าร่วมเดียวกับแบบวัดก่อนเล่น ' +
    'ตอนที่ 2 มีข้อความเหมือนแบบวัดก่อนเล่น กรุณาตอบตามความรู้สึกของท่านในขณะนี้');

  addCode_(form);
  addAnxiety_(form);

  form.addPageBreakItem().setTitle('ตอนที่ 3 แบบสอบถามประสบการณ์การเล่นเกม')
    .setHelpText('โปรดเลือกระดับความคิดเห็นของท่าน (1 = ' + SCALE_LOW + ', 5 = ' + SCALE_HIGH + ')');
  EXPERIENCE.forEach((_, i) => addScale_(form, experienceTitle(i)));

  form.addPageBreakItem().setTitle('ตอนที่ 4 ข้อเสนอแนะเพิ่มเติม');
  form.addParagraphTextItem().setTitle('1. สิ่งที่ท่านชอบมากที่สุดในเกมนี้');
  form.addParagraphTextItem().setTitle('2. สิ่งที่ทำให้ท่านรู้สึกกังวลหรือไม่สะดวกระหว่างเล่น');
  form.addParagraphTextItem().setTitle('3. ข้อเสนอแนะเพื่อปรับปรุงเกมนี้');

  form.setConfirmationMessage('ขอบคุณที่สละเวลาเข้าร่วมการทดลองครับ/ค่ะ');
  return form;
}

function setUp_(form, description) {
  form.setDescription(description)
    .setCollectEmail(false)
    .setAllowResponseEdits(false)
    .setShowLinkToRespondAgain(false)
    .setProgressBar(true);
}

function addCode_(form) {
  form.addTextItem().setTitle(CODE_TITLE)
    .setHelpText('ผู้ดำเนินการทดลองเป็นผู้แจ้งรหัส เช่น P01 (ใช้รหัสเดียวกันทั้งก่อนและหลังเล่น)')
    .setRequired(true)
    .setValidation(FormApp.createTextValidation()
      .requireTextMatchesPattern(CODE_PATTERN)
      .setHelpText('ใช้ตัวอักษรภาษาอังกฤษ ตัวเลข หรือขีด (-) ไม่เกิน 24 ตัว').build());
}

function addAnxiety_(form) {
  form.addPageBreakItem().setTitle('ตอนที่ 2 แบบวัดความวิตกกังวลในการพูดภาษาอังกฤษ')
    .setHelpText('โปรดเลือกระดับที่ตรงกับความรู้สึกของท่านมากที่สุด (1 = ' + SCALE_LOW + ', 5 = ' + SCALE_HIGH +
                 ') ไม่มีคำตอบที่ถูกหรือผิด');
  ANXIETY.forEach((_, i) => addScale_(form, anxietyTitle(i)));
}

function addScale_(form, title) {
  form.addScaleItem().setTitle(title).setBounds(1, 5)
    .setLabels(SCALE_LOW, SCALE_HIGH).setRequired(true);
}

// ---------------------------------------------------------------------------
// Scoring
// ---------------------------------------------------------------------------

function scoreResponses() {
  const props = PropertiesService.getScriptProperties();
  const pre = latestByCode_(FormApp.openById(props.getProperty('PRE_FORM_ID')));
  const post = latestByCode_(FormApp.openById(props.getProperty('POST_FORM_ID')));
  const ss = SpreadsheetApp.openById(props.getProperty('SHEET_ID'));

  // Means on the 1–5 scale: the thesis analyses item means (Chapter 3).
  const header = ['รหัส', 'ก่อน: เฉลี่ย', 'ก่อน: CA', 'ก่อน: FNE', 'ก่อน: รวม (12–60)',
                  'หลัง: เฉลี่ย', 'หลัง: CA', 'หลัง: FNE', 'หลัง: รวม (12–60)',
                  'ผลต่างค่าเฉลี่ย (หลัง − ก่อน)', 'ประสบการณ์: เฉลี่ย'];
  const rows = [];
  const preItems = [];          // the 12 scored pre-test answers of each participant
  const experienceItems = [];   // the 10 scored experience answers of each participant
  Object.keys(pre).sort().forEach((code) => {
    const a = scored_(pre[code], ANXIETY, anxietyTitle, ANXIETY_REVERSED);
    if (!a) return;
    preItems.push(a);
    const b = post[code] ? scored_(post[code], ANXIETY, anxietyTitle, ANXIETY_REVERSED) : null;
    const e = post[code] ? scored_(post[code], EXPERIENCE, experienceTitle, EXPERIENCE_REVERSED) : null;
    if (e) experienceItems.push(e);
    rows.push([code,
               mean_(a), mean_(pick_(a, COMMUNICATION_APPREHENSION)), mean_(pick_(a, FEAR_OF_NEGATIVE_EVALUATION)), sum_(a),
               b ? mean_(b) : '', b ? mean_(pick_(b, COMMUNICATION_APPREHENSION)) : '',
               b ? mean_(pick_(b, FEAR_OF_NEGATIVE_EVALUATION)) : '', b ? sum_(b) : '',
               b ? mean_(b) - mean_(a) : '', e ? mean_(e) : '']);
  });

  let sheet = ss.getSheetByName('คะแนน');
  if (sheet) sheet.clear(); else sheet = ss.insertSheet('คะแนน');
  sheet.getRange(1, 1, 1, header.length).setValues([header]).setFontWeight('bold');
  if (rows.length) sheet.getRange(2, 1, rows.length, header.length).setValues(rows);

  // Summary. Only participants with both a pre and a post test enter the paired test.
  const last = Math.max(rows.length + 1, 2);
  const pairedPre = 'FILTER(B2:B' + last + ',F2:F' + last + '<>"")';
  const pairedPost = 'FILTER(F2:F' + last + ',F2:F' + last + '<>"")';
  const diff = 'FILTER(J2:J' + last + ',J2:J' + last + '<>"")';
  const r = last + 2;
  const summary = [
    ['สรุป (ค่าเฉลี่ยรายข้อ 1–5)', ''],
    ['จำนวนผู้ตอบครบทั้งก่อนและหลัง (n)', '=COUNT(' + diff + ')'],
    ['ค่าเฉลี่ยความวิตกกังวลก่อนเล่น', '=AVERAGE(' + pairedPre + ')'],
    ['ส่วนเบี่ยงเบนมาตรฐานก่อนเล่น', '=STDEV(' + pairedPre + ')'],
    ['ค่าเฉลี่ยความวิตกกังวลหลังเล่น', '=AVERAGE(' + pairedPost + ')'],
    ['ส่วนเบี่ยงเบนมาตรฐานหลังเล่น', '=STDEV(' + pairedPost + ')'],
    ['Paired t-test: p (ทางเดียว, หลัง < ก่อน)', '=T.TEST(' + pairedPre + ',' + pairedPost + ',1,1)'],
    ["ขนาดอิทธิพล Cohen's d_z (ผลต่างเฉลี่ย / SD ของผลต่าง)", '=AVERAGE(' + diff + ')/STDEV(' + diff + ')'],
    ["Cronbach's alpha ด้าน CA (ก่อนเล่น)", cronbachAlpha_(preItems.map((p) => pick_(p, COMMUNICATION_APPREHENSION)))],
    ["Cronbach's alpha ด้าน FNE (ก่อนเล่น)", cronbachAlpha_(preItems.map((p) => pick_(p, FEAR_OF_NEGATIVE_EVALUATION)))],
    ["Cronbach's alpha ตอนที่ 3 (ประสบการณ์)", cronbachAlpha_(experienceItems)],
    ['ค่าเฉลี่ยประสบการณ์การเล่นเกม (เกณฑ์ ≥ 3.41)', '=AVERAGE(K2:K' + last + ')'],
    ['หมายเหตุ', 'ตรวจการแจกแจงของผลต่าง (Shapiro–Wilk) ในโปรแกรมสถิติก่อนเลือก t-test หรือ Wilcoxon'],
  ];
  sheet.getRange(r, 1, summary.length, 2).setValues(summary);
  sheet.getRange(r, 1).setFontWeight('bold');
  sheet.autoResizeColumns(1, header.length);
  Logger.log('เขียนชีต "คะแนน" แล้ว: ' + rows.length + ' คน');
}

/** The latest response of each participant code: code -> {title: answer}. */
function latestByCode_(form) {
  const out = {};
  form.getResponses().forEach((resp) => {
    const answers = {};
    resp.getItemResponses().forEach((ir) => { answers[ir.getItem().getTitle()] = ir.getResponse(); });
    const code = String(answers[CODE_TITLE] || '').trim().toUpperCase();
    if (code && answers[CONSENT_TITLE] !== CONSENT_NO) out[code] = answers;
  });
  return out;
}

/** The answers of one scale after reversing its reversed items; null if any is missing. */
function scored_(answers, items, titleOf, reversed) {
  const scores = items.map((_, i) => Number(answers[titleOf(i)]));
  if (scores.some((s) => !(s >= 1 && s <= 5))) return null;
  return scores.map((s, i) => (reversed.indexOf(i + 1) >= 0 ? 6 - s : s));
}

function cronbachAlpha_(people) {
  if (people.length < 3) return 'ต้องมีผู้ตอบอย่างน้อย 3 คน';
  const k = people[0].length;
  let itemVariance = 0;
  for (let i = 0; i < k; i++) itemVariance += variance_(people.map((p) => p[i]));
  const totalVariance = variance_(people.map(sum_));
  return totalVariance === 0 ? 'คำนวณไม่ได้ (คะแนนรวมเท่ากันทุกคน)' : (k / (k - 1)) * (1 - itemVariance / totalVariance);
}

function pick_(scores, items) { return items.map((n) => scores[n - 1]); }
function sum_(xs) { return xs.reduce((a, b) => a + b, 0); }
function mean_(xs) { return sum_(xs) / xs.length; }
function variance_(xs) {               // sample variance (n - 1), as used for Cronbach's alpha
  const m = mean_(xs);
  return xs.reduce((a, x) => a + (x - m) * (x - m), 0) / (xs.length - 1);
}
