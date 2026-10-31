/**
 * emergency-code.gs — หน้าเว็บ Google Apps Script แสดงรหัสฉุกเฉิน (TOTP) สำหรับครู (WP3-4, 2026-10-31)
 * =====================================================================================
 *
 * ทางเลือกแทนแอป Authenticator - ใช้ตอน Server ล่มและนักเรียนล็อกอินไม่ได้ทั้งห้อง ครูเปิดลิงก์นี้
 * (ทำงานได้แม้ Server ล่มเพราะรันอยู่บน Google ไม่เกี่ยวกับ Server ของโรงเรียนเลย) พิมพ์รหัส 6 หลักที่
 * เห็นที่จอล็อกของเครื่องนั้น (ดู docs/wireframes/st-emergency.dc.html)
 *
 * สูตรคำนวณรหัสต้องตรงกับ Server/Services/TotpService.cs (C#) และ Agent/Services/EmergencyCodeService.cs
 * (C#) เป๊ะๆ ทั้ง 3 ที่ (RFC 6238 TOTP บน HMAC-SHA1, 6 หลัก, step 30 วินาที, ±1 step) - ดูสคริปต์
 * ทดสอบ scratch ที่ orchestrator/worker ใช้ยืนยันกับ RFC 6238 Appendix B test vectors แล้วตอนเขียน WP นี้
 * (ไม่ได้ commit ไว้ในโฟลเดอร์นี้ - รันครั้งเดียวตอนพัฒนา)
 *
 *
 * วิธีติดตั้ง (ทำครั้งเดียว)
 * --------------------------
 * 1. เปิด https://script.google.com (ด้วยบัญชี Google Workspace ของโรงเรียน) สร้างโปรเจกต์ใหม่
 *    ชื่ออะไรก็ได้ เช่น "Onion ProcOparetor - รหัสฉุกเฉิน"
 * 2. ลบโค้ด default (Code.gs) ทั้งหมด แล้ววางเนื้อหาไฟล์นี้ทั้งไฟล์ลงไปแทน
 * 3. เปิด Console (WPF) ไปที่ "ตั้งค่า" > คอลัมน์ "ทั้งระบบ (Admin)" > การ์ด "รหัสฉุกเฉิน" >
 *    กด "แสดง QR..." - คัดลอก "key" (Base32) ที่เห็น
 * 4. ในหน้า Apps Script: เมนู Project Settings (ไอคอนเฟือง) > "Script Properties" > Add script
 *    property - เพิ่ม 2 ตัว:
 *      - TOTP_SECRET_BASE32  = key ที่คัดลอกมาจากขั้นที่ 3 (เว้นวรรค/ตัวพิมพ์เล็กก็ได้ สคริปต์ clean เอง)
 *      - ALLOWED_EMAILS      = อีเมลครูที่อนุญาตให้เปิดหน้านี้ คั่นด้วย comma เช่น
 *                              "teacher1@school.ac.th,teacher2@school.ac.th"
 *    (ตัวเลือก) LOG_SPREADSHEET_ID = ID ของ Google Sheet ที่จะใช้บันทึกว่าใครเปิดดูเมื่อไร (ตัดจาก URL
 *    ของ sheet ส่วน .../d/<ID>/edit) ถ้าไม่ตั้งไว้ สคริปต์จะเขียน log ไปที่ Apps Script's Logger แทน
 *    (ดูได้จาก "Executions" ในเมนูด้านซ้าย - อยู่ได้ไม่นานเท่า Sheet)
 * 5. Deploy > New deployment > เลือกประเภท "Web app"
 *      - Execute as: "User accessing the web app" (สำคัญ - ต้องเป็นอันนี้เท่านั้น ถึงจะรู้ได้ว่าใครเปิด)
 *      - Who has access: "Anyone within [โรงเรียน]" (หรือ "Anyone with Google account" ถ้า domain
 *        ไม่รองรับตัวเลือกแรก) - ตั้งใจไม่เลือก "Anyone" เด็ดขาด เพราะต้องรู้ตัวตนผู้เปิดก่อนเช็ค allow-list
 *      - กด Deploy แล้วคัดลอก "Web app URL" ที่ได้ - นี่คือลิงก์ที่ครูใช้เปิดหน้านี้ (ปุ่ม "คัดลอกลิงก์"
 *        ในการ์ด "รหัสฉุกเฉิน" ของ Console ให้ครู paste ลิงก์นี้เก็บไว้เอง - Console ไม่ทราบ URL นี้ล่วงหน้า)
 * 6. ทดสอบเปิดลิงก์ด้วยบัญชีที่อยู่ใน ALLOWED_EMAILS - ต้องเห็นรหัส 6 หลักเปลี่ยนทุก 30 วินาที และต้องตรง
 *    กับรหัสที่ Console "ทดสอบรหัส" ใน dialog เดียวกันบอกว่าถูก (พิมพ์ตัวเดียวกันไปทดสอบทั้ง 2 ที่)
 *
 * เมื่อ Console "เปลี่ยน secret ใหม่" (rotate) - ต้องมา "วางค่าทับ" TOTP_SECRET_BASE32 ในขั้นที่ 4 ใหม่
 * ทุกครั้งด้วย (secret เก่าจะคำนวณรหัสผิดทันที) ไม่ต้อง deploy ใหม่ - แก้ Script Properties แล้วมีผลทันที
 *
 *
 * ข้อจำกัดที่ยอมรับตรงๆ
 * ----------------------
 * - การตรวจ "ใครเปิดได้" อิงกับ Session.getActiveUser().getEmail() ซึ่งทำงานได้แน่นอนเฉพาะในองค์กร
 *   Google Workspace เดียวกัน (ตรงกับที่ roadmap บันทึกไว้ว่าต้องมี Google Workspace สำหรับหน้านี้)
 *   ถ้าโรงเรียนไม่มี Workspace ฟีเจอร์ "จำกัดบัญชี" นี้จะใช้ไม่ได้เต็มรูปแบบ - ต้องพึ่ง "Who has access"
 *   ของ deployment อย่างเดียว
 * - secret อยู่ใน Script Properties ของโปรเจกต์นี้ - ใครมีสิทธิ์ "Editor" ของ Apps Script project
 *   เห็น/แก้ได้ (เท่ากับสิทธิ์ Admin ของ Console) ให้จำกัดคนที่มีสิทธิ์แก้โปรเจกต์นี้เอง (Apps Script ไม่มี
 *   "view-only ยกเว้น Script Properties" ให้เลือก)
 */

var TOTP_DIGITS = 6;
var TOTP_STEP_SECONDS = 30;
var TOTP_WINDOW_STEPS = 1; // ±1 ช่วง - เอาไว้ให้หน้าเว็บโชว์ "รหัสก่อนหน้า/ถัดไป" เผื่อนาฬิกาเพี้ยน ไม่ได้ใช้ตรวจอะไร (หน้านี้แค่โชว์ ไม่มีใครพิมพ์รหัสกลับมาให้ตรวจ)
var BASE32_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

/**
 * จุดเข้าหน้าเว็บ (ต้อง deploy เป็น Web app แบบ "Execute as: User accessing the web app")
 */
function doGet(e) {
  var email = getActiveUserEmailSafe_();

  if (!isAllowedEmail_(email)) {
    return HtmlService.createHtmlOutput(renderDeniedHtml_(email));
  }

  logViewer_(email);

  var secret = getTotpSecret_();
  if (!secret) {
    return HtmlService.createHtmlOutput(
      "<p style='font-family:sans-serif'>ยังไม่ได้ตั้งค่า TOTP_SECRET_BASE32 ใน Script Properties - ดูวิธีติดตั้งที่ comment หัวไฟล์ emergency-code.gs</p>"
    );
  }

  var now = new Date();
  var code = computeTotp_(secret, now, TOTP_DIGITS, TOTP_STEP_SECONDS);
  var secondsRemaining = TOTP_STEP_SECONDS - (Math.floor(now.getTime() / 1000) % TOTP_STEP_SECONDS);

  return HtmlService.createHtmlOutput(renderCodeHtml_(code, secondsRemaining, email));
}

// ---- Access control ----

function getActiveUserEmailSafe_() {
  try {
    var email = Session.getActiveUser().getEmail();
    return email || null;
  } catch (err) {
    return null;
  }
}

function isAllowedEmail_(email) {
  if (!email) {
    return false;
  }

  var allowedCsv = PropertiesService.getScriptProperties().getProperty("ALLOWED_EMAILS") || "";
  var allowed = allowedCsv
    .split(",")
    .map(function (s) { return s.trim().toLowerCase(); })
    .filter(function (s) { return s.length > 0; });

  return allowed.indexOf(email.toLowerCase()) !== -1;
}

/** บันทึกว่าใครเปิดดูเมื่อไร (roadmap บังคับไว้ตรงๆ) - ลง Sheet ถ้าตั้ง LOG_SPREADSHEET_ID ไว้ ไม่งั้น Logger */
function logViewer_(email) {
  var sheetId = PropertiesService.getScriptProperties().getProperty("LOG_SPREADSHEET_ID");
  var timestamp = new Date();

  if (sheetId) {
    try {
      var sheet = SpreadsheetApp.openById(sheetId).getSheets()[0];
      sheet.appendRow([timestamp, email]);
      return;
    } catch (err) {
      Logger.log("เขียน log ลง Sheet ไม่สำเร็จ (sheetId=" + sheetId + "): " + err);
    }
  }

  Logger.log("Emergency code viewed by " + email + " at " + timestamp.toISOString());
}

// ---- TOTP (RFC 6238) - ต้องตรงกับ Server/Services/TotpService.cs เป๊ะๆ ----

function getTotpSecret_() {
  var raw = PropertiesService.getScriptProperties().getProperty("TOTP_SECRET_BASE32");
  return raw ? raw.trim() : null;
}

function computeTotp_(secretBase32, date, digits, stepSeconds) {
  var counter = Math.floor(date.getTime() / 1000 / stepSeconds);
  var keyBytes = base32Decode_(secretBase32);
  return computeHotp_(keyBytes, counter, digits);
}

function computeHotp_(keyBytes, counter, digits) {
  var counterBytes = counterToBytes_(counter);

  // Utilities.computeHmacSha1Signature คืน byte[] แบบ signed (-128..127 เหมือน Java byte) - ต้องแปลง
  // เป็น unsigned (0..255) ก่อนทำ dynamic truncation ไม่งั้นค่า offset/binary code จะผิด
  var hmacSigned = Utilities.computeHmacSha1Signature(counterBytes, keyBytes);
  var hmac = hmacSigned.map(function (b) { return b & 0xff; });

  var offset = hmac[hmac.length - 1] & 0x0f;
  var binaryCode =
    ((hmac[offset] & 0x7f) << 24) |
    ((hmac[offset + 1] & 0xff) << 16) |
    ((hmac[offset + 2] & 0xff) << 8) |
    (hmac[offset + 3] & 0xff);

  var mod = Math.pow(10, digits);
  var code = String(binaryCode % mod);
  while (code.length < digits) {
    code = "0" + code;
  }
  return code;
}

/** counter (วินาที/30, ปลอดภัยใน safe-integer range ของ JS ไปอีกหลายร้อยปี) -> 8 ไบต์ big-endian */
function counterToBytes_(counter) {
  var bytes = new Array(8);
  for (var i = 7; i >= 0; i--) {
    bytes[i] = counter & 0xff;
    counter = Math.floor(counter / 256);
  }
  return bytes;
}

/** ยอมรับ key ที่มีช่องว่าง/ตัวพิมพ์เล็ก/ขีด เหมือนฝั่ง C# (Server/Agent) ทั้งคู่ */
function base32Decode_(base32) {
  var cleaned = base32
    .trim()
    .replace(/=+$/, "")
    .replace(/[\s-]/g, "")
    .toUpperCase();

  var bytes = [];
  var bitBuffer = 0;
  var bitsInBuffer = 0;

  for (var i = 0; i < cleaned.length; i++) {
    var index = BASE32_ALPHABET.indexOf(cleaned.charAt(i));
    if (index < 0) {
      continue;
    }

    bitBuffer = (bitBuffer << 5) | index;
    bitsInBuffer += 5;

    if (bitsInBuffer >= 8) {
      bitsInBuffer -= 8;
      bytes.push((bitBuffer >> bitsInBuffer) & 0xff);
    }
  }

  return bytes;
}

// ---- HTML (ขั้นต่ำสุด - ไม่มี framework ไม่มี CSS framework ภายนอก) ----

function renderCodeHtml_(code, secondsRemaining, email) {
  var spaced = code.substring(0, 3) + " " + code.substring(3);
  return (
    "<html><head><meta http-equiv='refresh' content='" + Math.max(secondsRemaining, 1) + "'>" +
    "<meta name='viewport' content='width=device-width, initial-scale=1'></head>" +
    "<body style='font-family:system-ui,sans-serif;background:#3a3f47;color:#fff;display:flex;" +
    "flex-direction:column;align-items:center;justify-content:center;height:100vh;margin:0'>" +
    "<div style='font-size:13px;opacity:.7;margin-bottom:8px'>เข้าสู่ระบบในชื่อ " + email + "</div>" +
    "<div style='font-size:56px;font-weight:700;letter-spacing:.15em;font-family:monospace'>" + spaced + "</div>" +
    "<div style='font-size:14px;opacity:.7;margin-top:10px'>เปลี่ยนรหัสใหม่ในอีก " + secondsRemaining + " วินาที</div>" +
    "<div style='font-size:12px;opacity:.5;margin-top:24px;max-width:320px;text-align:center'>" +
    "พิมพ์รหัสนี้ที่จอล็อกของเครื่องนักเรียนที่ต้องการปลด - หน้านี้รีเฟรชอัตโนมัติทุกรอบ</div>" +
    "</body></html>"
  );
}

function renderDeniedHtml_(email) {
  return (
    "<html><body style='font-family:system-ui,sans-serif;padding:40px;text-align:center'>" +
    "<h2>ไม่มีสิทธิ์เข้าหน้านี้</h2>" +
    "<p>บัญชี " + (email || "(ไม่ทราบ - ยังไม่ได้ login ด้วยบัญชี Google)") +
    " ไม่อยู่ในรายชื่อที่ Admin อนุญาต</p>" +
    "<p style='font-size:13px;color:#666'>ถ้าควรมีสิทธิ์ ให้แจ้ง Admin เพิ่มอีเมลนี้ใน Script Properties (ALLOWED_EMAILS)</p>" +
    "</body></html>"
  );
}
