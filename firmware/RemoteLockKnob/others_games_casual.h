#ifndef OTHERS_GAMES_CASUAL_H
#define OTHERS_GAMES_CASUAL_H

// ============================================================================
// others_games_casual.h - ของเล่นสุ่ม/สั้นๆ ในเมนู Others: Magic 8-Ball, Dice Roller, Rock Paper
// Scissors, Slot Machine (ดู runOthersMenu ใน .ino หลัก) แยกออกมาจาก RemoteLockKnob.ino ตอนแยก
// Others ออกจากฟีเจอร์ remote lock หลัก เหตุผล/กติกาเดียวกับ others_games_arcade.h ทุกอย่าง (ดู
// comment หัวไฟล์นั้นถ้าอยากรู้รายละเอียดเรื่อง auto-prototype/forward declaration) - ไฟล์นี้ต้อง
// #include อยู่ก่อน others_menu.h เหมือนกัน
// ============================================================================

// ---------------------------------------------------------------------------
// FORWARD DECLARATIONS
// ---------------------------------------------------------------------------
void runMagic8Ball();
void drawDiceFrame(int value);
void runDiceRoller();
int rpsCompare(int a, int b);
void drawRpsIcon(int shape, int cx, int cy, int size);
void drawRpsPickFrame(int choice);
void drawRpsResultFrame(int you, int cpu, int result);
void runRockPaperScissors();
void drawSlotsFrame(const int reels[3], int credits, const char* resultMsg);
void runSlotMachine();

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


#endif // OTHERS_GAMES_CASUAL_H
