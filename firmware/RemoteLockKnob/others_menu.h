#ifndef OTHERS_MENU_H
#define OTHERS_MENU_H

// ============================================================================
// others_menu.h - เมนู Others เอง (dispatch เข้าเกม/ของเล่นทุกตัว) + Settings submenu + Device
// Stats page + คลัง Pic/GIF (struct/library/viewer) + Draw mode - แยกออกมาจาก RemoteLockKnob.ino
// ตอนแยก Others ออกจากฟีเจอร์ remote lock หลัก (ไฟล์ .ino เดิมโตจน 77% เป็นเนื้อหา Others ล้วนๆ)
//
// ข้อควรรู้ก่อนแก้ไฟล์นี้ (เหมือน fisch_game.h ทุกอย่าง): ฟังก์ชันในไฟล์ .h ที่ #include เข้ามาใน .ino
// จะ**ไม่ถูก**สแกนหา prototype อัตโนมัติแบบเดียวกับฟังก์ชันที่เขียนตรงใน .ino เอง (auto-prototype ของ
// Arduino สแกนแค่เนื้อไฟล์ .ino ดิบๆ ไม่มองเข้าไปใน #include) - ไฟล์นี้พิเศษกว่าไฟล์ Others อื่นตรงที่
// runOthersMenu() ด้านล่างเรียกเข้าไปยังฟังก์ชันของทุกไฟล์ Others อื่นด้วย (runMiniDinoGame,
// runTicTacToe, runFischGame ฯลฯ) เพราะงั้นก้อน FORWARD DECLARATIONS ด้านล่างเลย forward declare ทั้ง
// ฟังก์ชันของไฟล์นี้เอง **และ** ฟังก์ชันจากไฟล์ Others อื่นๆ ทั้งหมดไว้ด้วย - ผลคือไฟล์ others_*.h ทั้ง
// 6 ไฟล์ #include เข้า .ino ตามลำดับไหนก็ได้ ไม่ผูกกันอีกต่อไป (ต่างจาก fisch_game.h เดิมที่ต้องมาก่อน
// runOthersMenu เสมอ) - เพิ่มฟังก์ชันใหม่ในไฟล์นี้ = ต้องเพิ่ม prototype ในก้อนนั้นด้วย
//
// PicEntry/GifEntry ต้องประกาศ struct ไว้บนสุดของไฟล์นี้ (ก่อนฟังก์ชันที่รับเป็นพารามิเตอร์ เช่น
// runViewPic/runGifLoop) เหตุผลเดียวกับ forward declaration ข้างบน - auto-prototype จะพยายามสร้าง
// prototype ให้ฟังก์ชันพวกนี้ไม่ได้ถ้า struct ยังไม่ถูกประกาศตอนนั้น ("does not name a type")
// ============================================================================

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

// ---------------------------------------------------------------------------
// FORWARD DECLARATIONS - ฟังก์ชันของไฟล์นี้เอง
// ---------------------------------------------------------------------------
void showOthersMenuScreen(int menuIndex);
void runOthersMenu();
void showOthersSettingsScreen(int idx);
void runOthersSettingsMenu();
String padTwoDigits(unsigned int v);
void drawStatsPage(int page);
void runDeviceStats();
void runClearOldScore();
void runClearPongScore();
void runClearFlappyScore();
void runClearSlotCredits();
void runClearDeviceStats();
void runResetTamagotchi();
bool getDrawPixel(int x, int y);
void toggleDrawPixel(int x, int y);
void drawDrawModeFrame(int cursorX, int cursorY);
void runDrawMode();
void runClearDrawMode();
bool getPicPixel(const PicEntry &pic, int x, int y);
void drawPicFrame(const PicEntry &pic);
void runViewPic(const PicEntry &pic);
void drawGifFrame(const uint8_t* frame);
void runGifLoop(const GifEntry &gif);
void runGifPlayerMenu();

// ---------------------------------------------------------------------------
// FORWARD DECLARATIONS - ฟังก์ชันจากไฟล์ others_*.h อื่นที่ runOthersMenu()/runOthersSettingsMenu()
// เรียกเข้าไป (นิยามจริงอยู่คนละไฟล์ - ดู others_games_arcade.h / others_games_board.h /
// others_games_casual.h / others_tamagotchi.h / fisch_game.h)
// ---------------------------------------------------------------------------
void runMiniDinoGame();
void runPong();
void runDvdBounce();
void runFlappyBlock();
void runTicTacToe();
void runGameOfLife();
void runLightsOut();
void runWireConnect();
void runMagic8Ball();
void runDiceRoller();
void runRockPaperScissors();
void runSlotMachine();
void runTamagotchi();
void runFischGame();

// 2026-09-17: ล้างคลังภาพ/GIF เก่าทั้งหมดทิ้ง (pics.h + gif_*.h ลบออกจากโปรเจกต์แล้ว) ตั้งใจเริ่มวาด
// ใหม่ทั้งหมดผ่าน Pic Mode Editor artifact ตัวเดียว (ใช้ปุ่ม Export ในนั้น self-export ไฟล์ .h ได้เลย
// ไม่ต้องผ่าน Claude แปลงให้อีกต่อไป) - ให้ทุกภาพ/แอนิเมชันมาจากไปป์ไลน์เดียวกันหมด ไม่ปนกับของเก่าที่
// แปลงด้วย Python script คนละตัว (gen_pics.py/gen_gif.py) เพิ่มภาพใหม่ทีหลัง: วาดใน artifact แท็บ
// Export > Pic หรือ Export > Gif กด Download แล้วทำตาม install steps ในหน้านั้น (จะบอกเองว่าต้อง
// #include ไฟล์ไหน และเพิ่มบรรทัดไหนใน PIC_LIBRARY[]/GIF_LIBRARY[] ด้านล่างนี้)
const PicEntry PIC_LIBRARY[] = {};
const int PIC_LIBRARY_COUNT = sizeof(PIC_LIBRARY) / sizeof(PicEntry);

const GifEntry GIF_LIBRARY[] = {};
const int GIF_LIBRARY_COUNT = sizeof(GIF_LIBRARY) / sizeof(GifEntry);

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
      recoverDisplayIfNeeded(); // re-init จอกันเพี้ยนค้างจากของเล่นที่ redraw ถี่ๆ - ดู comment ใน .ino หลัก
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
  // คลังว่างได้ตอนนี้ (ดู comment เหนือ GIF_LIBRARY[] - ล้างของเก่าทิ้งรอวาดใหม่ผ่าน Pic Mode Editor)
  // ห้าม index GIF_LIBRARY[0] ตอน count เป็น 0 เด็ดขาด (อ่านเลยขอบ array)
  if (GIF_LIBRARY_COUNT == 0) {
    showDisplayMessage("No GIFs yet", "BACK to return");
    while (btnBack.isPressed()) { delay(20); }
    btnBack.lastPressed = false;
    while (!btnBack.consumeJustPressed()) { delay(20); }
    return;
  }

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

#endif // OTHERS_MENU_H
