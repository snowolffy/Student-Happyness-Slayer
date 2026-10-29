/*
  RemoteLockKnob - firmware สำหรับ Friend Robot Model HiFi (ESP32)

  หน้าที่: ปุ่ม < / > เลื่อน "เลือก" ระหว่าง LOCK / UNLOCK (แสดงบนจอ OLED SSD1306 128x64 I2C บนบอร์ด +
  Serial Monitor คู่กัน) แล้วกด SW_OK เพื่อ "ยืนยัน" ตัวเลือกที่ไฮไลต์อยู่ ณ ขณะนั้น - ตอนนั้นถึงจะยิง
  คำสั่งจริงไปหา RemoteBridge (ผ่าน Tailscale Funnel) ว่าให้ overwrite ทับสถานะทั้งห้องเป็นแบบไหน
  (ดูรายละเอียดสถาปัตยกรรมทั้งหมดใน project memory "remote lock bridge design") - ปุ่ม < / > เปลี่ยน
  ตัวเลือกเฉยๆ ไม่ยิง request จนกว่าจะกด SW_OK ยืนยัน

  2026-09-14: เปลี่ยนจาก knob (potentiometer) มาเป็นปุ่มกด 3 ปุ่มแทน หลังพบว่า knob ไม่ sweep เต็ม
  ADC range เท่าที่ควร (ค้างที่ 0 เป็นส่วนใหญ่) - ปุ่มดิจิทัลไม่มีปัญหานี้เลย

  WiFi: ต่อ WiFi วงไหนก็ได้ที่ออกเน็ตได้ (ไม่จำเป็นต้องเป็นวงเดียวกับเครื่องที่รัน RemoteBridge เลย)
  เพราะคุยกันผ่าน Tailscale Funnel (public HTTPS URL) ไม่ใช่ LAN โดยตรง (ดู project_vnc_refused_diagnosis:
  WiFi กับ LAN ของโรงเรียนอยู่คนละ subnet) รองรับหลายเครือข่ายพร้อม fallback อัตโนมัติ (WIFI_NETWORKS[]
  ใน secrets.h) + auto-reconnect ถ้า WiFi หลุดระหว่างทำงาน

  ออกแบบให้ "ไม่มีจุดไหนค้างรอโดยทำอะไรไม่ได้เลย" โดยตั้งใจ - ตอนบูตขึ้นหน้า LOCK/UNLOCK ทันทีไม่รอ
  WiFi ต่อก่อน แล้วค่อยเริ่มต่อ WiFi อัตโนมัติในพื้นหลัง ระหว่างต่อ (ไม่ว่าจะรอบแรกตอนบูต หรือตอน
  auto-reconnect ทีหลัง) กดปุ่ม BACK เมื่อไหร่ก็ได้เพื่อยกเลิกแล้วเข้าเมนูเลือก WiFi เอง (ดู
  runWifiMenu/tryConnectOnce) - แก้ปัญหาที่เจอจริงตอนทดสอบ (นั่งดูจอ "Connecting..." โดยทำอะไรไม่ได้เลย
  ทั้งที่ WiFi ตรงนั้นต่อไม่ติดจริงๆ)

  ปุ่มกดใหม่ (แทน knob เดิม): GPIO13 = `<`, GPIO14 = `>`, GPIO25 = BACK/ยกเลิก/เข้าเมนู WiFi -
  **ห้ามใช้ GPIO12** ยืนยันแล้วจากการทดสอบจริงว่าทำให้บอร์ดบูตไม่ติด (เป็น ESP32 strapping pin คุม
  แรงดัน flash ตอนบูต) GPIO13/14/25 ไม่ใช่ strapping pin และรองรับ INPUT_PULLUP จริง (ต่างจาก SW_OK
  ที่อยู่ใน GPIO 34-39 ซึ่งเป็นขา input-only ไม่มี pull-up ในตัวชิปเลย - ดู pinMode ใน setup())

  ทุกปุ่มเดายังว่าเป็น active-LOW ไว้ก่อน (ดู struct Button ด้านล่าง) - เปิด Serial Monitor แล้วดูค่า
  raw ที่พิมพ์ตอนกด/ปล่อยปุ่มจริงแต่ละปุ่ม ถ้าไม่ตรงพฤติกรรมจริงให้สลับ activeLow ของปุ่มนั้นในโค้ด

  ค่าลับ (WiFi/REMOTE_SECRET) อยู่ใน secrets.h แยกไฟล์ต่างหาก (gitignore ไว้แล้ว ไม่มีวันหลุดเข้า git
  ต่างจาก .ino นี้ที่ commit ได้ปกติ) - ครั้งแรกให้ copy secrets.h.example เป็น secrets.h ในโฟลเดอร์
  เดียวกัน แล้วใส่ค่าจริงในนั้น

  2026-09-15: เพิ่มเมนู "Others" ต่อท้ายรายชื่อ WiFi ในเมนูเลือก WiFi (runWifiMenu) - รวมของเล่น
  เล็กๆที่ไม่เกี่ยวกับ WiFi/lock เลย (ตอนนี้มีเกม Mini Dino + Clear old score) เข้าเมนูนี้แล้วทุกอย่าง
  ของ WiFi จะหยุดชั่วคราวโดยธรรมชาติอยู่แล้ว (ทั้งโปรแกรมเป็น loop() เดี่ยว ไม่มี multitasking จริง -
  ตอนอยู่ใน runOthersMenu()/runMiniDinoGame() ซึ่งเป็น blocking while(true) เหมือนเมนูอื่นๆ ในไฟล์นี้
  CPU ทั้งหมดอยู่ตรงนั้น ไม่มีทางที่ auto-reconnect หรืออะไรของ WiFi จะแทรกมาทำงานพร้อมกันได้เลย)
*/

#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <Preferences.h> // เก็บ high score ของ Mini Dino ถาวรข้าม reboot/ไฟดับ (ดู runMiniDinoGame)
#include <esp_netif.h> // esp_netif_set_dns_info() - ดู forceCleanDnsServers()
#include <lwip/dns.h> // dns_local_addhost() - ดู resolveViaDoh()
// ต้องลง library "Adafruit SSD1306" และ "Adafruit GFX Library" ผ่าน Library Manager ก่อน (Sketch >
// Include Library > Manage Libraries...) ถ้ายังไม่มี - เป็น library มาตรฐานสำหรับจอ OLED SSD1306

// เตรียมไว้ใช้ทีหลัง (ยังไม่ได้เอาไปใช้ที่ไหนเลยตอนนี้) - ฟอนต์ built-in ของ Adafruit_GFX (setCursor/
// print ปกติ) ขนาดตายตัว 5x7 พิกเซล (รวม spacing 6x8) setTextSize() ขยายได้อย่างเดียว เล็กกว่า size 1
// ไม่ได้เลย - Picopixel เป็นฟอนต์ที่แถมมากับ Adafruit_GFX เอง (ไม่ต้องลง library เพิ่ม) เล็กกว่า
// built-in มาก เหมาะกับจุดที่ข้อความยาวแล้วอยากยัดลงจอ 128x64 ให้ได้มากขึ้น (เช่น ชื่อปลายาวๆใน Fisch,
// SSID ยาวๆในหน้า Device Stats) ใช้งาน: เรียก display.setFont(&Picopixel) ก่อนวาด แล้ว
// display.setFont(NULL) กลับเป็นฟอนต์ default ตอนจบ (คนละระบบพิกัดกับ setCursor/println ของฟอนต์เดิม
// นิดหน่อย - baseline อยู่ที่ตำแหน่ง Y ที่ตั้ง ไม่ใช่มุมบนซ้ายแบบฟอนต์ built-in ลองวาดดูก่อนตัดสินใจตำแหน่ง)
#include <Fonts/Picopixel.h>

#define SCREEN_WIDTH 128
#define SCREEN_HEIGHT 64
#define OLED_I2C_ADDRESS 0x3C

// ตั้ง false ได้ถ้าอยากลอง bypass จอทั้งหมด (ข้าม Wire.begin()/display.* ทุกจุด) เพื่อ debug ปัญหาอื่น
// - พิสูจน์แล้ว 2026-09-14 ว่าไม่ใช่สาเหตุของบั๊ก knob เดิม (ปิดจอแล้วอาการเดิมยังอยู่)
const bool ENABLE_DISPLAY = true;

// ตั้ง true ได้ตอนต้องดีบักปัญหาปุ่ม (โชว์ raw HIGH/LOW ทุกครั้งที่ปุ่มไหนเปลี่ยนสถานะ - ดู
// Button::debugPrintIfChanged) ปิดไว้เป็น false ตามปกติเพราะปุ่มทุกตัวยืนยันแล้วว่า active-LOW ถูกต้อง
// หมดแล้ว ไม่ต้องคอยพิมพ์ raw value กวนตาอีก
const bool ENABLE_DEBUG_SERIAL = false;

Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, -1);
bool displayReady = false; // false = เปิดจอไม่สำเร็จ หรือ ENABLE_DISPLAY = false - ทำงานต่อได้โดยดู Serial Monitor แทน ไม่ทำให้ทั้งโปรแกรมค้าง

struct WifiCredential {
  const char* ssid;
  const char* password;
};


#include "secrets.h" // WIFI_NETWORKS[] / WIFI_NETWORK_COUNT / TRIGGER_URL_BASE / REMOTE_SECRET - ดู comment หัวไฟล์

const int BTN_PREV_PIN = 13; // `<`
const int BTN_NEXT_PIN = 14; // `>`
const int BTN_BACK_PIN = 25; // back / ยกเลิก / เข้าเมนูเลือก WiFi
const int SW_OK_PIN = 36;    // ยืนยัน - ยืนยันแล้วจากผู้ใช้เช็คของจริงมา
const int BUZZER_PIN = 4;    // buzzer TMB12A05 (HYDZ) - ยืนยันจริงแล้วว่าเป็น ACTIVE buzzer (มี
                              // oscillator ในตัว เสียงตายตัวเสียงเดียว) ไม่ใช่ passive - ทดสอบจริงแล้วว่า
                              // เปลี่ยน frequency ผ่าน tone() ไม่ทำให้เสียงสูงต่ำต่างกันเลย เพราะงั้น
                              // **ห้ามใช้ tone()/noTone() กับตัวนี้** ขับตรงด้วย digitalWrite HIGH/LOW
                              // เท่านั้น (ดู playBeep/playBeepPattern) แยกความหมายเสียงด้วยจังหวะ/ความยาว
                              // แทน pitch (ทำ Simon Says/เปียโนจริงจังไม่ได้แล้ว แค่ beep แจ้งเตือนได้)

// true = เปิดเสียง, false = ปิดเสียง (ผู้ใช้สลับได้ในเมนู Others - ดู OTHERS_SOUND_TOGGLE_INDEX) โหลด
// ค่าจริงจาก NVS ใน setup() ทับค่า default นี้อีกที - เก็บเป็น global เพราะ playBeep() ต้องอ่านค่านี้
// ทุกครั้งที่จะเล่นเสียง โดยไม่ต้องเปิด Preferences ใหม่ทุกครั้ง (ซึ่งช้ากว่า)
bool soundEnabled = true;

// true = Slot Machine หมุนแบบมี animation ก่อนออกผล (สุ่มโชว์สัญลักษณ์ถี่ๆชั่วครู่), false = ออกผลทันที
// - อยู่ในเมนู Others > Settings เหมือนกับ Sound toggle (ผู้ใช้ขอให้ toggle ได้เพราะเป็นแค่ของขำๆ)
bool slotAnimationEnabled = true;

/// ตัวช่วยอ่านปุ่มกดแบบมี edge-detection (กดครั้งเดียว = trigger ครั้งเดียว ไม่ใช่ทุก loop ที่กดค้าง)
/// ใช้ struct เดียวกันทั้ง 4 ปุ่มแทนที่จะเขียนซ้ำ 4 รอบ
// นับรวมจำนวนครั้งที่ "เพิ่งกด" (justPressed edge) ของทุกปุ่มทุกจุดในโปรแกรม - ผ่าน consumeJustPressed()
// เดียวเท่านั้นเสมอ (ทุกเมนู/เกมเรียกผ่านฟังก์ชันนี้หมด) เลยนับรวมศูนย์กลางได้ที่จุดเดียวง่ายๆ ไม่ persist
// ข้าม reboot (นับแค่ session นี้ คู่กับ Uptime ในหน้า Device Stats > Session - ดู runDeviceStats)
unsigned long totalButtonPresses = 0;

struct Button {
  int pin;
  bool activeLow;
  bool lastPressed = false;
  int lastRaw = -1;

  bool isPressed() {
    int raw = digitalRead(pin);
    return activeLow ? (raw == LOW) : (raw == HIGH);
  }

  /// true เฉพาะ "เพิ่งกด" (rising edge ของการกด) ไม่ใช่ตอนกดค้าง
  bool consumeJustPressed() {
    bool pressed = isPressed();
    bool justPressed = pressed && !lastPressed;
    lastPressed = pressed;
    if (justPressed) totalButtonPresses++;
    return justPressed;
  }

  /// พิมพ์ค่า raw ทุกครั้งที่เปลี่ยน - เอาไว้ยืนยัน activeLow ว่าตั้งถูกไหม (ดู comment หัวไฟล์)
  void debugPrintIfChanged(const char* name) {
    int raw = digitalRead(pin);
    if (raw != lastRaw) {
      Serial.print("[debug] ");
      Serial.print(name);
      Serial.print(" raw = ");
      Serial.println(raw == HIGH ? "HIGH" : "LOW");
      lastRaw = raw;
    }
  }
};

Button btnPrev = {BTN_PREV_PIN, true};
Button btnNext = {BTN_NEXT_PIN, true};
Button btnBack = {BTN_BACK_PIN, true};
Button btnOk = {SW_OK_PIN, true}; // active-LOW ยืนยันแล้วจากการทดสอบจริงก่อนหน้า

// ตัวเลือกที่ปุ่ม < / > เลื่อนไปมาได้ - เพิ่มตัวเลือกในอนาคตได้แค่เพิ่ม element ในนี้
const char* OPTIONS[] = {"UNLOCK", "LOCK"};
const int OPTION_COUNT = 2;

// เดิม 15000 - พบจริง 2026-09-14 ว่า Br_School ใช้เวลาต่อจริง ~15.03 วิ (ชนขอบ timeout เป๊ะๆ) ทำให้
// รอบแรกมักโดนตัดก่อนจะต่อสำเร็จ ต้องวนไปทั้ง 6 เครือข่ายก่อนจะย้อนมาเจอจังหวะที่ทันพอดีในรอบถัดไป -
// เพิ่มเป็น 30 วิ (ไม่ใช่ 25) ตามที่ผู้ใช้ขอ เผื่อการเชื่อมต่อที่ช้า/สัญญาณอ่อนแบบนี้มีโอกาสสำเร็จตั้งแต่
// รอบแรก ใช้ค่าเดียวกันทั้งตอน auto fallback และตอนเลือก WiFi เองในเมนู (ไม่แยก 2 ค่าอีกต่อไป)
const unsigned long WIFI_CONNECT_TIMEOUT_MS = 30000;


int selectedIndex = 0;

// true = tryConnectOnce() ครั้งล่าสุดจบเพราะผู้ใช้กด BACK ยกเลิก (ไม่ใช่ timeout ธรรมดา) - connectToWifi()
// เช็คค่านี้หลังเรียก tryConnectOnce() ทุกครั้งเพื่อแยกสองกรณีออกจากกัน: timeout ธรรมดา = ลองเครือข่าย
// ถัดไปในลิสต์ต่อ, ถูกยกเลิก = หยุดวนทั้งหมดทันทีแล้วคืนค่า false ให้ผู้เรียกไปเปิดเมนูเอง (ไม่งั้นกด
// BACK แล้วจะเห็นแค่ข้ามไปลองเครือข่ายถัดไปเฉยๆ ไม่เข้าเมนูตามที่ตั้งใจ)
bool wifiConnectCancelled = false;

// true = loop() ควร auto-reconnect ตอนนี้ถ้า WiFi หลุด - **ไม่ใช่** "เคยเชื่อมสำเร็จมาก่อนไหม" (นั่น
// เป็นรุ่นแรกที่เขียนไว้ แล้วพบว่าไม่พอ - ดูล่าง) แต่คือ "สถานะล่าสุดควร auto-reconnect ไหม" ล้วนๆ
//
// ตั้ง true ทุกครั้งที่เชื่อมสำเร็จ (ดู tryConnectOnce) ตั้ง false ทุกครั้งที่ผู้ใช้กด BACK ออกจาก
// runWifiMenu() "โดยที่ตอนนั้นยังไม่ได้เชื่อม" ไม่ว่าจะเพราะไม่เคยเชื่อมเลย หรือเพิ่งยกเลิกการสลับ
// เครือข่ายกลางคัน (ดู runWifiMenu) - ผู้ใช้เจอบั๊กจริง 2 รอบก่อนจะได้ตัวแปรนี้ครบ:
//   รอบแรก (ไม่เคยเชื่อมเลยตั้งแต่บูต): เดิมใช้ "hasConnectedOnce" (เช็คแค่ครั้งเดียวตอนบูต) แก้ได้
//   รอบสอง (เชื่อมสำเร็จแล้ว, กลับเข้าเมนูเพื่อสลับเครือข่าย, ยกเลิกกลางคัน): hasConnectedOnce ยังเป็น
//     true อยู่ (เพราะเคยเชื่อมสำเร็จมาก่อนจริง) ทำให้ auto-reconnect ลากกลับเข้าหน้า Connecting ทันที
//     เหมือนเดิมอยู่ดี - ต้องเปลี่ยนความหมายเป็น "ควร reconnect ตอนนี้ไหม" ไม่ใช่ "ประวัติเคยเชื่อมไหม"
bool autoReconnectEnabled = false;

/// เล่นเสียงสั้นๆ ผ่าน buzzer - เช็ค soundEnabled ให้ทุกจุดที่อยากเล่นเสียงในโปรแกรม ไม่ต้องเช็คเองซ้ำๆ
/// (ปิดเสียงจากเมนู Others แล้วจุดนี้จะ no-op เงียบๆ ไม่ error อะไร) ขับตรงด้วย digitalWrite ไม่ใช้ tone()
/// เพราะ TMB12A05 เป็น active buzzer (ทดสอบจริงแล้วว่าปรับ pitch ไม่ได้ - ดู comment ของ BUZZER_PIN)
/// **บล็อกโปรแกรมไว้ตลอดช่วง durationMs** (ต่างจาก tone() แบบเดิมที่ไม่บล็อก) แต่ทุกจุดที่เรียกใช้ตอนนี้
/// เป็นช่วงสั้นๆ (ไม่กี่สิบ-ร้อย ms) หรือเรียกตอนเกมจบอยู่แล้ว ไม่กระทบการเล่นจริง
void playBeep(unsigned int durationMs) {
  if (!soundEnabled) return;
  digitalWrite(BUZZER_PIN, HIGH);
  delay(durationMs);
  digitalWrite(BUZZER_PIN, LOW);
}

/// เล่นเสียงเป็นจังหวะ (beep เว้นจังหวะ ทำซ้ำ count ครั้ง) - ใช้แยกความหมายเสียงแทน pitch ที่ทำไม่ได้แล้ว
/// เช่น "ได้ high score" ใช้ 3 บี๊บถี่ๆ แทนเสียงสูงแบบเดิม
void playBeepPattern(int count, unsigned int onMs, unsigned int offMs) {
  if (!soundEnabled) return;
  for (int i = 0; i < count; i++) {
    digitalWrite(BUZZER_PIN, HIGH);
    delay(onMs);
    digitalWrite(BUZZER_PIN, LOW);
    if (i < count - 1) delay(offMs);
  }
}

// Others (เมนูของเล่น/เกมทั้งหมด - เดิมเคยอยู่ในไฟล์นี้ตรงๆ จนกลายเป็น 77% ของทั้งไฟล์ ย้ายออกไปเป็น
// ไฟล์ .h แยกตอนแยก Others ออกจากฟีเจอร์ remote lock หลัก) - ทั้ง 6 ไฟล์ forward declare ฟังก์ชันของ
// ตัวเองและของกันและกันไว้ครบแล้ว (ดูคอมเมนต์หัวไฟล์ others_menu.h) เพราะงั้น #include เรียงลำดับไหนก็ได้
// ต้องอยู่หลังจุดที่ btnPrev/btnNext/btnOk/btnBack/playBeep/playBeepPattern ถูกประกาศแล้วด้านบนเท่านั้น
// (ตัวแปร/ฟังก์ชันพวกนี้ไม่มี auto-prototype ช่วยข้ามลำดับแบบฟังก์ชันที่เขียนตรงใน .ino)
#include "others_games_arcade.h"  // Mini Dino, Pong, DVD Bounce, Flappy Block
#include "others_games_board.h"   // Tic-Tac-Toe (+ minimax), Game of Life, Lights Out, Wire Connect
#include "others_games_casual.h"  // Magic 8-Ball, Dice Roller, Rock Paper Scissors, Slot Machine
#include "others_tamagotchi.h"    // Tamagotchi
#include "fisch_game.h"           // Fisch (เกมตกปลา)
#include "others_menu.h"          // เมนู Others เอง + Settings + Device Stats + คลัง Pic/GIF + Draw mode

void setup() {
  Serial.begin(115200);
  randomSeed(micros()); // เอาไว้สุ่มระยะห่าง obstacle ใน Mini Dino (ดู runMiniDinoGame) ไม่ต้องแม่นยำมาก

  Preferences settingsPrefs;
  settingsPrefs.begin("settings", true); // read-only พอ - ไม่ได้เขียนตอน boot
  soundEnabled = settingsPrefs.getBool("soundOn", true);
  slotAnimationEnabled = settingsPrefs.getBool("slotAnim", true);
  settingsPrefs.end();
  Serial.print("[Sound] โหลดค่าจาก NVS: ");
  Serial.println(soundEnabled ? "เปิด" : "ปิด");

  pinMode(BUZZER_PIN, OUTPUT);
  digitalWrite(BUZZER_PIN, LOW); // ปิดไว้ก่อนตั้งแต่บูต กัน buzzer ค้างดังถ้า pin ลอยตัวช่วงสั้นๆ

  pinMode(BTN_PREV_PIN, INPUT_PULLUP);
  pinMode(BTN_NEXT_PIN, INPUT_PULLUP);
  pinMode(BTN_BACK_PIN, INPUT_PULLUP);
  // ไม่ใช้ INPUT_PULLUP สำหรับ SW_OK - GPIO 34-39 ไม่มี pull-up ในตัวชิป (ข้อจำกัดจริงของ ESP32)
  // พึ่ง pull resistor ที่บอร์ด Friend Robot ต่อไว้ให้แล้วจากโรงงานแทน
  pinMode(SW_OK_PIN, INPUT);

  if (!ENABLE_DISPLAY) {
    Serial.println("[debug] ENABLE_DISPLAY = false - ข้ามจอทั้งหมด (ไม่เรียก Wire.begin()/display.* เลย)");
  } else {
    Wire.begin();
    // กัน I2C ค้างทั้งโปรแกรมถ้าจอหรือสายมีปัญหา - ปกติ Wire library ไม่มี timeout เอง
    Wire.setTimeOut(50);
    displayReady = display.begin(SSD1306_SWITCHCAPVCC, OLED_I2C_ADDRESS);
    if (!displayReady) {
      Serial.println("[warn] เปิดจอ OLED ไม่สำเร็จ (เช็คสาย I2C/address 0x3C) - ทำงานต่อโดยไม่มีจอ ดู Serial Monitor แทน");
    } else {
      display.setTextColor(SSD1306_WHITE);
    }
  }

  WiFi.mode(WIFI_STA);

  // โชว์หน้า LOCK/UNLOCK ทันทีก่อนต่อ WiFi เลย - ไม่บล็อกผู้ใช้ไว้บนหน้า "Connecting..." เฉยๆ
  // (ดู comment หัวไฟล์) selectedIndex เริ่มที่ 0 (UNLOCK) เสมอตอนบูต เพราะไม่มี "ตำแหน่งเดิม" ให้อ่าน
  // เหมือน knob (ปุ่มไม่มีตำแหน่งค้างแบบนั้น)
  selectedIndex = 0;
  printSelection();

  bool connected = connectToWifi();
  if (!connected) {
    // ถูกยกเลิกด้วยปุ่ม BACK ระหว่าง auto-connect - เปิดเมนูเลือกเองต่อทันที
    runWifiMenu();
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.print("เชื่อม WiFi สำเร็จ - IP: ");
    Serial.println(WiFi.localIP());
    Serial.print("Gateway: ");
    Serial.println(WiFi.gatewayIP());
    Serial.print("DNS: ");
    Serial.println(WiFi.dnsIP());
    Serial.print("Free heap หลังเชื่อม WiFi: ");
    Serial.println(ESP.getFreeHeap());

    // ทดสอบว่าเน็ตออกอินเทอร์เน็ตได้จริงไหมก่อน ด้วยเว็บที่รู้อยู่แล้วว่าต้องเข้าได้ปกติ - แยกปัญหาว่า
    // "เน็ตออกไม่ได้เลย" กับ "เน็ตออกได้ แต่ Funnel URL เข้าไม่ได้เฉพาะเจาะจง" ออกจากกัน
    //
    // 2026-09-14: สงสัยว่า connection refused ที่เจอซ้ำๆ ทุกเครือข่ายอาจเป็นเพราะ ESP32 heap ไม่พอ
    // สำหรับ TLS handshake (WiFiClientSecure/mbedTLS ต้องการ heap ก้อนใหญ่ ~40KB+ ต่อครั้ง) - ยัง
    // ไม่ยืนยัน รอตัวเลข Free heap จริงจากการทดสอบก่อนสรุป (ESP32 ทั่วไปมี heap รวม ~300KB)
    Serial.println("ทดสอบเน็ตทั่วไปด้วย https://example.com ...");
    Serial.print("Free heap ก่อนทดสอบ: ");
    Serial.println(ESP.getFreeHeap());
    testConnectivity("https://example.com");
    Serial.print("Free heap หลังทดสอบ: ");
    Serial.println(ESP.getFreeHeap());
  }

  updateDisplay(); // รีเฟรชให้โชว์สถานะ WiFi ล่าสุด (ไม่ว่าจะเชื่อมสำเร็จหรือไม่ก็ตาม)
}

/// บังคับใช้ DNS server สาธารณะตายตัว (Google + Cloudflare) แทนที่จะเชื่อ DNS ที่เครือข่ายแจกมาให้ -
/// ต้องเรียก "หลัง" เชื่อม WiFi สำเร็จเท่านั้น (ไม่ใช่ก่อน WiFi.begin()) เพราะ DHCP client ของ ESP32
/// จะเขียนทับ DNS ด้วยค่าที่เครือข่ายแจกมาเองทันทีที่ negotiate เสร็จ
///
/// ลองมาแล้ว 2 วิธี: (1) WiFi.config() ก่อน begin() - โดน DHCP ทับกลับ (2) dns_setserver() ของ lwIP
/// ตรงๆ หลัง connect - WiFi.dnsIP() (ซึ่งอ่านจาก esp_netif ไม่ใช่ lwIP โดยตรง) โชว์ค่าใหม่ถูกต้อง แต่
/// การ resolve จริงยังพังอยู่ (ได้ 0.0.0.0) - สงสัยว่า esp_netif (ชั้นที่ ESP-IDF ใช้จัดการ DNS ของ
/// interface จริงๆ) กับ lwIP ระดับล่างเป็นคนละแหล่งข้อมูลที่ไม่ sync กันอัตโนมัติ เปลี่ยนมาเรียก
/// esp_netif_set_dns_info() ตรงๆ แทน (API ระดับที่ ESP-IDF ตั้งใจให้ใช้สำหรับตั้ง DNS ของ interface
/// จริงๆ - ควรจะเป็น "แหล่งความจริงเดียว" ที่ทั้ง resolver จริงและ WiFi.dnsIP() ต้องอ่านค่าเดียวกัน)
///
/// พบจริง 2026-09-14: เครือข่ายบางวง (เช่น "kit-test-net") แจก DNS ที่ "รู้จัก" Tailscale (เพราะ
/// อุปกรณ์ที่แชร์ฮอตสปอตนั้นลง Tailscale + MagicDNS ไว้เอง) ทำให้ ESP32 (ซึ่งไม่มี Tailscale เลย)
/// resolve ชื่อ .ts.net ได้ IP ภายในของ tailnet (100.x.x.x) แทนที่จะเป็น IP สาธารณะจริง - IP แบบนั้น
/// เข้าไม่ได้จากอินเทอร์เน็ตทั่วไปเลย (ไม่ใช่ปัญหา TLS/heap อย่างที่เคยสงสัยก่อนหน้า)
void forceCleanDnsServers() {
  esp_netif_t *netif = esp_netif_get_handle_from_ifkey("WIFI_STA_DEF");
  if (netif == nullptr) {
    Serial.println("[warn] หา netif ของ WiFi STA ไม่เจอ - ตั้ง DNS เองไม่ได้รอบนี้");
    return;
  }

  esp_netif_dns_info_t dns1 = {};
  dns1.ip.type = ESP_IPADDR_TYPE_V4;
  dns1.ip.u_addr.ip4.addr = ESP_IP4TOADDR(8, 8, 8, 8);
  esp_netif_set_dns_info(netif, ESP_NETIF_DNS_MAIN, &dns1);

  esp_netif_dns_info_t dns2 = {};
  dns2.ip.type = ESP_IPADDR_TYPE_V4;
  dns2.ip.u_addr.ip4.addr = ESP_IP4TOADDR(1, 1, 1, 1);
  esp_netif_set_dns_info(netif, ESP_NETIF_DNS_BACKUP, &dns2);

  Serial.println("บังคับ DNS เป็น 8.8.8.8 / 1.1.1.1 แล้ว (ผ่าน esp_netif_set_dns_info โดยตรง)");
}

/// พยายามเชื่อมกับเครือข่ายเดียวครั้งเดียว (มี timeout) - คืน true ถ้าเชื่อมสำเร็จ, false ถ้าไม่สำเร็จ
/// (timeout หรือถูกยกเลิกด้วยปุ่ม BACK - เช็คสถานะจริงจาก WiFi.status() เอาเองถ้าต้องแยกสองกรณีนี้)
bool tryConnectOnce(const WifiCredential &cred, unsigned long timeoutMs) {
  wifiConnectCancelled = false;

  // เคลียร์ state ค้างก่อน begin() ใหม่ทุกครั้ง - พบว่า reset ด้วยปุ่ม (ไม่ใช่ power cycle เต็มรูปแบบ)
  // บางครั้งทิ้ง WiFi radio ไว้ในสถานะเดิมจากรอบก่อน ทำให้รอบใหม่ negotiate ช้ากว่าที่ควร
  WiFi.disconnect(true);
  delay(100);

  unsigned long startMs = millis();
  WiFi.begin(cred.ssid, cred.password);

  // อัปเดตวินาทีที่นับไปแล้วบนจอ OLED ทุกๆ 2 วิ (ไม่ใช่ 1 วิ) - ลดความถี่การวาดจอ (I2C) ลงตามที่ขอ
  // เพื่อลดภาระ CPU/บัส I2C ระหว่างรอต่อ WiFi นานๆ เอา Serial ออกไปแล้วก่อนหน้านี้ (สแปมเกินไป) เหลือ
  // แค่จอ OLED อย่างเดียว + เช็คปุ่ม BACK ทุกรอบเพื่อให้ยกเลิกได้ทันที ไม่ต้องรอ timeout เต็มๆ - นี่คือ
  // หัวใจของการ "ไม่ค้างรอโดยทำอะไรไม่ได้เลย" ตามที่ตั้งใจไว้
  const unsigned long DISPLAY_UPDATE_INTERVAL_MS = 2000;
  unsigned long lastDisplayMs = 0;
  while (WiFi.status() != WL_CONNECTED && millis() - startMs < timeoutMs) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("ยกเลิกการเชื่อมต่อ (กด BACK)");
      wifiConnectCancelled = true;
      return false;
    }
    delay(100);
    if (millis() - lastDisplayMs >= DISPLAY_UPDATE_INTERVAL_MS) {
      unsigned long elapsedSec = (millis() - startMs) / 1000;
      String line1 = "Connecting... " + String(elapsedSec) + "s";
      showDisplayMessage(line1.c_str(), cred.ssid);
      lastDisplayMs = millis();
    }
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.print("เชื่อมสำเร็จกับ ");
    Serial.print(cred.ssid);
    Serial.print(" ใช้เวลา ");
    Serial.print((millis() - startMs) / 1000.0);
    Serial.println(" วินาที");
    forceCleanDnsServers(); // ต้องเรียกตรงนี้ (หลังเชื่อมสำเร็จ) ไม่ใช่ก่อน WiFi.begin() - ดู comment ของฟังก์ชัน
    autoReconnectEnabled = true;
    return true;
  }

  // ต่อไม่ติดภายในเวลาที่กำหนด - WiFi.status() ล่าสุดช่วยบอกว่าติดปัญหาอะไร (รหัสผ่านผิด/หา AP ไม่เจอ
  // /ฯลฯ) ดูความหมายได้ที่ https://docs.espressif.com/projects/arduino-esp32/en/latest/api/wifi.html
  Serial.print("เชื่อม ");
  Serial.print(cred.ssid);
  Serial.print(" ไม่ติดภายใน ");
  Serial.print(timeoutMs / 1000);
  Serial.print(" วิ (WiFi.status() = ");
  Serial.print(WiFi.status());
  Serial.println(")");
  return false;
}

/// ลองต่อทีละเครือข่ายตามลำดับใน WIFI_NETWORKS[] ตัวไหนต่อได้ก่อนก็ใช้ตัวนั้น ถ้าลองครบทุกตัวแล้วยัง
/// ไม่ติดสักตัวจะวนกลับไปเริ่มตัวแรกใหม่เรื่อยๆ (ไม่ยอมแพ้) - คืน false ทันทีถ้าถูกยกเลิกด้วยปุ่ม BACK
/// ระหว่างลองเครือข่ายไหนก็ตาม (ไม่ลองเครือข่ายที่เหลือต่อ) ผู้เรียกควรเปิด runWifiMenu() ต่อถ้า false
bool connectToWifi() {
  while (WiFi.status() != WL_CONNECTED) {
    for (int i = 0; i < WIFI_NETWORK_COUNT; i++) {
      Serial.print("กำลังลองเชื่อม WiFi (");
      Serial.print(i + 1);
      Serial.print("/");
      Serial.print(WIFI_NETWORK_COUNT);
      Serial.print("): ");
      Serial.println(WIFI_NETWORKS[i].ssid);
      showDisplayMessage("Connecting WiFi...", WIFI_NETWORKS[i].ssid);

      if (tryConnectOnce(WIFI_NETWORKS[i], WIFI_CONNECT_TIMEOUT_MS)) {
        return true;
      }

      // แยก "timeout ธรรมดา" (ลองเครือข่ายถัดไปต่อ) ออกจาก "ผู้ใช้กด BACK ยกเลิก" (หยุดวนทั้งหมด
      // ทันที คืน false ให้ผู้เรียกไปเปิดเมนูเอง) - ดู comment ของ wifiConnectCancelled
      if (wifiConnectCancelled) {
        return false;
      }
    }

    Serial.println("ลองครบทุก WiFi แล้วยังไม่ติดสักตัว - เริ่มวนใหม่ทั้งชุด");
  }
  return true;
}

/// http.errorToString() ของ HTTPClient เป็นแค่ wrapper ทั่วไป - ไม่ว่า DNS resolve ไม่ได้, TCP
/// connect ไม่ติด, หรือ TLS handshake คุยกันไม่รู้เรื่อง (cipher/TLS version ไม่ตรงกัน) ก็โผล่มาเป็น
/// "-1 connection refused" เหมือนกันหมด ไม่บอกว่าจริงๆ พังตรงไหน - client.lastError() ดึง error
/// message จริงจาก mbedTLS ชั้นล่างสุดมาให้ ละเอียดกว่ามาก (เช่น "SSL - Handshake protocol not within
/// min/max boundaries" ถ้าเป็นปัญหา TLS version/cipher ไม่ตรงกัน) เรียกหลัง GET() ล้มเหลวเท่านั้น
void printTlsErrorIfAny(WiFiClientSecure &client) {
  char errBuf[128];
  int errCode = client.lastError(errBuf, sizeof(errBuf));
  if (errCode != 0) {
    Serial.print("  TLS error จริง: ");
    Serial.println(errBuf);
  }
}

/// ดึง hostname ล้วนๆ ออกจาก URL รูปแบบ https://host/path - ตัด scheme กับ path ออก เหลือแค่ชื่อโดเมน
String extractHost(const String &url) {
  int start = url.indexOf("://");
  start = (start == -1) ? 0 : start + 3;
  int end = url.indexOf('/', start);
  if (end == -1) {
    end = url.length();
  }
  return url.substring(start, end);
}

/// resolve DNS แยกต่างหากจากการต่อ HTTPS จริง เพื่อดูว่า "Generic error" ที่ TLS โยนมา เกิดก่อนจะรู้จัก
/// IP ปลายทางด้วยซ้ำไหม (DNS resolve ไม่ได้เลย) หรือ resolve ได้ปกติแต่ต่อ TCP/TLS ไปยัง IP นั้นไม่ติด
/// - สองกรณีนี้สาเหตุคนละเรื่องกันเลย ต้องแยกให้ชัดก่อนเดาต่อ
void debugPrintDnsResolution(const char* url) {
  String host = extractHost(String(url));
  IPAddress resolvedIp;
  bool ok = WiFi.hostByName(host.c_str(), resolvedIp);
  Serial.print("  DNS resolve ");
  Serial.print(host);
  Serial.print(" -> ");
  Serial.println(ok ? resolvedIp.toString() : String("resolve ไม่ได้เลย"));
}

/// resolve hostname ผ่าน DNS-over-HTTPS (Cloudflare 1.1.1.1) โดยใช้ WiFiClientSecure/HTTPClient
/// เดียวกับที่พิสูจน์แล้วว่าใช้งานได้ปกติ (เหมือน testConnectivity ที่ต่อ example.com สำเร็จทุกครั้ง)
/// แทนการพึ่ง built-in DNS resolver ของ ESP32 (WiFi.hostByName/lwIP) ที่พิสูจน์แล้วว่า resolve โดเมน
/// .ts.net ไม่ได้เลย (ได้ 0.0.0.0 ซ้ำๆ ไม่ว่าจะตั้ง DNS server เป็นอะไร ทั้งที่ DNS จริงตอบถูกต้อง -
/// พิสูจน์แล้วจากเบราว์เซอร์จริงและ Google DoH ภายนอก) เดาว่า lwIP DNS client (ซึ่งเป็น
/// implementation แบบพื้นฐาน) parse response ของ Tailscale ไม่ได้ ไม่ว่าจะเป็นเรื่อง response
/// size/CNAME/รูปแบบอื่นๆ
///
/// เดิมตั้งใจฉีดผลลัพธ์เข้า lwIP local host list ด้วย dns_local_addhost() แต่ฟังก์ชันนั้นไม่มีในบอร์ด
/// นี้ (compile error - DNS_LOCAL_HOSTLIST ไม่ได้เปิดใน lwIP build ของ FRIENDROBOT_ESP32 core 2.0.1)
/// เปลี่ยนมาคืนค่า IPAddress ตรงๆ ให้ผู้เรียกเอาไปต่อ socket ด้วย IP ตรงๆ เอง (ดู sendTrigger) แทนที่
/// จะพึ่งกลไก DNS override ระดับ lwIP เลย - คืนค่า IPAddress(0,0,0,0) ถ้า resolve ไม่สำเร็จ
IPAddress resolveViaDoh(const String &hostname) {
  WiFiClientSecure client;
  client.setInsecure();
  HTTPClient http;

  String url = "https://1.1.1.1/dns-query?name=" + hostname + "&type=A";
  http.begin(client, url);
  http.addHeader("Accept", "application/dns-json");
  int httpCode = http.GET();

  if (httpCode != 200) {
    Serial.print("[DoH] ขอ DNS ผ่าน HTTPS ไม่สำเร็จ: HTTP ");
    Serial.println(httpCode);
    http.end();
    return IPAddress(0, 0, 0, 0);
  }

  String body = http.getString();
  http.end();

  // หา "data":"x.x.x.x" ตัวแรกใน JSON ด้วย string search ธรรมดา - format ของ Cloudflare DoH JSON
  // ค่อนข้างตายตัว ไม่ต้องพึ่ง JSON library เพิ่มแค่เพื่อดึงค่าเดียวนี้
  int dataIdx = body.indexOf("\"data\":\"");
  if (dataIdx == -1) {
    Serial.println("[DoH] หา IP ใน response ไม่เจอ (ไม่มี Answer record)");
    return IPAddress(0, 0, 0, 0);
  }
  int start = dataIdx + 8;
  int end = body.indexOf('"', start);
  String ipStr = body.substring(start, end);

  IPAddress resolvedIp;
  if (!resolvedIp.fromString(ipStr)) {
    Serial.print("[DoH] IP ที่ได้ไม่ใช่รูปแบบที่ถูกต้อง: ");
    Serial.println(ipStr);
    return IPAddress(0, 0, 0, 0);
  }

  Serial.print("[DoH] resolve ");
  Serial.print(hostname);
  Serial.print(" -> ");
  Serial.println(ipStr);
  return resolvedIp;
}

void testConnectivity(const char* url) {
  WiFiClientSecure client;
  client.setInsecure();
  HTTPClient http;
  http.begin(client, url);
  int httpCode = http.GET();
  Serial.print("  ผลทดสอบ: HTTP ");
  Serial.print(httpCode);
  Serial.print(" (");
  Serial.print(http.errorToString(httpCode));
  Serial.println(")");
  if (httpCode <= 0) {
    printTlsErrorIfAny(client);
  }
  http.end();
}

/// index 0 ของเมนูคือ "Auto (ลองทุกเครือข่าย)" เสมอ ตามด้วย WIFI_NETWORKS[0..WIFI_NETWORK_COUNT-1]
/// แล้วปิดท้ายด้วย "Others" (ของเล่นที่ไม่เกี่ยวกับ WiFi เลย - ดู runOthersMenu) - รวมเป็น
/// WIFI_NETWORK_COUNT+2 ตัวเลือก (ดู runWifiMenu)
const int WIFI_MENU_ITEM_COUNT = WIFI_NETWORK_COUNT + 2;
const int WIFI_MENU_OTHERS_INDEX = WIFI_MENU_ITEM_COUNT - 1;

/// วาดหน้าจอเมนูเลือก WiFi ตาม index ที่ไฮไลต์อยู่ - แยกออกมาเป็นฟังก์ชันเดียวเพราะเรียกซ้ำหลายจุดใน
/// runWifiMenu() (ตอนเข้าเมนู, ตอนเลื่อน, ตอนเชื่อมไม่สำเร็จแล้วกลับมา)
void showWifiMenuScreen(int menuIndex) {
  if (menuIndex == 0) {
    showDisplayMessage("Select WiFi:", "Auto (try all)");
  } else if (menuIndex == WIFI_MENU_OTHERS_INDEX) {
    showDisplayMessage("Select WiFi:", "Others");
  } else {
    showDisplayMessage("Select WiFi:", WIFI_NETWORKS[menuIndex - 1].ssid);
  }
}

/// เมนูเลือก WiFi ด้วยปุ่ม < / > (เลื่อนดู "Auto" + รายชื่อเครือข่าย) + SW_OK (เชื่อมกับตัวที่ไฮไลต์
/// อยู่ - "Auto" ใช้ connectToWifi() ลองทั้งลิสต์เหมือนตอนบูต ส่วนชื่อเครือข่ายเจาะจงใช้ tryConnectOnce()
/// แค่ตัวเดียว) + BACK (กลับโดยไม่เปลี่ยนอะไร หรือถ้ากำลังเชื่อมอยู่ = ยกเลิกการเชื่อมนั้นแล้วกลับมา
/// เมนูนี้ต่อ) - เรียกได้ทุกเมื่อ ไม่ว่าจะระหว่าง auto-connect ถูกขัดจังหวะ หรือผู้ใช้กด BACK เองระหว่าง
/// ใช้งานปกติ (พบจริง 2026-09-14: เดิมไม่มีทางกลับไป auto-connect ได้อีกเลยหลังจากกด BACK ออกจากเมนู
/// ครั้งแรก เพราะ auto-connect เดิมรันแค่ครั้งเดียวตอนบูตเท่านั้น - เพิ่ม "Auto" เป็นตัวเลือกในเมนูแก้)
void runWifiMenu() {
  int menuIndex = 0; // เริ่มที่ "Auto" เสมอ
  showWifiMenuScreen(menuIndex);
  Serial.println("เข้าเมนูเลือก WiFi: Auto (try all)");

  // รอให้ปล่อยปุ่ม BACK ก่อน กัน edge การกดที่เพิ่งใช้เข้าเมนูนี้ (หรือยกเลิกการเชื่อมก่อนหน้า) ถูก
  // ตีความเป็นการกดออกจากเมนูซ้ำทันที
  while (btnBack.isPressed()) {
    delay(20);
  }
  btnBack.lastPressed = false;

  while (true) {
    if (btnNext.consumeJustPressed()) {
      menuIndex = (menuIndex + 1) % WIFI_MENU_ITEM_COUNT;
      showWifiMenuScreen(menuIndex);
    }
    if (btnPrev.consumeJustPressed()) {
      menuIndex = (menuIndex - 1 + WIFI_MENU_ITEM_COUNT) % WIFI_MENU_ITEM_COUNT;
      showWifiMenuScreen(menuIndex);
    }
    if (btnBack.consumeJustPressed()) {
      // ออกจากเมนูทั้งที่ยังไม่ได้เชื่อม (ไม่ว่าจะไม่เคยเชื่อมเลย หรือเพิ่งยกเลิกการสลับเครือข่าย
      // กลางคัน) - ปิด auto-reconnect ไว้ก่อน ผู้ใช้ตั้งใจออกมาเอง ไม่ควรถูกลากกลับเข้าหน้า Connecting
      // ทันทีโดยอัตโนมัติ (ดู comment ของ autoReconnectEnabled - นี่คือจุดที่เคยพลาดตอนแก้รอบแรก)
      if (WiFi.status() != WL_CONNECTED) {
        autoReconnectEnabled = false;
      }
      Serial.println("ออกจากเมนูเลือก WiFi โดยไม่เปลี่ยนอะไร");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      if (menuIndex == WIFI_MENU_OTHERS_INDEX) {
        // ไม่เกี่ยวกับ WiFi เลย - เข้าไปเล่น/ทำงานใน Others แล้ววนกลับมาเมนูนี้ต่อเฉยๆ ไม่แตะ WiFi อะไร
        runOthersMenu();
        showWifiMenuScreen(menuIndex);
        continue;
      }

      bool success;
      if (menuIndex == 0) {
        Serial.println("เลือก Auto - ลองทุกเครือข่ายตามลำดับ");
        success = connectToWifi(); // ใช้ fallback loop เดิม, interruptible ด้วย BACK เหมือนกัน
      } else {
        const WifiCredential &cred = WIFI_NETWORKS[menuIndex - 1];
        Serial.print("เลือก WiFi เอง: ");
        Serial.println(cred.ssid);
        showDisplayMessage("Connecting to:", cred.ssid);
        success = tryConnectOnce(cred, WIFI_CONNECT_TIMEOUT_MS);
      }

      if (success) {
        return; // สำเร็จ - กลับไปหน้าที่เรียกมา (updateDisplay() ของผู้เรียกจะโชว์ WiFi ใหม่เอง)
      }

      // ไม่สำเร็จ (timeout หรือกด BACK ยกเลิกระหว่างเชื่อม) - กลับมาที่เมนูนี้ให้เลือกใหม่/ลองใหม่ได้
      showWifiMenuScreen(menuIndex);
    }
    delay(50);
  }
}

void loop() {
  // เข้าเมนูเลือก WiFi ได้ทุกเมื่อด้วยปุ่ม BACK ไม่ต้องรอ WiFi หลุดก่อน
  if (btnBack.consumeJustPressed()) {
    runWifiMenu();
    updateDisplay();
    return; // เริ่ม loop รอบถัดไปสะอาดๆ แทนที่จะทำ logic ที่เหลือของรอบนี้ต่อ
  }

  // auto-reconnect: ถ้า WiFi หลุดระหว่างทำงาน (router รีสตาร์ท/สัญญาณหลุดชั่วคราว) ให้พยายามต่อใหม่
  // ทันที - interruptible ด้วยปุ่ม BACK เหมือนตอนบูต (ผ่าน connectToWifi()/tryConnectOnce() เดิม)
  //
  // เช็ค autoReconnectEnabled ด้วยโดยตั้งใจ - ปิดไว้ทุกครั้งที่ผู้ใช้กด BACK ออกจากเมนูทั้งที่ยังไม่ได้
  // เชื่อม (ดู runWifiMenu) จะได้ไม่ลาก auto-reconnect กลับเข้าหน้า Connecting ทันทีทั้งที่ผู้ใช้เพิ่ง
  // ตั้งใจออกมาเอง ปล่อยให้อยู่หน้า LOCK/UNLOCK นิ่งๆ จนกว่าจะกด BACK เองอีกครั้ง (ดู comment ของ
  // autoReconnectEnabled - เจอบั๊กนี้ 2 รอบกว่าจะครบทุกเคส)
  if (autoReconnectEnabled && WiFi.status() != WL_CONNECTED) {
    Serial.println("WiFi หลุด - พยายามเชื่อมใหม่...");
    bool connected = connectToWifi();
    if (!connected) {
      runWifiMenu();
    }
    updateDisplay();
  }

  if (ENABLE_DEBUG_SERIAL) {
    btnPrev.debugPrintIfChanged("BTN_PREV");
    btnNext.debugPrintIfChanged("BTN_NEXT");
    btnBack.debugPrintIfChanged("BTN_BACK");
    btnOk.debugPrintIfChanged("SW_OK");
  }

  if (btnNext.consumeJustPressed()) {
    selectedIndex = (selectedIndex + 1) % OPTION_COUNT;
    printSelection();
  }
  if (btnPrev.consumeJustPressed()) {
    selectedIndex = (selectedIndex - 1 + OPTION_COUNT) % OPTION_COUNT;
    printSelection();
  }

  if (btnOk.consumeJustPressed()) {
    bool locked = (strcmp(OPTIONS[selectedIndex], "LOCK") == 0);
    sendTrigger(locked);
  }

  delay(50);
}

void printSelection() {
  Serial.print("เลือกอยู่ตอนนี้: ");
  Serial.print(OPTIONS[selectedIndex]);
  Serial.println(" (กด SW_OK เพื่อยืนยัน)");
  updateDisplay();
}

/// วาดหน้าจอหลัก (WiFi status + ตัวเลือกที่ไฮไลต์อยู่) - เรียกทุกครั้งที่ selection เปลี่ยนหรือหลังส่ง
/// trigger เสร็จแล้ว ไม่มีผลอะไรถ้า displayReady = false (เปิดจอไม่สำเร็จตอน setup)
void updateDisplay() {
  if (!displayReady) return;

  display.clearDisplay();

  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("WiFi: ");
  display.println(WiFi.status() == WL_CONNECTED ? WiFi.SSID() : "disconnected");

  display.setTextSize(2);
  display.setCursor(16, 26);
  display.println(OPTIONS[selectedIndex]);

  display.setTextSize(1);
  display.setCursor(0, 56);
  display.println("<> select  OK confirm");

  display.display();
}

/// โชว์ข้อความ 2 บรรทัดเต็มจอ (เช่น "Connecting WiFi...", ผลการส่ง trigger) - แทนที่หน้าจอหลัก
/// ชั่วคราว ไม่มีผลอะไรถ้า displayReady = false
void showDisplayMessage(const char* line1, const char* line2) {
  if (!displayReady) return;

  display.clearDisplay();
  display.setTextSize(1);
  display.setCursor(0, 24);
  display.println(line1);
  display.setCursor(0, 36);
  display.println(line2);
  display.display();
}

// ทดลองแก้บั๊กที่ผู้ใช้รายงาน 2026-09-17: เล่นของเล่นที่ redraw ถี่ๆ (Mini Dino/Flappy Block ~25fps)
// นานๆทีแล้วจอเพี้ยน (ตัวหนังสือ/ภาพเหมือนโดน mirror แต่ตำแหน่งยังถูก) ต้องถอดปลั๊ก/รีเซ็ตบอร์ดถึงหาย -
// กลับเมนูเฉยๆ ไม่หาย สงสัยว่า I2C transaction หลุด sync กับ SSD1306 กลางทาง (ตัว pointer
// column/page ภายในชิปเพี้ยน) เพราะ display.begin() เดิมรันแค่ครั้งเดียวตอน setup() เท่านั้นทั้ง
// โปรแกรม ไม่มีจุดไหน re-init จอเลยแม้จอจะ desync ไปแล้ว - ฟังก์ชันนี้เรียก display.begin() ซ้ำ (ไม่ใช่
// แค่ clearDisplay ธรรมดาซึ่งไม่ช่วยถ้าปัญหาอยู่ที่ pointer ภายในชิป ไม่ใช่ข้อมูลที่ส่งไป) เรียกจากจุด
// checkpoint ที่ปลอดภัย (กลับมาเมนู Others หลังออกจากของเล่นแต่ละตัว - ดู runOthersMenu ใน others_menu.h)
// **ยังไม่ยืนยัน root cause แน่ชัด 100%** เป็นการทดลองแก้ตามสมมติฐานที่มีหลักฐานสนับสนุน ต้องรอผลทดสอบ
// จริงบนบอร์ดว่าอาการหายไปจริงไหม
void recoverDisplayIfNeeded() {
  if (!ENABLE_DISPLAY) return;
  displayReady = display.begin(SSD1306_SWITCHCAPVCC, OLED_I2C_ADDRESS);
  if (displayReady) {
    display.setTextColor(SSD1306_WHITE);
  }
}

void incrementLockStats(bool locked) {
  Preferences prefs;
  prefs.begin("stats", false);
  const char* key = locked ? "lockCount" : "unlockCount";
  unsigned int count = prefs.getUInt(key, 0);
  count++;
  prefs.putUInt(key, count);
  prefs.end();
}

void sendTrigger(bool locked) {
  Serial.print("ยืนยันแล้ว - กำลังส่ง ");
  Serial.print(locked ? "LOCK" : "UNLOCK");
  Serial.print(" -> ");
  showDisplayMessage(locked ? "Sending LOCK..." : "Sending UNLOCK...", "");

  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("WiFi หลุด - ยิงไม่ได้ ลองกด SW_OK ใหม่อีกครั้งหลัง WiFi กลับมา");
    showDisplayMessage("WiFi disconnected", "Cannot send");
    delay(1500);
    updateDisplay();
    return;
  }

  Serial.print("Free heap ก่อนส่ง: ");
  Serial.println(ESP.getFreeHeap());

  // built-in DNS resolver ของ ESP32 resolve โดเมนนี้ไม่ได้เลย (พิสูจน์แล้วหลายรอบ) - resolve เองผ่าน
  // DoH (ดู resolveViaDoh) แล้วต่อ TCP/TLS ด้วย IP ที่ได้ตรงๆ ข้าม HTTPClient ไปเลย (ถ้าใช้ HTTPClient
  // ต่อด้วยชื่อโดเมน มันจะไปเรียก resolver เดิมที่พังอยู่ดี) เขียน HTTP request เองแทน - Host header
  // ยังใส่ชื่อโดเมนจริงตรงๆ (เป็นแค่ text ธรรมดา ไม่เกี่ยวกับ DNS เลย)
  String triggerHost = extractHost(String(TRIGGER_URL_BASE));
  IPAddress resolvedIp = resolveViaDoh(triggerHost);

  if (resolvedIp == IPAddress(0, 0, 0, 0)) {
    Serial.println("resolve ผ่าน DoH ไม่สำเร็จ - ยิงไม่ได้รอบนี้");
    showDisplayMessage("DNS resolve FAILED", "");
    delay(1500);
    updateDisplay();
    return;
  }

  WiFiClientSecure client;
  // setInsecure() = เชื่อมต่อแบบเข้ารหัส TLS ปกติ แต่ไม่ตรวจสอบ certificate ของปลายทาง (ESP32 ไม่มี
  // root CA store ให้ตรวจ .ts.net cert แบบเต็มรูปแบบโดยไม่ต้อง bundle root CA เอง) ยอมรับ tradeoff
  // นี้เพราะ REMOTE_SECRET เป็นตัวป้องกันหลักอยู่แล้ว จุดนี้แค่กันไม่ให้ใครดัก traffic อ่านง่ายๆ
  client.setInsecure();

  // หมายเหตุ: ต่อด้วย IP ตรงๆ แบบนี้ยังไม่ยืนยันว่า SNI จะถูกส่งไปถูกต้องหรือเปล่า (ปกติ SNI มาจาก
  // hostname string ที่ใช้ connect ตอนนี้ต่อด้วย IP เลยไม่มี hostname ให้ตั้ง SNI) ถ้า Funnel ของ
  // Tailscale ต้องพึ่ง SNI ในการ route จริงๆ ตามที่สงสัยไว้ก่อนหน้า จุดนี้อาจจะยังไม่ผ่าน - ต้องดูผล
  // จริงถึงจะรู้
  if (!client.connect(resolvedIp, 443)) {
    Serial.println("ต่อ TCP/TLS ไป IP ที่ resolve ได้ไม่ติด");
    printTlsErrorIfAny(client);
    showDisplayMessage("Send FAILED", "connect failed");
    delay(1500);
    updateDisplay();
    return;
  }

  String path = "/trigger?secret=" + String(REMOTE_SECRET) + "&locked=" + (locked ? "true" : "false");
  client.print("GET " + path + " HTTP/1.1\r\n"
               + "Host: " + triggerHost + "\r\n"
               + "Connection: close\r\n\r\n");

  // default timeout ของ Stream (readStringUntil ใช้ค่านี้) คือแค่ 1000ms - พบจริงว่าบาง request คำสั่ง
  // ไปถึงเซิร์ฟเวอร์สำเร็จแต่ผลตอบกลับมาไม่ทัน 1 วิ ทำให้ readStringUntil คืนค่าว่างและจอโชว์
  // "no response" หลอกๆ ทั้งที่ล็อก/ปลดล็อกสำเร็จจริง (เห็นใน Serial log ว่า HTTP 200 กลับมาช้ากว่านั้น
  // นิดเดียว) ขยาย timeout ให้ยาวขึ้นกันพลาด
  client.setTimeout(5000);
  unsigned long waitStart = millis();
  while (!client.available() && client.connected() && millis() - waitStart < 5000) {
    delay(10);
  }

  String statusLine = client.readStringUntil('\n');
  String rest = client.readString(); // อ่าน header/body ที่เหลือรวมกันเป็นก้อนเดียว - แค่เอาไว้ดู log ไม่ต้อง parse ละเอียด

  Serial.print("Response: ");
  Serial.println(statusLine);
  Serial.println(rest);

  if (statusLine.length() > 0) {
    showDisplayMessage(locked ? "LOCK sent" : "UNLOCK sent", statusLine.c_str());
    incrementLockStats(locked); // ได้ response กลับมาจริง = ถือว่าคำสั่งไปถึงแล้ว นับเข้าสถิติ (ดู runDeviceStats)
  } else {
    showDisplayMessage("Send FAILED", "no response");
  }

  client.stop();
  delay(1500); // โชว์ผลลัพธ์ค้างไว้ให้อ่านทัน ก่อนกลับไปหน้าจอเลือกตามปกติ
  updateDisplay();
}
