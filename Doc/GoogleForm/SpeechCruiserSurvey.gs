/**
 * SpeechCruiser study: builds the Google Form from Appendix ก (ภาคผนวก ก) and scores the answers.
 *
 * How to use
 *   1. Open https://script.google.com and create a new project.
 *   2. Replace the contents of Code.gs with this file and save.
 *   3. Run createForms() once and allow the permissions it asks for.
 *      The log (View > Logs) prints the form's link and the response sheet.
 *   4. After the study, run scoreResponses(). It writes the sheet "คะแนน" with each
 *      participant's pre/post scores, Cronbach's alpha and the paired t-test.
 *
 * One form for the whole session, sent once at the end:
 *   consent, participant code, Part 1 (general), Part 2 (anxiety, before playing),
 *   a stop page while the participant plays (the form stays open),
 *   Part 2 again (after playing), Part 3 (game experience), Part 4 (comments).
 * Pre and post answers are in the same response, so nothing has to be matched up.
 * No participant code is asked: the survey is not linked to the game's CSV logs, which the
 * study separates by game session instead (the game is restarted for each participant).
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
const CONSENT_TITLE = 'ความยินยอมในการเข้าร่วม';
const CONSENT_YES = 'ยินยอมเข้าร่วม';
const CONSENT_NO = 'ไม่ยินยอม';

const anxietyTitle = (i) => (i + 1) + '. ' + ANXIETY[i];
const experienceTitle = (i) => (i + 1) + '. ' + EXPERIENCE[i];

function createForms() {
  const sheet = SpreadsheetApp.create('ผลแบบสอบถาม SpeechCruiser');
  const form = FormApp.create(TITLE);
  form.setDescription(
      'แบบสอบถามนี้เป็นส่วนหนึ่งของโครงงาน "การออกแบบและพัฒนาเกมเดินเรือด้วยระบบสั่งการด้วยเสียงเพื่อส่งเสริมทักษะการสื่อสาร" ' +
      'ท่านจะตอบตอนที่ 1 และ 2 ก่อนเล่นเกม แล้วเล่นเกมประมาณ 30 นาทีโดยเปิดฟอร์มนี้ค้างไว้ ' +
      'จากนั้นกลับมาตอบตอนที่ 2 อีกครั้ง พร้อมตอนที่ 3 และ 4 แล้วจึงกดส่ง\n\n' +
      'การเข้าร่วมเป็นไปโดยสมัครใจ ท่านยุติการเข้าร่วมได้ทุกเมื่อโดยไม่มีผลกระทบใด ๆ ' +
      'แบบสอบถามไม่เก็บชื่อ อีเมล หรือข้อมูลที่ระบุตัวตนได้ และนำไปใช้เพื่อการศึกษาเท่านั้น ' +
      'เกมไม่บันทึกเสียงของผู้เข้าร่วม แต่ข้อความที่ระบบถอดจากเสียงพูดอาจส่งไปประมวลผลผ่าน OpenAI API')
    .setCollectEmail(false)
    .setAllowResponseEdits(false)
    .setShowLinkToRespondAgain(false)
    .setProgressBar(true);

  const consent = form.addMultipleChoiceItem()
    .setTitle(CONSENT_TITLE)
    .setHelpText('ข้าพเจ้าได้อ่านคำชี้แจงแล้ว และยินยอมให้นำคำตอบและข้อมูลการเล่นเกมไปใช้เพื่อการศึกษา')
    .setRequired(true);
  consent.setChoices([
    consent.createChoice(CONSENT_YES, FormApp.PageNavigationType.CONTINUE),
    consent.createChoice(CONSENT_NO, FormApp.PageNavigationType.SUBMIT),
  ]);

  // ---- before playing
  form.addPageBreakItem().setTitle('ตอนที่ 1 ข้อมูลทั่วไป (ก่อนเล่น)');
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

  const preIds = addAnxiety_(form, 'ตอนที่ 2 แบบวัดความวิตกกังวลในการพูดภาษาอังกฤษ (ก่อนเล่น)');

  // ---- the game, with the form left open; the check stops an accidental "next"
  form.addPageBreakItem().setTitle('หยุดตรงนี้ และเริ่มเล่นเกม')
    .setHelpText('เปิดหน้านี้ค้างไว้ เล่นเกมประมาณ 30 นาทีตามที่ผู้ดำเนินการทดลองแนะนำ ' +
                 'เมื่อเล่นเสร็จแล้วจึงกลับมาที่หน้านี้ และกดถัดไป');
  form.addCheckboxItem().setTitle('ข้าพเจ้าเล่นเกมเสร็จแล้ว')
    .setChoiceValues(['เล่นเสร็จแล้ว']).setRequired(true);

  // ---- after playing
  const postIds = addAnxiety_(form, 'ตอนที่ 2 แบบวัดความวิตกกังวลในการพูดภาษาอังกฤษ (หลังเล่น)',
    'ข้อความเหมือนกับก่อนเล่น กรุณาตอบตามความรู้สึกของท่านในขณะนี้');

  form.addPageBreakItem().setTitle('ตอนที่ 3 แบบสอบถามประสบการณ์การเล่นเกม (หลังเล่น)')
    .setHelpText('โปรดเลือกระดับความคิดเห็นของท่าน (1 = ' + SCALE_LOW + ', 5 = ' + SCALE_HIGH + ')');
  const expIds = EXPERIENCE.map((_, i) => addScale_(form, experienceTitle(i)));

  form.addPageBreakItem().setTitle('ตอนที่ 4 ข้อเสนอแนะเพิ่มเติม (หลังเล่น)');
  form.addParagraphTextItem().setTitle('1. สิ่งที่ท่านชอบมากที่สุดในเกมนี้');
  form.addParagraphTextItem().setTitle('2. สิ่งที่ทำให้ท่านรู้สึกกังวลหรือไม่สะดวกระหว่างเล่น');
  form.addParagraphTextItem().setTitle('3. ข้อเสนอแนะเพื่อปรับปรุงเกมนี้');
  form.setConfirmationMessage('ขอบคุณที่สละเวลาเข้าร่วมการทดลองครับ/ค่ะ');

  form.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());

  // The pre and post items share their wording, so scoring finds them by item id.
  PropertiesService.getScriptProperties().setProperties({
    FORM_ID: form.getId(),
    SHEET_ID: sheet.getId(),
    PRE_IDS: JSON.stringify(preIds),
    POST_IDS: JSON.stringify(postIds),
    EXP_IDS: JSON.stringify(expIds),
  });

  Logger.log('ลิงก์แบบสอบถาม (ส่งให้ผู้เข้าร่วม): ' + form.getPublishedUrl());
  Logger.log('แก้ไขแบบสอบถาม: ' + form.getEditUrl());
  Logger.log('ชีตผลคำตอบ: ' + sheet.getUrl());
}

/** One page of the 12 anxiety items; returns their item ids in order. */
function addAnxiety_(form, title, note) {
  form.addPageBreakItem().setTitle(title)
    .setHelpText((note ? note + ' ' : '') + 'โปรดเลือกระดับที่ตรงกับความรู้สึกของท่านมากที่สุด (1 = ' +
                 SCALE_LOW + ', 5 = ' + SCALE_HIGH + ') ไม่มีคำตอบที่ถูกหรือผิด');
  return ANXIETY.map((_, i) => addScale_(form, anxietyTitle(i)));
}

function addScale_(form, title) {
  return form.addScaleItem().setTitle(title).setBounds(1, 5)
    .setLabels(SCALE_LOW, SCALE_HIGH).setRequired(true).getId();
}

// ---------------------------------------------------------------------------
// Scoring
// ---------------------------------------------------------------------------

function scoreResponses() {
  const props = PropertiesService.getScriptProperties();
  const form = FormApp.openById(props.getProperty('FORM_ID'));
  const ss = SpreadsheetApp.openById(props.getProperty('SHEET_ID'));
  const preIds = JSON.parse(props.getProperty('PRE_IDS'));
  const postIds = JSON.parse(props.getProperty('POST_IDS'));
  const expIds = JSON.parse(props.getProperty('EXP_IDS'));

  // Means on the 1–5 scale: the thesis analyses item means (Chapter 3).
  const header = ['ลำดับ', 'ก่อน: เฉลี่ย', 'ก่อน: CA', 'ก่อน: FNE', 'ก่อน: รวม (12–60)',
                  'หลัง: เฉลี่ย', 'หลัง: CA', 'หลัง: FNE', 'หลัง: รวม (12–60)',
                  'ผลต่างค่าเฉลี่ย (หลัง − ก่อน)', 'ประสบการณ์: เฉลี่ย'];
  const rows = [];
  const preItems = [];          // the 12 scored pre-test answers of each participant
  const experienceItems = [];   // the 10 scored experience answers of each participant
  responses_(form).forEach(({ code, byId }) => {
    const a = scored_(byId, preIds, ANXIETY_REVERSED);
    const b = scored_(byId, postIds, ANXIETY_REVERSED);
    const e = scored_(byId, expIds, EXPERIENCE_REVERSED);
    if (!a || !b) return;       // the form is only sent after both parts, so this is a broken response
    preItems.push(a);
    if (e) experienceItems.push(e);
    rows.push([code,
               mean_(a), mean_(pick_(a, COMMUNICATION_APPREHENSION)), mean_(pick_(a, FEAR_OF_NEGATIVE_EVALUATION)), sum_(a),
               mean_(b), mean_(pick_(b, COMMUNICATION_APPREHENSION)), mean_(pick_(b, FEAR_OF_NEGATIVE_EVALUATION)), sum_(b),
               mean_(b) - mean_(a), e ? mean_(e) : '']);
  });

  let sheet = ss.getSheetByName('คะแนน');
  if (sheet) sheet.clear(); else sheet = ss.insertSheet('คะแนน');
  sheet.getRange(1, 1, 1, header.length).setValues([header]).setFontWeight('bold');
  if (rows.length) sheet.getRange(2, 1, rows.length, header.length).setValues(rows);

  const last = Math.max(rows.length + 1, 2);
  const pre = 'B2:B' + last, post = 'F2:F' + last, diff = 'J2:J' + last;
  const r = last + 2;
  const summary = [
    ['สรุป (ค่าเฉลี่ยรายข้อ 1–5)', ''],
    ['จำนวนผู้ตอบครบ (n)', '=COUNT(' + diff + ')'],
    ['ค่าเฉลี่ยความวิตกกังวลก่อนเล่น', '=AVERAGE(' + pre + ')'],
    ['ส่วนเบี่ยงเบนมาตรฐานก่อนเล่น', '=STDEV(' + pre + ')'],
    ['ค่าเฉลี่ยความวิตกกังวลหลังเล่น', '=AVERAGE(' + post + ')'],
    ['ส่วนเบี่ยงเบนมาตรฐานหลังเล่น', '=STDEV(' + post + ')'],
    ['Paired t-test: p (ทางเดียว, หลัง < ก่อน)', '=T.TEST(' + pre + ',' + post + ',1,1)'],
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

/** Every consenting response in the order sent, with answers keyed by item id and a running number as its label. */
function responses_(form) {
  const out = [];
  form.getResponses().forEach((resp) => {
    const byId = {};
    let consent = '';
    resp.getItemResponses().forEach((ir) => {
      byId[ir.getItem().getId()] = ir.getResponse();
      if (ir.getItem().getTitle() === CONSENT_TITLE) consent = ir.getResponse();
    });
    if (consent !== CONSENT_NO) out.push({ code: 'R' + String(out.length + 1).padStart(2, '0'), byId });
  });
  return out;
}

/** The answers of one scale after reversing its reversed items; null if any is missing. */
function scored_(byId, ids, reversed) {
  const scores = ids.map((id) => Number(byId[id]));
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
