#ifndef OTHERS_TAMAGOTCHI_H
#define OTHERS_TAMAGOTCHI_H

// ============================================================================
// others_tamagotchi.h - Tamagotchi ในเมนู Others (ดู runOthersMenu ใน .ino หลัก) แยกออกมาจาก
// RemoteLockKnob.ino ตอนแยก Others ออกจากฟีเจอร์ remote lock หลัก เหตุผล/กติกาเดียวกับ
// others_games_arcade.h ทุกอย่าง (ดู comment หัวไฟล์นั้นถ้าอยากรู้รายละเอียดเรื่อง
// auto-prototype/forward declaration) - ไฟล์นี้ต้อง #include อยู่ก่อน others_menu.h เหมือนกัน
// ============================================================================

#include "pet_sprites.h" // หน้าสัตว์เลี้ยง 3 mood (happy/neutral/sad) - ย้ายมาจาก .ino หลักตอนแยกไฟล์นี้

// ---------------------------------------------------------------------------
// FORWARD DECLARATIONS
// ---------------------------------------------------------------------------
int clampStat(int v);
void drawPetBar(int x, int y, int w, int value);
void drawPetSprite(int mood);
void drawPetFrame(int hunger, int fun, int energy, int actionIdx);
void runTamagotchi();

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

#endif // OTHERS_TAMAGOTCHI_H
