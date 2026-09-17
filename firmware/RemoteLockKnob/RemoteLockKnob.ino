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

/// รายละเอียด GIF หนึ่งตัวในคลัง - ชี้ไปที่ array ของเฟรม (จาก header ที่แปลงด้วย gen_gif.py) ไม่ได้เก็บ
/// เฟรมเองตรงๆ เพื่อให้เพิ่ม GIF ใหม่ได้ง่ายแค่แปลงไฟล์ใหม่แล้วเพิ่ม element ใน GIF_LIBRARY (ดูด้านล่าง
/// ไฟล์) - ต้องประกาศ struct นี้ไว้บนสุดของไฟล์ ไม่ใช่ตรงจุดที่ใช้จริง เพราะ Arduino auto-generate
/// function prototype ไว้ก่อนโค้ดทั้งหมดตอน compile - ถ้า struct ยังไม่ถูกประกาศตอนนั้น prototype ของ
/// ฟังก์ชันที่รับ struct นี้เป็นพารามิเตอร์ (เช่น runGifLoop) จะ compile ไม่ผ่าน ("does not name a type")
struct GifEntry {
  const char* name;
  const uint8_t* const* frames;
  int frameCount;
  unsigned long frameDelayMs;
};

/// รายละเอียดภาพ Pic mode หนึ่งรูปในคลัง - แต่ละรูปมีความละเอียดเป็นของตัวเองได้ตั้งแต่ Pic Mode Editor
/// เพิ่ม resolution selector (แต่ก่อนล็อกไว้ที่ 64x32/cellSize 2 ตายตัวเพราะกลัวขนาด flash - ตอนนี้ไม่ใช่
/// ปัญหาแล้ว) cellSize คือกี่พิกเซลจอจริงต่อ 1 ช่องที่วาด (cols*cellSize และ rows*cellSize ต้องเท่ากับ
/// 128x64 เสมอ) ต้องประกาศ struct นี้ไว้บนสุดของไฟล์เหมือน GifEntry เหตุผลเดียวกัน (ดู comment ด้านบน)
struct PicEntry {
  const char* name;
  const uint8_t* data;
  int cols;
  int rows;
  int cellSize;
};

#include "secrets.h" // WIFI_NETWORKS[] / WIFI_NETWORK_COUNT / TRIGGER_URL_BASE / REMOTE_SECRET - ดู comment หัวไฟล์
#include "pics.h" // PIC_PIC1[] .. PIC_PICn[] - ภาพจาก Pic Mode Editor artifact แปลงมาอัตโนมัติ (ห้ามแก้มือ) ประกอบเป็น PIC_LIBRARY[] ด้านล่างนี้เลย

// คลังภาพ Pic mode ทั้งหมดที่แปลงไว้แล้ว - เพิ่มภาพใหม่ได้แค่ (1) วาดใน Pic Mode Editor artifact
// (2) บอก Claude ให้แปลง (3) Claude เพิ่ม element ในนี้บรรทัดเดียว ไม่ต้องแก้อะไรที่อื่นเลย (เหมือน
// GIF_LIBRARY ทุกอย่าง) cols/rows/cellSize มาจากตอนวาดจริงใน artifact - ต้องประกาศตรงนี้ (ก่อน
// OTHERS_MENU_ITEM_COUNT ที่ใช้ PIC_LIBRARY_COUNT) ไม่ใช่ตอนหลังไฟล์แบบ GIF_LIBRARY เพราะตัวแปร
// (ไม่ใช่ฟังก์ชัน) ต้องประกาศก่อนใช้จริงเสมอ ไม่มี auto-prototype ช่วยแบบฟังก์ชัน
const PicEntry PIC_LIBRARY[] = {
  {"pic1", PIC_PIC1, 64, 32, 2},
  {"pic2", PIC_PIC2, 64, 32, 2},
  {"pic3", PIC_PIC3, 64, 32, 2},
  {"pic4", PIC_PIC4, 64, 32, 2},
  {"pic5", PIC_PIC5, 64, 32, 2},
};
const int PIC_LIBRARY_COUNT = sizeof(PIC_LIBRARY) / sizeof(PIC_LIBRARY[0]);

#include "gif_pixelcat.h" // PIXELCAT_FRAMES[] / PIXELCAT_FRAME_COUNT / PIXELCAT_FRAME_DELAY_MS - แปลงจาก pixel-cat.gif (ห้ามแก้มือ)
#include "gif_telefragger.h"      // แปลงจาก Telefragger.gif
#include "gif_absolute_onion.h"   // แปลงจาก absolute-onion.gif
#include "gif_cat1.h"             // แปลงจาก cat-1.gif
#include "gif_does_this_to_you.h" // แปลงจาก does-this-to-you.gif
#include "gif_dog1.h"             // แปลงจาก dog-1.gif
#include "gif_dogspin.h"          // แปลงจาก dog-spin.gif
#include "gif_groove_battle.h"    // แปลงจาก groove-battle.gif
#include "gif_nootnoot.h"         // แปลงจาก noot-noot-meme.gif
#include "pet_sprites.h"          // หน้าสัตว์เลี้ยง Tamagotchi 3 mood (happy/neutral/sad) แปลงจากภาพวาดที่ผู้ใช้ส่งมา

// คลัง GIF ทั้งหมดที่แปลงไว้แล้ว - เพิ่ม GIF ใหม่ได้แค่ (1) ให้ Claude แปลงไฟล์ .gif เป็น header ใหม่ด้วย
// gen_gif.py (2) #include header นั้นไว้ข้างบน (3) เพิ่มบรรทัดในนี้บรรทัดเดียว ไม่ต้องแก้อะไรที่อื่นเลย -
// ย้ายมาไว้ตรงนี้ (เดิมอยู่ใกล้ runGifLoop ด้านล่างไฟล์) เพราะ GIF_LIBRARY_COUNT ถูกใช้ใน
// drawStatsPage() ซึ่งอยู่ค่อนไปทางต้นไฟล์ - ตัวแปร (ไม่ใช่ฟังก์ชัน) ต้องประกาศก่อนใช้จริงเสมอ เหตุผล
// เดียวกับ PIC_LIBRARY ด้านบน
const GifEntry GIF_LIBRARY[] = {
  {"Pixel Cat", PIXELCAT_FRAMES, PIXELCAT_FRAME_COUNT, PIXELCAT_FRAME_DELAY_MS},
  {"Telefragger", TELEFRAGGER_FRAMES, TELEFRAGGER_FRAME_COUNT, TELEFRAGGER_FRAME_DELAY_MS},
  {"Absolute Onion", ABSOLUTE_ONION_FRAMES, ABSOLUTE_ONION_FRAME_COUNT, ABSOLUTE_ONION_FRAME_DELAY_MS},
  {"Cat 1", CAT1_FRAMES, CAT1_FRAME_COUNT, CAT1_FRAME_DELAY_MS},
  {"Does This To You", DOESTHISTOYOU_FRAMES, DOESTHISTOYOU_FRAME_COUNT, DOESTHISTOYOU_FRAME_DELAY_MS},
  {"Dog 1", DOG1_FRAMES, DOG1_FRAME_COUNT, DOG1_FRAME_DELAY_MS},
  {"Dog Spin", DOGSPIN_FRAMES, DOGSPIN_FRAME_COUNT, DOGSPIN_FRAME_DELAY_MS},
  {"Groove Battle", GROOVEBATTLE_FRAMES, GROOVEBATTLE_FRAME_COUNT, GROOVEBATTLE_FRAME_DELAY_MS},
  {"Noot Noot Meme", NOOTNOOT_FRAMES, NOOTNOOT_FRAME_COUNT, NOOTNOOT_FRAME_DELAY_MS},
};
const int GIF_LIBRARY_COUNT = sizeof(GIF_LIBRARY) / sizeof(GIF_LIBRARY[0]);

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

// ค่าคงที่ของเกม Mini Dino (ดู runMiniDinoGame) - ปรับความยากง่าย/ขนาดได้ตรงนี้ที่เดียว
const int DINO_X = 10;          // ตำแหน่ง X คงที่ของไดโน (ขยับแค่แนวตั้งตอนกระโดด)
const int DINO_SIZE = 8;        // สี่เหลี่ยมจัตุรัส 8x8 พิกเซล
const int DINO_GROUND_Y = 56;   // เส้นพื้น (baseline) - เผื่อที่ด้านบนไว้โชว์คะแนน/high score
const int OBSTACLE_WIDTH = 6;
const int OBSTACLE_HEIGHT = 12;
// ความเร็ว obstacle ไม่คงที่แล้ว - เริ่มที่ BASE แล้วเพิ่มทีละ 1 ทุกๆ SCORE_DIVISOR แต้ม จนถึง MAX (กัน
// เร็วจนเกมเล่นไม่ได้จริง/ชนตรวจจับพลาด) ดู runMiniDinoGame ตรงคำนวณ currentSpeed แต่ละ tick
const int OBSTACLE_SPEED_BASE = 3;
const int OBSTACLE_SPEED_MAX = 8;
const int OBSTACLE_SPEED_SCORE_DIVISOR = 100; // score ~10/วินาที เลยแปลว่าเร็วขึ้นทุกๆ ~10 วิที่รอด
const unsigned long DINO_TICK_MS = 40;    // ~25 fps
const float DINO_JUMP_VELOCITY = 4.5;     // ความเร็วเริ่มกระโดด (บวก = ขึ้น)
const float DINO_GRAVITY = 0.35;          // แรงดึงกลับลงต่อ tick

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

// fisch_game.h ต้อง include ตรงนี้ (หลัง btnPrev/btnNext/btnOk/btnBack/playBeep/playBeepPattern ถูก
// ประกาศแล้วด้านบน) เพราะเนื้อหาข้างในเป็นฟังก์ชันจริง ไม่ใช่แค่ data array แบบ gif_*.h/pics.h - ใช้ตัวแปร
// global พวกนี้โดยตรง (ตัวแปรต้องประกาศก่อนใช้จริงเสมอ ไม่มี auto-prototype ช่วยเหมือนฟังก์ชัน) ดูคอมเมนต์
// หัวไฟล์ fisch_game.h สำหรับรายละเอียดเรื่อง forward declaration ของฟังก์ชันภายในไฟล์นั้นเอง
#include "fisch_game.h"

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

// ตัวเลือกคงที่ในเมนู Others - เพิ่มของเล่นตัวใหม่ในอนาคตได้แค่เพิ่ม element ในนี้ + เพิ่ม branch ใน
// runOthersMenu() ส่วนรูปจาก Pic mode (PIC_LIBRARY จาก pics.h) จะถูกต่อท้ายรายการนี้อัตโนมัติ - ไม่ต้อง
// มาแก้ตรงนี้ทุกครั้งที่วาดรูปใหม่ (ดู OTHERS_MENU_ITEM_COUNT/showOthersMenuScreen)
// รวมของ "จัดการ/ตั้งค่า" (clear score, toggle ต่างๆ) ไว้ในเมนูย่อย "Settings" ต่างหาก ไม่ปนอยู่กับ
// ลิสต์ของเล่น/เกมตรงๆ แบบเดิม - เมนูหลักนี้เหลือแค่ของเล่น/เกมจริงๆ + "Settings" ปิดท้าย
const char* OTHERS_STATIC_ITEMS[] = {
  "Mini Dino", "Draw mode", "Pong", "Tic-Tac-Toe", "Magic 8-Ball", "Dice Roll", "Rock Paper Scissors",
  "DVD Bounce", "Game of Life", "GIF Player", "Lights Out", "Slot Machine", "Flappy Block", "Wire Connect",
  "Tamagotchi", "Fisch", "Settings"
};
const int OTHERS_STATIC_ITEM_COUNT = 17;
const int OTHERS_MENU_ITEM_COUNT = OTHERS_STATIC_ITEM_COUNT + PIC_LIBRARY_COUNT;

void showOthersMenuScreen(int menuIndex) {
  if (menuIndex < OTHERS_STATIC_ITEM_COUNT) {
    showDisplayMessage("Others:", OTHERS_STATIC_ITEMS[menuIndex]);
  } else {
    showDisplayMessage("Others:", PIC_LIBRARY[menuIndex - OTHERS_STATIC_ITEM_COUNT].name);
  }
}

/// เมนูย่อยของของเล่นที่ไม่เกี่ยวกับ WiFi/lock เลย เรียกจากภายใน runWifiMenu() (ดู
/// WIFI_MENU_OTHERS_INDEX) - ใช้ปุ่มชุดเดียวกันทั้งไฟล์: < / > เลื่อน, SW_OK เข้า, BACK ออกกลับไปเมนู
/// เลือก WiFi ตามปกติ
void runOthersMenu() {
  int menuIndex = 0;
  showOthersMenuScreen(menuIndex);
  Serial.println("เข้าเมนู Others");

  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnNext.consumeJustPressed()) {
      menuIndex = (menuIndex + 1) % OTHERS_MENU_ITEM_COUNT;
      showOthersMenuScreen(menuIndex);
    }
    if (btnPrev.consumeJustPressed()) {
      menuIndex = (menuIndex - 1 + OTHERS_MENU_ITEM_COUNT) % OTHERS_MENU_ITEM_COUNT;
      showOthersMenuScreen(menuIndex);
    }
    if (btnBack.consumeJustPressed()) {
      Serial.println("ออกจากเมนู Others");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      if (menuIndex == 0) {
        runMiniDinoGame();
      } else if (menuIndex == 1) {
        runDrawMode();
      } else if (menuIndex == 2) {
        runPong();
      } else if (menuIndex == 3) {
        runTicTacToe();
      } else if (menuIndex == 4) {
        runMagic8Ball();
      } else if (menuIndex == 5) {
        runDiceRoller();
      } else if (menuIndex == 6) {
        runRockPaperScissors();
      } else if (menuIndex == 7) {
        runDvdBounce();
      } else if (menuIndex == 8) {
        runGameOfLife();
      } else if (menuIndex == 9) {
        runGifPlayerMenu();
      } else if (menuIndex == 10) {
        runLightsOut();
      } else if (menuIndex == 11) {
        runSlotMachine();
      } else if (menuIndex == 12) {
        runFlappyBlock();
      } else if (menuIndex == 13) {
        runWireConnect();
      } else if (menuIndex == 14) {
        runTamagotchi();
      } else if (menuIndex == 15) {
        runFischGame();
      } else if (menuIndex == 16) {
        runOthersSettingsMenu();
      } else {
        runViewPic(PIC_LIBRARY[menuIndex - OTHERS_STATIC_ITEM_COUNT]);
      }
      showOthersMenuScreen(menuIndex); // วาดเมนูนี้กลับมาหลังออกจากของเล่นแต่ละตัว
    }
    delay(50);
  }
}

// เมนูย่อย "Settings" ภายใต้ Others - รวม toggle/clear score/หน้าข้อมูลทั้งหมดไว้ที่เดียว แยกจากลิสต์
// เกม/ของเล่นจริงๆ (ผู้ใช้ขอให้จัดกลุ่มใหม่ เพราะเดิมปนกันอยู่ในเมนูเดียว) 2 อันแรกเป็น toggle (label
// เปลี่ยนตามสถานะปัจจุบัน แบบ dynamic) ตรงกลางเป็น action เรียกฟังก์ชัน clear ที่มีอยู่แล้วตรงๆ อันสุดท้าย
// เป็นหน้าดูสถิติเครื่องอย่างเดียว (ไม่มีอะไรให้กด แค่โชว์)
const char* OTHERS_SETTINGS_STATIC_ITEMS[] = {
  "Sound", "Slot Animation", "Clear Dino old score", "Clear draw mode", "Clear Pong old score",
  "Clear Flappy old score", "Reset Slot credits", "Clear Device Stats", "Reset Tamagotchi", "Device Stats"
};
const int OTHERS_SETTINGS_ITEM_COUNT = 10;

void showOthersSettingsScreen(int idx) {
  if (idx == 0) {
    showDisplayMessage("Settings:", soundEnabled ? "Sound: On" : "Sound: Off");
  } else if (idx == 1) {
    showDisplayMessage("Settings:", slotAnimationEnabled ? "Slot Anim: On" : "Slot Anim: Off");
  } else {
    showDisplayMessage("Settings:", OTHERS_SETTINGS_STATIC_ITEMS[idx]);
  }
}

/// เมนูย่อย Settings - < / > เลื่อน, OK เข้า/สลับ, BACK ออกกลับเมนู Others - แพทเทิร์นเดียวกับเมนูอื่น
/// ทั้งไฟล์
void runOthersSettingsMenu() {
  int idx = 0;
  showOthersSettingsScreen(idx);
  Serial.println("เข้าเมนู Others > Settings");

  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnNext.consumeJustPressed()) {
      idx = (idx + 1) % OTHERS_SETTINGS_ITEM_COUNT;
      showOthersSettingsScreen(idx);
    }
    if (btnPrev.consumeJustPressed()) {
      idx = (idx - 1 + OTHERS_SETTINGS_ITEM_COUNT) % OTHERS_SETTINGS_ITEM_COUNT;
      showOthersSettingsScreen(idx);
    }
    if (btnBack.consumeJustPressed()) {
      Serial.println("ออกจากเมนู Settings");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      if (idx == 0) {
        soundEnabled = !soundEnabled;
        Preferences prefs;
        prefs.begin("settings", false);
        prefs.putBool("soundOn", soundEnabled);
        prefs.end();
        Serial.print("[Sound] ");
        Serial.println(soundEnabled ? "เปิดเสียงแล้ว" : "ปิดเสียงแล้ว");
        if (soundEnabled) playBeep(80); // ยืนยันด้วยเสียงสั้นๆ เฉพาะตอนเพิ่งเปิด
      } else if (idx == 1) {
        slotAnimationEnabled = !slotAnimationEnabled;
        Preferences prefs;
        prefs.begin("settings", false);
        prefs.putBool("slotAnim", slotAnimationEnabled);
        prefs.end();
        Serial.print("[Slots] Animation ");
        Serial.println(slotAnimationEnabled ? "เปิดแล้ว" : "ปิดแล้ว");
      } else if (idx == 2) {
        runClearOldScore();
      } else if (idx == 3) {
        runClearDrawMode();
      } else if (idx == 4) {
        runClearPongScore();
      } else if (idx == 5) {
        runClearFlappyScore();
      } else if (idx == 6) {
        runClearSlotCredits();
      } else if (idx == 7) {
        runClearDeviceStats();
      } else if (idx == 8) {
        runResetTamagotchi();
      } else if (idx == 9) {
        runDeviceStats();
      }
      showOthersSettingsScreen(idx);
    }
    delay(50);
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

/// เติม 0 นำหน้าให้ครบ 2 หลัก - ใช้ฟอร์แมตเวลาในหน้า Device Stats (HH:MM:SS)
String padTwoDigits(unsigned int v) {
  String s = String(v);
  if (s.length() < 2) s = "0" + s;
  return s;
}

/// หน้าสถิติเครื่อง - จำนวนครั้งที่ส่ง LOCK/UNLOCK สำเร็จสะสมทั้งหมด (นับใน sendTrigger() ตอนได้
/// response กลับมาจริง - ดู incrementLockStats) เก็บถาวรผ่าน NVS ข้ามการรีสตาร์ทได้ ส่วน uptime นับจาก
/// millis() ตั้งแต่บูตครั้งนี้เท่านั้น (ไม่ persist ข้ามรีสตาร์ท - ความหมายของ "uptime" คือของรอบนี้จริงๆ)
/// รีเฟรชแค่บรรทัด uptime ทุก 1 วิ ไม่ต้อง redraw ทั้งจอ ลดภาระ I2C BACK ออกได้ทุกเมื่อ (ไม่มีอะไรให้กด
/// นอกจากนั้นเลย เป็นหน้าดูอย่างเดียว)
const int STATS_PAGE_COUNT = 6;

/// วาดหน้าสถิติตัวที่ page (0-5) - แยกฟังก์ชันจากตัวเลื่อนหน้าเพื่อให้ redraw ได้ทั้งตอนเปลี่ยนหน้าและ
/// ตอนรีเฟรช uptime/heap ทุก 1 วิ เหมือนกัน
void drawStatsPage(int page) {
  if (!displayReady) return;

  display.clearDisplay();
  display.setTextSize(1);
  display.setCursor(0, 0);

  if (page == 0) {
    display.println("Stats: Usage");
    Preferences p;
    p.begin("stats", true);
    unsigned int lockCount = p.getUInt("lockCount", 0);
    unsigned int unlockCount = p.getUInt("unlockCount", 0);
    p.end();
    display.setCursor(0, 16);
    display.print("Locks sent: ");
    display.println(lockCount);
    display.setCursor(0, 28);
    display.print("Unlocks sent: ");
    display.println(unlockCount);
  } else if (page == 1) {
    display.println("Stats: Session");
    unsigned long uptimeSec = millis() / 1000;
    unsigned int hh = uptimeSec / 3600;
    unsigned int mm = (uptimeSec % 3600) / 60;
    unsigned int ss = uptimeSec % 60;
    String uptimeStr = padTwoDigits(hh) + ":" + padTwoDigits(mm) + ":" + padTwoDigits(ss);
    display.setCursor(0, 16);
    display.print("Uptime: ");
    display.println(uptimeStr);
    display.setCursor(0, 28);
    display.print("Buttons: ");
    display.println(totalButtonPresses);
  } else if (page == 2) {
    display.println("Stats: High Scores");
    Preferences p;
    p.begin("dino", true);
    unsigned int dinoHi = p.getUInt("highscore", 0);
    p.end();
    p.begin("pong", true);
    unsigned int pongHi = p.getUInt("highscore", 0);
    p.end();
    p.begin("flappy", true);
    unsigned int flapHi = p.getUInt("highscore", 0);
    p.end();
    display.setCursor(0, 16);
    display.print("Dino: ");
    display.println(dinoHi);
    display.setCursor(0, 28);
    display.print("Pong: ");
    display.println(pongHi);
    display.setCursor(0, 40);
    display.print("Flappy: ");
    display.println(flapHi);
  } else if (page == 3) {
    display.println("Stats: System");
    display.setCursor(0, 16);
    display.print("Heap: ");
    display.print(ESP.getFreeHeap() / 1024);
    display.println(" KB");
    display.setCursor(0, 28);
    display.print("Chip: ");
    display.println(ESP.getChipModel());
    display.setCursor(0, 40);
    display.print("Flash: ");
    display.print(ESP.getFlashChipSize() / (1024 * 1024));
    display.println(" MB");
  } else if (page == 4) {
    display.println("Stats: WiFi");
    if (WiFi.status() == WL_CONNECTED) {
      String ssid = WiFi.SSID();
      if (ssid.length() > 21) ssid = ssid.substring(0, 21); // กันยาวเกิน 128px แล้วขึ้นบรรทัดใหม่ทับกัน
      display.setCursor(0, 16);
      display.println(ssid);
      display.setCursor(0, 28);
      display.print("IP ");
      display.println(WiFi.localIP());
      display.setCursor(0, 40);
      display.print("RSSI ");
      display.print(WiFi.RSSI());
      display.println(" dBm");
    } else {
      display.setCursor(0, 16);
      display.println("Not connected");
    }
  } else if (page == 5) {
    display.println("Stats: Library");
    display.setCursor(0, 16);
    display.print("Pictures: ");
    display.println(PIC_LIBRARY_COUNT);
    display.setCursor(0, 28);
    display.print("GIFs: ");
    display.println(GIF_LIBRARY_COUNT);
  }

  display.setCursor(0, 56);
  display.print("<> page  BACK exit");

  display.display();
}

/// หน้าสถิติเครื่อง - เลื่อนดูได้หลายหน้าด้วย < / > (Usage, Session, High Scores, System, WiFi,
/// Library) ไม่มีอะไรให้กด OK เพราะเป็นหน้าดูข้อมูลอย่างเดียวล้วนๆ รีเฟรชทุก 1 วิ (uptime/heap เปลี่ยน
/// ตลอดเวลา) BACK ออกได้ทุกเมื่อ
void runDeviceStats() {
  Serial.println("[Stats] เข้าหน้าสถิติเครื่อง");

  int page = 0;

  while (btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  drawStatsPage(page);
  unsigned long lastRefreshMs = millis();

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Stats] ออก (กด BACK)");
      return;
    }
    bool moved = false;
    if (btnNext.consumeJustPressed()) {
      page = (page + 1) % STATS_PAGE_COUNT;
      moved = true;
    }
    if (btnPrev.consumeJustPressed()) {
      page = (page - 1 + STATS_PAGE_COUNT) % STATS_PAGE_COUNT;
      moved = true;
    }
    if (moved || millis() - lastRefreshMs >= 1000) {
      drawStatsPage(page);
      lastRefreshMs = millis();
    }
    delay(20);
  }
}

/// เช็คชนแบบ AABB ธรรมดา - ไดโนอยู่ X คงที่ (แค่ขยับแนวตั้งตาม jumpOffsetPx), obstacle ขยับแนวนอนอย่างเดียว
bool checkDinoCollision(int jumpOffsetPx, int obstacleXPx) {
  int dinoTop = DINO_GROUND_Y - DINO_SIZE - jumpOffsetPx;
  // บั๊กเดิม: เคยตั้งเป็น DINO_GROUND_Y ตรงๆ (ไม่ลบ jumpOffsetPx) ทำให้ hitbox ยืดยาวจากตัวไดโนลงไป
  // ถึงพื้นเสมอไม่ว่าจะกระโดดสูงแค่ไหน กลายเป็นชนจากแนวนอน (X) เพียงอย่างเดียวเป็นตัวตัดสิน กระโดดพ้น
  // สิ่งกีดขวางจริงบนจอแต่ยังโดนนับว่าชนอยู่ดี - แก้โดยลบ jumpOffsetPx ออกให้ตรงกับ dinoTop จริงๆ
  int dinoBottom = DINO_GROUND_Y - jumpOffsetPx;
  int dinoLeft = DINO_X;
  int dinoRight = DINO_X + DINO_SIZE;

  int obsTop = DINO_GROUND_Y - OBSTACLE_HEIGHT;
  int obsBottom = DINO_GROUND_Y;
  int obsLeft = obstacleXPx;
  int obsRight = obstacleXPx + OBSTACLE_WIDTH;

  bool overlapX = dinoRight > obsLeft && dinoLeft < obsRight;
  bool overlapY = dinoBottom > obsTop && dinoTop < obsBottom;
  return overlapX && overlapY;
}

void drawDinoFrame(int jumpOffsetPx, int obstacleXPx, unsigned int score, unsigned int highScore, bool gameOver) {
  if (!displayReady) return;

  display.clearDisplay();

  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("HI ");
  display.print(highScore);
  display.setCursor(76, 0);
  display.print("SC ");
  display.print(score);

  display.drawFastHLine(0, DINO_GROUND_Y + 1, SCREEN_WIDTH, SSD1306_WHITE);

  int dinoTop = DINO_GROUND_Y - DINO_SIZE - jumpOffsetPx;
  display.fillRect(DINO_X, dinoTop, DINO_SIZE, DINO_SIZE, SSD1306_WHITE);

  int obsTop = DINO_GROUND_Y - OBSTACLE_HEIGHT;
  display.fillRect(obstacleXPx, obsTop, OBSTACLE_WIDTH, OBSTACLE_HEIGHT, SSD1306_WHITE);

  if (gameOver) {
    display.setTextSize(1);
    display.setCursor(22, 24);
    display.println("GAME OVER");
    display.setCursor(4, 36);
    display.println("OK=retry BACK=exit");
  }

  display.display();
}

/// เกมไดโนกระโดดขั้นต่ำ (มีแค่กระโดด ไม่มีย่อ ตามที่ขอ) - กด < กระโดด (ครั้งเดียวตอนอยู่พื้นเท่านั้น
/// กดค้างตอนลอยอยู่ไม่มีผล), BACK ออกได้ทุกเมื่อแม้กำลังเล่นอยู่ (กลับไป Others menu ทันที ไม่ต้องรอ
/// จบเกมก่อน) คะแนนสูงสุด (high score) เก็บถาวรผ่าน Preferences/NVS flash - รอดข้าม reboot/ไฟดับ
/// ต่างจากตัวแปรใน RAM ธรรมดาที่หายทุกครั้งที่ไฟดับ (บอร์ดพวกนี้ไม่มี disk ให้เขียนแบบคอมทั่วไป)
void runMiniDinoGame() {
  Preferences prefs;
  prefs.begin("dino", false);
  unsigned int highScore = prefs.getUInt("highscore", 0);

  // รอปล่อยปุ่มทุกปุ่มที่อาจมีผลในเกมก่อนเริ่ม กัน edge การกดที่เพิ่งใช้เลือกเมนูนี้ถูกตีความเป็น
  // กระโดด/ยืนยันซ้ำทันทีที่เข้าเกม
  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;

  Serial.println("[Dino] เข้าเกม Mini Dino");

  while (true) {
    // --- รอบเล่นใหม่หนึ่งรอบ ---
    float jumpOffset = 0;
    float jumpVelocity = 0;
    bool isJumping = false;
    float obstacleX = SCREEN_WIDTH;
    unsigned int score = 0;
    bool gameOver = false;
    unsigned long lastTickMs = millis();
    unsigned long lastScoreMs = millis();

    while (!gameOver) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Dino] ออกจากเกม (กด BACK)");
        prefs.end();
        return;
      }

      // เรียก consumeJustPressed() ของทั้งสองปุ่มแยกกันก่อน "เสมอ" ไม่ใช้ || แบบ short-circuit ตรงๆ
      // เพราะ short-circuit จะข้ามการอัปเดต state ของปุ่มที่สองไปถ้าปุ่มแรก true อยู่แล้ว ทำให้
      // edge-detection ของปุ่มที่ถูกข้ามเพี้ยนได้ในรอบถัดไป (เดายังว่ากด/ปล่อยพลาดจังหวะ)
      bool jumpFromPrev = btnPrev.consumeJustPressed();
      bool jumpFromOk = btnOk.consumeJustPressed();
      if ((jumpFromPrev || jumpFromOk) && !isJumping) {
        isJumping = true;
        jumpVelocity = DINO_JUMP_VELOCITY;
        playBeep(40); // กระโดด - บี๊บสั้นเดียว
      }

      if (millis() - lastTickMs >= DINO_TICK_MS) {
        lastTickMs = millis();

        if (isJumping) {
          jumpOffset += jumpVelocity;
          jumpVelocity -= DINO_GRAVITY;
          if (jumpOffset <= 0) {
            jumpOffset = 0;
            isJumping = false;
            jumpVelocity = 0;
          }
        }

        int currentSpeed = OBSTACLE_SPEED_BASE + (int)(score / OBSTACLE_SPEED_SCORE_DIVISOR);
        if (currentSpeed > OBSTACLE_SPEED_MAX) currentSpeed = OBSTACLE_SPEED_MAX;
        obstacleX -= currentSpeed;
        if (obstacleX < -OBSTACLE_WIDTH) {
          // บั๊กเดิม: สุ่มระยะห่างเป็น "พิกเซล" คงที่ (20-80) ไม่ว่าความเร็วจะเท่าไหร่ - พอความเร็วเพิ่ม
          // ตามคะแนน ระยะทางเท่าเดิมแต่วิ่งเร็วขึ้น เวลาจริงที่มีให้ตอบสนองต่อ obstacle แต่ละตัวเลยสั้นลง
          // เรื่อยๆ จนเล่นไม่ทันจริงๆ (ผู้ใช้รายงานว่าตายไม่เกิน ~500 คะแนนเสมอ) แก้โดย scale ระยะห่างตาม
          // currentSpeed ด้วย ให้ "เวลา" ระหว่าง obstacle แต่ละตัวใกล้เคียงเดิมไม่ว่าความเร็วเท่าไหร่
          int gapMin = 20 * currentSpeed / OBSTACLE_SPEED_BASE;
          int gapMax = 80 * currentSpeed / OBSTACLE_SPEED_BASE;
          obstacleX = SCREEN_WIDTH + random(gapMin, gapMax);
        }

        if (millis() - lastScoreMs >= 100) {
          lastScoreMs = millis();
          score++;
        }

        if (checkDinoCollision((int)jumpOffset, (int)obstacleX)) {
          gameOver = true;
          Serial.print("[Dino] ชน - คะแนน: ");
          Serial.println(score);
          if (score > highScore) {
            highScore = score;
            prefs.putUInt("highscore", highScore);
            Serial.println("[Dino] สถิติใหม่! เซฟลง NVS แล้ว");
            playBeepPattern(3, 60, 60); // สถิติใหม่ - บี๊บถี่ 3 ครั้งแทนเสียงสูง (ปรับ pitch ไม่ได้แล้ว)
          } else {
            playBeep(250); // ชนธรรมดา - บี๊บยาวครั้งเดียว
          }
        }

        drawDinoFrame((int)jumpOffset, (int)obstacleX, score, highScore, false);
      }

      delay(5);
    }

    drawDinoFrame((int)jumpOffset, (int)obstacleX, score, highScore, true);

    while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
    btnOk.lastPressed = false;
    btnBack.lastPressed = false;

    // รอผู้เล่นตัดสินใจ: OK = เล่นใหม่ (วนกลับไป while(true) ด้านบน), BACK = ออกไป Others menu
    while (true) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Dino] ออกจากเกมหลังจบรอบ (กด BACK)");
        prefs.end();
        return;
      }
      if (btnOk.consumeJustPressed()) {
        Serial.println("[Dino] เล่นใหม่อีกรอบ");
        break;
      }
      delay(20);
    }
  }
}

/// ล้าง high score ที่เซฟไว้ใน NVS - ต้องกด SW_OK ยืนยันอีกครั้งกันกดพลาดเข้ามาจากเมนูแล้วล้างทิ้งเฉยๆ
void runClearOldScore() {
  showDisplayMessage("Clear high score?", "OK=yes  BACK=no");

  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Dino] ยกเลิกการล้างสถิติ");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      Preferences prefs;
      prefs.begin("dino", false);
      prefs.putUInt("highscore", 0);
      prefs.end();
      Serial.println("[Dino] ล้าง high score แล้ว");
      showDisplayMessage("High score cleared", "");
      delay(1000);
      return;
    }
    delay(20);
  }
}

/// ล้าง high score ของ Pong ที่เซฟไว้ใน NVS (namespace "pong" แยกจาก "dino" ของ Mini Dino คนละเกม
/// คนละสถิติกัน) - ต้องกด SW_OK ยืนยันอีกครั้งกันกดพลาด เหมือน runClearOldScore() ของ Dino ทุกอย่าง
void runClearPongScore() {
  showDisplayMessage("Clear Pong score?", "OK=yes  BACK=no");

  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Pong] ยกเลิกการล้างสถิติ");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      Preferences prefs;
      prefs.begin("pong", false);
      prefs.putUInt("highscore", 0);
      prefs.end();
      Serial.println("[Pong] ล้าง high score แล้ว");
      showDisplayMessage("High score cleared", "");
      delay(1000);
      return;
    }
    delay(20);
  }
}

/// ล้าง high score ของ Flappy Block - namespace "flappy" แยกจากเกมอื่น เหมือน Dino/Pong ทุกอย่าง
void runClearFlappyScore() {
  showDisplayMessage("Clear Flappy score?", "OK=yes  BACK=no");

  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Flappy] ยกเลิกการล้างสถิติ");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      Preferences prefs;
      prefs.begin("flappy", false);
      prefs.putUInt("highscore", 0);
      prefs.end();
      Serial.println("[Flappy] ล้าง high score แล้ว");
      showDisplayMessage("High score cleared", "");
      delay(1000);
      return;
    }
    delay(20);
  }
}

/// รีเซ็ตเครดิต Slot Machine กลับเป็น 20 (ค่าเริ่มต้นเดียวกับตอนหมดแล้วเติมอัตโนมัติ - ไม่ใช่ 0 เพราะ
/// 0 เล่นไม่ได้เลย ไม่มีประโยชน์ที่จะ "ล้าง" ไปเป็น 0)
void runClearSlotCredits() {
  showDisplayMessage("Reset Slot credits?", "OK=yes  BACK=no");

  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Slots] ยกเลิกการรีเซ็ตเครดิต");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      Preferences prefs;
      prefs.begin("slots", false);
      prefs.putInt("credits", 20);
      prefs.end();
      Serial.println("[Slots] รีเซ็ตเครดิตเป็น 20 แล้ว");
      showDisplayMessage("Credits reset", "");
      delay(1000);
      return;
    }
    delay(20);
  }
}

/// ล้างสถิติ Device Stats (จำนวน lock/unlock สะสม) - ไม่กระทบ uptime เพราะนั่นไม่ได้เก็บถาวรอยู่แล้ว
/// (นับจาก millis() ตั้งแต่บูตทุกครั้ง)
void runClearDeviceStats() {
  showDisplayMessage("Clear Device Stats?", "OK=yes  BACK=no");

  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Stats] ยกเลิกการล้างสถิติ");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      Preferences prefs;
      prefs.begin("stats", false);
      prefs.putUInt("lockCount", 0);
      prefs.putUInt("unlockCount", 0);
      prefs.end();
      Serial.println("[Stats] ล้างสถิติแล้ว");
      showDisplayMessage("Stats cleared", "");
      delay(1000);
      return;
    }
    delay(20);
  }
}

/// รีเซ็ตสัตว์เลี้ยง Tamagotchi กลับเป็นค่าเริ่มต้น (หิว/สนุก/พลังงาน = 80 ทั้งหมด, Age = 0) - เผื่อ
/// กรณีอยากเริ่มเลี้ยงใหม่ หรือ stat ค้างที่ 0 ไปหมดแล้วอยากรีเซ็ต
void runResetTamagotchi() {
  showDisplayMessage("Reset Tamagotchi?", "OK=yes  BACK=no");

  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Pet] ยกเลิกการรีเซ็ต");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      Preferences prefs;
      prefs.begin("pet", false);
      prefs.putInt("hunger", 80);
      prefs.putInt("fun", 80);
      prefs.putInt("energy", 80);
      prefs.putULong("ageMin", 0);
      prefs.end();
      Serial.println("[Pet] รีเซ็ตสัตว์เลี้ยงแล้ว");
      showDisplayMessage("Pet reset", "");
      delay(1000);
      return;
    }
    delay(20);
  }
}

// Draw mode วาดเป็น "ช่อง" ขนาด 2x2 พิกเซลจริงแทนที่จะเป็น 1 พิกเซลตรงๆ - เหตุผล: เดินทีละพิกเซลจริงบน
// จอ 128x64 กดเยอะเกินไป (สุดจอนึงต้องกด 128 ครั้ง) ลดเหลือ 64x32 ช่องทำให้เดินน้อยลงครึ่งนึงทันที แถม
// เคอร์เซอร์กระพริบก็เห็นชัดขึ้นมากบนจอเล็กขนาดนี้ - Pic mode ที่จะทำต่อก็จะใช้ความละเอียดเดียวกันนี้
// (ตาราง 64x32 บน Artifact ก็กดวาดง่ายกว่า 128x64 เยอะเหมือนกัน)
const int DRAW_CELL_SIZE = 2;
const int DRAW_COLS = SCREEN_WIDTH / DRAW_CELL_SIZE;   // 64
const int DRAW_ROWS = SCREEN_HEIGHT / DRAW_CELL_SIZE;  // 32

// framebuffer ของ Draw mode แยกต่างหากจาก buffer ภายในของ library จอเอง (ไม่ใช้ display.getPixel()
// เพราะไม่แน่ใจว่ามีในทุกเวอร์ชันของ Adafruit_SSD1306 ที่ใช้ - เก็บเป็นบิตของเราเองชัดเจนกว่า) 1 = ขาว
// (เปิด) 0 = ดำ (ปิด) ตามที่ user นิยามไว้ แพ็ค 8 ช่องต่อไบต์ รวม 64*32/8 = 256 ไบต์ (เทียบกับ SRAM ของ
// ESP32 หลักแสนไบต์ ถือว่าเล็กมาก) เป็น global เดียว ไม่ล้างทุกครั้งที่เข้า Draw mode ใหม่ - ภาพที่วาด
// ค้างไว้ได้ข้ามการเข้าออกเมนูภายใน session เดียวกัน (หายเฉพาะตอนไฟดับ/reset จริง เพราะเป็น RAM ธรรมดา
// ไม่เหมือน high score ของ Mini Dino ที่ตั้งใจเก็บถาวรผ่าน NVS) - x/y ที่รับเข้าทุกฟังก์ชันด้านล่างคือ
// พิกัด "ช่อง" (0..DRAW_COLS-1, 0..DRAW_ROWS-1) ไม่ใช่พิกเซลจอจริง
uint8_t drawBuffer[DRAW_COLS * DRAW_ROWS / 8];

bool getDrawPixel(int x, int y) {
  int idx = y * DRAW_COLS + x;
  return (drawBuffer[idx / 8] >> (idx % 8)) & 1;
}

void toggleDrawPixel(int x, int y) {
  int idx = y * DRAW_COLS + x;
  drawBuffer[idx / 8] ^= (1 << (idx % 8));
}

/// วาดทั้งจอจาก drawBuffer จริง (ช่องละ DRAW_CELL_SIZE x DRAW_CELL_SIZE พิกเซลจริง) แล้วโชว์ช่องตรง
/// เคอร์เซอร์เป็น "สีตรงข้าม" ของค่าจริงมันค้างไว้เฉย (ผ่านช่องดำ = ขึ้นขาวค้าง, ผ่านช่องขาว = ขึ้นดำค้าง)
/// - เดิมเคยทำเป็นกระพริบสลับสีทุก 400ms แต่พอเลื่อนเร็วๆ (กดค้างแบบ repeat) จังหวะกระพริบชนกับจังหวะ
/// เลื่อน ทำให้มองตามยากมาก เปลี่ยนเป็นค้างสีตรงข้ามนิ่งๆ แทน เห็นตำแหน่งชัดเจนกว่าไม่ว่าจะเลื่อนเร็วแค่ไหน
/// (จอ SSD1306 มีแค่ขาว/ดำ ไม่มีสีที่ 3 ให้ใช้ทำเคอร์เซอร์แยกแบบจอสี ถึงต้องใช้สีตรงข้ามแทน)
void drawDrawModeFrame(int cursorX, int cursorY) {
  if (!displayReady) return;

  display.clearDisplay();
  for (int y = 0; y < DRAW_ROWS; y++) {
    for (int x = 0; x < DRAW_COLS; x++) {
      if (getDrawPixel(x, y)) {
        display.fillRect(x * DRAW_CELL_SIZE, y * DRAW_CELL_SIZE, DRAW_CELL_SIZE, DRAW_CELL_SIZE, SSD1306_WHITE);
      }
    }
  }

  bool current = getDrawPixel(cursorX, cursorY);
  display.fillRect(cursorX * DRAW_CELL_SIZE, cursorY * DRAW_CELL_SIZE, DRAW_CELL_SIZE, DRAW_CELL_SIZE,
                    current ? SSD1306_BLACK : SSD1306_WHITE);

  display.display();
}

// กดค้าง < หรือ > ใน Draw mode แล้วเลื่อนต่อเนื่องได้ (ไม่ใช่กดครั้งเดียว=ขยับทีละพิกเซลอย่างเดียว) -
// กดครั้งแรกขยับทันที 1 พิกเซล ถ้ายังกดค้างต่อเกิน DRAW_REPEAT_INITIAL_DELAY_MS จะเริ่มขยับซ้ำเองทุกๆ
// DRAW_REPEAT_INTERVAL_MS จนกว่าจะปล่อยปุ่ม (ดู runDrawMode)
const unsigned long DRAW_REPEAT_INITIAL_DELAY_MS = 400;
const unsigned long DRAW_REPEAT_INTERVAL_MS = 90;

/// Draw mode: เลื่อนเคอร์เซอร์ทีละพิกเซลด้วย < / > (กดค้างเลื่อนต่อเนื่องได้ - ดู DRAW_REPEAT_*) ตาม
/// ปกติเป็นซ้าย/ขวา (สุดแถวไหลไปแถวถัดไปเอง) แต่ถ้า **กด SW_OK ค้างไว้** ระหว่างกด < / > จะกลายเป็น
/// ขึ้น/ลง (แถวก่อนหน้า/ถัดไป ที่คอลัมน์เดิม) แทน - แยกจากการ "แตะ" SW_OK เฉยๆ (ไม่ได้กด < หรือ > ระหว่าง
/// ที่ค้างเลย) ซึ่งยังคง toggle พิกเซลตรงเคอร์เซอร์เหมือนเดิมทุกอย่าง (ตัดสินใจตอน "ปล่อย" SW_OK ว่ารอบ
/// นั้นถูกใช้เป็นตัวช่วยเปลี่ยนทิศไปแล้วหรือยัง - ถ้าใช้แล้วจะไม่ toggle ซ้ำตอนปล่อย) BACK ออกกลับเมนู
/// Others ทันที (ภาพที่วาดค้างไว้ใน RAM ไม่หาย จนกว่าจะ reset/ไฟดับจริง หรือสั่งล้างเองผ่าน "Clear draw
/// mode" ในเมนู Others - ดู comment ของ drawBuffer / runClearDrawMode)
void runDrawMode() {
  int cursorX = 0;
  int cursorY = 0;

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  Serial.println("[Draw] เข้า Draw mode");
  drawDrawModeFrame(cursorX, cursorY);

  unsigned long nextHeldSinceMs = 0;
  unsigned long lastNextMoveMs = 0;
  bool nextRepeating = false;

  unsigned long prevHeldSinceMs = 0;
  unsigned long lastPrevMoveMs = 0;
  bool prevRepeating = false;

  bool okWasHeld = false;
  bool okUsedAsModifier = false; // true ถ้ารอบการกดค้าง SW_OK นี้ถูกใช้เปลี่ยนทิศไปแล้ว (กันไม่ให้ toggle
                                  // ซ้ำตอนปล่อยนิ้ว)

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Draw] ออกจาก Draw mode (กด BACK)");
      return;
    }

    bool moved = false;
    bool okHeld = btnOk.isPressed();

    if (btnNext.consumeJustPressed()) {
      if (okHeld) {
        cursorY = (cursorY + 1) % DRAW_ROWS;
        okUsedAsModifier = true;
      } else {
        cursorX++;
        if (cursorX >= DRAW_COLS) {
          cursorX = 0;
          cursorY = (cursorY + 1) % DRAW_ROWS;
        }
      }
      moved = true;
      nextHeldSinceMs = millis();
      lastNextMoveMs = millis();
      nextRepeating = false;
    } else if (btnNext.isPressed()) {
      if (!nextRepeating && millis() - nextHeldSinceMs >= DRAW_REPEAT_INITIAL_DELAY_MS) {
        nextRepeating = true;
        lastNextMoveMs = millis();
      }
      if (nextRepeating && millis() - lastNextMoveMs >= DRAW_REPEAT_INTERVAL_MS) {
        if (okHeld) {
          cursorY = (cursorY + 1) % DRAW_ROWS;
          okUsedAsModifier = true;
        } else {
          cursorX++;
          if (cursorX >= DRAW_COLS) {
            cursorX = 0;
            cursorY = (cursorY + 1) % DRAW_ROWS;
          }
        }
        moved = true;
        lastNextMoveMs = millis();
      }
    } else {
      nextRepeating = false;
    }

    if (btnPrev.consumeJustPressed()) {
      if (okHeld) {
        cursorY = (cursorY - 1 + DRAW_ROWS) % DRAW_ROWS;
        okUsedAsModifier = true;
      } else {
        cursorX--;
        if (cursorX < 0) {
          cursorX = DRAW_COLS - 1;
          cursorY = (cursorY - 1 + DRAW_ROWS) % DRAW_ROWS;
        }
      }
      moved = true;
      prevHeldSinceMs = millis();
      lastPrevMoveMs = millis();
      prevRepeating = false;
    } else if (btnPrev.isPressed()) {
      if (!prevRepeating && millis() - prevHeldSinceMs >= DRAW_REPEAT_INITIAL_DELAY_MS) {
        prevRepeating = true;
        lastPrevMoveMs = millis();
      }
      if (prevRepeating && millis() - lastPrevMoveMs >= DRAW_REPEAT_INTERVAL_MS) {
        if (okHeld) {
          cursorY = (cursorY - 1 + DRAW_ROWS) % DRAW_ROWS;
          okUsedAsModifier = true;
        } else {
          cursorX--;
          if (cursorX < 0) {
            cursorX = DRAW_COLS - 1;
            cursorY = (cursorY - 1 + DRAW_ROWS) % DRAW_ROWS;
          }
        }
        moved = true;
        lastPrevMoveMs = millis();
      }
    } else {
      prevRepeating = false;
    }

    // SW_OK ไม่ใช้ consumeJustPressed() แบบปุ่มอื่นแล้ว เพราะต้องรู้ทั้ง "เพิ่งกด" (เริ่มรอบการค้างใหม่)
    // และ "เพิ่งปล่อย" (ตัดสินใจว่าจะ toggle ไหม) - Button struct ไม่มี consumeJustReleased() สำเร็จรูป
    // เลยเทียบ okHeld กับ okWasHeld ของ loop ก่อนหน้าเอาเอง
    if (okHeld && !okWasHeld) {
      okUsedAsModifier = false; // เริ่มรอบกดค้างใหม่ ยังไม่รู้ว่าจะใช้เป็นตัวช่วยเปลี่ยนทิศไหม
    }
    if (!okHeld && okWasHeld) {
      if (!okUsedAsModifier) {
        toggleDrawPixel(cursorX, cursorY);
        moved = true;
      }
      okUsedAsModifier = false;
    }
    okWasHeld = okHeld;

    // เคอร์เซอร์ไม่กระพริบแล้ว (ค้างสีตรงข้ามนิ่งๆ - ดู drawDrawModeFrame) เลยวาดใหม่เฉพาะตอนมีอะไร
    // เปลี่ยนจริง (เลื่อน/toggle) พอ ไม่ต้องรีเฟรชเป็นจังหวะอีกต่อไป ลดภาระ I2C ลงไปด้วย
    if (moved) {
      drawDrawModeFrame(cursorX, cursorY);
    }

    delay(20);
  }
}

/// ล้างภาพที่วาดไว้ใน Draw mode ทิ้งทั้งหมด (กลับเป็นจอดำล้วน) - ต้องกด SW_OK ยืนยันอีกครั้งกันกดพลาด
/// เหมือน runClearOldScore() (แยกฟังก์ชันเพราะคนละความหมายกัน: อันนี้แค่ล้าง RAM ธรรมดา ไม่ใช่ NVS)
void runClearDrawMode() {
  showDisplayMessage("Clear drawing?", "OK=yes  BACK=no");

  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Draw] ยกเลิกการล้างภาพ");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      for (unsigned int i = 0; i < sizeof(drawBuffer); i++) {
        drawBuffer[i] = 0;
      }
      Serial.println("[Draw] ล้างภาพวาดแล้ว");
      showDisplayMessage("Drawing cleared", "");
      delay(1000);
      return;
    }
    delay(20);
  }
}

/// อ่านค่าพิกเซลจากภาพใน PROGMEM (pic ที่แปลงมาจาก Pic Mode Editor artifact - ดู pics.h) แพ็คบิตแบบ
/// เดียวกับ drawBuffer ของ Draw mode (idx = y*cols+x, byte = idx/8, bit = idx%8) ต่างกันแค่ต้องอ่านผ่าน
/// pgm_read_byte() เพราะข้อมูลอยู่ใน flash ไม่ใช่ RAM ธรรมดา และ cols/rows เป็นของแต่ละภาพเอง (ไม่ใช่
/// DRAW_COLS/DRAW_ROWS คงที่แบบเดิมอีกต่อไป - ภาพความละเอียดสูงสุด cellSize 1 จะเป็น 128x64 เต็มจอ)
bool getPicPixel(const PicEntry &pic, int x, int y) {
  int idx = y * pic.cols + x;
  uint8_t b = pgm_read_byte(&pic.data[idx / 8]);
  return (b >> (idx % 8)) & 1;
}

void drawPicFrame(const PicEntry &pic) {
  if (!displayReady) return;

  display.clearDisplay();
  for (int y = 0; y < pic.rows; y++) {
    for (int x = 0; x < pic.cols; x++) {
      if (getPicPixel(pic, x, y)) {
        display.fillRect(x * pic.cellSize, y * pic.cellSize, pic.cellSize, pic.cellSize, SSD1306_WHITE);
      }
    }
  }
  display.display();
}

/// โชว์ภาพเต็มจอตัวที่เลือก (index เข้า PIC_LIBRARY ตรงๆ) - แค่โชว์นิ่งๆ ไม่มีอะไรให้แก้ (แก้ภาพต้องไป
/// วาดใหม่ใน Pic Mode Editor artifact แล้วให้ Claude แปลงมาใหม่) BACK ออกกลับเมนู Others ทันที
void runViewPic(const PicEntry &pic) {
  drawPicFrame(pic);
  Serial.print("[Pic] แสดง ");
  Serial.println(pic.name);

  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Pic] ออกจาก Pic mode (กด BACK)");
      return;
    }
    delay(50);
  }
}

/// วาด 1 เฟรมของ GIF loop เต็มความละเอียดจอจริง 128x64 (ไม่ผ่าน 2x2 cell แบบ Draw/Pic mode เพราะเฟรม
/// พวกนี้สร้างจากโปรแกรมไม่มีใครต้อง toggle มือทีละพิกเซล) แพ็คบิตแบบเดียวกับ getPicPixel แค่ขนาดเต็มจอ
void drawGifFrame(const uint8_t* frame) {
  if (!displayReady) return;

  display.clearDisplay();
  for (int y = 0; y < SCREEN_HEIGHT; y++) {
    for (int x = 0; x < SCREEN_WIDTH; x++) {
      int idx = y * SCREEN_WIDTH + x;
      uint8_t b = pgm_read_byte(&frame[idx / 8]);
      if ((b >> (idx % 8)) & 1) {
        display.drawPixel(x, y, SSD1306_WHITE);
      }
    }
  }
  display.display();
}

/// วนเล่นแอนิเมชัน GIF ตัวที่เลือก - ไม่มีการโต้ตอบใดๆ นอกจาก BACK ออกกลับเมนูเลือก GIF ทันที ใช้จังหวะ
/// เวลาเดิมจาก GIF ต้นฉบับ (gif.frameDelayMs)
void runGifLoop(const GifEntry &gif) {
  Serial.print("[GIF] เข้า ");
  Serial.println(gif.name);

  while (btnBack.isPressed()) { delay(20); }
  btnBack.lastPressed = false;

  int frameIndex = 0;
  drawGifFrame(gif.frames[frameIndex]);
  unsigned long lastFrameMs = millis();

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.print("[GIF] ออกจาก ");
      Serial.print(gif.name);
      Serial.println(" (กด BACK)");
      return;
    }
    if (millis() - lastFrameMs >= gif.frameDelayMs) {
      frameIndex = (frameIndex + 1) % gif.frameCount;
      drawGifFrame(gif.frames[frameIndex]);
      lastFrameMs = millis();
    }
    delay(10);
  }
}

/// หน้าเลือก GIF ก่อนเล่น - < / > เลื่อนดูรายชื่อใน GIF_LIBRARY, OK เข้าไปเล่น (runGifLoop), BACK ออก
/// กลับเมนู Others - เหมือนแพทเทิร์นเมนู Others/WiFi ทุกอย่างในไฟล์นี้ (< / > เลื่อน, OK เข้า, BACK ออก)
void runGifPlayerMenu() {
  int menuIndex = 0;
  showDisplayMessage("Play GIF:", GIF_LIBRARY[menuIndex].name);
  Serial.println("[GIF] เข้าเมนูเลือก GIF");

  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnNext.consumeJustPressed()) {
      menuIndex = (menuIndex + 1) % GIF_LIBRARY_COUNT;
      showDisplayMessage("Play GIF:", GIF_LIBRARY[menuIndex].name);
    }
    if (btnPrev.consumeJustPressed()) {
      menuIndex = (menuIndex - 1 + GIF_LIBRARY_COUNT) % GIF_LIBRARY_COUNT;
      showDisplayMessage("Play GIF:", GIF_LIBRARY[menuIndex].name);
    }
    if (btnBack.consumeJustPressed()) {
      Serial.println("[GIF] ออกจากเมนูเลือก GIF");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      runGifLoop(GIF_LIBRARY[menuIndex]);
      showDisplayMessage("Play GIF:", GIF_LIBRARY[menuIndex].name);
    }
    delay(50);
  }
}

// ค่าคงที่ของ Tic-Tac-Toe - กระดาน 3x3 วาดกึ่งกลางจอ 128x64 เซลล์ละ 20 พิกเซล (60x60 รวม)
const int TTT_CELL_PX = 20;
const int TTT_GRID_PX = TTT_CELL_PX * 3;
const int TTT_OFFSET_X = (SCREEN_WIDTH - TTT_GRID_PX) / 2;
const int TTT_OFFSET_Y = (SCREEN_HEIGHT - TTT_GRID_PX) / 2;
const unsigned long TTT_CPU_THINK_MS = 400; // หน่วงให้เห็นจังหวะ "CPU กำลังคิด" ไม่ใช่ตอบโต้ทันทีจนดูแข็งๆ

/// เช็คผู้ชนะจากกระดาน 9 ช่อง (index 0-8 แถวละ 3) - คืน 'X'/'O' ถ้ามีผู้ชนะ, 'D' ถ้าเสมอ (เต็มกระดานไม่มี
/// ใครชนะ), ' ' ถ้ายังเล่นต่อได้
char checkWinner(char *b) {
  const int lines[8][3] = {{0,1,2},{3,4,5},{6,7,8},{0,3,6},{1,4,7},{2,5,8},{0,4,8},{2,4,6}};
  for (int i = 0; i < 8; i++) {
    int a = lines[i][0], c = lines[i][1], d = lines[i][2];
    if (b[a] != ' ' && b[a] == b[c] && b[c] == b[d]) return b[a];
  }
  bool full = true;
  for (int i = 0; i < 9; i++) {
    if (b[i] == ' ') full = false;
  }
  return full ? 'D' : ' ';
}

/// minimax ธรรมดา ไม่ต้อง alpha-beta pruning เพราะ search space ของ OX เล็กมาก (สูงสุด 9 ช่อง) ตัดสินใจ
/// ได้ในเวลาแทบเป็นศูนย์บน ESP32 - O (CPU) เป็นฝ่าย maximize, X (ผู้เล่น) เป็นฝ่าย minimize เล่นสมบูรณ์
/// แบบ (perfect play) เลยแพ้ CPU ไม่ได้ อย่างมากก็เสมอ - ตั้งใจให้เป็นแบบนี้ ("บอทที่เอาชนะไม่ได้")
int minimax(char *b, bool maximizingO) {
  char result = checkWinner(b);
  if (result == 'O') return 10;
  if (result == 'X') return -10;
  if (result == 'D') return 0;

  int best = maximizingO ? -1000 : 1000;
  for (int i = 0; i < 9; i++) {
    if (b[i] != ' ') continue;
    b[i] = maximizingO ? 'O' : 'X';
    int val = minimax(b, !maximizingO);
    b[i] = ' ';
    if (maximizingO) {
      if (val > best) best = val;
    } else {
      if (val < best) best = val;
    }
  }
  return best;
}

/// หาช่องที่ดีที่สุดให้ O (CPU) เล่น - คืน -1 ถ้าไม่มีช่องว่างเหลือ (ไม่ควรถูกเรียกตอนนั้นอยู่แล้วเพราะ
/// checkWinner จะจับ 'D' ไปก่อน แต่กันไว้เผื่อ)
int bestMoveForO(char *b) {
  int bestVal = -1000;
  int bestIdx = -1;
  for (int i = 0; i < 9; i++) {
    if (b[i] != ' ') continue;
    b[i] = 'O';
    int val = minimax(b, false);
    b[i] = ' ';
    if (val > bestVal) {
      bestVal = val;
      bestIdx = i;
    }
  }
  return bestIdx;
}

// ระดับความยาก: ยิ่งเลขมาก โอกาสที่ CPU จะเดินแบบสุ่ม (แทนที่จะเดินแบบ optimal จาก minimax) ยิ่งต่ำ -
// Hard = 0% สุ่มเลย (เอาชนะไม่ได้จริงตามที่ทำไว้ตอนแรก), Easy = สุ่มบ่อยจนพอเอาชนะได้
const char* TTT_DIFFICULTY_NAMES[] = {"Easy", "Medium", "Hard"};
const int TTT_DIFFICULTY_RANDOM_PERCENT[] = {70, 35, 0};
const int TTT_DIFFICULTY_COUNT = 3;

/// เลือกช่องให้ O (CPU) เดินตามระดับความยาก - โอกาส TTT_DIFFICULTY_RANDOM_PERCENT[difficulty]% จะเดิน
/// สุ่มไปที่ช่องว่างช่องใดช่องหนึ่งแทนช่องที่ดีที่สุดจาก minimax ทำให้ผู้เล่นชนะได้จริงในระดับที่ง่ายลง
int chooseMoveForO(char *b, int difficulty) {
  int optimal = bestMoveForO(b);
  if (optimal < 0) return -1;

  int randomPercent = TTT_DIFFICULTY_RANDOM_PERCENT[difficulty];
  if (randomPercent > 0 && (int)random(100) < randomPercent) {
    int empties[9];
    int n = 0;
    for (int i = 0; i < 9; i++) {
      if (b[i] == ' ') empties[n++] = i;
    }
    if (n > 0) return empties[random(n)];
  }
  return optimal;
}

/// หน้าเลือกระดับความยากก่อนเริ่มเกม - < / > เลื่อน, OK ยืนยันแล้วคืนค่าที่เลือก, BACK ยกเลิกคืน -1
/// (ให้ runTicTacToe รู้ว่าต้องกลับเมนู Others โดยไม่เริ่มเกมเลย)
int runTttDifficultyPicker(int startDifficulty) {
  int idx = startDifficulty;
  showDisplayMessage("Difficulty:", TTT_DIFFICULTY_NAMES[idx]);

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) return -1;
    if (btnNext.consumeJustPressed()) {
      idx = (idx + 1) % TTT_DIFFICULTY_COUNT;
      showDisplayMessage("Difficulty:", TTT_DIFFICULTY_NAMES[idx]);
    }
    if (btnPrev.consumeJustPressed()) {
      idx = (idx - 1 + TTT_DIFFICULTY_COUNT) % TTT_DIFFICULTY_COUNT;
      showDisplayMessage("Difficulty:", TTT_DIFFICULTY_NAMES[idx]);
    }
    if (btnOk.consumeJustPressed()) return idx;
    delay(20);
  }
}

void drawTicTacToeFrame(char *board, int cursor, bool gameOver, char winner, int difficulty) {
  if (!displayReady) return;

  display.clearDisplay();

  for (int i = 1; i < 3; i++) {
    display.drawFastVLine(TTT_OFFSET_X + i * TTT_CELL_PX, TTT_OFFSET_Y, TTT_GRID_PX, SSD1306_WHITE);
    display.drawFastHLine(TTT_OFFSET_X, TTT_OFFSET_Y + i * TTT_CELL_PX, TTT_GRID_PX, SSD1306_WHITE);
  }

  // โชว์ระดับความยากไว้มุมซ้ายบน (ในพื้นที่ว่างข้างกระดาน - กระดานเริ่มที่ x=TTT_OFFSET_X ไม่ใช่ 0)
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print(TTT_DIFFICULTY_NAMES[difficulty]);

  for (int i = 0; i < 9; i++) {
    int cx = TTT_OFFSET_X + (i % 3) * TTT_CELL_PX;
    int cy = TTT_OFFSET_Y + (i / 3) * TTT_CELL_PX;
    if (board[i] == 'X') {
      display.drawLine(cx + 3, cy + 3, cx + TTT_CELL_PX - 3, cy + TTT_CELL_PX - 3, SSD1306_WHITE);
      display.drawLine(cx + TTT_CELL_PX - 3, cy + 3, cx + 3, cy + TTT_CELL_PX - 3, SSD1306_WHITE);
    } else if (board[i] == 'O') {
      display.drawCircle(cx + TTT_CELL_PX / 2, cy + TTT_CELL_PX / 2, TTT_CELL_PX / 2 - 3, SSD1306_WHITE);
    }
    if (!gameOver && i == cursor) {
      display.drawRect(cx + 1, cy + 1, TTT_CELL_PX - 2, TTT_CELL_PX - 2, SSD1306_WHITE);
    }
  }

  if (gameOver) {
    display.fillRect(18, 26, 92, 16, SSD1306_BLACK);
    display.drawRect(18, 26, 92, 16, SSD1306_WHITE);
    display.setTextSize(1);
    display.setCursor(24, 31);
    if (winner == 'X') {
      display.print("You win!");
    } else if (winner == 'O') {
      display.print("CPU wins");
    } else {
      display.print("Draw!");
    }
  }

  display.display();
}

/// Tic-Tac-Toe กับ CPU (ความยากปรับได้ - ดู chooseMoveForO/runTttDifficultyPicker) ผู้เล่นเป็น X ไปก่อน
/// เสมอ เลื่อนเคอร์เซอร์ไล่ช่อง 0-8 ด้วย < / > (วนครบกลับมาช่องแรกได้) กด SW_OK วางที่ช่องว่างตรง
/// เคอร์เซอร์ CPU ตอบสนองอัตโนมัติหลังหน่วง TTT_CPU_THINK_MS ให้ดูเหมือน "กำลังคิด" จบเกมแล้วกด OK
/// เล่นใหม่ได้ทันที (ความยากเดิมที่เลือกไว้) หรือ BACK ออกกลับเมนู Others - BACK กดได้ทุกเมื่อไม่ต้องรอจบเกม
void runTicTacToe() {
  Preferences prefs;
  prefs.begin("ttt", false);
  int difficulty = prefs.getInt("difficulty", 2); // default Hard (พฤติกรรมเดิมก่อนมีระดับความยาก)

  int chosen = runTttDifficultyPicker(difficulty);
  if (chosen < 0) {
    Serial.println("[TTT] ยกเลิกก่อนเริ่มเกม (กด BACK ที่หน้าเลือกความยาก)");
    prefs.end();
    return;
  }
  difficulty = chosen;
  prefs.putInt("difficulty", difficulty);
  prefs.end();

  char board[9];
  for (int i = 0; i < 9; i++) board[i] = ' ';
  int cursor = 4;
  bool playerTurn = true;
  char winner = ' ';
  unsigned long cpuMoveDueMs = 0;

  Serial.print("[TTT] เริ่มเกม Tic-Tac-Toe ใหม่ - ความยาก: ");
  Serial.print(TTT_DIFFICULTY_NAMES[difficulty]);
  Serial.println(" - ผู้เล่น (X) ไปก่อน");

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  drawTicTacToeFrame(board, cursor, false, ' ', difficulty);

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[TTT] ออกจากเกม (กด BACK)");
      return;
    }

    if (winner != ' ') {
      // จบเกมแล้ว - รอ OK เล่นใหม่ (BACK ออกเช็คไว้ด้านบนแล้ว)
      if (btnOk.consumeJustPressed()) {
        for (int i = 0; i < 9; i++) board[i] = ' ';
        cursor = 4;
        playerTurn = true;
        winner = ' ';
        Serial.println("[TTT] เริ่มเกมใหม่");
        drawTicTacToeFrame(board, cursor, false, ' ', difficulty);
      }
      delay(20);
      continue;
    }

    if (playerTurn) {
      bool moved = false;
      if (btnNext.consumeJustPressed()) { cursor = (cursor + 1) % 9; moved = true; }
      if (btnPrev.consumeJustPressed()) { cursor = (cursor - 1 + 9) % 9; moved = true; }
      if (btnOk.consumeJustPressed() && board[cursor] == ' ') {
        board[cursor] = 'X';
        winner = checkWinner(board);
        if (winner == ' ') {
          playerTurn = false;
          cpuMoveDueMs = millis() + TTT_CPU_THINK_MS;
        } else {
          Serial.print("[TTT] จบเกม - ผล: ");
          Serial.println(winner);
        }
        moved = true;
      }
      if (moved) drawTicTacToeFrame(board, cursor, winner != ' ', winner, difficulty);
    } else if (millis() >= cpuMoveDueMs) {
      int m = chooseMoveForO(board, difficulty);
      if (m >= 0) {
        board[m] = 'O';
        winner = checkWinner(board);
      } else {
        winner = 'D';
      }
      playerTurn = true;
      if (winner != ' ') {
        Serial.print("[TTT] จบเกม - ผล: ");
        Serial.println(winner);
      }
      drawTicTacToeFrame(board, cursor, winner != ' ', winner, difficulty);
    }

    delay(20);
  }
}

// คำตอบของ Magic 8-Ball - สั้นๆ ตั้งใจ (ไม่เกิน ~16 ตัวอักษร) กัน showDisplayMessage ขึ้นบรรทัดใหม่เอง
// จน content ทับกับบรรทัดคำใบ้ด้านล่าง (จอกว้าง 128px, ตัวอักษร size 1 กว้างตัวละ 6px = ~21 ตัวต่อแถว)
// ปรับเป็นคำง่ายๆตรงไปตรงมาล้วน (ตัดสำนวน/ศัพท์ยากอย่าง "No doubt", "Outlook good/bad",
// "Signs point to yes" ออกหมด) - ผู้ใช้รายงานว่าบางคำแปลไทยไม่ออก/ฟังแปลกในบางสถานการณ์
const char* EIGHTBALL_ANSWERS[] = {
  "Yes", "No", "Maybe", "absolutely", "absolutely not", "Maybe", "Maybe not",
  "Very likely", "Very unlikely", "Not sure", "Ask again", "High chance", "Low chance", "I don't know"
};
const int EIGHTBALL_ANSWER_COUNT = 14;

/// Magic 8-Ball - กด OK สุ่มคำตอบใหม่ทุกครั้ง BACK ออกกลับเมนู Others ทันที
void runMagic8Ball() {
  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  Serial.println("[8Ball] เข้า Magic 8-Ball");
  showDisplayMessage("Magic 8-Ball", "OK = ask");

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[8Ball] ออก (กด BACK)");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      int idx = random(EIGHTBALL_ANSWER_COUNT);
      Serial.print("[8Ball] คำตอบ: ");
      Serial.println(EIGHTBALL_ANSWERS[idx]);
      showDisplayMessage(EIGHTBALL_ANSWERS[idx], "OK = ask again");
    }
    delay(20);
  }
}

void drawDiceFrame(int value) {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("Dice - OK to roll");
  display.setTextSize(4);
  display.setCursor(56, 22);
  display.print(value);
  display.display();
}

/// ทอยลูกเต๋า d6 ธรรมดา - กด OK ทอยใหม่ทุกครั้ง BACK ออกกลับเมนู Others ทันที
void runDiceRoller() {
  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  Serial.println("[Dice] เข้า Dice Roll");
  int value = random(1, 7);
  drawDiceFrame(value);

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Dice] ออก (กด BACK)");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      value = random(1, 7);
      Serial.print("[Dice] ออกเลข: ");
      Serial.println(value);
      drawDiceFrame(value);
    }
    delay(20);
  }
}

// 0 = Rock, 1 = Paper, 2 = Scissors
const char* RPS_NAMES[] = {"Rock", "Paper", "Scissors"};

/// เทียบผลเป่ายิ้งฉุบ - คืน 1 ถ้า a ชนะ b, -1 ถ้า b ชนะ a, 0 ถ้าเสมอ
int rpsCompare(int a, int b) {
  if (a == b) return 0;
  if ((a == 0 && b == 2) || (a == 1 && b == 0) || (a == 2 && b == 1)) return 1;
  return -1;
}

/// วาดไอคอนแทนท่า (ไม่ใช้ตัวหนังสือเฉยๆ) - ก้อนหิน = วงกลมทึบ, กระดาษ = สี่เหลี่ยมกรอบ, กรรไกร = เส้น
/// กากบาท (สื่อถึงใบมีด 2 ข้างไขว้กัน) วาดด้วย Adafruit_GFX primitive ธรรมดา ไม่ต้องแปลงภาพจากไฟล์
void drawRpsIcon(int shape, int cx, int cy, int size) {
  if (!displayReady) return;
  int half = size / 2;
  if (shape == 0) {
    display.fillCircle(cx, cy, half, SSD1306_WHITE);
  } else if (shape == 1) {
    display.drawRect(cx - half, cy - half, size, size, SSD1306_WHITE);
  } else {
    display.drawLine(cx - half, cy - half, cx + half, cy + half, SSD1306_WHITE);
    display.drawLine(cx - half, cy + half, cx + half, cy - half, SSD1306_WHITE);
  }
}

void drawRpsPickFrame(int choice) {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("Pick: <> OK=go");
  drawRpsIcon(choice, SCREEN_WIDTH / 2, 32, 30);
  int labelX = SCREEN_WIDTH / 2 - (strlen(RPS_NAMES[choice]) * 3);
  display.setCursor(labelX, 54);
  display.print(RPS_NAMES[choice]);
  display.display();
}

void drawRpsResultFrame(int you, int cpu, int result) {
  if (!displayReady) return;
  display.clearDisplay();

  const char* resultText = (result == 1) ? "You Win!" : ((result == -1) ? "CPU Wins" : "Draw!");
  display.setTextSize(1);
  display.setCursor((SCREEN_WIDTH - strlen(resultText) * 6) / 2, 0);
  display.print(resultText);

  drawRpsIcon(you, 26, 32, 24);
  drawRpsIcon(cpu, 102, 32, 24);
  display.setCursor(26 - strlen(RPS_NAMES[you]) * 3, 54);
  display.print(RPS_NAMES[you]);
  display.setCursor(102 - strlen(RPS_NAMES[cpu]) * 3, 54);
  display.print(RPS_NAMES[cpu]);

  display.display();
}

/// เป่ายิ้งฉุบกับ CPU - < / > เลื่อนเลือกท่า (โชว์เป็นไอคอน ไม่ใช่ตัวหนังสือเฉยๆ), OK ยืนยันแล้วสู้กับ
/// ท่าสุ่มของ CPU ทันที (เลือกท่าใหม่ได้เรื่อยๆ ด้วย < / > แม้หลังเห็นผลแล้ว) BACK ออกกลับเมนู Others ทันที
void runRockPaperScissors() {
  int choice = 0;

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  Serial.println("[RPS] เข้า Rock Paper Scissors");
  drawRpsPickFrame(choice);

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[RPS] ออก (กด BACK)");
      return;
    }
    if (btnNext.consumeJustPressed()) {
      choice = (choice + 1) % 3;
      drawRpsPickFrame(choice);
    }
    if (btnPrev.consumeJustPressed()) {
      choice = (choice - 1 + 3) % 3;
      drawRpsPickFrame(choice);
    }
    if (btnOk.consumeJustPressed()) {
      int cpu = random(3);
      int result = rpsCompare(choice, cpu);
      Serial.print("[RPS] You: ");
      Serial.print(RPS_NAMES[choice]);
      Serial.print(" / CPU: ");
      Serial.print(RPS_NAMES[cpu]);
      Serial.print(" -> ");
      Serial.println(result == 1 ? "You win" : (result == -1 ? "CPU wins" : "Draw"));
      drawRpsResultFrame(choice, cpu, result);
    }
    delay(20);
  }
}

/// DVD logo เด้งชนขอบจอ - ไม่มี interaction เลยนอกจาก BACK ออกกลับเมนู Others ทันที (ปล่อยดูเพลินๆ)
void runDvdBounce() {
  while (btnBack.isPressed()) { delay(20); }
  btnBack.lastPressed = false;

  Serial.println("[DVD] เข้า DVD Bounce");

  const int BOX_W = 20;
  const int BOX_H = 10;
  float x = 10, y = 10;
  float vx = 1.6, vy = 1.1;
  unsigned long lastTickMs = millis();

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[DVD] ออก (กด BACK)");
      return;
    }

    if (millis() - lastTickMs >= 30) {
      lastTickMs = millis();
      x += vx;
      y += vy;
      if (x <= 0) { x = 0; vx = -vx; }
      if (x + BOX_W >= SCREEN_WIDTH) { x = SCREEN_WIDTH - BOX_W; vx = -vx; }
      if (y <= 0) { y = 0; vy = -vy; }
      if (y + BOX_H >= SCREEN_HEIGHT) { y = SCREEN_HEIGHT - BOX_H; vy = -vy; }

      if (displayReady) {
        display.clearDisplay();
        display.drawRect((int)x, (int)y, BOX_W, BOX_H, SSD1306_WHITE);
        display.setTextSize(1);
        display.setCursor((int)x + 2, (int)y + 1);
        display.print("DVD");
        display.display();
      }
    }
    delay(5);
  }
}

// ค่าคงที่ของ Pong - ไม้อยู่ซ้ายสุด, ลูกบอลเด้งกำแพงขวา/บน/ล่าง พลาดไม้ (หลุดซ้าย) = จบเกม
const int PONG_PADDLE_HEIGHT = 16;
const int PONG_PADDLE_WIDTH = 2;
const int PONG_PADDLE_X = 4;
const float PONG_PADDLE_SPEED = 2.2; // เพิ่มจาก 1.0 ตามที่ขอ ให้ไม้ขยับไวขึ้น
const int PONG_BALL_SIZE = 3;

/// Pong กระทบกำแพง (single-player) - ไม้ควบคุมด้วย < (ขึ้น) / > (ลง) กดค้างขยับต่อเนื่องได้ (poll
/// isPressed() ทุก loop ไม่ใช่ edge-trigger) ลูกบอลเด้งกำแพงบน/ล่าง/ขวา หลุดออกทางซ้าย = จบเกม คะแนน =
/// จำนวนครั้งที่ตีโดนไม้ high score เก็บถาวรผ่าน NVS เหมือน Mini Dino
void runPong() {
  Preferences prefs;
  prefs.begin("pong", false);
  unsigned int highScore = prefs.getUInt("highscore", 0);

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  Serial.println("[Pong] เข้าเกม Pong");

  while (true) {
    float paddleY = (SCREEN_HEIGHT - PONG_PADDLE_HEIGHT) / 2.0;
    float ballX = SCREEN_WIDTH / 2.0;
    float ballY = SCREEN_HEIGHT / 2.0;
    // สุ่มทิศเริ่มต้นทุกรอบใหม่ (ทั้งซ้าย/ขวา และมุมขึ้น/ลง) ไม่ใช่ทิศตายตัวเหมือนเดิม - สุ่มขนาด
    // ความเร็วในช่วงแคบๆ ด้วยเผื่อความหลากหลาย ไม่ใช้ sin/cos ให้ยุ่งยาก แค่สุ่มเครื่องหมาย+ขนาดตรงๆ
    float ballVX = (random(2) == 0 ? 1.0 : -1.0) * (1.8 + random(0, 8) / 10.0);
    float ballVY = (random(2) == 0 ? 1.0 : -1.0) * (0.8 + random(0, 8) / 10.0);
    unsigned int score = 0;
    bool gameOver = false;
    unsigned long lastTickMs = millis();

    while (!gameOver) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Pong] ออกจากเกม (กด BACK)");
        prefs.end();
        return;
      }

      if (btnPrev.isPressed()) paddleY -= PONG_PADDLE_SPEED;
      if (btnNext.isPressed()) paddleY += PONG_PADDLE_SPEED;
      if (paddleY < 0) paddleY = 0;
      if (paddleY > SCREEN_HEIGHT - PONG_PADDLE_HEIGHT) paddleY = SCREEN_HEIGHT - PONG_PADDLE_HEIGHT;

      if (millis() - lastTickMs >= 25) {
        lastTickMs = millis();
        ballX += ballVX;
        ballY += ballVY;

        if (ballY <= 0) { ballY = 0; ballVY = -ballVY; }
        if (ballY + PONG_BALL_SIZE >= SCREEN_HEIGHT) { ballY = SCREEN_HEIGHT - PONG_BALL_SIZE; ballVY = -ballVY; }
        if (ballX + PONG_BALL_SIZE >= SCREEN_WIDTH) { ballX = SCREEN_WIDTH - PONG_BALL_SIZE; ballVX = -ballVX; }

        // เช็คไม้เฉพาะตอนลูกกำลังวิ่งเข้าหา (vx < 0) กันนับซ้ำหลังเด้งแล้ว (ดู comment หัวไฟล์เรื่อง
        // การเช็ค collision ทีละเฟรมของ Mini Dino - หลักการเดียวกัน)
        if (ballVX < 0 && ballX <= PONG_PADDLE_X + PONG_PADDLE_WIDTH) {
          if (ballY + PONG_BALL_SIZE >= paddleY && ballY <= paddleY + PONG_PADDLE_HEIGHT) {
            ballX = PONG_PADDLE_X + PONG_PADDLE_WIDTH + 1;
            ballVX = -ballVX;
            score++;
          } else if (ballX < 0) {
            gameOver = true;
            Serial.print("[Pong] พลาด - คะแนน: ");
            Serial.println(score);
            if (score > highScore) {
              highScore = score;
              prefs.putUInt("highscore", highScore);
              Serial.println("[Pong] สถิติใหม่! เซฟลง NVS แล้ว");
            }
          }
        }

        if (displayReady) {
          display.clearDisplay();
          display.setTextSize(1);
          display.setCursor(0, 0);
          display.print("HI ");
          display.print(highScore);
          display.setCursor(90, 0);
          display.print("SC ");
          display.print(score);
          display.fillRect(PONG_PADDLE_X, (int)paddleY, PONG_PADDLE_WIDTH, PONG_PADDLE_HEIGHT, SSD1306_WHITE);
          display.fillRect((int)ballX, (int)ballY, PONG_BALL_SIZE, PONG_BALL_SIZE, SSD1306_WHITE);
          display.display();
        }
      }
      delay(5);
    }

    if (displayReady) {
      display.setTextSize(1);
      display.setCursor(28, 26);
      display.print("GAME OVER");
      display.setCursor(4, 38);
      display.print("OK=retry BACK=exit");
      display.display();
    }

    while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
    btnOk.lastPressed = false;
    btnBack.lastPressed = false;

    while (true) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Pong] ออกจากเกมหลังจบรอบ (กด BACK)");
        prefs.end();
        return;
      }
      if (btnOk.consumeJustPressed()) {
        Serial.println("[Pong] เล่นใหม่อีกรอบ");
        break;
      }
      delay(20);
    }
  }
}

// framebuffer คู่ของ Conway's Game of Life - แยกจาก drawBuffer ของ Draw mode เด็ดขาด (ไม่งั้นจะไปทับ
// ภาพที่ผู้ใช้วาดไว้) ใช้ความละเอียดเดียวกัน (64x32 ผ่าน DRAW_CELL_SIZE 2x2 พิกเซล/ช่อง) ต้องมี 2 buffer
// เพราะคำนวณ generation ถัดไปต้องอ้างอิงสถานะเดิมทั้งกระดานพร้อมกัน เขียนทับ buffer เดียวกันระหว่างคำนวณ
// จะพังตั้งแต่กลางที่ (สลับ current/next ด้วย pointer แทนการ copy ข้อมูลจริง ประหยัดกว่า)
uint8_t lifeBufA[DRAW_COLS * DRAW_ROWS / 8];
uint8_t lifeBufB[DRAW_COLS * DRAW_ROWS / 8];

/// อ่านค่าเซลล์แบบ wraparound (ขอบจอวนกลับมาอีกฝั่ง - torus topology) กันเซลล์ตายเพราะขอบจอไปเฉยๆ
/// ทั้งที่กฎ Game of Life จริงไม่ได้ตั้งใจให้มี "ขอบโลก"
bool getLifeCell(uint8_t *buf, int x, int y) {
  x = ((x % DRAW_COLS) + DRAW_COLS) % DRAW_COLS;
  y = ((y % DRAW_ROWS) + DRAW_ROWS) % DRAW_ROWS;
  int idx = y * DRAW_COLS + x;
  return (buf[idx / 8] >> (idx % 8)) & 1;
}

void setLifeCell(uint8_t *buf, int x, int y, bool alive) {
  int idx = y * DRAW_COLS + x;
  if (alive) buf[idx / 8] |= (1 << (idx % 8));
  else buf[idx / 8] &= ~(1 << (idx % 8));
}

/// สุ่มเซลล์เริ่มต้นประมาณ 25% ของกระดาน - ทดลองแล้วให้แพทเทิร์นที่น่าดูกว่าทั้งแน่นเกินไป (ตายหมดเร็ว)
/// หรือโล่งเกินไป (ไม่มีอะไรเกิดขึ้นเลย)
void seedLifeRandom(uint8_t *buf) {
  for (unsigned int i = 0; i < DRAW_COLS * DRAW_ROWS / 8; i++) buf[i] = 0;
  for (int y = 0; y < DRAW_ROWS; y++) {
    for (int x = 0; x < DRAW_COLS; x++) {
      if (random(100) < 25) setLifeCell(buf, x, y, true);
    }
  }
}

/// คำนวณ generation ถัดไปตามกฎ B3/S23 ของ Conway's Game of Life มาตรฐาน: เซลล์มีชีวิตรอดถ้ามีเพื่อนบ้าน
/// (8 ทิศ) 2 หรือ 3 ตัว, เซลล์ตายเกิดใหม่ถ้ามีเพื่อนบ้านมีชีวิตพอดี 3 ตัว นอกนั้นตาย/ยังตายต่อ
void stepLife(uint8_t *cur, uint8_t *next) {
  for (int y = 0; y < DRAW_ROWS; y++) {
    for (int x = 0; x < DRAW_COLS; x++) {
      int n = 0;
      for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
          if (dx == 0 && dy == 0) continue;
          if (getLifeCell(cur, x + dx, y + dy)) n++;
        }
      }
      bool alive = getLifeCell(cur, x, y);
      bool nextAlive = alive ? (n == 2 || n == 3) : (n == 3);
      setLifeCell(next, x, y, nextAlive);
    }
  }
}

void drawLifeFrame(uint8_t *buf) {
  if (!displayReady) return;
  display.clearDisplay();
  for (int y = 0; y < DRAW_ROWS; y++) {
    for (int x = 0; x < DRAW_COLS; x++) {
      if (getLifeCell(buf, x, y)) {
        display.fillRect(x * DRAW_CELL_SIZE, y * DRAW_CELL_SIZE, DRAW_CELL_SIZE, DRAW_CELL_SIZE, SSD1306_WHITE);
      }
    }
  }
  display.display();
}

/// Conway's Game of Life - ปล่อยวิวัฒน์เองอัตโนมัติทุก ~150ms ไม่ต้องกดอะไรเลย (ดูเพลินๆ) กด OK สุ่ม
/// กระดานเริ่มใหม่ได้ทุกเมื่อ BACK ออกกลับเมนู Others ทันที
void runGameOfLife() {
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  Serial.println("[Life] เข้า Game of Life");

  uint8_t *cur = lifeBufA;
  uint8_t *next = lifeBufB;
  seedLifeRandom(cur);
  drawLifeFrame(cur);
  unsigned long lastStepMs = millis();

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Life] ออก (กด BACK)");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      Serial.println("[Life] สุ่มกระดานใหม่ (กด OK)");
      seedLifeRandom(cur);
      drawLifeFrame(cur);
      lastStepMs = millis();
    }
    if (millis() - lastStepMs >= 150) {
      stepLife(cur, next);
      uint8_t *tmp = cur;
      cur = next;
      next = tmp;
      drawLifeFrame(cur);
      lastStepMs = millis();
    }
    delay(10);
  }
}

// ค่าคงที่ของ Lights Out - เลือกขนาดกระดานได้ 6 แบบ (3x3 เล็กสุด ถึง 8x8 ใหญ่สุด) cellPx คำนวณมาให้พอดี
// กับจอ 128x64 แต่ละขนาด ใช้ uint64_t เป็น bitboard เดียว (บิตที่ idx = y*n+x) เพราะ 8x8=64 บิตพอดี
// เป๊ะกับขนาด uint64_t ตัวเดียว ไม่ต้องแยกเป็น array หลายไบต์แบบ Draw mode/Game of Life
const int LIGHTSOUT_SIZE_COUNT = 6;
const int LIGHTSOUT_SIZES[LIGHTSOUT_SIZE_COUNT] = {3, 4, 5, 6, 7, 8};
const int LIGHTSOUT_CELL_PX[LIGHTSOUT_SIZE_COUNT] = {16, 14, 12, 10, 8, 7};

bool getLightsOutCell(uint64_t board, int n, int x, int y) {
  int idx = y * n + x;
  return (board >> idx) & 1ULL;
}

/// คำนวณ mask ของบิตที่ต้อง XOR ตอนกดช่อง (x,y) - ตัวมันเอง + เพื่อนบ้าน 4 ทิศ (ถ้ามี ไม่วนขอบ) ตามกฎ
/// Lights Out มาตรฐาน
uint64_t toggleLightsOutMask(int n, int x, int y) {
  uint64_t mask = 1ULL << (y * n + x);
  if (x > 0) mask |= 1ULL << (y * n + (x - 1));
  if (x < n - 1) mask |= 1ULL << (y * n + (x + 1));
  if (y > 0) mask |= 1ULL << ((y - 1) * n + x);
  if (y < n - 1) mask |= 1ULL << ((y + 1) * n + x);
  return mask;
}

bool lightsOutSolved(uint64_t board, int n) {
  // n=8 -> n*n=64 พอดีเต็ม uint64_t - เลื่อนบิต 64 ตำแหน่งเป็น undefined behavior ต้องกันเคสนี้แยก
  uint64_t usedBits = (n * n >= 64) ? ~0ULL : ((1ULL << (n * n)) - 1ULL);
  return (board & usedBits) == 0;
}

/// สุ่มโจทย์โดยเริ่มจากกระดานที่ดับหมด (แก้แล้ว) แล้วกดสุ่มไปเรื่อยๆ - รับประกันว่าแก้ได้เสมอ เพราะการกด
/// แต่ละครั้งเป็น XOR ซึ่งกลับด้วยตัวเองได้ (กดซ้ำตำแหน่งเดิมชุดเดียวกันจะกลับไปดับหมดเสมอ ไม่ว่าลำดับไหน)
void scrambleLightsOut(uint64_t &board, int n) {
  board = 0;
  int presses = n + random(n, n * n);
  for (int i = 0; i < presses; i++) {
    int x = random(n);
    int y = random(n);
    board ^= toggleLightsOutMask(n, x, y);
  }
}

void drawLightsOutFrame(uint64_t board, int n, int cellPx, int cursorX, int cursorY, unsigned int moves, bool solved) {
  if (!displayReady) return;

  display.clearDisplay();
  int gridPx = n * cellPx;
  int offsetX = (SCREEN_WIDTH - gridPx) / 2;
  int offsetY = (SCREEN_HEIGHT - gridPx) / 2;

  for (int y = 0; y < n; y++) {
    for (int x = 0; x < n; x++) {
      int cx = offsetX + x * cellPx;
      int cy = offsetY + y * cellPx;
      if (getLightsOutCell(board, n, x, y)) {
        display.fillRect(cx + 1, cy + 1, cellPx - 2, cellPx - 2, SSD1306_WHITE);
      } else {
        display.drawRect(cx + 1, cy + 1, cellPx - 2, cellPx - 2, SSD1306_WHITE);
      }
      if (!solved && x == cursorX && y == cursorY) {
        display.drawRect(cx, cy, cellPx, cellPx, SSD1306_WHITE); // กรอบนอกเน้นตำแหน่งเคอร์เซอร์
      }
    }
  }

  if (solved) {
    display.fillRect(20, 24, 88, 16, SSD1306_BLACK);
    display.drawRect(20, 24, 88, 16, SSD1306_WHITE);
    display.setTextSize(1);
    display.setCursor(26, 29);
    display.print("Solved! ");
    display.print(moves);
  }

  display.display();
}

void showLightsOutSizeScreen(int idx) {
  int n = LIGHTSOUT_SIZES[idx];
  String line2 = String(n) + "x" + String(n);
  showDisplayMessage("Lights Out size:", line2.c_str());
}

/// หน้าเลือกขนาดกระดานก่อนเริ่ม - < / > เลื่อน, OK ยืนยันคืน index, BACK ยกเลิกคืน -1 (แพทเทิร์นเดียวกับ
/// runTttDifficultyPicker)
int runLightsOutSizePicker(int startIdx) {
  int idx = startIdx;
  showLightsOutSizeScreen(idx);

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) return -1;
    if (btnNext.consumeJustPressed()) {
      idx = (idx + 1) % LIGHTSOUT_SIZE_COUNT;
      showLightsOutSizeScreen(idx);
    }
    if (btnPrev.consumeJustPressed()) {
      idx = (idx - 1 + LIGHTSOUT_SIZE_COUNT) % LIGHTSOUT_SIZE_COUNT;
      showLightsOutSizeScreen(idx);
    }
    if (btnOk.consumeJustPressed()) return idx;
    delay(20);
  }
}

/// Lights Out - เลื่อนเคอร์เซอร์ด้วย < / > ปกติ (ซ้าย-ขวา วนในแถวเดิม ไม่ไหลข้ามแถวแบบ Draw mode เพราะ
/// เป็นกริด 2 มิติจริงๆ ไม่ใช่ผืนผ้าใบต่อเนื่อง) กด SW_OK ค้างไว้แล้วกด < / > เพื่อขึ้น-ลงแทน (แพทเทิร์น
/// เดียวกับ Draw mode เป๊ะ - ดู comment ของ runDrawMode) แตะ SW_OK เฉยๆ (ไม่ได้ใช้เป็นตัวช่วยเปลี่ยนทิศ
/// เลยระหว่างที่ค้าง) = กดช่องนั้น (toggle ตัวเอง+เพื่อนบ้าน 4 ทิศ) ดับให้หมดทุกดวงเพื่อชนะ BACK ออกได้
/// ทุกเมื่อ
void runLightsOut() {
  Preferences prefs;
  prefs.begin("lightsout", false);
  int sizeIdx = prefs.getInt("sizeIdx", 2); // default index 2 = 5x5 กลางๆ

  int chosen = runLightsOutSizePicker(sizeIdx);
  if (chosen < 0) {
    Serial.println("[LightsOut] ยกเลิกก่อนเริ่ม (กด BACK ที่หน้าเลือกขนาด)");
    prefs.end();
    return;
  }
  sizeIdx = chosen;
  prefs.putInt("sizeIdx", sizeIdx);
  prefs.end();

  int n = LIGHTSOUT_SIZES[sizeIdx];
  int cellPx = LIGHTSOUT_CELL_PX[sizeIdx];

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  Serial.print("[LightsOut] เริ่มเกม ขนาด ");
  Serial.print(n);
  Serial.print("x");
  Serial.println(n);

  while (true) {
    uint64_t board;
    scrambleLightsOut(board, n);
    int cursorX = n / 2;
    int cursorY = n / 2;
    unsigned int moves = 0;
    bool solved = false;

    unsigned long nextHeldSinceMs = 0, lastNextMoveMs = 0;
    bool nextRepeating = false;
    unsigned long prevHeldSinceMs = 0, lastPrevMoveMs = 0;
    bool prevRepeating = false;
    bool okWasHeld = false, okUsedAsModifier = false;

    drawLightsOutFrame(board, n, cellPx, cursorX, cursorY, moves, false);

    while (!solved) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[LightsOut] ออกจากเกม (กด BACK)");
        return;
      }

      bool moved = false;
      bool okHeld = btnOk.isPressed();

      if (btnNext.consumeJustPressed()) {
        if (okHeld) { cursorY = (cursorY + 1) % n; okUsedAsModifier = true; }
        else { cursorX = (cursorX + 1) % n; }
        moved = true;
        nextHeldSinceMs = millis();
        lastNextMoveMs = millis();
        nextRepeating = false;
      } else if (btnNext.isPressed()) {
        if (!nextRepeating && millis() - nextHeldSinceMs >= DRAW_REPEAT_INITIAL_DELAY_MS) {
          nextRepeating = true;
          lastNextMoveMs = millis();
        }
        if (nextRepeating && millis() - lastNextMoveMs >= DRAW_REPEAT_INTERVAL_MS) {
          if (okHeld) { cursorY = (cursorY + 1) % n; okUsedAsModifier = true; }
          else { cursorX = (cursorX + 1) % n; }
          moved = true;
          lastNextMoveMs = millis();
        }
      } else {
        nextRepeating = false;
      }

      if (btnPrev.consumeJustPressed()) {
        if (okHeld) { cursorY = (cursorY - 1 + n) % n; okUsedAsModifier = true; }
        else { cursorX = (cursorX - 1 + n) % n; }
        moved = true;
        prevHeldSinceMs = millis();
        lastPrevMoveMs = millis();
        prevRepeating = false;
      } else if (btnPrev.isPressed()) {
        if (!prevRepeating && millis() - prevHeldSinceMs >= DRAW_REPEAT_INITIAL_DELAY_MS) {
          prevRepeating = true;
          lastPrevMoveMs = millis();
        }
        if (prevRepeating && millis() - lastPrevMoveMs >= DRAW_REPEAT_INTERVAL_MS) {
          if (okHeld) { cursorY = (cursorY - 1 + n) % n; okUsedAsModifier = true; }
          else { cursorX = (cursorX - 1 + n) % n; }
          moved = true;
          lastPrevMoveMs = millis();
        }
      } else {
        prevRepeating = false;
      }

      if (okHeld && !okWasHeld) okUsedAsModifier = false;
      if (!okHeld && okWasHeld) {
        if (!okUsedAsModifier) {
          board ^= toggleLightsOutMask(n, cursorX, cursorY);
          moves++;
          moved = true;
          playBeep(30);
          if (lightsOutSolved(board, n)) {
            solved = true;
            Serial.print("[LightsOut] แก้สำเร็จ! ใช้ ");
            Serial.print(moves);
            Serial.println(" ครั้ง");
            playBeepPattern(3, 60, 60);
          }
        }
        okUsedAsModifier = false;
      }
      okWasHeld = okHeld;

      if (moved) drawLightsOutFrame(board, n, cellPx, cursorX, cursorY, moves, solved);
      delay(20);
    }

    while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
    btnOk.lastPressed = false;
    btnBack.lastPressed = false;

    while (true) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[LightsOut] ออกจากเกมหลังแก้สำเร็จ (กด BACK)");
        return;
      }
      if (btnOk.consumeJustPressed()) {
        Serial.println("[LightsOut] เล่นใหม่อีกรอบ (ขนาดเดิม)");
        break;
      }
      delay(20);
    }
  }
}

// สัญลักษณ์ของสล็อต - ใช้ตัวอักษร/สัญลักษณ์เดี่ยวแทนไอคอนกราฟิก ง่ายและอ่านออกชัดในขนาดตัวใหญ่
const char* SLOTS_SYMBOLS[] = {"7", "$", "*", "#", "@", "%"};
const int SLOTS_SYMBOL_COUNT = 6;

void drawSlotsFrame(const int reels[3], int credits, const char* resultMsg) {
  if (!displayReady) return;

  display.clearDisplay();
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("Credits: ");
  display.print(credits);

  display.setTextSize(3);
  display.setCursor(14, 20);
  display.print(SLOTS_SYMBOLS[reels[0]]);
  display.setCursor(56, 20);
  display.print(SLOTS_SYMBOLS[reels[1]]);
  display.setCursor(98, 20);
  display.print(SLOTS_SYMBOLS[reels[2]]);

  display.setTextSize(1);
  display.setCursor(0, 54);
  display.print(resultMsg);

  display.display();
}

/// สล็อตแมชชีนจิ๋ว - กด OK หมุน (หัก 1 เครดิต) 3 วงตรงกันหมด = ชนะ ได้ +5 เครดิต เครดิตหมด (<=0) จะเติม
/// ให้ใหม่ 20 อัตโนมัติตอนกด OK ครั้งถัดไป (กันเล่นไม่ได้อีกเลย - เกมทดลองเฉยๆ ไม่มีเดิมพันจริง) เครดิต
/// เก็บถาวรผ่าน NVS ข้าม session ได้ BACK ออกได้ทุกเมื่อ
void runSlotMachine() {
  Preferences prefs;
  prefs.begin("slots", false);
  int credits = prefs.getInt("credits", 20);

  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  int reels[3] = {0, 1, 2};
  drawSlotsFrame(reels, credits, "OK = spin");
  Serial.println("[Slots] เข้า Slot Machine");

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Slots] ออก (กด BACK)");
      prefs.putInt("credits", credits);
      prefs.end();
      return;
    }
    if (btnOk.consumeJustPressed()) {
      if (credits <= 0) {
        credits = 20;
        Serial.println("[Slots] หมดเครดิต - เติมให้ใหม่ 20");
      }
      credits--;

      // animation ขำๆ ก่อนออกผลจริง - สุ่มโชว์สัญลักษณ์ถี่ๆชั่วครู่ให้ดูเหมือนวงล้อกำลังหมุน (ปิดได้ที่
      // Others > Settings ถ้าไม่อยากรอ - ดู slotAnimationEnabled)
      if (slotAnimationEnabled) {
        unsigned long spinStart = millis();
        const unsigned long SPIN_DURATION_MS = 450;
        while (millis() - spinStart < SPIN_DURATION_MS) {
          int tempReels[3] = {random(SLOTS_SYMBOL_COUNT), random(SLOTS_SYMBOL_COUNT), random(SLOTS_SYMBOL_COUNT)};
          drawSlotsFrame(tempReels, credits, "Spinning...");
          delay(60);
        }
      }

      for (int i = 0; i < 3; i++) reels[i] = random(SLOTS_SYMBOL_COUNT);

      const char* msg;
      if (reels[0] == reels[1] && reels[1] == reels[2]) {
        credits += 5;
        msg = "WIN! +5";
        Serial.println("[Slots] WIN!");
        playBeepPattern(3, 60, 60);
      } else if (reels[0] == reels[1] || reels[1] == reels[2] || reels[0] == reels[2]) {
        msg = "So close!";
        playBeep(40);
      } else {
        msg = "Try again";
        playBeep(40);
      }

      prefs.putInt("credits", credits);
      drawSlotsFrame(reels, credits, msg);
    }
    delay(20);
  }
}

// ค่าคงที่ของ Flappy Block ("นก" เป็นสี่เหลี่ยมเฉยๆ เพราะไม่ได้วาดรูปนกจริง) ไปป์เดียวบนจอเหมือน Mini
// Dino (obstacle เดียว ไม่ใช่หลายอันพร้อมกัน)
const int FLAPPY_BLOCK_X = 20;
const int FLAPPY_BLOCK_SIZE = 8;
const float FLAPPY_GRAVITY = 0.35;
const float FLAPPY_FLAP_VELOCITY = -3.2;
const int FLAPPY_PIPE_WIDTH = 8; // ลดจาก 14 - ผู้เล่นรายงานว่าไปป์ดูหนาไปหน่อย เล่นยาก
const int FLAPPY_PIPE_GAP = 26;
const int FLAPPY_PIPE_SPEED = 2;
const unsigned long FLAPPY_TICK_MS = 40;

void drawFlappyFrame(float blockY, float pipeX, int gapY, unsigned int score, unsigned int highScore, bool gameOver) {
  if (!displayReady) return;

  display.clearDisplay();

  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("HI ");
  display.print(highScore);
  display.setCursor(90, 0);
  display.print("SC ");
  display.print(score);

  display.fillRect((int)pipeX, 0, FLAPPY_PIPE_WIDTH, gapY, SSD1306_WHITE);
  display.fillRect((int)pipeX, gapY + FLAPPY_PIPE_GAP, FLAPPY_PIPE_WIDTH, SCREEN_HEIGHT - (gapY + FLAPPY_PIPE_GAP), SSD1306_WHITE);

  display.fillRect(FLAPPY_BLOCK_X, (int)blockY, FLAPPY_BLOCK_SIZE, FLAPPY_BLOCK_SIZE, SSD1306_WHITE);

  if (gameOver) {
    display.fillRect(24, 24, 80, 16, SSD1306_BLACK);
    display.drawRect(24, 24, 80, 16, SSD1306_WHITE);
    display.setTextSize(1);
    display.setCursor(30, 29);
    display.print("GAME OVER");
  }

  display.display();
}

bool checkFlappyCollision(float blockY, float pipeX, int gapY) {
  int blockTop = (int)blockY;
  int blockBottom = blockTop + FLAPPY_BLOCK_SIZE;
  if (blockTop <= 0 || blockBottom >= SCREEN_HEIGHT) return true; // ชนพื้น/เพดาน

  int pipeLeft = (int)pipeX;
  int pipeRight = pipeLeft + FLAPPY_PIPE_WIDTH;
  int blockLeft = FLAPPY_BLOCK_X;
  int blockRight = FLAPPY_BLOCK_X + FLAPPY_BLOCK_SIZE;

  bool overlapX = blockRight > pipeLeft && blockLeft < pipeRight;
  if (!overlapX) return false;

  bool inGap = blockTop >= gapY && blockBottom <= gapY + FLAPPY_PIPE_GAP;
  return !inGap;
}

/// Flappy Block - กด < หรือ SW_OK "กระพือ" (ดันขึ้นทันที ไม่ใช่กระโดดครั้งเดียวจากพื้นแบบ Dino - กระพือ
/// กลางอากาศซ้ำได้เรื่อยๆ) หลบช่องว่างของไปป์ที่ไหลเข้ามา คะแนน = จำนวนไปป์ที่ผ่านไปได้ high score เก็บ
/// ถาวรผ่าน NVS เหมือน Dino/Pong BACK ออกได้ทุกเมื่อ
void runFlappyBlock() {
  Preferences prefs;
  prefs.begin("flappy", false);
  unsigned int highScore = prefs.getUInt("highscore", 0);

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;

  Serial.println("[Flappy] เข้าเกม Flappy Block");

  while (true) {
    float blockY = SCREEN_HEIGHT / 2.0;
    float blockVY = 0;
    float pipeX = SCREEN_WIDTH;
    int gapY = random(6, SCREEN_HEIGHT - FLAPPY_PIPE_GAP - 6);
    unsigned int score = 0;
    bool scoredThisPipe = false;
    bool gameOver = false;
    unsigned long lastTickMs = millis();

    drawFlappyFrame(blockY, pipeX, gapY, score, highScore, false);

    while (!gameOver) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Flappy] ออกจากเกม (กด BACK)");
        prefs.end();
        return;
      }

      // เหมือน Dino เป๊ะ: เรียก consumeJustPressed() ทั้งสองปุ่มแยกกันก่อนเสมอ ไม่ใช้ || แบบ
      // short-circuit กัน edge-detection ของปุ่มที่ถูกข้ามเพี้ยน
      bool flapFromPrev = btnPrev.consumeJustPressed();
      bool flapFromOk = btnOk.consumeJustPressed();
      if (flapFromPrev || flapFromOk) {
        blockVY = FLAPPY_FLAP_VELOCITY;
        playBeep(20);
      }

      if (millis() - lastTickMs >= FLAPPY_TICK_MS) {
        lastTickMs = millis();

        blockY += blockVY;
        blockVY += FLAPPY_GRAVITY;

        pipeX -= FLAPPY_PIPE_SPEED;
        if (!scoredThisPipe && pipeX + FLAPPY_PIPE_WIDTH < FLAPPY_BLOCK_X) {
          score++;
          scoredThisPipe = true;
        }
        if (pipeX < -FLAPPY_PIPE_WIDTH) {
          pipeX = SCREEN_WIDTH;
          gapY = random(6, SCREEN_HEIGHT - FLAPPY_PIPE_GAP - 6);
          scoredThisPipe = false;
        }

        if (checkFlappyCollision(blockY, pipeX, gapY)) {
          gameOver = true;
          Serial.print("[Flappy] ชน - คะแนน: ");
          Serial.println(score);
          if (score > highScore) {
            highScore = score;
            prefs.putUInt("highscore", highScore);
            Serial.println("[Flappy] สถิติใหม่! เซฟลง NVS แล้ว");
            playBeepPattern(3, 60, 60);
          } else {
            playBeep(250);
          }
        }

        drawFlappyFrame(blockY, pipeX, gapY, score, highScore, false);
      }
      delay(5);
    }

    drawFlappyFrame(blockY, pipeX, gapY, score, highScore, true);

    while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
    btnOk.lastPressed = false;
    btnBack.lastPressed = false;

    while (true) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Flappy] ออกจากเกมหลังจบรอบ (กด BACK)");
        prefs.end();
        return;
      }
      if (btnOk.consumeJustPressed()) {
        Serial.println("[Flappy] เล่นใหม่อีกรอบ");
        break;
      }
      delay(20);
    }
  }
}

// ===== Wire Connect =====
// กระดาน 6x6 คงที่ (ยังไม่มี size picker แบบ Lights Out ในรุ่นแรกนี้ กันความซับซ้อนบวมเกินไป) สุ่ม
// จำนวนเส้น 3-5 เส้นทุกรอบ โจทย์สร้างด้วย self-avoiding random walk: สร้าง "คำตอบ" ขึ้นมาก่อนทีละเส้น
// (เดินสุ่มไปเรื่อยๆ ห้ามเดินทับช่องที่เส้นไหนใช้ไปแล้ว) แล้วโชว์ผู้เล่นแค่หัว-ท้ายของแต่ละเส้น (เก็บเป็น
// เลขกำกับ ไม่ใช้สีเพราะจอขาวดำล้วน) รับประกันแก้ได้เสมอเพราะคำตอบที่สร้างขึ้นมาเองใช้งานได้จริงอยู่แล้ว
// - ผู้เล่นไม่จำเป็นต้องเดินเส้นทางเดิมเป๊ะๆ แค่เชื่อมหัว-ท้ายให้ถึงกันโดยไม่ทับเส้นอื่นก็พอ (ไม่บังคับ
// เติมเต็มทุกช่องแบบเกม Flow Free ตัวเต็ม - เวอร์ชันนี้เป็นแค่ "เชื่อมให้ครบ" ธรรมดา)
const int WIRE_GRID_SIZE = 6;
const int WIRE_CELL_PX = 9; // ลดจาก 10 เหลือที่ว่างด้านบนไว้โชว์หัวข้อ Wires/Moves
const int WIRE_HEADER_H = 9; // ความสูงแถบหัวข้อด้านบน (ตัวหนังสือ size 1 สูง 8px + เผื่อขอบ 1px)
const int WIRE_MAX_WIRES = 5;
const int WIRE_MIN_WIRES = 3;
const int WIRE_MAX_PATH_LEN = WIRE_GRID_SIZE * WIRE_GRID_SIZE; // เผื่อพอสำหรับเส้นที่ยาวที่สุดที่เป็นไปได้ (เต็มกระดาน)

int wireCount;
int wireStartX[WIRE_MAX_WIRES], wireStartY[WIRE_MAX_WIRES];
int wireEndX[WIRE_MAX_WIRES], wireEndY[WIRE_MAX_WIRES];
int8_t wireCellOwner[WIRE_GRID_SIZE][WIRE_GRID_SIZE]; // -1 = ว่าง, else = index เส้นที่ครองช่องนั้นอยู่ตอนนี้ (สถานะการเล่นจริง)
int wirePathX[WIRE_MAX_WIRES][WIRE_MAX_PATH_LEN];
int wirePathY[WIRE_MAX_WIRES][WIRE_MAX_PATH_LEN];
int wirePathLen[WIRE_MAX_WIRES];
bool wireConnected[WIRE_MAX_WIRES];
int wireCursorX, wireCursorY;
int wireActivePen; // -1 = ยังไม่ได้หยิบปากกา, else = index เส้นที่กำลังลากอยู่
unsigned int wireMoves; // นับทุกครั้งที่ต่อเส้นไปข้างหน้าสำเร็จ (ไม่นับตอนถอยเส้นกลับ) รีเซ็ตทุกโจทย์ใหม่

/// พยายามสร้างโจทย์ 1 รอบ - คืน false ถ้าเส้นไหนเดินไม่ได้แม้แต่ก้าวเดียว (โจทย์ใช้ไม่ได้ ต้องสร้างใหม่
/// ทั้งหมด) ใช้ wireCellOwner เป็นพื้นที่ทำงานชั่วคราวระหว่างสร้าง (เก็บ "คำตอบ" ที่เดินไว้) - ตัวคำตอบ
/// จริงไม่ได้ถูกเก็บต่อหลังจากนี้ เอาแค่ตำแหน่งหัว-ท้ายของแต่ละเส้นไปใช้ (ผู้เล่นไม่จำเป็นต้องเดินตาม)
bool tryGenerateWirePuzzle(int n, int count) {
  for (int y = 0; y < n; y++) {
    for (int x = 0; x < n; x++) wireCellOwner[y][x] = -1;
  }

  for (int w = 0; w < count; w++) {
    int candX[WIRE_MAX_PATH_LEN], candY[WIRE_MAX_PATH_LEN], candCount = 0;
    for (int y = 0; y < n; y++) {
      for (int x = 0; x < n; x++) {
        if (wireCellOwner[y][x] == -1) {
          candX[candCount] = x;
          candY[candCount] = y;
          candCount++;
        }
      }
    }
    if (candCount == 0) return false;

    int pick = random(candCount);
    int curX = candX[pick], curY = candY[pick];
    wireCellOwner[curY][curX] = w;
    int startX = curX, startY = curY;
    int steps = 1;
    int targetLen = random(3, n + 2);

    while (steps < targetLen) {
      int dx[4] = {0, 0, -1, 1};
      int dy[4] = {-1, 1, 0, 0};
      for (int i = 3; i > 0; i--) { // สลับลำดับ 4 ทิศแบบสุ่ม (Fisher-Yates) กันเดินเอนไปทิศเดิมซ้ำๆ
        int j = random(i + 1);
        int tx = dx[i]; dx[i] = dx[j]; dx[j] = tx;
        int ty = dy[i]; dy[i] = dy[j]; dy[j] = ty;
      }
      bool extended = false;
      for (int d = 0; d < 4; d++) {
        int nx = curX + dx[d], ny = curY + dy[d];
        if (nx >= 0 && nx < n && ny >= 0 && ny < n && wireCellOwner[ny][nx] == -1) {
          wireCellOwner[ny][nx] = w;
          curX = nx; curY = ny;
          steps++;
          extended = true;
          break;
        }
      }
      if (!extended) break; // ตันแล้ว (ล้อมรอบด้วยช่องที่ใช้ไปแล้วหมด) หยุดแค่นี้
    }

    if (steps < 2) return false; // เดินไม่ได้แม้แต่ก้าวเดียว - โจทย์ใช้ไม่ได้

    wireStartX[w] = startX; wireStartY[w] = startY;
    wireEndX[w] = curX; wireEndY[w] = curY;
  }
  return true;
}

/// สร้างโจทย์ใหม่ทั้งหมด (สุ่มจำนวนเส้น 3-5 เส้น ลองสร้างซ้ำจนกว่าจะสำเร็จ จำกัดไว้ 80 ครั้งกันวนไม่รู้จบ
/// จริงๆ แล้วโอกาสพังเกิน 80 ครั้งบนกระดาน 6x6 แค่ 3-5 เส้นแทบเป็นไปไม่ได้เลย) แล้วรีเซ็ตสถานะการเล่นจริง
/// ให้เหลือแค่ node หัว-ท้ายของแต่ละเส้น - node ทั้งสองฝั่งของทุกเส้นถูก "สงวน" ไว้ถาวร (wireCellOwner
/// เป็น index เส้นตัวเองเสมอ ไม่มีวันว่าง แม้ตอนยังไม่ได้เริ่มลากหรือแม้ถอยเส้นออกมาก็ตาม) กันเส้นอื่นมา
/// เดินทับ node ของเส้นอื่นโดยไม่ตั้งใจ
void generateWirePuzzle() {
  wireCount = WIRE_MIN_WIRES + random(WIRE_MAX_WIRES - WIRE_MIN_WIRES + 1);
  int attempts = 0;
  while (!tryGenerateWirePuzzle(WIRE_GRID_SIZE, wireCount) && attempts < 80) attempts++;

  for (int y = 0; y < WIRE_GRID_SIZE; y++) {
    for (int x = 0; x < WIRE_GRID_SIZE; x++) wireCellOwner[y][x] = -1;
  }
  for (int w = 0; w < wireCount; w++) {
    wireCellOwner[wireStartY[w]][wireStartX[w]] = w;
    wireCellOwner[wireEndY[w]][wireEndX[w]] = w;
    wirePathX[w][0] = wireStartX[w];
    wirePathY[w][0] = wireStartY[w];
    wirePathLen[w] = 1;
    wireConnected[w] = false;
  }
}

bool isAdjacentToWireTip(int w, int x, int y) {
  int tipX = wirePathX[w][wirePathLen[w] - 1];
  int tipY = wirePathY[w][wirePathLen[w] - 1];
  int dx = abs(x - tipX), dy = abs(y - tipY);
  return (dx + dy) == 1; // ติดกันแนวตั้ง/แนวนอนเท่านั้น (ไม่ทแยง)
}

/// ลองขยับปลายเส้น w ไปที่ (x,y) - คืน true ถ้าทำสำเร็จ (ต่อเส้นออกไป, ถอยเส้นกลับ, หรือต่อจนจบ) false
/// ถ้าทำไม่ได้ (ไม่ติดกับปลายปัจจุบัน หรือช่องนั้นเป็นของเส้นอื่นไปแล้ว)
bool tryExtendWire(int w, int x, int y) {
  if (!isAdjacentToWireTip(w, x, y)) return false;

  // ถอยเส้น: ช่องที่จะไปคือช่องก่อนหน้าปลายปัจจุบันในเส้นทางเดียวกันเป๊ะ
  if (wirePathLen[w] >= 2 && wirePathX[w][wirePathLen[w] - 2] == x && wirePathY[w][wirePathLen[w] - 2] == y) {
    int tipX = wirePathX[w][wirePathLen[w] - 1];
    int tipY = wirePathY[w][wirePathLen[w] - 1];
    bool tipIsEndNode = (tipX == wireEndX[w] && tipY == wireEndY[w]);
    if (tipIsEndNode) {
      wireConnected[w] = false; // เพิ่งเชื่อมสำเร็จแล้วถอยออกมา - ยกเลิกสถานะเชื่อม
      // ไม่ clear wireCellOwner เพราะ node สงวนไว้ให้เส้นตัวเองถาวรอยู่แล้ว
    } else {
      wireCellOwner[tipY][tipX] = -1; // ช่องกลางทางธรรมดา - ปล่อยว่างจริง ให้เส้นอื่นเดินผ่านได้
    }
    wirePathLen[w]--;
    return true;
  }

  // เดินหน้า: ช่องต้องว่าง หรือเป็นของตัวเองอยู่แล้ว (กรณีเดียวที่เป็นไปได้คือ node ปลายทางของตัวเองที่
  // สงวนไว้ตั้งแต่แรก - ไปถึงแปลว่าต่อจบพอดี)
  int owner = wireCellOwner[y][x];
  if (owner != -1 && owner != w) return false;

  wirePathX[w][wirePathLen[w]] = x;
  wirePathY[w][wirePathLen[w]] = y;
  wirePathLen[w]++;
  wireCellOwner[y][x] = w;
  wireMoves++;

  if (x == wireEndX[w] && y == wireEndY[w]) wireConnected[w] = true;
  return true;
}

/// หาว่า cursor ตอนนี้หยิบปากกาเส้นไหนได้บ้าง - ต้องอยู่ตรงปลาย (tip) ปัจจุบันของเส้นนั้นพอดี หรือถ้า
/// ยังไม่ได้ลากอะไรเลย (pathLen==1) จะหยิบจากฝั่ง "end" ที่ยังไม่ได้แตะก็ได้ด้วย (ผู้เล่นแยกไม่ออกอยู่แล้ว
/// ว่าฝั่งไหนคือ start/end ในโค้ด เพราะเลขกำกับเหมือนกันทั้งสองฝั่ง - เริ่มจากฝั่งไหนก่อนก็ได้)
int findPickupWire(int x, int y) {
  for (int w = 0; w < wireCount; w++) {
    int tipX = wirePathX[w][wirePathLen[w] - 1];
    int tipY = wirePathY[w][wirePathLen[w] - 1];
    if (tipX == x && tipY == y) return w;
    if (wirePathLen[w] == 1 && x == wireEndX[w] && y == wireEndY[w]) return w;
  }
  return -1;
}

bool allWiresConnected() {
  for (int w = 0; w < wireCount; w++) {
    if (!wireConnected[w]) return false;
  }
  return true;
}

/// ขยับเคอร์เซอร์ 1 ก้าวในทิศ (dx,dy) - ถ้ายังไม่ได้หยิบปากกา (wireActivePen==-1) เคอร์เซอร์เดินอิสระได้
/// ทุกช่อง (แค่ไว้เล็งว่าจะไปหยิบปากกาเส้นไหน) แต่ถ้าหยิบปากกาอยู่ จะพยายามขยับ/ถอยเส้นนั้นจริงๆ ผ่าน
/// tryExtendWire() - เคอร์เซอร์ขยับก็ต่อเมื่อขยับเส้นสำเร็จเท่านั้น (เคอร์เซอร์ = ปลายปากกาตอนนั้นเอง)
bool wireStepCursor(int dx, int dy) {
  int nx = wireCursorX + dx;
  int ny = wireCursorY + dy;
  if (nx < 0 || nx >= WIRE_GRID_SIZE || ny < 0 || ny >= WIRE_GRID_SIZE) return false;

  if (wireActivePen == -1) {
    wireCursorX = nx;
    wireCursorY = ny;
    return true;
  }

  if (tryExtendWire(wireActivePen, nx, ny)) {
    wireCursorX = nx;
    wireCursorY = ny;
    return true;
  }
  return false;
}

void drawWireConnectFrame(int cursorX, int cursorY, bool solved) {
  if (!displayReady) return;

  display.clearDisplay();
  int gridPx = WIRE_GRID_SIZE * WIRE_CELL_PX;
  int offsetX = (SCREEN_WIDTH - gridPx) / 2;
  int offsetY = WIRE_HEADER_H + (SCREEN_HEIGHT - WIRE_HEADER_H - gridPx) / 2;

  // หัวข้อบนสุด: จำนวนเส้นที่เชื่อมแล้ว/ทั้งหมด + จำนวน moves
  int connectedCount = 0;
  for (int w = 0; w < wireCount; w++) {
    if (wireConnected[w]) connectedCount++;
  }
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("W:");
  display.print(connectedCount);
  display.print("/");
  display.print(wireCount);
  display.setCursor(70, 0);
  display.print("M:");
  display.print(wireMoves);

  // กรอบรอบกระดาน
  display.drawRect(offsetX - 1, offsetY - 1, gridPx + 2, gridPx + 2, SSD1306_WHITE);

  // วาดเส้นแต่ละเส้นก่อน (บาง 1px เชื่อมจุดกึ่งกลางของแต่ละช่องตามเส้นทางที่ลากไว้จริง)
  for (int w = 0; w < wireCount; w++) {
    for (int i = 0; i < wirePathLen[w] - 1; i++) {
      int x1 = offsetX + wirePathX[w][i] * WIRE_CELL_PX + WIRE_CELL_PX / 2;
      int y1 = offsetY + wirePathY[w][i] * WIRE_CELL_PX + WIRE_CELL_PX / 2;
      int x2 = offsetX + wirePathX[w][i + 1] * WIRE_CELL_PX + WIRE_CELL_PX / 2;
      int y2 = offsetY + wirePathY[w][i + 1] * WIRE_CELL_PX + WIRE_CELL_PX / 2;
      display.drawLine(x1, y1, x2, y2, SSD1306_WHITE);
    }
  }

  // เลขกำกับหัว-ท้ายทุกเส้น (แทนสีที่จอขาวดำทำไม่ได้ - เลขเดียวกันทั้งสองฝั่งของเส้นเดียวกัน)
  display.setTextSize(1);
  for (int w = 0; w < wireCount; w++) {
    display.setCursor(offsetX + wireStartX[w] * WIRE_CELL_PX + (WIRE_CELL_PX - 6) / 2,
                       offsetY + wireStartY[w] * WIRE_CELL_PX + (WIRE_CELL_PX - 8) / 2);
    display.print(w + 1);
    display.setCursor(offsetX + wireEndX[w] * WIRE_CELL_PX + (WIRE_CELL_PX - 6) / 2,
                       offsetY + wireEndY[w] * WIRE_CELL_PX + (WIRE_CELL_PX - 8) / 2);
    display.print(w + 1);
  }

  if (!solved) {
    display.drawRect(offsetX + cursorX * WIRE_CELL_PX, offsetY + cursorY * WIRE_CELL_PX, WIRE_CELL_PX, WIRE_CELL_PX, SSD1306_WHITE);
  } else {
    display.fillRect(30, 24, 68, 16, SSD1306_BLACK);
    display.drawRect(30, 24, 68, 16, SSD1306_WHITE);
    display.setCursor(38, 29);
    display.print("Solved!");
  }

  display.display();
}

/// Wire Connect - เลื่อนเคอร์เซอร์ด้วย < / > ปกติ (ซ้าย-ขวา), กด SW_OK ค้าง + < / > เพื่อขึ้น-ลง (แพทเทิร์น
/// เดียวกับ Draw mode/Lights Out เป๊ะ) แตะ SW_OK เฉยๆ (ไม่ได้ใช้เป็นตัวช่วยเปลี่ยนทิศเลย) ตอนเคอร์เซอร์อยู่
/// ตรงปลายเส้นไหน = หยิบ/วางปากกาเส้นนั้น ระหว่างถือปากกาอยู่ เคอร์เซอร์จะขยับได้แค่ตามเส้นทางที่ต่อได้
/// จริงเท่านั้น (ห้ามทับเส้นอื่น) เชื่อมครบทุกเส้นเพื่อชนะ BACK ออกได้ทุกเมื่อ
void runWireConnect() {
  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  Serial.println("[Wire] เข้าเกม Wire Connect");

  while (true) {
    generateWirePuzzle();
    wireCursorX = wireStartX[0];
    wireCursorY = wireStartY[0];
    wireActivePen = -1;
    wireMoves = 0;
    bool solved = false;

    unsigned long nextHeldSinceMs = 0, lastNextMoveMs = 0;
    bool nextRepeating = false;
    unsigned long prevHeldSinceMs = 0, lastPrevMoveMs = 0;
    bool prevRepeating = false;
    bool okWasHeld = false, okUsedAsModifier = false;

    Serial.print("[Wire] โจทย์ใหม่ - จำนวนเส้น: ");
    Serial.println(wireCount);
    drawWireConnectFrame(wireCursorX, wireCursorY, false);

    while (!solved) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Wire] ออกจากเกม (กด BACK)");
        return;
      }

      bool moved = false;
      bool okHeld = btnOk.isPressed();

      if (btnNext.consumeJustPressed()) {
        int dx = okHeld ? 0 : 1, dy = okHeld ? 1 : 0;
        if (okHeld) okUsedAsModifier = true;
        if (wireStepCursor(dx, dy)) moved = true;
        nextHeldSinceMs = millis();
        lastNextMoveMs = millis();
        nextRepeating = false;
      } else if (btnNext.isPressed()) {
        if (!nextRepeating && millis() - nextHeldSinceMs >= DRAW_REPEAT_INITIAL_DELAY_MS) {
          nextRepeating = true;
          lastNextMoveMs = millis();
        }
        if (nextRepeating && millis() - lastNextMoveMs >= DRAW_REPEAT_INTERVAL_MS) {
          int dx = okHeld ? 0 : 1, dy = okHeld ? 1 : 0;
          if (okHeld) okUsedAsModifier = true;
          if (wireStepCursor(dx, dy)) moved = true;
          lastNextMoveMs = millis();
        }
      } else {
        nextRepeating = false;
      }

      if (btnPrev.consumeJustPressed()) {
        int dx = okHeld ? 0 : -1, dy = okHeld ? -1 : 0;
        if (okHeld) okUsedAsModifier = true;
        if (wireStepCursor(dx, dy)) moved = true;
        prevHeldSinceMs = millis();
        lastPrevMoveMs = millis();
        prevRepeating = false;
      } else if (btnPrev.isPressed()) {
        if (!prevRepeating && millis() - prevHeldSinceMs >= DRAW_REPEAT_INITIAL_DELAY_MS) {
          prevRepeating = true;
          lastPrevMoveMs = millis();
        }
        if (prevRepeating && millis() - lastPrevMoveMs >= DRAW_REPEAT_INTERVAL_MS) {
          int dx = okHeld ? 0 : -1, dy = okHeld ? -1 : 0;
          if (okHeld) okUsedAsModifier = true;
          if (wireStepCursor(dx, dy)) moved = true;
          lastPrevMoveMs = millis();
        }
      } else {
        prevRepeating = false;
      }

      if (okHeld && !okWasHeld) okUsedAsModifier = false;
      if (!okHeld && okWasHeld) {
        if (!okUsedAsModifier) {
          if (wireActivePen == -1) {
            int w = findPickupWire(wireCursorX, wireCursorY);
            if (w >= 0) {
              if (wirePathLen[w] == 1 && wireCursorX == wireEndX[w] && wireCursorY == wireEndY[w]) {
                // เริ่มจากฝั่ง "end" แทน - สลับบทบาท start/end ในตัวแปรเฉยๆ (ไม่มีผลต่อผู้เล่นเลย เพราะ
                // เลขกำกับเหมือนกันทั้งสองฝั่งอยู่แล้ว)
                int tmpX = wireStartX[w], tmpY = wireStartY[w];
                wireStartX[w] = wireEndX[w];
                wireStartY[w] = wireEndY[w];
                wireEndX[w] = tmpX;
                wireEndY[w] = tmpY;
                wirePathX[w][0] = wireStartX[w];
                wirePathY[w][0] = wireStartY[w];
              }
              wireActivePen = w;
              Serial.print("[Wire] หยิบปากกาเส้น ");
              Serial.println(w + 1);
            }
          } else {
            Serial.print("[Wire] วางปากกาเส้น ");
            Serial.println(wireActivePen + 1);
            wireActivePen = -1;
          }
          moved = true;
        }
        okUsedAsModifier = false;
      }
      okWasHeld = okHeld;

      if (allWiresConnected()) {
        solved = true;
        Serial.println("[Wire] เชื่อมครบทุกเส้นแล้ว!");
        playBeepPattern(3, 60, 60);
      }

      if (moved) drawWireConnectFrame(wireCursorX, wireCursorY, solved);
      delay(20);
    }

    while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
    btnOk.lastPressed = false;
    btnBack.lastPressed = false;

    while (true) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Wire] ออกจากเกมหลังแก้สำเร็จ (กด BACK)");
        return;
      }
      if (btnOk.consumeJustPressed()) {
        Serial.println("[Wire] เล่นโจทย์ใหม่");
        break;
      }
      delay(20);
    }
  }
}

// ===== Tamagotchi =====
// เครื่องนี้ไม่มี RTC แบตแยก และเปิดๆปิดๆ ตามปกติ (ไม่ได้เสียบไฟทิ้งไว้ตลอด) - แปลว่า stat จะ "หยุดเวลา"
// ระหว่างที่ไม่ได้เปิดหน้านี้อยู่ (ทั้งตอนปิดเครื่องและตอนอยู่เมนูอื่น) ไม่ได้ลดลงเองต่อเนื่องจริงแบบ
// Tamagotchi ของแท้ - stat (หิว/สนุก/พลังงาน) เก็บถาวรผ่าน NVS (โหลดตอนเข้า เซฟตอนออกทุกครั้ง) แต่ลด
// ลงเฉพาะตอนอยู่ในหน้านี้จริงๆ เท่านั้น "Age" (นาทีสะสม) นับจากเวลาที่เคยอยู่ในหน้านี้จริงรวมทุกครั้ง
// ไม่ใช่เวลาตั้งแต่สร้างสัตว์เลี้ยงครั้งแรก
const unsigned long PET_DECAY_INTERVAL_MS = 8000; // ทุกๆ 8 วิที่อยู่ในหน้านี้ ลดแต่ละ stat ลง 1 แต้ม
const char* PET_ACTIONS[] = {"Feed", "Play", "Rest"};
const int PET_ACTION_COUNT = 3;

int clampStat(int v) {
  if (v < 0) return 0;
  if (v > 100) return 100;
  return v;
}

void drawPetBar(int x, int y, int w, int value) {
  display.drawRect(x, y, w, 5, SSD1306_WHITE);
  int fillW = (w - 2) * value / 100;
  if (fillW > 0) display.fillRect(x + 1, y + 1, fillW, 3, SSD1306_WHITE);
}

/// วาด sprite หน้าสัตว์เลี้ยง (40x40 - แปลงจากภาพวาดจริงที่ผู้ใช้ส่งมา ดู pet_sprites.h) ตาม mood ที่
/// เลือก - เดิมเคยวาดด้วย primitive (วงกลม/สามเหลี่ยม) ไปก่อน แต่เปลี่ยนมาใช้ภาพจริงแทนตามที่ขอ แพ็คบิต
/// แบบเดียวกับ getPicPixel/drawGifFrame (idx = y*W+x, byte = idx/8, bit = idx%8) mood: 0 เศร้า, 1 เฉยๆ,
/// 2 มีความสุข
void drawPetSprite(int mood) {
  const uint8_t* sprite;
  if (mood == 2) sprite = PET_SPRITE_HAPPY;
  else if (mood == 0) sprite = PET_SPRITE_SAD;
  else sprite = PET_SPRITE_NEUTRAL;

  int offsetX = (SCREEN_WIDTH - PET_SPRITE_W) / 2;
  int offsetY = 10; // เผื่อที่แถบ stat ด้านบน (y 0-7) กับแถบ action ด้านล่าง (y 56-63) ไว้แล้ว

  for (int y = 0; y < PET_SPRITE_H; y++) {
    for (int x = 0; x < PET_SPRITE_W; x++) {
      int idx = y * PET_SPRITE_W + x;
      uint8_t b = pgm_read_byte(&sprite[idx / 8]);
      if ((b >> (idx % 8)) & 1) {
        display.drawPixel(offsetX + x, offsetY + y, SSD1306_WHITE);
      }
    }
  }
}

/// จอหลักของ Tamagotchi - แถบ stat บนสุด (1 บรรทัดรวม 3 ตัว ประหยัดที่ให้ sprite ได้เต็มๆ), sprite
/// กึ่งกลาง, แถบ action ล่างสุด - ไม่โชว์ Age ค้างจอแล้ว (เคยมีแต่ไม่พอที่ พอมาใช้ sprite จริงต้องการ
/// พื้นที่เยอะกว่า primitive เดิมมาก) โชว์ Age แค่ตอนเพิ่งเข้ามาเป็นข้อความแวบเดียวแทน (ดู runTamagotchi)
void drawPetFrame(int hunger, int fun, int energy, int actionIdx) {
  if (!displayReady) return;

  display.clearDisplay();

  // เปลี่ยนจาก "H"(unger) เป็น "Fd"(Food) - ค่าตัวแปร hunger จริงๆหมายถึง "อิ่มแค่ไหน" (ให้อาหารแล้ว
  // ค่าเพิ่ม) แต่ label เดิม "Hunger" สื่อความหมายตรงข้าม (คนอ่านคิดว่ายิ่งเพิ่ม = ยิ่งหิว) ทำให้งง -
  // ใช้ 2 ตัวอักษรทั้ง 3 อันเพื่อเลี่ยงตัวย่อ F ซ้ำกันระหว่าง Food กับ Fun
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("Fd");
  drawPetBar(14, 1, 24, hunger);
  display.setCursor(44, 0);
  display.print("Fn");
  drawPetBar(58, 1, 24, fun);
  display.setCursor(88, 0);
  display.print("En");
  drawPetBar(102, 1, 24, energy);

  int avg = (hunger + fun + energy) / 3;
  int mood = (avg >= 66) ? 2 : ((avg >= 33) ? 1 : 0);
  drawPetSprite(mood);

  display.setCursor(0, 56);
  display.print("< ");
  display.print(PET_ACTIONS[actionIdx]);
  display.print(" > OK=do");

  display.display();
}

/// Tamagotchi - < / > เลือก action (Feed/Play/Rest), OK ยืนยันทำทันที Feed +25 หิว, Play +25 สนุก
/// -10 พลังงาน (เล่นแล้วเหนื่อย), Rest +25 พลังงาน stat ลดลงเองทุก ~8 วิที่อยู่ในหน้านี้ (ดู
/// PET_DECAY_INTERVAL_MS) BACK ออก - เซฟ stat ปัจจุบัน + สะสม Age เพิ่มตามเวลาที่อยู่ในหน้านี้จริงก่อน
/// ออกทุกครั้ง
void runTamagotchi() {
  Preferences prefs;
  prefs.begin("pet", false);
  int hunger = clampStat(prefs.getInt("hunger", 80));
  int fun = clampStat(prefs.getInt("fun", 80));
  int energy = clampStat(prefs.getInt("energy", 80));
  unsigned long ageMinutes = prefs.getULong("ageMin", 0);
  prefs.end();

  int actionIdx = 0;

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  Serial.println("[Pet] เข้า Tamagotchi");
  String ageMsg = "Age: " + String(ageMinutes) + "m";
  showDisplayMessage("Welcome back!", ageMsg.c_str());
  delay(1000);
  drawPetFrame(hunger, fun, energy, actionIdx);

  unsigned long sessionStartMs = millis();
  unsigned long lastDecayMs = millis();

  while (true) {
    if (btnBack.consumeJustPressed()) {
      unsigned long sessionMinutes = (millis() - sessionStartMs) / 60000;
      ageMinutes += sessionMinutes;
      Preferences p2;
      p2.begin("pet", false);
      p2.putInt("hunger", hunger);
      p2.putInt("fun", fun);
      p2.putInt("energy", energy);
      p2.putULong("ageMin", ageMinutes);
      p2.end();
      Serial.println("[Pet] ออกจาก Tamagotchi (กด BACK) - เซฟสถานะแล้ว");
      return;
    }

    bool moved = false;
    if (btnNext.consumeJustPressed()) {
      actionIdx = (actionIdx + 1) % PET_ACTION_COUNT;
      moved = true;
    }
    if (btnPrev.consumeJustPressed()) {
      actionIdx = (actionIdx - 1 + PET_ACTION_COUNT) % PET_ACTION_COUNT;
      moved = true;
    }

    if (btnOk.consumeJustPressed()) {
      if (actionIdx == 0) {
        hunger = clampStat(hunger + 25);
      } else if (actionIdx == 1) {
        fun = clampStat(fun + 25);
        energy = clampStat(energy - 10);
      } else if (actionIdx == 2) {
        energy = clampStat(energy + 25);
      }
      playBeep(30);
      Serial.print("[Pet] ทำ action: ");
      Serial.println(PET_ACTIONS[actionIdx]);
      moved = true;
    }

    if (millis() - lastDecayMs >= PET_DECAY_INTERVAL_MS) {
      lastDecayMs = millis();
      hunger = clampStat(hunger - 1);
      fun = clampStat(fun - 1);
      energy = clampStat(energy - 1);
      moved = true;
    }

    if (moved) drawPetFrame(hunger, fun, energy, actionIdx);
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
