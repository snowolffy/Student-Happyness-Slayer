#ifndef OTHERS_GAMES_BOARD_H
#define OTHERS_GAMES_BOARD_H

// ============================================================================
// others_games_board.h - เกมแนว board/logic ในเมนู Others: Tic-Tac-Toe (+ minimax AI), Game of
// Life, Lights Out, Wire Connect (ดู runOthersMenu ใน .ino หลัก) แยกออกมาจาก RemoteLockKnob.ino
// ตอนแยก Others ออกจากฟีเจอร์ remote lock หลัก เหตุผล/กติกาเดียวกับ others_games_arcade.h ทุกอย่าง
// (ดู comment หัวไฟล์นั้นถ้าอยากรู้รายละเอียดเรื่อง auto-prototype/forward declaration) - ไฟล์นี้ต้อง
// #include อยู่ก่อน others_menu.h เหมือนกัน
//
// หมายเหตุ Game of Life: เดิมใช้ DRAW_COLS/DRAW_ROWS/DRAW_CELL_SIZE ร่วมกับ Draw mode (ตอนทุกอย่างยัง
// อยู่ไฟล์เดียวกัน) ตอนแยกไฟล์ออกมา Draw mode ย้ายไปอยู่ others_menu.h แทน เลยประกาศค่าคงที่ชุดเดียวกัน
// แยกเป็นของตัวเอง (LIFE_COLS/LIFE_ROWS/LIFE_CELL_SIZE ค่าเท่ากันเป๊ะ) กันไฟล์ต้องผูกลำดับ include กัน
// ไม่จำเป็น - เพิ่ม 3 บรรทัดซ้ำแลกกับไฟล์ที่เป็นอิสระจากกันจริงๆ
// ---------------------------------------------------------------------------
// FORWARD DECLARATIONS
// ---------------------------------------------------------------------------
char checkWinner(char *b);
int minimax(char *b, bool maximizingO);
int bestMoveForO(char *b);
int chooseMoveForO(char *b, int difficulty);
int runTttDifficultyPicker(int startDifficulty);
void drawTicTacToeFrame(char *board, int cursor, bool gameOver, char winner, int difficulty);
void runTicTacToe();
bool getLifeCell(uint8_t *buf, int x, int y);
void setLifeCell(uint8_t *buf, int x, int y, bool alive);
void seedLifeRandom(uint8_t *buf);
void stepLife(uint8_t *cur, uint8_t *next);
void drawLifeFrame(uint8_t *buf);
void runGameOfLife();
bool getLightsOutCell(uint64_t board, int n, int x, int y);
uint64_t toggleLightsOutMask(int n, int x, int y);
bool lightsOutSolved(uint64_t board, int n);
void scrambleLightsOut(uint64_t &board, int n);
void drawLightsOutFrame(uint64_t board, int n, int cellPx, int cursorX, int cursorY, unsigned int moves, bool solved);
void showLightsOutSizeScreen(int idx);
int runLightsOutSizePicker(int startIdx);
void runLightsOut();
bool tryGenerateWirePuzzle(int n, int count);
void generateWirePuzzle();
bool isAdjacentToWireTip(int w, int x, int y);
bool tryExtendWire(int w, int x, int y);
int findPickupWire(int x, int y);
bool allWiresConnected();
bool wireStepCursor(int dx, int dy);
void drawWireConnectFrame(int cursorX, int cursorY, bool solved);
void runWireConnect();

// Lights Out และ Wire Connect (คนละที่ในไฟล์นี้) ต่างก็เลื่อนเคอร์เซอร์แบบ "กดค้างเลื่อนต่อเนื่องได้"
// เหมือน Draw mode ทุกอย่าง (กดครั้งแรกขยับ 1 ช่อง กดค้างต่อเกิน INITIAL_DELAY ถึงเริ่มขยับซ้ำเองทุกๆ
// INTERVAL) - เดิมใช้ค่าคงที่ร่วมกับ Draw mode (DRAW_REPEAT_*) ตอนทุกอย่างยังอยู่ไฟล์เดียวกัน ตอนแยกไฟล์
// Draw mode ย้ายไปอยู่ others_menu.h แทน เลยประกาศชุดเดียวกันแยกเป็นของตัวเอง (ค่าเท่ากันเป๊ะ) กันไฟล์
// ต้องผูกลำดับ include กันโดยไม่จำเป็น
const unsigned long BOARD_REPEAT_INITIAL_DELAY_MS = 400;
const unsigned long BOARD_REPEAT_INTERVAL_MS = 90;

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

// framebuffer คู่ของ Conway's Game of Life - แยกจาก drawBuffer ของ Draw mode เด็ดขาด (ไม่งั้นจะไปทับ
// ภาพที่ผู้ใช้วาดไว้) ใช้ความละเอียดเดียวกัน (64x32 ผ่าน DRAW_CELL_SIZE 2x2 พิกเซล/ช่อง) ต้องมี 2 buffer
// เพราะคำนวณ generation ถัดไปต้องอ้างอิงสถานะเดิมทั้งกระดานพร้อมกัน เขียนทับ buffer เดียวกันระหว่างคำนวณ
// จะพังตั้งแต่กลางที่ (สลับ current/next ด้วย pointer แทนการ copy ข้อมูลจริง ประหยัดกว่า)
const int LIFE_CELL_SIZE = 2;
const int LIFE_COLS = SCREEN_WIDTH / LIFE_CELL_SIZE;   // 64
const int LIFE_ROWS = SCREEN_HEIGHT / LIFE_CELL_SIZE;  // 32

uint8_t lifeBufA[LIFE_COLS * LIFE_ROWS / 8];
uint8_t lifeBufB[LIFE_COLS * LIFE_ROWS / 8];

/// อ่านค่าเซลล์แบบ wraparound (ขอบจอวนกลับมาอีกฝั่ง - torus topology) กันเซลล์ตายเพราะขอบจอไปเฉยๆ
/// ทั้งที่กฎ Game of Life จริงไม่ได้ตั้งใจให้มี "ขอบโลก"
bool getLifeCell(uint8_t *buf, int x, int y) {
  x = ((x % LIFE_COLS) + LIFE_COLS) % LIFE_COLS;
  y = ((y % LIFE_ROWS) + LIFE_ROWS) % LIFE_ROWS;
  int idx = y * LIFE_COLS + x;
  return (buf[idx / 8] >> (idx % 8)) & 1;
}

void setLifeCell(uint8_t *buf, int x, int y, bool alive) {
  int idx = y * LIFE_COLS + x;
  if (alive) buf[idx / 8] |= (1 << (idx % 8));
  else buf[idx / 8] &= ~(1 << (idx % 8));
}

/// สุ่มเซลล์เริ่มต้นประมาณ 25% ของกระดาน - ทดลองแล้วให้แพทเทิร์นที่น่าดูกว่าทั้งแน่นเกินไป (ตายหมดเร็ว)
/// หรือโล่งเกินไป (ไม่มีอะไรเกิดขึ้นเลย)
void seedLifeRandom(uint8_t *buf) {
  for (unsigned int i = 0; i < LIFE_COLS * LIFE_ROWS / 8; i++) buf[i] = 0;
  for (int y = 0; y < LIFE_ROWS; y++) {
    for (int x = 0; x < LIFE_COLS; x++) {
      if (random(100) < 25) setLifeCell(buf, x, y, true);
    }
  }
}

/// คำนวณ generation ถัดไปตามกฎ B3/S23 ของ Conway's Game of Life มาตรฐาน: เซลล์มีชีวิตรอดถ้ามีเพื่อนบ้าน
/// (8 ทิศ) 2 หรือ 3 ตัว, เซลล์ตายเกิดใหม่ถ้ามีเพื่อนบ้านมีชีวิตพอดี 3 ตัว นอกนั้นตาย/ยังตายต่อ
void stepLife(uint8_t *cur, uint8_t *next) {
  for (int y = 0; y < LIFE_ROWS; y++) {
    for (int x = 0; x < LIFE_COLS; x++) {
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
  for (int y = 0; y < LIFE_ROWS; y++) {
    for (int x = 0; x < LIFE_COLS; x++) {
      if (getLifeCell(buf, x, y)) {
        display.fillRect(x * LIFE_CELL_SIZE, y * LIFE_CELL_SIZE, LIFE_CELL_SIZE, LIFE_CELL_SIZE, SSD1306_WHITE);
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
        if (!nextRepeating && millis() - nextHeldSinceMs >= BOARD_REPEAT_INITIAL_DELAY_MS) {
          nextRepeating = true;
          lastNextMoveMs = millis();
        }
        if (nextRepeating && millis() - lastNextMoveMs >= BOARD_REPEAT_INTERVAL_MS) {
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
        if (!prevRepeating && millis() - prevHeldSinceMs >= BOARD_REPEAT_INITIAL_DELAY_MS) {
          prevRepeating = true;
          lastPrevMoveMs = millis();
        }
        if (prevRepeating && millis() - lastPrevMoveMs >= BOARD_REPEAT_INTERVAL_MS) {
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
        if (!nextRepeating && millis() - nextHeldSinceMs >= BOARD_REPEAT_INITIAL_DELAY_MS) {
          nextRepeating = true;
          lastNextMoveMs = millis();
        }
        if (nextRepeating && millis() - lastNextMoveMs >= BOARD_REPEAT_INTERVAL_MS) {
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
        if (!prevRepeating && millis() - prevHeldSinceMs >= BOARD_REPEAT_INITIAL_DELAY_MS) {
          prevRepeating = true;
          lastPrevMoveMs = millis();
        }
        if (prevRepeating && millis() - lastPrevMoveMs >= BOARD_REPEAT_INTERVAL_MS) {
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

#endif // OTHERS_GAMES_BOARD_H
